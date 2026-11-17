#!/usr/bin/env bash
# ISK-0008 developer topology: asserts the running default topology's state before ISK-0021 (local-environment
# contract, Smoke assertions 1 to 7; research R13). It runs every check, names each failure, and exits 1 if any failed.
# The values it searches for are held only in shell variables and never printed or passed as an argument.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: local/scripts/smoke.sh

Asserts the running local topology: every service running, each plane live, each plane's exact readiness answer
before ISK-0021, the migration job's failure, the PostgreSQL roles, the Valkey eviction policy, and that no plane log,
readiness answer, or smoke output holds a password, the database name, a login name, or a secret reference name.

Exit codes: 0 every assertion passed, 1 an assertion failed, 2 prerequisite missing, 64 usage error.
EOF
}

. "$(dirname "$0")/lib/common.sh"
. "$(dirname "$0")/lib/readiness.sh"

for argument in "$@"; do
  case "$argument" in
    --help) usage; exit 0 ;;
    *) isk_fail 64 "usage: unknown argument; see --help" ;;
  esac
done

isk_root
isk_project
isk_prereqs

failures=0
report=""
captured=""

# Records and writes one failed check.
fail_check() {
  failures=$((failures + 1))
  report="$report$1
"
  isk_info "smoke: $1"
}

host_dll() {
  case "$1" in
    data-plane) printf 'Iskender.Gateway.Host.dll' ;;
    control-plane) printf 'Iskender.ControlPlane.Host.dll' ;;
  esac
}

# 1. Services running.
running="$(isk_compose ps --services --status running 2>/dev/null || true)"
# A here-string, not a pipe: grep -q exits on the first match, and under pipefail the writer's broken pipe would fail the check.
is_running() {
  grep -qx -- "$1" <<<"$running"
}
for service in postgres valkey data-plane control-plane; do
  is_running "$service" || fail_check "$service: not running"
done

for plane in data-plane control-plane; do
  is_running "$plane" || continue

  # 2. Liveness, through the image's own command.
  isk_compose exec -T "$plane" dotnet "/app/$(host_dll "$plane")" health live >/dev/null 2>&1 ||
    fail_check "$plane: health live failed"

  # 3. Readiness, read through the probe service, waiting while lifecycle is starting.
  deadline=$(($(date +%s) + 60))
  while :; do
    answer="$(isk_compose --profile probe run --rm --no-TTY probe --silent --show-error --max-time 15 --output - --write-out '\n%{http_code}' "http://$plane:8081/health/ready" 2>/dev/null || true)"
    status="$(printf '%s\n' "$answer" | sed -n '$p')"
    body="$(printf '%s\n' "$answer" | sed '$d')"
    if ! isk_ready_starting "$body" || [ "$(date +%s)" -ge "$deadline" ]; then
      break
    fi
    sleep 1
  done
  captured="$captured$body
"
  if ! differences="$(isk_ready_compare "$status" "$body")"; then
    while IFS= read -r line; do
      [ -n "$line" ] && fail_check "$plane readiness: $line"
    done <<EOF_DIFFERENCES
$differences
EOF_DIFFERENCES
  fi
done

# 4. The migration job cannot connect before ISK-0021.
if is_running postgres; then
  migration_status=0
  migration="$(bash local/scripts/migrate.sh status 2>&1)" || migration_status=$?
  captured="$captured$migration
"
  if [ "$migration_status" != 5 ]; then
    fail_check "migrate status: expected exit code 5, found $migration_status"
  fi
  events="$(printf '%s\n' "$migration" | grep -F '"EventId":7006' || true)"
  if [ "$(printf '%s' "$events" | grep -c '')" != 1 ] ||
    ! printf '%s\n' "$events" | grep -Fq '"Operation":"status"' ||
    ! printf '%s\n' "$events" | grep -Fq '"Code":"persistence.unavailable"'; then
    fail_check "migrate status: expected one event 7006 naming status and persistence.unavailable"
  fi
fi

