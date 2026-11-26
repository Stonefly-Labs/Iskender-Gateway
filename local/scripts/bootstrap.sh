#!/usr/bin/env bash
# ISK-0010 developer topology: runs one bootstrap operation through the control-plane image's bootstrap command, as a
# one-shot job, and exits with its exit code (local-environment contract, version 3, Scripts and Exit codes). The token
# the command issues reaches only the developer's terminal: this script writes it to no file and holds none.
set -euo pipefail

usage() {
  cat <<'EOF_USAGE'
Usage: local/scripts/bootstrap.sh issue | recover

Runs the control-plane image's bootstrap command once, with the control plane's documents, against the topology's
postgres. issue prints a bootstrap token for the first operator; recover prints a recovery token on an initialized
installation. The token is printed once, to standard output, and the job's logs go to standard error. Before ISK-0021
every operation exits 5, because no credential provider resolves the runtime login's password.

Exit codes: the bootstrap command's own code, or 2 (prerequisite missing, or no control-plane image: run start.sh) or
64 (usage error) before the job starts.
EOF_USAGE
}

. "$(dirname "$0")/lib/common.sh"

[ "$#" -eq 1 ] || { usage >&2; exit 64; }
case "$1" in
  --help) usage; exit 0 ;;
  issue | recover) operation="$1" ;;
  *) usage >&2; exit 64 ;;
esac

isk_root
isk_project
isk_prereqs

docker image inspect "$project/control-plane:local" >/dev/null 2>&1 ||
  isk_fail 2 "no control-plane image for $project; run local/scripts/start.sh first"

# --no-deps: the job never starts or recreates postgres. --no-TTY keeps the token on standard output apart from the
# job's logs on standard error.
status=0
isk_compose --profile bootstrap run --rm --no-deps --no-TTY bootstrap bootstrap "$operation" || status=$?
exit "$status"
