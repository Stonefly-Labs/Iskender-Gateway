#!/usr/bin/env bash
# License scan for the Constitution I gate: proves the repository is MIT-licensed.
# Read-only and offline. Usage: check-license.sh [repo-root]
#
# It checks the project's own license: the LICENSE text, the package metadata, stray license files,
# and that every package a production project resolves declares an allowed permissive license in its restored
# .nuspec. It reads only local files, so packages must be restored first.
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

# Package licenses: every package a production project resolves, direct or transitive, must declare one of these
# SPDX expressions in the .nuspec that restore placed in the NuGet global packages folder.
allowed_licenses=' MIT Apache-2.0 BSD-2-Clause BSD-3-Clause '

# Prints "<id> <version>" for each package entry in a packages.lock.json. NuGet writes "resolved" right after the
# entry's key, and project references have no "resolved", so they are skipped.
locked_packages() {
  tr -d '\r' <"$1" | awk '
    /^[[:space:]]*"[^"]+": \{[[:space:]]*$/ { key = $0; sub(/^[[:space:]]*"/, "", key); sub(/".*$/, "", key); next }
    /^[[:space:]]*"resolved": "/ { v = $0; sub(/^[[:space:]]*"resolved": "/, "", v); sub(/".*$/, "", v); print key " " v }
  '
}

locks=$(find "$root/src" -name packages.lock.json -not -path '*/bin/*' -not -path '*/obj/*' 2>/dev/null)
packages=$(for lock in $locks; do locked_packages "$lock"; done | sort -u)

if [ -n "$packages" ]; then
  global_packages=${NUGET_PACKAGES:-}
  if [ -z "$global_packages" ]; then
    global_packages=$(dotnet nuget locals global-packages --list 2>/dev/null | sed -n 's/^global-packages: *//p' | tr -d '\r')
  fi
  global_packages=${global_packages%/}

  if [ -z "$global_packages" ] || [ ! -d "$global_packages" ]; then
    fail "the NuGet global packages folder could not be found" "Run dotnet restore, or set NUGET_PACKAGES"
  else
    while IFS=' ' read -r id version; do
      [ -n "$id" ] || continue
      lower_id=$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')
      lower_version=$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')
      nuspec="$global_packages/$lower_id/$lower_version/$lower_id.nuspec"
      if [ ! -f "$nuspec" ]; then
        fail "package $id $version has no restored .nuspec, so its license cannot be read" "Run dotnet restore, then re-run this scan"
        continue
      fi
      expression=$(tr -d '\r' <"$nuspec" | sed -n 's/.*<license[^>]*type="expression"[^>]*>\([^<]*\)<\/license>.*/\1/p' | head -n 1)
      if [ -z "$expression" ]; then
        fail "package $id $version declares no SPDX license expression" "Review its license and extend the allow-list, or remove the package"
      elif [ "${allowed_licenses#* "$expression" }" = "$allowed_licenses" ]; then
        fail "package $id $version is licensed $expression, which is not on the allow-list" "Review its license and extend the allow-list, or remove the package"
      else
        printf 'license: %s %s %s\n' "$id" "$version" "$expression"
      fi
    done <<EOF
$packages
EOF
  fi
fi

if [ "$failures" -ne 0 ]; then
  printf 'check-license: %d failure(s)\n' "$failures"
  exit 1
fi

printf 'check-license: OK (MIT)\n'