# 5. PostgreSQL holds exactly the provisioned roles and grants, and nothing of Iskender's.
sql() {
  isk_compose exec -T postgres psql -U postgres -d iskender_local -At -c "$1" 2>/dev/null
}
if is_running postgres; then
  roles="$(sql "SELECT rolname || ':' || rolcanlogin || ':' || rolsuper || ':' || rolbypassrls || ':' || rolcreatedb || ':' || rolcreaterole FROM pg_roles WHERE rolname <> 'postgres' AND rolname NOT LIKE 'pg\\_%' ORDER BY rolname" || true)"
  [ "$roles" = "iskender_local_migrator:true:false:false:false:false
iskender_local_runtime:true:false:false:false:false
iskender_runtime:false:false:false:false:false" ] || fail_check "postgres: the roles or their attributes differ from the contract"
  members="$(sql "SELECT r.rolname || '>' || g.rolname FROM pg_auth_members a JOIN pg_roles r ON r.oid = a.member JOIN pg_roles g ON g.oid = a.roleid WHERE r.rolname <> 'postgres' AND r.rolname NOT LIKE 'pg\\_%' ORDER BY 1" || true)"
  [ "$members" = "iskender_local_runtime>iskender_runtime" ] || fail_check "postgres: the role memberships differ from the contract"
  grants="$(sql "SELECT string_agg(rolname || ':' || has_database_privilege(rolname, 'iskender_local', 'CONNECT') || ':' || has_database_privilege(rolname, 'iskender_local', 'CREATE'), ',' ORDER BY rolname) FROM pg_roles WHERE rolname IN ('iskender_local_runtime', 'iskender_local_migrator')" || true)"
  [ "$grants" = "iskender_local_migrator:true:true,iskender_local_runtime:true:false" ] || fail_check "postgres: the database grants differ from the contract"
  schemas="$(sql "SELECT count(*) FROM pg_namespace WHERE nspname NOT IN ('pg_catalog', 'information_schema', 'pg_toast', 'public') AND nspname NOT LIKE 'pg\\_temp\\_%' AND nspname NOT LIKE 'pg\\_toast\\_temp\\_%'" || true)"
  [ "$schemas" = 0 ] || fail_check "postgres: a schema exists outside pg_catalog, information_schema, pg_toast, and public"
  relations="$(sql "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public'" || true)"
  [ "$relations" = 0 ] || fail_check "postgres: a relation exists in public"
fi

# 6. Valkey never evicts.
if is_running valkey; then
  policy="$(isk_compose exec -T valkey valkey-cli CONFIG GET maxmemory-policy 2>/dev/null | sed -n '2p' | tr -d '\r' || true)"
  [ "$policy" = noeviction ] || fail_check "valkey: maxmemory-policy is not noeviction"
fi

# 7. No value in any plane log, readiness answer, or smoke output.
secrets="$(isk_secrets_file)"
document=local/config/control-plane/iskender.yaml
passwords=""
if [ -f "$secrets" ]; then
  passwords="$(sed -n 's/^[A-Z_]*_PASSWORD=//p' "$secrets")"
fi
database="$(sed -n 's/^ *database: *//p' "$document")"
logins="$(sed -n 's/^ *username: *//p' "$document")"
references="$(sed -n 's/^ *secretRef: *//p' "$document")"
logs="$(isk_compose logs --no-color --no-log-prefix data-plane control-plane 2>/dev/null || true)"
corpus="$logs
$captured
$report"
search() {
  [ -n "$2" ] || return 0
  if printf '%s\n' "$corpus" | grep -Fq -f <(printf '%s\n' "$2"); then
    fail_check "values: a $1 was found in a plane log, readiness answer, or smoke output"
  fi
}
search password "$passwords"
search "database name" "$database"
search "login name" "$logins"
search "secret reference" "$references"
passwords=""

if [ "$failures" -gt 0 ]; then
  isk_fail 1 "smoke failed: $failures check(s)"
fi
isk_info "smoke passed for $project"
