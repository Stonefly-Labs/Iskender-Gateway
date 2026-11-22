#!/usr/bin/env bash
# ISK-0008 developer topology: asserts the running default topology's state before ISK-0021 (local-environment
# contract, Smoke assertions 1 to 7; research R13), with ISK-0009's worker (contract version 2, assertions 1 to 3
# and 7), and ISK-0010's admin surface and bootstrap job (contract version 3, assertions 7 to 10). It runs every check,
# names each failure, and exits 1 if any failed.
# The values it searches for, and the bootstrap token it generates, are held only in shell variables and never printed
# or passed as an argument.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: local/scripts/smoke.sh

Asserts the running local topology: every service running, each plane and the worker live, each one's exact
readiness answer before ISK-0021, the migration job's failure, the PostgreSQL roles, the Valkey eviction policy, the
control plane's admin routes and the bootstrap job failing before ISK-0021, and that no plane or worker log,
readiness answer, or smoke output holds a password, the database name, a login name, a secret reference name, or the
bootstrap token it generates.

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
    control-plane | worker) printf 'Iskender.ControlPlane.Host.dll' ;;
  esac
}

# 1. Services running.
running="$(isk_compose ps --services --status running 2>/dev/null || true)"
# A here-string, not a pipe: grep -q exits on the first match, and under pipefail the writer's broken pipe would fail the check.
is_running() {
  grep -qx -- "$1" <<<"$running"
}
for service in postgres valkey data-plane control-plane worker; do
  is_running "$service" || fail_check "$service: not running"
done

for plane in data-plane control-plane worker; do
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
  expected_for=""
  [ "$plane" = worker ] && expected_for=worker
  if ! differences="$(isk_ready_compare "$status" "$body" "$expected_for")"; then
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

# 8 and 9. The admin routes answer 503 with operator.state_unavailable before ISK-0021. The bootstrap token is a
# well-formed one generated at each run, never a fixed value, and reaches probe only on its standard input.
# Each reader takes a fixed count of bytes, since a caller may ignore SIGPIPE and an unbounded reader would then never end.
# The ID's 26 characters come from 26 random bytes, the first reduced to 0 to 7 and the rest to the 32-character
# alphabet, which divides 256, so each character is uniform.
# Lowercase Crockford base32, in parts so that no tracked line holds a long run that reads like a secret.
alphabet="0123456789""abcdefghjk""mnpqrstvwxyz"
token_id=""
for byte in $(head -c 26 /dev/urandom | od -An -v -tu1); do
  if [ -z "$token_id" ]; then
    token_id="${alphabet:$((byte % 8)):1}"
  else
    token_id="$token_id${alphabet:$((byte % 32)):1}"
  fi
done
token_secret="$(head -c 32 /dev/urandom | base64 | tr -d '\n=' | tr '+/' '-_')"
token="iskb_${token_id}_${token_secret}"
token_id=""
token_secret=""
admin_code() {
  printf '%s\n' "$1" | sed '$d' | grep -o '"code":"[^"]*"' | sed -n '1s/^"code":"\(.*\)"$/\1/p'
}
if is_running control-plane; then
  # printf is a builtin, so the token is in no process's arguments; probe reads the body from its standard input.
  answer="$(printf '{"token":"%s"}' "$token" | isk_compose --profile probe run --rm --no-TTY probe --silent --show-error --max-time 15 --output - --write-out '\n%{http_code}' --header 'Content-Type: application/json' --data-binary @- "http://control-plane:8080/admin/v1/bootstrap" 2>/dev/null || true)"
  captured="$captured$answer
"
  if [ "$(printf '%s\n' "$answer" | sed -n '$p')" != 503 ] || [ "$(admin_code "$answer")" != operator.state_unavailable ]; then
    fail_check "control-plane: POST /admin/v1/bootstrap did not answer 503 with operator.state_unavailable"
  fi
  answer="$(isk_compose --profile probe run --rm --no-TTY probe --silent --show-error --max-time 15 --output - --write-out '\n%{http_code}' "http://control-plane:8080/admin/v1/installation" 2>/dev/null || true)"
  captured="$captured$answer
"
  if [ "$(printf '%s\n' "$answer" | sed -n '$p')" != 503 ] || [ "$(admin_code "$answer")" != operator.state_unavailable ]; then
    fail_check "control-plane: GET /admin/v1/installation did not answer 503 with operator.state_unavailable"
  fi
fi

# 10. The bootstrap job cannot connect before ISK-0021, prints nothing, and writes one event 7008 to standard error.
if is_running postgres; then
  bootstrap_errors="$(mktemp)"
  bootstrap_status=0
  bootstrap_output="$(bash local/scripts/bootstrap.sh issue 2>"$bootstrap_errors")" || bootstrap_status=$?
  bootstrap_log="$(cat "$bootstrap_errors")"
  rm -f "$bootstrap_errors"
  captured="$captured$bootstrap_output
$bootstrap_log
"
  if [ "$bootstrap_status" != 5 ]; then
    fail_check "bootstrap issue: expected exit code 5, found $bootstrap_status"
  fi
  [ -z "$bootstrap_output" ] || fail_check "bootstrap issue: wrote to standard output"
  events="$(printf '%s\n' "$bootstrap_log" | grep -F '"EventId":7008' || true)"
  if [ "$(printf '%s' "$events" | grep -c '')" != 1 ] ||
    ! printf '%s\n' "$events" | grep -Fq '"Operation":"issue"' ||
    ! printf '%s\n' "$events" | grep -Fq '"Code":"persistence.unavailable"'; then
    fail_check "bootstrap issue: expected one event 7008 naming issue and persistence.unavailable"
  fi
fi

# 7. No value in any plane or worker log, readiness answer, or smoke output.
secrets="$(isk_secrets_file)"
document=local/config/control-plane/iskender.yaml
passwords=""
if [ -f "$secrets" ]; then
  passwords="$(sed -n 's/^[A-Z_]*_PASSWORD=//p' "$secrets")"
fi
database="$(sed -n 's/^ *database: *//p' "$document")"
logins="$(sed -n 's/^ *username: *//p' "$document")"
references="$(sed -n 's/^ *secretRef: *//p' "$document")"
logs="$(isk_compose logs --no-color --no-log-prefix data-plane control-plane worker 2>/dev/null || true)"
corpus="$logs
$captured
$report"
search() {
  [ -n "$2" ] || return 0
  if printf '%s\n' "$corpus" | grep -Fq -f <(printf '%s\n' "$2"); then
    fail_check "values: a $1 was found in a plane or worker log, readiness answer, or smoke output"
  fi
}
search password "$passwords"
search "database name" "$database"
search "login name" "$logins"
search "secret reference" "$references"
search "bootstrap token" "$token"
passwords=""
token=""

if [ "$failures" -gt 0 ]; then
  isk_fail 1 "smoke failed: $failures check(s)"
fi
isk_info "smoke passed for $project"
