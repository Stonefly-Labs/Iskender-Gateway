#!/usr/bin/env bash
# ISK-0008 developer topology: runs one migration operation through the control-plane image's migrate command, as a
# one-shot job, and exits with its exit code (local-environment contract, Scripts and Exit codes; research R11).
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: local/scripts/migrate.sh [apply | reverse [--steps <n>] [--confirm-data-loss] | status]

Runs the control-plane image's migrate command once, with the control plane's documents, against the topology's
postgres. The operation is status when none is given. Before ISK-0021 every operation exits 5, because no credential
provider resolves the migration login's password.

Exit codes: the migrate command's own code, or 2 (prerequisite missing, or no control-plane image: run start.sh) or
64 (usage error) before the job starts.
EOF
}

. "$(dirname "$0")/lib/common.sh"

operation=status
if [ "$#" -gt 0 ]; then
  operation="$1"
  shift
fi
options=()
case "$operation" in
  --help) usage; exit 0 ;;
  apply | status)
    [ "$#" -eq 0 ] || isk_fail 64 "usage: $operation takes no option; see --help"
    ;;
  reverse)
    while [ "$#" -gt 0 ]; do
      case "$1" in
        --steps)
          [ "$#" -ge 2 ] || isk_fail 64 "usage: --steps needs a positive integer; see --help"
          case "$2" in
            '' | *[!0-9]* | 0*) isk_fail 64 "usage: --steps needs a positive integer; see --help" ;;
          esac
          options+=(--steps "$2")
          shift 2
          ;;
        --confirm-data-loss)
          options+=(--confirm-data-loss)
          shift
          ;;
        *) isk_fail 64 "usage: unknown reverse option; see --help" ;;
      esac
    done
    ;;
  *) isk_fail 64 "usage: unknown operation; see --help" ;;
esac

isk_root
isk_project
isk_prereqs

docker image inspect "$project/control-plane:local" >/dev/null 2>&1 ||
  isk_fail 2 "no control-plane image for $project; run local/scripts/start.sh first"

# --no-deps: the job never starts or recreates postgres (research R2, R11).
status=0
if [ "${#options[@]}" -gt 0 ]; then
  isk_compose --profile migrate run --rm --no-deps --no-TTY migrate migrate "$operation" "${options[@]}" || status=$?
else
  isk_compose --profile migrate run --rm --no-deps --no-TTY migrate migrate "$operation" || status=$?
fi
exit "$status"
