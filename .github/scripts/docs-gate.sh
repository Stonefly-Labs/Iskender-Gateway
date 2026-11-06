#!/usr/bin/env bash
# Documentation merge gate (FR-043). A PR changing a behavior-bearing path must also change docs/**
# or a root README.md/CLAUDE.md, unless a CODEOWNER of every affected path applied the
# docs-not-required label and the PR description holds a written justification.
#
# In CI it reads the PR through `gh api`, using GITHUB_REPOSITORY, PR_NUMBER, BASE_SHA and GH_TOKEN.
# CODEOWNERS is read from the base commit, so a PR cannot grant itself the skip.
#
# These variables replace the API calls, so a scenario can be tried by hand:
#   DOCS_GATE_CHANGED_FILES   newline-separated "status<TAB>path" lines, or bare paths, meaning modified.
#                             Status is a GitHub file status, "previous" for a rename's old path, or
#                             "renamed-unchanged" for a rename with no content change.
#   DOCS_GATE_PR_BODY         PR description
#   DOCS_GATE_LABELS          newline-separated label names
#   DOCS_GATE_LABEL_ACTOR     login that most recently applied docs-not-required
#   DOCS_GATE_CODEOWNERS      CODEOWNERS file content
set -euo pipefail

label="docs-not-required"

api_repo() {
  printf 'repos/%s' "${GITHUB_REPOSITORY:?GITHUB_REPOSITORY is required outside override mode}"
}

api_pr() {
  printf '%s' "${PR_NUMBER:?PR_NUMBER is required outside override mode}"
}

if [ -n "${DOCS_GATE_CHANGED_FILES+x}" ]; then
  changed_files=$DOCS_GATE_CHANGED_FILES
else
  changed_files=$(gh api --paginate "$(api_repo)/pulls/$(api_pr)/files" --jq '.[] |
    "\(if .status == "renamed" and .changes == 0 then "renamed-unchanged" else .status end)\t\(.filename)",
    (if .previous_filename then "previous\t\(.previous_filename)" else empty end)')
fi

# Matches a CODEOWNERS-style path pattern against a path using grep -E.
pattern_matches() {
  local pattern=$1 path=$2 regex
  regex=$(printf '%s' "$pattern" | sed -e 's/[.+?()|^$]/\\&/g' -e 's/\*\*/\x01/g' -e 's/\*/[^\/]*/g' -e 's/\x01/.*/g')
  case "$pattern" in
    /*) regex="^${regex#/}" ;;
    */*) regex="^$regex" ;;
    *) regex="(^|/)$regex" ;;
  esac
  case "$pattern" in
    */) regex="$regex" ;;
    *) regex="$regex(/|\$)" ;;
  esac
  printf '%s\n' "$path" | grep -Eq "$regex"
}

