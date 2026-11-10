#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks our own C, Objective-C and C++ code with the pinned clang-format and clang-tidy
# (C-0010 section 3, C-0011 section 2), which eng/mac/bootstrap.sh --native installs. The
# root .clang-format and .clang-tidy hold the rules.
#
#   eng/mac/check-native-style.sh format [file ...]
#       clang-format in check mode, on every such file of src/, tests/ and tools/ that git
#       tracks or would add, or on the files given.
#   eng/mac/check-native-style.sh tidy <build folder> ...
#       clang-tidy on every source file of src/native and tests/native in the compile
#       database of each build folder, which CMake writes when it configures a preset.
#
# Exit codes: 0 nothing to report, 1 a finding, 2 a usage error. Bash 3.2 (C-0010 section 1).

set -euo pipefail

cd "$(dirname "$0")/../.."

usage() {
    echo "usage: eng/mac/check-native-style.sh format [file ...] | tidy <build folder> ..." >&2
    exit 2
}

[ $# -ge 1 ] || usage
mode=$1
shift

case "$mode" in
    format)
        if [ $# -gt 0 ]; then
            files=("$@")
        else
            files=()
            while IFS= read -r file; do
                files+=("$file")
            done < <(git ls-files --cached --others --exclude-standard -- \
                'src/*.c' 'src/*.h' 'src/*.m' 'src/*.mm' 'src/*.cpp' \
                'tests/*.c' 'tests/*.h' 'tests/*.m' 'tests/*.mm' 'tests/*.cpp' \
                'tools/*.c' 'tools/*.h' 'tools/*.cpp')
        fi
        [ ${#files[@]} -gt 0 ] || { echo "No file to check."; exit 0; }
        echo "clang-format: $(clang-format --version)"
        echo "Checking ${#files[@]} files."
        clang-format --dry-run --Werror "${files[@]}"
        echo "Every file is formatted."
        ;;
    tidy)
        [ $# -ge 1 ] || usage
        echo "clang-tidy: $(clang-tidy --version | grep 'version' | head -n 1)"
        status=0
        for build in "$@"; do
            database="$build/compile_commands.json"
            [ -f "$database" ] || { echo "$database does not exist; configure the preset first." >&2; exit 2; }
            # CMake writes each entry's file on a line of its own. The generated sources of the
            # header test live in the build folder and are left out.
            files=()
            while IFS= read -r file; do
                files+=("$file")
            done < <(sed -n 's/^ *"file": "\(.*\)",\{0,1\}$/\1/p' "$database" \
                | grep -e '/src/native/' -e '/tests/native/' | grep -v '/build/' | sort -u)
            [ ${#files[@]} -gt 0 ] || { echo "$database lists no source file of ours." >&2; exit 2; }
            echo "Analyzing ${#files[@]} files with $database."
            clang-tidy --quiet -p "$build" "${files[@]}" || status=1
        done
        [ "$status" -eq 0 ] && echo "clang-tidy reports nothing."
        exit "$status"
        ;;
    *)
        usage
        ;;
esac
