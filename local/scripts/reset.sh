#!/usr/bin/env bash
# ISK-0008 developer topology: removes the project's containers, network, volumes, and password file
# (local-environment contract, Scripts and Volumes; research R12). It removes no image, and nothing of another project.
set -euo pipefail

usage() {
  cat <<'EOF'
Usage: local/scripts/reset.sh [--yes]

Removes the local topology's containers, network, and volumes, and its password file, so the next start is a first
start. Asks for confirmation in a terminal; --yes skips it, and is required in a non-interactive shell.

Exit codes: 0 success or declined, 1 Compose failed, 2 prerequisite missing, 64 usage error.
EOF
}

. "$(dirname "$0")/lib/common.sh"

confirmed=false
for argument in "$@"; do
  case "$argument" in
    --help) usage; exit 0 ;;
    --yes) confirmed=true ;;
    *) isk_fail 64 "usage: unknown argument; see --help" ;;
  esac
done

isk_root
isk_project
isk_prereqs

if [ "$confirmed" != true ]; then
  if [ ! -t 0 ]; then
    isk_fail 64 "usage: standard input is not a terminal, so pass --yes to reset; nothing was removed"
  fi
  printf 'Remove all data of %s? [y/N] ' "$project" >&2
  answer=""
  read -r answer || answer=""
  case "$answer" in
    y | Y) ;;
    *) isk_info "reset declined; nothing was removed"; exit 0 ;;
  esac
fi

isk_compose --profile migrate --profile probe down --volumes --remove-orphans --timeout 40 || isk_fail 1 "docker compose down failed"

secrets="$(isk_secrets_file)"
if [ -e "$secrets" ]; then
  rm -f "$secrets"
fi
isk_info "reset complete"
