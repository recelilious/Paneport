#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Stages the x86-64 Win32 test programs for the device test flavor (C-0009 section 4, S4.4.8):
#
#   eng/mac/stage-test-programs.sh
#
# It copies the .exe files that eng/mac/build-win32-tests.sh x86_64 built into
# $PANEPORT_BUILD_DIR/win32/x86_64/ (by default build/win32/x86_64/ in the repository) to
# $PANEPORT_BUILD_DIR/test-programs/, without their linker maps and objects, and writes
# SHA256SUMS beside them, a line of `<hash>  <name>` for each program. tests/device/ bundles that
# folder as test-programs/, and tools/CheckBundle.cs checks the list against the bundle. Bash 3.2
# (C-0010 section 1).

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)

fail() {
    printf 'stage-test-programs: %s\n' "$*" >&2
    exit 1
}

[ $# -eq 0 ] || fail "usage: eng/mac/stage-test-programs.sh"

build_dir=${PANEPORT_BUILD_DIR:-$repo/build}
case $build_dir in
    /*) ;;
    *) fail "PANEPORT_BUILD_DIR must be an absolute path, not $build_dir." ;;
esac
source_dir="$build_dir/win32/x86_64"
out="$build_dir/test-programs"
[ -d "$source_dir" ] || fail "$source_dir does not exist; run eng/mac/build-win32-tests.sh x86_64 first."

hash_of() {
    if command -v shasum > /dev/null 2>&1; then
        shasum -a 256 "$1" | cut -d ' ' -f 1
    else
        sha256sum "$1" | cut -d ' ' -f 1
    fi
}

rm -rf "$out"
mkdir -p "$out"
count=0
: > "$out/SHA256SUMS"
for program in "$source_dir"/*.exe; do
    [ -e "$program" ] || fail "$source_dir holds no .exe file."
    name=$(basename "$program")
    cp "$program" "$out/$name"
    printf '%s  %s\n' "$(hash_of "$out/$name")" "$name" >> "$out/SHA256SUMS"
    count=$((count + 1))
done

echo "stage-test-programs: $count programs and SHA256SUMS in $out"
