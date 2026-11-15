#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Builds FEX's ARM64EC module, libarm64ecfex.dll, from the FEX fork in external/Paneport-FEX
# with the pinned llvm-mingw (C-0009 section 3). Wine loads it as its x86-64 emulator (S4.2.3,
# docs/design/runtime-macos-harness.md).
#
#   eng/mac/build-fex.sh [--jobs <n>]
#
# The build tree goes to $PANEPORT_BUILD_DIR/fex/build and the module, with its linker map, to
# $PANEPORT_BUILD_DIR/fex/install, by default under build/ in the repository. Each run starts
# from an empty build tree. The fork tools come from eng/mac/bootstrap.sh --forks: CMake,
# Ninja and Python on the PATH, and llvm-mingw's folder in LLVM_MINGW. The script fetches the
# submodules of FEX that the build needs when they are missing. Bash 3.2 (C-0010 section 1).

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)

say() {
    printf 'build-fex: %s\n' "$*"
}

fail() {
    printf 'build-fex: %s\n' "$*" >&2
    exit 1
}

jobs=$(sysctl -n hw.ncpu 2>/dev/null || echo 4)
while [ $# -gt 0 ]; do
    case "$1" in
        --jobs)
            [ $# -ge 2 ] || fail "--jobs needs a number."
            jobs=$2
            shift 2
            ;;
        *) fail "unknown option $1. Usage: eng/mac/build-fex.sh [--jobs <n>]" ;;
    esac
done

[ -n "${LLVM_MINGW:-}" ] || fail "LLVM_MINGW is not set. Run eng/mac/bootstrap.sh --forks and the \
export lines it prints."
[ -x "$LLVM_MINGW/bin/arm64ec-w64-mingw32-clang" ] || fail "LLVM_MINGW does not name an llvm-mingw folder."
source="$repo/external/Paneport-FEX"
[ -f "$source/CMakeLists.txt" ] || fail "the FEX fork is not checked out. Fetch it with: git \
submodule update --init --depth 1 external/Paneport-FEX"

# The submodules that the MinGW build of FEX needs (docs/design/forks.md section 5.3); the
# others stay unfetched.
needed="External/fmt External/xxhash External/range-v3 External/unordered_dense External/rpmalloc Source/Common/cpp-optparse"
for submodule in $needed; do
    if [ -z "$(ls -A "$source/$submodule" 2>/dev/null)" ]; then
        say "fetching FEX's submodule $submodule"
        git -C "$source" submodule update --init --depth 1 "$submodule"
    fi
done

build_dir=${PANEPORT_BUILD_DIR:-$repo/build}
case $build_dir in
    /*) ;;
    *) fail "PANEPORT_BUILD_DIR must be an absolute path, not $build_dir." ;;
esac
work="$build_dir/fex/build"
install="$build_dir/fex/install"
rm -rf "$work" "$install"
mkdir -p "$work" "$install"

export PATH="$LLVM_MINGW/bin:$PATH"
commit=$(git -C "$source" rev-parse HEAD)
# A checkout at depth 1 holds no tag, so the version names the fork's base and commit
# (docs/design/forks.md section 11).
version="FEX-2610-paneport-${commit:0:12}"
say "FEX fork at $commit, built as $version"
say "$(arm64ec-w64-mingw32-clang --version | head -n 1)"
say "$(cmake --version | head -n 1); ninja $(ninja --version); $(python3 --version)"

# Sections are aligned to 16 KiB, the page size of Apple silicon, so that no host page holds
# both code and writable data. The linker map serves the check of docs/design/forks.md
# section 9.
started=$(date +%s)
if ! cmake -S "$source" -B "$work" -G Ninja \
    -DCMAKE_TOOLCHAIN_FILE="$source/Data/CMake/toolchain_mingw.cmake" \
    -DMINGW_TRIPLE=arm64ec-w64-mingw32 -DCMAKE_BUILD_TYPE=Release -DENABLE_LTO=False \
    -DENABLE_ASSERTIONS=False -DBUILD_TESTING=False -DENABLE_JEMALLOC_GLIBC_ALLOC=False \
    -DENABLE_CCACHE=False -DTUNE_CPU=none -DTUNE_ARCH=generic \
    -DOVERRIDE_VERSION="$version" -DOVERRIDE_HASH="$commit" \
    -DPython_EXECUTABLE="$(command -v python3)" \
    -DCMAKE_SHARED_LINKER_FLAGS="-Wl,--section-alignment,16384 -Wl,-Map=$install/libarm64ecfex.map" \
    > "$work/../configure.log" 2>&1; then
    tail -n 80 "$work/../configure.log" >&2
    fail "CMake's configuration failed; the end of its output is above."
fi
grep -E 'FEX version|FEX commit|Clang version' "$work/../configure.log" || true
configured=$(date +%s)
say "configured in $((configured - started)) s"

if ! cmake --build "$work" --target arm64ecfex --parallel "$jobs" > "$work/../build.log" 2>&1; then
    grep -n -E 'error:' "$work/../build.log" | head -n 40 >&2 || true
    tail -n 60 "$work/../build.log" >&2
    fail "the build failed; its first errors and the end of its output are above."
fi
say "built in $(($(date +%s) - configured)) s"

module=$(find "$work" -name libarm64ecfex.dll -type f | head -n 1)
[ -n "$module" ] || fail "the build made no libarm64ecfex.dll."
cp "$module" "$install/libarm64ecfex.dll"

# An ARM64EC module carries the machine type of x86-64 in its header and its ARM64EC code is
# described by the CHPE metadata of its load configuration.
readobj="$LLVM_MINGW/bin/llvm-readobj"
headers=$("$readobj" --file-headers "$install/libarm64ecfex.dll")
machine=$(printf '%s\n' "$headers" | sed -n 's/^ *Machine: \([A-Z0-9_]*\).*/\1/p' | head -n 1)
alignment=$(printf '%s\n' "$headers" | sed -n 's/^ *SectionAlignment: \([0-9]*\).*/\1/p' | head -n 1)
"$readobj" --coff-load-config "$install/libarm64ecfex.dll" | grep -q 'CHPEMetadata' \
    || fail "libarm64ecfex.dll has no CHPE metadata, so it is not an ARM64EC module."
say "libarm64ecfex.dll: $machine with CHPE metadata, sections aligned to $alignment bytes, $(wc -c < "$install/libarm64ecfex.dll" | tr -d ' ') bytes"

# The objects that the link took from llvm-mingw's libraries, for the check against the
# runtime review of docs/design/forks.md section 9: mingw-w64's runtime, and LLVM's runtime
# libraries, which C-0003 registers.
map="$install/libarm64ecfex.map"
if [ -f "$map" ]; then
    members=$(grep -o -E '[A-Za-z0-9_+.-]+\.a\([^)]+\)' "$map" | sort -u || true)
    say "objects linked from libraries: $(printf '%s\n' "$members" | sed '/^$/d' | wc -l | tr -d ' ')"
    printf '%s\n' "$members" | sed '/^$/d' | sed 's/(.*//' | sort | uniq -c | sed 's/^/build-fex:   /'
    printf '%s\n' "$members" | sed '/^$/d' | grep -E '^lib(mingw|mingwex|mingw32|moldname|ucrt|msvcrt)' \
        | sed 's/^/build-fex:   mingw-w64 runtime object: /' || true
else
    say "the link wrote no map at $map" >&2
fi
say "ready"
