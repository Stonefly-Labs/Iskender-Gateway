#!/usr/bin/env bash
# License scan for the Constitution I gate: proves the repository is MIT-licensed.
# Read-only and offline. Usage: check-license.sh [repo-root]
#
# It checks the project's own license: the LICENSE text, the package metadata, stray license files,
# and that production projects carry no third-party packages whose licenses have not been scanned.
# When a production project first takes a package, this check fails until it can scan that license.
set -u

root=${1:-$(git rev-parse --show-toplevel)}
failures=0

fail() {
  printf 'FAIL license: %s\n  Owner action: %s (owner: Product/Charter owner).\n' "$1" "$2"
  failures=$((failures + 1))
}

# The MIT text after the copyright line, with whitespace collapsed so wrapping cannot change the hash.
mit_body_hash=fe2a9817987f862eaced948f0468c7f51d2fedfc48c5c505b246a49a3870e9a5
license="$root/LICENSE"

if [ ! -s "$license" ]; then
  fail "LICENSE is missing or empty" "Restore the MIT LICENSE file"
else
  normalized=$(tr -d '\r' <"$license" | sed -e '/^MIT License$/d' -e '/^Copyright (c) /d' | tr -s ' \n\t' ' ' | sed -e 's/^ //' -e 's/ $//')
  actual=$(printf '%s' "$normalized" | sha256sum | cut -d' ' -f1)
  if [ "$actual" != "$mit_body_hash" ]; then
    fail "LICENSE does not match the MIT text (body hash $actual)" "Restore the MIT LICENSE text"
  fi
fi

props="$root/Directory.Build.props"
if ! grep -q '<PackageLicenseExpression>MIT</PackageLicenseExpression>' "$props" 2>/dev/null; then
  fail "Directory.Build.props does not set PackageLicenseExpression to MIT" "Set it to MIT"
fi

while IFS= read -r project; do
  [ -n "$project" ] || continue
  if grep -q '<PackageLicense\(File\|Expression\)>' "$project" && ! grep -q '<PackageLicenseExpression>MIT</PackageLicenseExpression>' "$project"; then
    fail "${project#"$root"/} overrides the package license" "Remove the override"
  fi
done <<EOF
$(find "$root/src" "$root/tests" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null)
EOF

while IFS= read -r other; do
  [ -n "$other" ] || continue
  fail "unexpected license file ${other#"$root"/}" "Remove it, or get the owner's approval for mixed licensing"
done <<EOF
$(find "$root" \( -name .git -o -name bin -o -name obj -o -name artifacts \) -prune -o -type f \( -iname 'LICENSE*' -o -iname 'COPYING*' \) -print | grep -v -x "$root/LICENSE")
EOF

while IFS= read -r project; do
  [ -n "$project" ] || continue
  if grep -q '<PackageReference' "$project"; then
    fail "${project#"$root"/} takes a package, whose license this scan cannot read yet" "Extend this scan to cover package licenses"
  fi
done <<EOF
$(find "$root/src" -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null)
EOF

if [ "$failures" -ne 0 ]; then
  printf 'check-license: %d failure(s)\n' "$failures"
  exit 1
fi

printf 'check-license: OK (MIT)\n'
