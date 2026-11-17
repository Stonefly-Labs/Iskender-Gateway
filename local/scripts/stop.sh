#!/usr/bin/env bash
# ISK-0008 developer topology: stops every service of the project, keeping its volumes and password file
# (local-environment contract, Scripts).
set -euo pipefail

usage() {
  cat <<'USAGE'
Usage: local/scripts/stop.sh

Stops the local topology. Its volumes and password file are kept, so the next start reuses them.

Exit codes: 0 success, 1 Compose failed, 2 prerequisite missing, 64 usage error.
USAGE
}

. "$(dirname "$0")/lib/common.sh"

for argument in "$@"; do
  case "$argument" in
    --help) usage; exit 0 ;;
    *) isk_fail 64 "usage: unknown argument; see --help" ;;
  esac
done

isk_root
isk_project
isk_prereqs

isk_compose stop --timeout 40 || isk_fail 1 "docker compose stop failed"
isk_info "stopped $project; its data is kept"
