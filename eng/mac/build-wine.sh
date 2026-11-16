#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Builds the Wine fork in external/Paneport-wine for arm64 macOS: its Unix side with Xcode's
# clang, and its PE side for arm64ec, aarch64 and i386 with the pinned llvm-mingw (C-0009
# section 3), then installs it and checks the result (S4.2.1, S4.2.2).
#
#   eng/mac/build-wine.sh [--jobs <n>]
#
# The build tree goes to $PANEPORT_BUILD_DIR/wine/build and the install to
# $PANEPORT_BUILD_DIR/wine/install, by default under build/ in the repository. Each run starts
# from an empty build tree. The fork tools come from eng/mac/bootstrap.sh --forks: Bison and
# flex on the PATH, and llvm-mingw's folder in LLVM_MINGW. Bash 3.2 (C-0010 section 1).
# docs/design/runtime-macos-harness.md describes the build.

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)

say() {
    printf 'build-wine: %s\n' "$*"
}

fail() {
    printf 'build-wine: %s\n' "$*" >&2
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
        *) fail "unknown option $1. Usage: eng/mac/build-wine.sh [--jobs <n>]" ;;
    esac
done

[ "$(uname -s)" = Darwin ] || fail "this script builds Wine for macOS and needs macOS."
[ -n "${LLVM_MINGW:-}" ] || fail "LLVM_MINGW is not set. Run eng/mac/bootstrap.sh --forks and the \
export lines it prints."
[ -x "$LLVM_MINGW/bin/aarch64-w64-mingw32-clang" ] || fail "LLVM_MINGW does not name an llvm-mingw folder."
source="$repo/external/Paneport-wine"
[ -x "$source/configure" ] || fail "the Wine fork is not checked out. Fetch it with: git submodule \
update --init --depth 1 external/Paneport-wine"

