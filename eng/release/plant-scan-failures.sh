#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# The planted failures of the package scan (M9.1, S9.1.3; C-0009 section 6). It copies a scanned
# .ipa twice into a temporary folder, plants a generated fake provisioning profile inside the app of
# the first copy and a generated local path inside the main executable of the second, and runs
# tools/ScanPackage.cs on each copy. Each scan must find what was planted: it must exit with 1 and
# name the planted file and its rule. Both copies stay in the temporary folder, which the script
# deletes; neither is uploaded or committed.
#
#   eng/release/plant-scan-failures.sh <package.ipa>     from the repository root, on macOS
#
# The planted values are made up and exist only in the copies. Exit codes: 0 both scans failed as
# expected, 1 a scan did not, 2 usage. Bash 3.2 (C-0010 section 1).

set -euo pipefail

if [ $# -ne 1 ] || [ ! -f "$1" ]; then
    echo "usage: eng/release/plant-scan-failures.sh <package.ipa>" >&2
    exit 2
fi
ipa=$1

problems=0
problem() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Planted scan failures::$*"
    fi
    echo "plant-scan-failures: $*" >&2
    problems=$((problems + 1))
}

work=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/plant-scan-failures.XXXXXX")
trap 'rm -rf "$work"' EXIT

app=$(zipinfo -1 "$ipa" | grep -E -m 1 '^Payload/[^/]+\.app/Info\.plist$' | sed 's|/Info\.plist$||' || true)
if [ -z "$app" ]; then
    echo "plant-scan-failures: $ipa holds no app." >&2
    exit 2
fi
unzip -p "$ipa" "$app/Info.plist" > "$work/Info.plist"
executable=$(plutil -extract CFBundleExecutable raw -o - "$work/Info.plist")

# Scans one copy and checks that the scan exits with 1 and names <entry> with <rule>.
expect_finding() {
    local copy=$1 entry=$2 rule=$3 output status
    status=0
    output=$(dotnet run tools/ScanPackage.cs -- "$copy" 2>&1) || status=$?
    if [ "$status" -ne 1 ]; then
        problem "the scan of $(basename "$copy") exited with $status, not 1."
    elif ! grep -F -q ": $entry: $rule" <<< "$output"; then
        problem "the scan of $(basename "$copy") did not name $entry with the rule $rule."
    else
        echo "plant-scan-failures: the scan of $(basename "$copy") named $entry with the rule $rule, as expected."
    fi
}

# 1. A provisioning profile inside the app, under the name that signing tools give it.
profile_copy="$work/planted-profile.ipa"
cp "$ipa" "$profile_copy"
mkdir -p "$work/profile/$app"
printf 'Planted by eng/release/plant-scan-failures.sh. Not a provisioning profile.\n' \
    > "$work/profile/$app/embedded.mobileprovision"
(cd "$work/profile" && zip -q "$profile_copy" "$app/embedded.mobileprovision")
expect_finding "$profile_copy" "$app/embedded.mobileprovision" provisioning-profile

# 2. A local path inside the main executable, as a build step that records its folder leaves it.
path_copy="$work/planted-path.ipa"
cp "$ipa" "$path_copy"
mkdir -p "$work/path/$app"
unzip -p "$ipa" "$app/$executable" > "$work/path/$app/$executable"
printf '\000/Users/planted-name/build/planted.o\000' >> "$work/path/$app/$executable"
(cd "$work/path" && zip -q "$path_copy" "$app/$executable")
expect_finding "$path_copy" "$app/$executable" local-path

if [ "$problems" -ne 0 ]; then
    exit 1
fi
echo "plant-scan-failures: both planted findings failed the scan."
