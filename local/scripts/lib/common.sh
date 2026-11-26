# ISK-0008 developer topology: functions sourced by every script under local/scripts (local-environment contract,
# Scripts, Project name, Ports; research R9, R10, R12). Sourcing it defines functions and nothing else. Written for
# bash 3.2. Messages go to standard error and name a check, service, variable, or prerequisite, never a value.

# Writes a message to standard error.
isk_info() {
  printf 'iskender-local: %s\n' "$1" >&2
}

# Writes a message to standard error and exits with the code.
isk_fail() {
  printf 'iskender-local: %s\n' "$2" >&2
  exit "$1"
}

# Finds the repository root from the calling script's own path, checks that it is an Iskender checkout, and changes to
# it. Exits 2 outside a checkout.
isk_root() {
  local root
  root="$(git -C "$(dirname "$0")" rev-parse --show-toplevel 2>/dev/null)" || isk_fail 2 "outside a checkout: git rev-parse --show-toplevel failed"
  if [ -z "$root" ] || [ ! -f "$root/Iskender.slnx" ] || [ ! -f "$root/local/compose.yaml" ]; then
    isk_fail 2 "outside a checkout: the root has no Iskender.slnx and local/compose.yaml"
  fi
  cd "$root"
}

# Sets and exports ISKENDER_LOCAL_PROJECT, iskender-local when unset or empty. Exits 64 for any other name than
# iskender-local or iskender-local- followed by 1 to 24 characters of a-z and 0-9.
isk_project() {
  project="${ISKENDER_LOCAL_PROJECT:-iskender-local}"
  if ! printf '%s\n' "$project" | grep -Eq '^iskender-local(-[a-z0-9]{1,24})?$' || [ "$(printf '%s\n' "$project" | grep -c '')" != 1 ]; then
    isk_fail 64 "invalid ISKENDER_LOCAL_PROJECT: it must be iskender-local or iskender-local- and 1 to 24 of a-z and 0-9"
  fi
  ISKENDER_LOCAL_PROJECT="$project"
  export ISKENDER_LOCAL_PROJECT
}

# isk_port <variable> <default>: sets and exports the variable, the default when unset or empty. Exits 64 unless the
# value is an integer from 1024 to 65535.
isk_port() {
  local name="$1" value
  eval "value=\"\${$name:-$2}\""
  case "$value" in
    '' | *[!0-9]*) isk_fail 64 "invalid $name: it must be an integer from 1024 to 65535" ;;
  esac
  if [ "${#value}" -gt 5 ] || [ "$value" -lt 1024 ] || [ "$value" -gt 65535 ]; then
    isk_fail 64 "invalid $name: it must be an integer from 1024 to 65535"
  fi
  eval "$name=\"\$value\""
  export "$name"
}

# Checks and exports the four port variables.
isk_ports() {
  isk_port ISKENDER_LOCAL_DATA_PLANE_PORT 18080
  isk_port ISKENDER_LOCAL_CONTROL_PLANE_PORT 18090
  isk_port ISKENDER_LOCAL_POSTGRES_PORT 15432
  isk_port ISKENDER_LOCAL_VALKEY_PORT 16379
}

# Exits 2 naming the first prerequisite that fails: git and docker on PATH, the container runtime answering, and
# Docker Compose 2.24.0 or later, compared by major, minor, and patch.
isk_prereqs() {
  command -v git >/dev/null 2>&1 || isk_fail 2 "prerequisite missing: git is not on PATH"
  command -v docker >/dev/null 2>&1 || isk_fail 2 "prerequisite missing: docker is not on PATH"
  docker version >/dev/null 2>&1 || isk_fail 2 "prerequisite unusable: docker version failed, so the container runtime is not running"
  local version major minor
  version="$(docker compose version --short 2>/dev/null)" || isk_fail 2 "prerequisite missing: docker compose version failed"
  version="$(printf '%s\n' "$version" | head -n 1 | sed -e 's/^v//' -e 's/[^0-9.].*$//')"
  major="$(printf '%s\n' "$version" | sed -n 's/^\([0-9][0-9]*\).*$/\1/p')"
  minor="$(printf '%s\n' "$version" | sed -n 's/^[0-9][0-9]*\.\([0-9][0-9]*\).*$/\1/p')"
  if [ -z "$major" ] || [ -z "$minor" ]; then
    isk_fail 2 "prerequisite unusable: the Docker Compose version could not be read; 2.24.0 or later is required"
  fi
  # Any patch of 2.24 is at least 2.24.0, so only the major and minor decide.
  if [ "$major" -lt 2 ] || { [ "$major" -eq 2 ] && [ "$minor" -lt 24 ]; }; then
    isk_fail 2 "prerequisite unusable: Docker Compose $version is older than 2.24.0"
  fi
}

# Runs docker compose for the project with both Compose files.
isk_compose() {
  docker compose --project-name "$project" -f local/compose.yaml -f local/compose.dependencies.yaml "$@"
}

# Runs docker compose for the project with only compose.yaml, for the default mode's up, so that the dependencies
# mode's ports never apply in the default mode (contract, Project name rule 3).
isk_compose_default() {
  docker compose --project-name "$project" -f local/compose.yaml "$@"
}

# Prints the project's password file path.
isk_secrets_file() {
  printf 'local/.secrets/%s.env\n' "$project"
}

# isk_volume_exists <name>: succeeds when the project's volume exists.
isk_volume_exists() {
  docker volume inspect "${project}_$1" >/dev/null 2>&1
}

# isk_port_free <variable> <service> <container port>: exits 2, naming the variable, when the port it holds answers on
# 127.0.0.1, unless the project's own service already publishes it (contract, Ports rule 3).
isk_port_free() {
  local name="$1" service="$2" container_port="$3" port published
  eval "port=\"\$$name\""
  published="$(isk_compose port "$service" "$container_port" 2>/dev/null | head -n 1)" || published=""
  case "$published" in
    127.0.0.1:"$port") return 0 ;;
  esac
  if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
    isk_fail 2 "port in use: the port in $name already answers on 127.0.0.1; set $name to a free port"
  fi
}

# isk_fail_services <port variables> <compose output>: after a failed up, names each service that is not running or
# not healthy, with its exit code, names the port variables when a bind failed, and exits 1.
isk_fail_services() {
  local variables="$1" output="$2" service state health code
  while IFS='|' read -r service state health code; do
    [ -n "$service" ] || continue
    if [ "$state" != running ]; then
      isk_info "service $service is $state, exit code $code"
    elif [ -n "$health" ] && [ "$health" != healthy ]; then
      isk_info "service $service is running but $health"
    fi
  done <<EOF_SERVICES
$(isk_compose ps --all --format '{{.Service}}|{{.State}}|{{.Health}}|{{.ExitCode}}' 2>/dev/null || true)
EOF_SERVICES
  if printf '%s\n' "$output" | grep -Eiq 'port is already allocated|address already in use|bind'; then
    isk_info "a port could not be bound; check $variables"
  fi
  isk_fail 1 "the topology did not become healthy within 180 seconds"
}
