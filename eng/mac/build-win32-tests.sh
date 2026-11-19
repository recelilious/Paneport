#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Builds the Win32 test programs of tests/win32 with the pinned llvm-mingw (C-0009 section 3),
# as C17 with every warning an error (C-0010 section 3), for one target:
#
#   eng/mac/build-win32-tests.sh <target>
#
#   <target>  x86_64. M7.3 adds i686.
#
# The programs go to $PANEPORT_BUILD_DIR/win32/<target>/, by default build/win32/<target>/ in
# the repository, with a linker map beside each one and a compile_commands.json that
# eng/mac/check-native-style.sh tidy reads. LLVM_MINGW names llvm-mingw's folder, as
# eng/mac/bootstrap.sh --forks hands it on. The script also runs on Linux with llvm-mingw for
# Linux, because it needs nothing of macOS. Bash 3.2 (C-0010 section 1).

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)

fail() {
    printf 'build-win32-tests: %s\n' "$*" >&2
    exit 1
}

[ $# -eq 1 ] || fail "usage: eng/mac/build-win32-tests.sh <target>, where <target> is x86_64."
target=$1
case "$target" in
    x86_64) ;;
    i686) fail "the i686 build comes with M7.3." ;;
    *) fail "unknown target $target; the only target is x86_64." ;;
esac

[ -n "${LLVM_MINGW:-}" ] || fail "LLVM_MINGW is not set. Run eng/mac/bootstrap.sh --forks and the \
export line it prints."
compiler="$LLVM_MINGW/bin/$target-w64-mingw32-clang"
readobj="$LLVM_MINGW/bin/llvm-readobj"
[ -x "$compiler" ] || fail "$compiler is missing; LLVM_MINGW does not name an llvm-mingw folder."

build_dir=${PANEPORT_BUILD_DIR:-$repo/build}
case $build_dir in
    /*) ;;
    *) fail "PANEPORT_BUILD_DIR must be an absolute path, not $build_dir." ;;
esac
out="$build_dir/win32/$target"
rm -rf "$out"
mkdir -p "$out/objects"

# UCRT formats the output, not mingw-w64's own printf, as for the C program of the runtime review
# in docs/design/forks.md section 9. Source paths in the binaries are relative to the
# repository (C-0009 section 6). Sections are aligned to 16 KiB, the page size of Apple
# silicon, so that no host page holds both code and writable data
# (docs/design/runtime-macos-harness.md).
cflags=(-std=c17 -O2 -g -Wall -Wextra -Wpedantic -Werror -Wvla
    -D__USE_MINGW_ANSI_STDIO=0 "-ffile-prefix-map=$repo/=" -I "$repo/tests/win32/checks")
ldflags=(-Wl,--section-alignment,16384)

echo "build-win32-tests: $("$compiler" --version | head -n 1), target $target-w64-mingw32"

checks=()
for source in "$repo"/tests/win32/checks/*.c; do
    checks+=("$source")
done
programs=()
for source in "$repo"/tests/win32/programs/*.c; do
    programs+=("$source")
done

# A JSON string: backslashes and double quotes escaped, as the folders of a checkout may hold them.
json_string() {
    local value=$1
    value=${value//\\/\\\\}
    value=${value//\"/\\\"}
    printf '"%s"' "$value"
}

# Every source compiles once; compile_commands.json records the arguments of each command,
# with each member on a line of its own, as CMake writes it and eng/mac/check-native-style.sh
# reads it.
database="$out/compile_commands.json"
printf '[\n' > "$database"
first=true
for source in "${checks[@]}" "${programs[@]}"; do
    relative=${source#"$repo"/}
    object="$out/objects/$(printf '%s' "$relative" | tr '/' '_' | sed 's/\.c$/.o/')"
    # -fms-extensions gives __try, __except and __finally to tests/win32/checks/seh_check.c,
    # the only file that needs them. With it, clang-tidy's readability-duplicate-include
    # reports includes that are not repeated, so no other file gets it.
    flags=("${cflags[@]}")
    case "$source" in
        */seh_check.c) flags+=(-fms-extensions) ;;
    esac
    "$compiler" "${flags[@]}" -c "$source" -o "$object"
    if [ "$first" = true ]; then
        first=false
    else
        printf ',\n' >> "$database"
    fi
    {
        printf '{\n  "directory": %s,\n  "arguments": [' "$(json_string "$repo")"
        separator=""
        for argument in "$compiler" "${flags[@]}" -c "$source" -o "$object"; do
            printf '%s%s' "$separator" "$(json_string "$argument")"
            separator=", "
        done
        printf '],\n  "file": %s\n}' "$(json_string "$source")"
    } >> "$database"
done
printf '\n]\n' >> "$database"

shared_objects=()
for source in "${checks[@]}"; do
    relative=${source#"$repo"/}
    shared_objects+=("$out/objects/$(printf '%s' "$relative" | tr '/' '_' | sed 's/\.c$/.o/')")
done

for source in "${programs[@]}"; do
    name=$(basename "$source" .c)
    relative=${source#"$repo"/}
    object="$out/objects/$(printf '%s' "$relative" | tr '/' '_' | sed 's/\.c$/.o/')"
    "$compiler" "${ldflags[@]}" "-Wl,-Map=$out/$name.map" -o "$out/$name.exe" \
        "$object" "${shared_objects[@]}"
    # The machine type and the section alignment, read back from the program's headers.
    headers=$("$readobj" --file-headers "$out/$name.exe")
    machine=$(printf '%s\n' "$headers" | sed -n 's/^ *Machine: \([A-Z0-9_]*\).*/\1/p' | head -n 1)
    alignment=$(printf '%s\n' "$headers" | sed -n 's/^ *SectionAlignment: \([0-9]*\).*/\1/p' | head -n 1)
    [ "$machine" = IMAGE_FILE_MACHINE_AMD64 ] \
        || fail "$name.exe has the machine type $machine, not IMAGE_FILE_MACHINE_AMD64."
    [ "$alignment" = 16384 ] || fail "$name.exe has the section alignment $alignment, not 16384."
    echo "build-win32-tests: $name.exe ($machine, sections aligned to $alignment bytes)"
done

echo "build-win32-tests: ${#programs[@]} programs in $out"
