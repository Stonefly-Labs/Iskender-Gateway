#!/usr/bin/env bash
# Validates the grounding manifest, the master inventory, and relative Markdown links.
# Read-only and fail-closed: any failure prints the rule, the offending value and the owner
# action, and the script exits non-zero. It never guesses a path.
#
# Usage: check-grounding.sh [repo-root]
# Rules V1-V8: specs/0000-repo-bootstrap/contracts/grounding-manifest.schema.md
set -u

failures=0
owner_action="Fix .iskender/grounding.yaml or the file it names (owner: Spec Kit maintainer), then re-run this check."

fail() {
  printf 'FAIL %s: %s\n  Owner action: %s\n' "$1" "$2" "${3:-$owner_action}"
  failures=$((failures + 1))
}

warn() {
  printf 'WARN %s: %s\n' "$1" "$2"
}

if [ $# -ge 1 ]; then
  root=$(cd "$1" 2>/dev/null && pwd -P) || {
    fail V1 "repository root '$1' does not exist"
    exit 1
  }
else
  root=$(git rev-parse --show-toplevel 2>/dev/null) || {
    fail V1 "not inside a Git repository, so the repository root cannot be resolved"
    exit 1
  }
  root=$(cd "$root" && pwd -P)
fi

manifest="$root/.iskender/grounding.yaml"

if [ ! -f "$manifest" ]; then
  fail V1 "manifest is missing: $manifest" "Restore .iskender/grounding.yaml from version control (owner: Spec Kit maintainer)."
  exit 1
fi

# Parses the small YAML subset the manifest uses into "dotted.key<TAB>raw value" lines. Anything
# else under a known section is a parse error. Unknown top-level sections may hold any nested YAML.
parsed=$(tr -d '\r' <"$manifest" | awk '
  BEGIN {
    n = split("grounding.root grounding.inventory grounding.constitutionPrompt grounding.bootstrapBrief grounding.decisionRegister speckit.constitution speckit.specificationsRoot workflow.initialSpec", names, " ")
    for (i = 1; i <= n; i++) required[names[i]] = 1
  }
  function error(message) {
    printf "line %d: %s\n", NR, message > "/dev/stderr"
    bad = 1
  }
  function clean(value) {
    sub(/^[ \t]+/, "", value)
    if (value !~ /^["\047]/) sub(/[ \t]+#.*$/, "", value)
    sub(/[ \t]+$/, "", value)
    return value
  }
  function check_value(value) {
    if (value ~ /^"/ && value !~ /^"[^"]*"$/) error("unterminated double quote")
    if (value ~ /^\047/ && value !~ /^\047[^\047]*\047$/) error("unterminated single quote")
    if (value ~ /^\[/ && value !~ /\]$/) error("unterminated flow sequence")
    if (value ~ /^\{/ && value !~ /\}$/) error("unterminated flow mapping")
  }
  function record(key, value) {
    if (key in seen) error("duplicate key " key)
    seen[key] = 1
    check_value(value)
    printf "%s\t%s\n", key, value
  }
  /^[ \t]*(#.*)?$/ { next }
  /^---[ \t]*$/ && !started { started = 1; next }
  /^\t/ || /^ +\t/ { error("tabs may not indent YAML"); next }
  /^[A-Za-z_][A-Za-z0-9_.-]*:([ \t].*)?$/ {
    started = 1
    key = $0; sub(/:.*/, "", key)
    value = $0; sub(/^[^:]*:/, "", value); value = clean(value)
    section = key
    known = (key == "grounding" || key == "speckit" || key == "workflow" || key == "schemaVersion")
    parent = ""
    record(key, value)
    next
  }
  /^  [A-Za-z_][A-Za-z0-9_.-]*:([ \t].*)?$/ {
    if (section == "") { error("nested key with no parent section"); next }
    key = $0; sub(/^  /, "", key); sub(/:.*/, "", key)
    value = $0; sub(/^  [^:]*:/, "", value); value = clean(value)
    parent = section "." key
    record(parent, value)
    next
  }
  # Deeper YAML under an unknown key is tolerated (V8). A known key holds one plain value, so
  # anything deeper under it stays a parse error.
  known && parent != "" && !(parent in required) && /^    / {
    if ($0 ~ /^ +[A-Za-z_][A-Za-z0-9_.-]*:([ \t].*)?$/) {
      key = $0; sub(/^ +/, "", key); sub(/:.*/, "", key)
      printf "%s.%s\t\n", parent, key
    }
    next
  }
  known { error("cannot parse: " $0); next }
  section != "" && /^ / { next }
  { error("cannot parse: " $0) }
  END { exit bad }
' 2>"${TMPDIR:-/tmp}/grounding-parse.$$")
parse_status=$?
parse_errors=$(cat "${TMPDIR:-/tmp}/grounding-parse.$$" 2>/dev/null)
rm -f "${TMPDIR:-/tmp}/grounding-parse.$$"

if [ "$parse_status" -ne 0 ]; then
  fail V1 "$manifest does not parse: $parse_errors"
  exit 1
fi

if command -v yq >/dev/null 2>&1 && yq --version 2>/dev/null | grep -q mikefarah; then
  if ! yq_error=$(yq '.' "$manifest" 2>&1 >/dev/null); then
    fail V1 "$manifest does not parse: $yq_error"
    exit 1
  fi
fi

raw() {
  printf '%s\n' "$parsed" | awk -F '\t' -v key="$1" '$1 == key { print $2; exit }'
}

value() {
  raw "$1" | sed -e "s/^[\"']//" -e "s/[\"']\$//"
}

# V2
schema_version=$(raw schemaVersion)
case "$schema_version" in
  1) ;;
  *) fail V2 "schemaVersion is '${schema_version:-<absent>}'; supported majors: 1 (an integer)" ;;
esac

# V3
for key in grounding.root grounding.inventory grounding.constitutionPrompt grounding.bootstrapBrief \
  grounding.decisionRegister speckit.constitution speckit.specificationsRoot workflow.initialSpec; do
  if [ -z "$(value "$key")" ]; then
    fail V3 "required key '$key' is missing or empty"
  fi
done

# V8
printf '%s\n' "$parsed" | cut -f1 | while IFS= read -r key; do
  case "$key" in
    schemaVersion | grounding | speckit | workflow | grounding.root | grounding.inventory | \
      grounding.constitutionPrompt | grounding.bootstrapBrief | grounding.decisionRegister | \
      speckit.constitution | speckit.specificationsRoot | workflow.initialSpec) ;;
    *) warn V8 "unknown key '$key' is ignored" ;;
  esac
done

# V7: rejects traversal and absolute paths in the raw value.
is_unsafe() {
  case "$1" in
    /* | \\* | [A-Za-z]:*) return 0 ;;
  esac
  printf '%s\n' "$1" | tr '\\' '/' | tr '/' '\n' | grep -qx '\.\.'
}

# V7: the real path, after full symlink expansion, must stay under the given directory.
is_under() {
  local real
  real=$(readlink -f "$1" 2>/dev/null) || return 1
  [ -n "$real" ] || return 1
  case "$real" in
    "$2" | "$2"/*) return 0 ;;
  esac
  return 1
}

grounding_root=$(value grounding.root)
grounding_dir=""
if [ -n "$grounding_root" ]; then
  if is_unsafe "$grounding_root"; then
    fail V7 "grounding.root '$grounding_root' is unsafe (absolute path or '..' traversal)"
  elif [ ! -d "$root/$grounding_root" ]; then
    fail V4 "grounding.root '$grounding_root' is not an existing directory under the repository root"
  elif ! is_under "$root/$grounding_root" "$root"; then
    fail V7 "grounding.root '$grounding_root' is unsafe: its real path escapes the repository root"
  else
    grounding_dir=$(readlink -f "$root/$grounding_root")
  fi
fi

# V5
for key in grounding.inventory grounding.constitutionPrompt grounding.bootstrapBrief grounding.decisionRegister; do
  path=$(value "$key")
  [ -n "$path" ] || continue
  if is_unsafe "$path"; then
    fail V7 "$key '$path' is unsafe (absolute path or '..' traversal)"
    continue
  fi
  [ -n "$grounding_dir" ] || continue
  target="$root/$grounding_root/$path"
  if [ ! -e "$target" ] && [ ! -L "$target" ]; then
    fail V5 "$key '$path' is absent under grounding.root"
  elif ! is_under "$target" "$root"; then
    fail V7 "$key '$path' is unsafe: its real path escapes the repository root"
  elif ! is_under "$target" "$grounding_dir"; then
    fail V5 "$key '$path' does not resolve under grounding.root"
  elif [ ! -f "$target" ]; then
    fail V5 "$key '$path' is not a regular file"
  elif [ ! -r "$target" ]; then
    fail V5 "$key '$path' is unreadable"
  elif [ ! -s "$target" ]; then
    fail V5 "$key '$path' is empty"
  fi
done

# V6
for key in speckit.constitution speckit.specificationsRoot; do
  path=$(value "$key")
  [ -n "$path" ] || continue
  if is_unsafe "$path"; then
    fail V7 "$key '$path' is unsafe (absolute path or '..' traversal)"
  elif { [ -e "$root/$path" ] || [ -L "$root/$path" ]; } && ! is_under "$root/$path" "$root"; then
    fail V7 "$key '$path' is unsafe: its real path escapes the repository root"
  fi
done

# V6: the constitution may be absent before ratification, but a present one must have content.
constitution=$(value speckit.constitution)
if [ -n "$constitution" ] && ! is_unsafe "$constitution" && { [ -e "$root/$constitution" ] || [ -L "$root/$constitution" ]; }; then
  constitution_owner="Restore the constitution from version control (owner: Spec Kit maintainer)."
  if [ ! -f "$root/$constitution" ]; then
    fail V6 "speckit.constitution '$constitution' is not a regular file" "$constitution_owner"
  elif [ ! -r "$root/$constitution" ]; then
    fail V6 "speckit.constitution '$constitution' is unreadable" "$constitution_owner"
  elif ! grep -q '[^[:space:]]' "$root/$constitution"; then
    fail V6 "speckit.constitution '$constitution' is empty" "$constitution_owner"
  fi
fi

# Inventory consistency: one prompt per heading, known dependencies, valid status lines.
inventory_owner="Fix the entry in the master inventory through the roadmap process (owner: Roadmap Steward + human approver)."
inventory_path=$(value grounding.inventory)
inventory="$root/$grounding_root/$inventory_path"

if [ -n "$grounding_dir" ] && [ -s "$inventory" ] && ! is_unsafe "$inventory_path"; then
  inventory_report=$(tr -d '\r' <"$inventory" | awk '
    function finish() {
      if (id == "") return
      if (fences != 1) printf "%s has %d fenced prompts, expected exactly 1\n", id, fences
      if (open) printf "%s has an unterminated prompt fence\n", id
      if (status == "") printf "%s has no Workflow status line\n", id
      if (merge == "") printf "%s has no Final merge commit line\n", id
      else if (merge != "null" && merge !~ /^[0-9a-f]+$/) printf "%s has an invalid final merge commit: %s\n", id, merge
      if (!labelled) printf "%s has no literal prompt label\n", id
    }
    /^## ISK-[0-9][0-9][0-9][0-9]([ \t]|$)/ {
      finish()
      id = $2; fences = 0; open = 0; status = ""; merge = ""; labelled = 0; first = 0
      if (id in ids) printf "%s is a duplicate heading\n", id
      ids[id] = 1
      entries++
      next
    }
    /^## / { finish(); id = ""; next }
    id == "" { next }
    open {
      if (first && $0 !~ /^\/speckit\.specify/) printf "%s prompt does not start with /speckit.specify\n", id
      first = 0
      if ($0 == "```") open = 0
      next
    }
    /^- \*\*Workflow status:\*\*/ { status = $0 }
    /^- \*\*Final merge commit/ {
      merge = $0; sub(/^[^`]*`/, "", merge); sub(/`.*$/, "", merge)
    }
    /^- \*\*Dependencies:\*\*/ {
      line = $0
      while (match(line, /ISK-[0-9][0-9][0-9][0-9]/)) {
        deps[++dep_count] = id " " substr(line, RSTART, RLENGTH)
        line = substr(line, RSTART + RLENGTH)
      }
    }
    /^\*\*Starting `\/speckit\.specify` prompt/ { labelled = 1 }
    /^```text$/ { fences++; open = 1; first = 1; next }
    END {
      finish()
      for (i = 1; i <= dep_count; i++) {
        split(deps[i], pair, " ")
        if (!(pair[2] in ids)) printf "%s depends on unknown %s\n", pair[1], pair[2]
        if (pair[1] == pair[2]) printf "%s depends on itself\n", pair[1]
      }
      if (entries == 0) print "the inventory has no ISK-xxxx headings"
    }
  ')
  if [ -n "$inventory_report" ]; then
    while IFS= read -r line; do
      fail INVENTORY "$line" "$inventory_owner"
    done <<EOF
$inventory_report
EOF
  fi
fi

# Relative Markdown links, outside code fences, in the docs and the root onboarding files.
link_owner="Fix or remove the broken link (owner: the PR author)."
for file in "$root"/*.md "$root/$grounding_root"/*.md "$root"/.github/*.md; do
  [ -f "$file" ] || continue
  dir=$(dirname "$file")
  links=$(tr -d '\r' <"$file" | awk '
    /^[ \t]*(```|~~~)/ { fenced = !fenced; next }
    fenced { next }
    {
      line = $0
      gsub(/`[^`]*`/, "", line)
      while (match(line, /\]\([^) \t]+/)) {
        print substr(line, RSTART + 2, RLENGTH - 2)
        line = substr(line, RSTART + RLENGTH)
      }
    }
  ' | sed 's/#.*$//' | grep -v -E '^([A-Za-z][A-Za-z0-9+.-]*:|/|$)' | sort -u)
  [ -n "$links" ] || continue
  while IFS= read -r link; do
    if [ ! -e "$dir/$link" ]; then
      fail LINK "${file#"$root"/} links to missing '$link'" "$link_owner"
    fi
  done <<EOF
$links
EOF
done

if [ "$failures" -ne 0 ]; then
  printf 'check-grounding: %d failure(s)\n' "$failures"
  exit 1
fi

printf 'check-grounding: OK (manifest, inventory, links)\n'