is_behavior_bearing() {
  case "$1" in
    src/Iskender.Core.Contracts/* | src/Iskender.Providers.Abstractions/* | src/Iskender.Policy.Sdk/* | \
      src/Iskender.Gateway.Host/* | .github/workflows/* | .github/scripts/* | .iskender/grounding.yaml) return 0 ;;
  esac
  return 1
}

# The inventory is its own documentation, so a PR that changes only it is not blocked. It also changes on every
# spec PR, as status values move, so it does not count as documentation for any other path.
inventory="docs/17-master-spec-inventory.md"

is_documentation() {
  case "$1" in
    "$inventory") return 1 ;;
    docs/* | README.md | CLAUDE.md) return 0 ;;
  esac
  return 1
}

# Only a file that still exists, with changed content, is a documentation change.
counts_as_documentation_status() {
  case "$1" in
    added | modified | changed | copied | renamed) return 0 ;;
  esac
  return 1
}

affected=()
has_docs=false
while IFS= read -r line; do
  [ -n "$line" ] || continue
  case "$line" in
    *$'\t'*) status=${line%%$'\t'*}; file=${line#*$'\t'} ;;
    *) status=modified; file=$line ;;
  esac
  if is_behavior_bearing "$file"; then affected+=("$file"); fi
  if is_documentation "$file" && counts_as_documentation_status "$status"; then has_docs=true; fi
done <<EOF
$changed_files
EOF

if [ "${#affected[@]}" -eq 0 ]; then
  echo "docs-gate: PASS (no behavior-bearing path changed)"
  exit 0
fi

if [ "$has_docs" = true ]; then
  echo "docs-gate: PASS (documentation changed alongside ${#affected[@]} behavior-bearing path(s))"
  exit 0
fi

if [ -n "${DOCS_GATE_LABELS+x}" ]; then
  labels=$DOCS_GATE_LABELS
else
  labels=$(gh api --paginate "$(api_repo)/issues/$(api_pr)/labels" --jq '.[].name')
fi

block() {
  echo "docs-gate: BLOCKED: $1"
  printf 'Behavior-bearing paths changed:\n'
  printf '  %s\n' "${affected[@]}"
  echo "Unblock: add a change under docs/ or to README.md/CLAUDE.md, or have a CODEOWNER of every path above apply '$label' and fill in the '## Docs-not-required justification' section."
  exit 1
}

if ! printf '%s\n' "$labels" | grep -Fxq "$label"; then
  block "no documentation change in this PR"
fi

if [ -n "${DOCS_GATE_PR_BODY+x}" ]; then
  body=$DOCS_GATE_PR_BODY
else
  body=$(gh api "$(api_repo)/pulls/$(api_pr)" --jq '.body // ""')
fi

# The text under the justification heading, up to the next heading, without HTML comments or the
# generated PR footer, which is not a justification.
justification=$(printf '%s\n' "$body" | tr -d '\r' | awk '
  /^## / { active = ($0 ~ /^## Docs-not-required justification[ \t]*$/); next }
  active && !/Generated with \[Claude Code\]/ { print }
' | awk '
  {
    line = $0
    while (line != "") {
      if (in_comment) {
        end = index(line, "-->")
        if (end == 0) { line = ""; break }
        line = substr(line, end + 3)
        in_comment = 0
      } else {
        start = index(line, "<!--")
        if (start == 0) { print line; line = "" }
        else {
          printf "%s\n", substr(line, 1, start - 1)
          line = substr(line, start + 4)
          in_comment = 1
        }
      }
    }
  }
' | tr -d ' \t\n')

if [ -z "$justification" ]; then
  block "'$label' is applied but the '## Docs-not-required justification' section is empty"
fi

if [ -n "${DOCS_GATE_LABEL_ACTOR+x}" ]; then
  actor=$DOCS_GATE_LABEL_ACTOR
else
  actor=$(gh api --paginate "$(api_repo)/issues/$(api_pr)/events" \
    --jq ".[] | select(.event == \"labeled\" and .label.name == \"$label\") | .actor.login" | tail -n 1)
fi

if [ -z "$actor" ]; then
  block "no label event shows who applied '$label'"
fi

if [ -n "${DOCS_GATE_CODEOWNERS+x}" ]; then
  codeowners=$DOCS_GATE_CODEOWNERS
else
  codeowners=$(gh api "$(api_repo)/contents/.github/CODEOWNERS?ref=${BASE_SHA:?BASE_SHA is required outside override mode}" \
    -H 'Accept: application/vnd.github.raw+json' 2>/dev/null || true)
fi

# Last matching CODEOWNERS line wins.
owners_of() {
  local path=$1 owners="" line pattern rest
  while IFS= read -r line; do
    line=${line%$'\r'}
    case "$line" in '' | '#'*) continue ;; esac
    read -r pattern rest <<<"$line"
    if pattern_matches "$pattern" "$path"; then owners=${rest%%#*}; fi
  done <<EOF
$codeowners
EOF
  printf '%s' "$owners"
}

for file in "${affected[@]}"; do
  if ! owners_of "$file" | tr ' \t' '\n\n' | grep -Fixq "@$actor"; then
    block "'$label' was applied by '$actor', who is not a CODEOWNER of $file"
  fi
done

echo "docs-gate: PASS ('$label' applied by CODEOWNER '$actor', with justification)"