build_dir=${PANEPORT_BUILD_DIR:-$repo/build}
case $build_dir in
    /*) ;;
    *) fail "PANEPORT_BUILD_DIR must be an absolute path, not $build_dir." ;;
esac
work="$build_dir/wine/build"
install="$build_dir/wine/install"

# llvm-mingw first, as the FEX wiki asks for the PE side; Xcode's clang still builds the Unix
# side, because configure asks for it by name.
export PATH="$LLVM_MINGW/bin:$PATH"
say "Wine $(cat "$source/VERSION" 2>/dev/null || echo "(no VERSION file)"), commit $(git -C "$source" rev-parse --short=12 HEAD)"
say "$(aarch64-w64-mingw32-clang --version | head -n 1)"
say "$(clang --version | head -n 1)"
say "$(bison --version | head -n 1); $(flex --version)"

rm -rf "$work" "$install"
mkdir -p "$work"
cd "$work"

# Only what macOS itself provides is used, so the build is the same on every Mac: the optional
# libraries that would come from Homebrew or similar are left out, and Wine builds its bundled
# copies of the libraries that its PE side needs. The tests of Wine are not built.
started=$(date +%s)
say "configuring"
if ! "$source/configure" --prefix="$install" --enable-archs=arm64ec,aarch64,i386 \
    --with-mingw=clang --disable-tests \
    --without-alsa --without-capi --without-cups --without-dbus --without-ffmpeg \
    --without-fontconfig --without-freetype --without-gettext --without-gettextpo \
    --without-gphoto --without-gnutls --without-gssapi --without-gstreamer --without-hwloc \
    --without-inotify --without-krb5 --without-netapi --without-opencl --without-oss \
    --without-pcap --without-pcsclite --without-pulse --without-sane --without-sdl \
    --without-udev --without-usb --without-v4l2 --without-va --without-vulkan \
    --without-wayland --without-x > configure.log 2>&1; then
    tail -n 60 configure.log >&2
    tail -n 120 config.log >&2
    fail "configure failed; the end of its output and of config.log are above."
fi
grep -E '^configure: (WARNING|NOTICE)' configure.log || true
configured=$(date +%s)
say "configured in $((configured - started)) s"

say "building with $jobs jobs"
if ! make -j"$jobs" > make.log 2>&1; then
    grep -n -E 'error:|Error [0-9]' make.log | head -n 40 >&2 || true
    tail -n 60 make.log >&2
    fail "make failed; its first errors and the end of its output are above."
fi
built=$(date +%s)
say "built in $((built - configured)) s"

if ! make install > install.log 2>&1; then
    tail -n 40 install.log >&2
    fail "make install failed; the end of its output is above."
fi
say "installed in $(($(date +%s) - built)) s into $install"

# S4.2.1: the loader starts and prints its version.
version=$("$install/bin/wine" --version) || fail "the installed loader does not start."
say "the loader reports $version"

# bin/wine is Wine's launcher, which execs the loader in lib/wine/aarch64-unix as the program's
# process. macOS kills an arm64 executable whose segments are not aligned to its 16 KiB pages,
# and keeps x18, which holds the Windows TEB, only for a main executable that names the macOS 12
# SDK or an older one (docs/design/runtime-macos-harness.md section 2.1).
for loader in "$install/bin/wine" "$install/lib/wine/aarch64-unix/wine"; do
    [ -f "$loader" ] || fail "${loader#"$install/"} is missing."
    name=${loader#"$install/"}
    sdk=$(vtool -show-build "$loader" 2>/dev/null | awk '$1 == "sdk" { print $2 }' | head -n 1)
    misaligned=0
    for address in $(otool -l "$loader" | awk '$1 == "vmaddr" { print $2 }'); do
        [ $((address % 16384)) -eq 0 ] || misaligned=$((misaligned + 1))
    done
    say "$name: SDK version ${sdk:-unknown}, $misaligned segments not aligned to 16 KiB"
    [ "$misaligned" -eq 0 ] || fail "$name has segments that macOS will not map."
done
[ -n "$sdk" ] && [ "${sdk%%.*}" -le 12 ] \
    || fail "lib/wine/aarch64-unix/wine names SDK ${sdk:-unknown}, so macOS will not keep x18."

# S4.2.2: every module that the build made for a PE architecture is installed in that
# architecture's folder, and a sample of modules has the machine type of its folder.
readobj="$LLVM_MINGW/bin/llvm-readobj"
problems=0
for folder in "$install"/lib/wine/*-windows; do
    [ -d "$folder" ] || continue
    arch=$(basename "$folder")
    built_list=$(cd "$work" && find dlls programs -path "*/$arch/*" -type f \
        \( -name '*.dll' -o -name '*.exe' -o -name '*.drv' -o -name '*.sys' -o -name '*.acm' \
        -o -name '*.cpl' -o -name '*.ocx' -o -name '*.tlb' -o -name '*.ax' -o -name '*.ds' \
        -o -name '*.com' -o -name '*.msstyles' -o -name '*.vxd' -o -name '*.dll16' \
        -o -name '*.exe16' -o -name '*.drv16' -o -name '*.mod16' \) -exec basename {} \; | sort -u)
    installed_list=$(cd "$folder" && find . -maxdepth 1 -type f -exec basename {} \; | sort -u)
    missing=$(comm -23 <(printf '%s\n' "$built_list") <(printf '%s\n' "$installed_list") | sed '/^$/d')
    count=$(printf '%s\n' "$installed_list" | sed '/^$/d' | wc -l | tr -d ' ')
    say "$arch: $count modules installed"
    if [ -n "$missing" ]; then
        printf '%s\n' "$missing" | sed "s/^/build-wine: $arch: built but not installed: /" >&2
        problems=1
    fi
    # The 64-bit modules are hybrid ARM64X files, which llvm-readobj reports as such; the
    # programs, such as wineboot.exe, exist for the 64-bit architecture only.
    samples="ntdll.dll kernel32.dll kernelbase.dll ucrtbase.dll user32.dll"
    case "$arch" in
        aarch64-windows) expected="IMAGE_FILE_MACHINE_ARM64X IMAGE_FILE_MACHINE_ARM64"
                         samples="$samples wineboot.exe" ;;
        i386-windows) expected=IMAGE_FILE_MACHINE_I386 ;;
        x86_64-windows) expected=IMAGE_FILE_MACHINE_AMD64 ;;
        *) expected="" ;;
    esac
    # shellcheck disable=SC2086 # The sample names are words of their own.
    for module in $samples; do
        if [ ! -f "$folder/$module" ]; then
            say "$arch: $module is missing" >&2
            problems=1
            continue
        fi
        headers=$("$readobj" --file-headers "$folder/$module")
        machine=$(printf '%s\n' "$headers" | sed -n 's/^ *Machine: \([A-Z0-9_]*\).*/\1/p' | head -n 1)
        hybrid=""
        if "$readobj" --coff-load-config "$folder/$module" 2>/dev/null |
            grep -Eq 'CHPEMetadata(Pointer)?: 0x0*[1-9A-Fa-f]'; then
            hybrid=", with ARM64EC code (CHPE metadata)"
        fi
        say "$arch: $module is $machine$hybrid"
        if [ -n "$expected" ] && ! [[ " $expected " == *" $machine "* ]]; then
            say "$arch: $module should be one of: $expected" >&2
            problems=1
        fi
    done
done
[ "$problems" -eq 0 ] || fail "the installed PE modules are incomplete or of the wrong machine type; see above."
say "ready"
