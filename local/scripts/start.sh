#!/usr/bin/env bash
# ISK-0008 developer topology: checks the prerequisites and ports, writes the password file when the contract allows
# it, builds both plane images, and starts the topology (local-environment contract, Scripts, Passwords, Ports, Modes;
# research R2, R3, R8, R10, R12). It prints no password and runs no migration.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: local/scripts/start.sh [--dependencies]

Builds both plane images from this checkout and starts postgres, valkey, data-plane, and control-plane, waiting up to
180 seconds for each to be healthy. --dependencies starts only postgres and valkey, published on 127.0.0.1.

Variables: ISKENDER_LOCAL_PROJECT (default iskender-local), ISKENDER_LOCAL_DATA_PLANE_PORT (18080),
ISKENDER_LOCAL_POSTGRES_PORT (15432), ISKENDER_LOCAL_VALKEY_PORT (16379).

Exit codes: 0 every started service healthy, 1 a build or Compose step failed or a service was not healthy,
2 prerequisite missing or unusable, 64 usage error.
EOF
}

. "$(dirname "$0")/lib/common.sh"

dependencies=false
for argument in "$@"; do
  case "$argument" in
    --help) usage; exit 0 ;;
    --dependencies) dependencies=true ;;
    *) isk_fail 64 "usage: unknown argument; see --help" ;;
  esac
done

if [ "$dependencies" = true ]; then
  isk_fail 64 "usage: --dependencies is not yet supported"
fi

isk_root
isk_project
isk_ports
isk_prereqs

# The password file (contract, Passwords rule 2).
secrets="$(isk_secrets_file)"
names='POSTGRES_PASSWORD ISKENDER_LOCAL_RUNTIME_PASSWORD ISKENDER_LOCAL_MIGRATION_PASSWORD'
if [ ! -e "$secrets" ]; then
  if isk_volume_exists postgres-data; then
    isk_fail 2 "the postgres-data volume exists but its password file is missing; run local/scripts/reset.sh, then start again"
  fi
  umask 077
  mkdir -p local/.secrets
  chmod 700 local/.secrets
  pending="local/.secrets/.$project.env.tmp"
  rm -f "$pending"
  for name in $names; do
    # A bounded read, so nothing depends on SIGPIPE, which a caller may have set to be ignored.
    value="$(set +o pipefail; head -c 4096 /dev/urandom | LC_ALL=C tr -dc 'A-Za-z0-9' | head -c 32)"
    printf '%s\n' "$value" | grep -Eq '^[A-Za-z0-9]{32}$' || isk_fail 1 "password generation failed"
    printf '%s=%s\n' "$name" "$value" >> "$pending"
  done
  value=""
  chmod 600 "$pending"
  mv -f "$pending" "$secrets"
  isk_info "wrote a new password file for $project"
else
  valid=true
  [ "$(grep -c '' "$secrets")" = 3 ] || valid=false
  for name in $names; do
    [ "$(grep -Ec "^$name=[A-Za-z0-9]{32}\$" "$secrets")" = 1 ] || valid=false
  done
  if [ "$valid" != true ]; then
    isk_fail 2 "the password file does not hold exactly its three generated values; run local/scripts/reset.sh, then start again"
  fi
fi
chmod 700 local/.secrets
chmod 600 "$secrets"

# Ports (contract, Ports rule 3).
isk_port_free ISKENDER_LOCAL_DATA_PLANE_PORT data-plane 8080

# Images (contract, Services rule 2).
revision="$(git rev-parse HEAD)"
docker build -f src/Iskender.Gateway.Host/Dockerfile -t "$project/data-plane:local" --build-arg REVISION="$revision" . >&2 ||
  isk_fail 1 "the data-plane image build failed"
docker build -f src/Iskender.ControlPlane.Host/Dockerfile -t "$project/control-plane:local" --build-arg REVISION="$revision" . >&2 ||
  isk_fail 1 "the control-plane image build failed"

# Start. Only compose.yaml, so the dependencies mode's ports never apply (contract, Project name rule 3).
if output="$(isk_compose_default up --detach --wait --wait-timeout 180 postgres valkey data-plane control-plane 2>&1)"; then
  printf '%s\n' "$output" >&2
  isk_info "started $project: postgres, valkey, data-plane, and control-plane are healthy"
  exit 0
fi
printf '%s\n' "$output" >&2
isk_fail_services "ISKENDER_LOCAL_DATA_PLANE_PORT" "$output"
