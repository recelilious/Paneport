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
wine_version=$(sed -n 's/^Wine version //p' "$source/VERSION" 2>/dev/null | head -n 1)
[ -n "$wine_version" ] || fail "external/Paneport-wine/VERSION does not name a Wine version."
say "Wine $wine_version, commit $(git -C "$source" rev-parse --short=12 HEAD)"
say "the PE side: $(aarch64-w64-mingw32-clang --version | head -n 1)"
say "$(bison --version | head -n 1); $(flex --version)"

# The fork's single-process mode includes the bridge header paneport/wine_bridge.h from this
# repository's src/native/include on its Unix side, which builds with CFLAGS, not with the PE
# side's CROSSCFLAGS (C-0005 section 3). The header's SHA-256 is written here, so that the
# runtime job's cache key, which covers this script, follows the header.
bridge_header="$repo/src/native/include/paneport/wine_bridge.h"
bridge_sha256=a596fa9b01753dc863e6a847f411957c2a3fba683506b1b431e5416bf8700c52
[ "$(shasum -a 256 "$bridge_header" | cut -d ' ' -f 1)" = "$bridge_sha256" ] \
    || fail "src/native/include/paneport/wine_bridge.h has changed: write its new SHA-256 into \
bridge_sha256 in this script, so that the cache of the Wine build follows it."
case $repo in
    *[[:space:]]*) fail "the repository's path holds a space, which Wine's CFLAGS cannot carry." ;;
esac

# The checks of the PE modules below are a script of their own, which build-fex.sh runs too. Its
# SHA-256 is written here for the same reason as the bridge header's: a build that passed an
# older check must not be restored from the cache.
pe_check="$repo/eng/mac/check-pe-modules.sh"
pe_check_sha256=22cb98cb633109352e152e002c94fd3aa3530550431ed0e8497cfc69e9da1598
[ "$(shasum -a 256 "$pe_check" | cut -d ' ' -f 1)" = "$pe_check_sha256" ] \
    || fail "eng/mac/check-pe-modules.sh has changed: write its new SHA-256 into pe_check_sha256 \
in this script and in eng/mac/build-fex.sh, so that the caches of the fork builds follow it."

rm -rf "$work" "$install"
mkdir -p "$work"
cd "$work"

# Only what macOS itself provides is used, so the build is the same on every Mac: the optional
# libraries that would come from Homebrew or similar are left out, and Wine builds its bundled
# copies of the libraries that its PE side needs. The tests of Wine are not built.
started=$(date +%s)
say "configuring"
if ! "$source/configure" --prefix="$install" --enable-archs=arm64ec,aarch64,i386 \
    --with-mingw=clang --disable-tests CFLAGS="-g -O2 -I$repo/src/native/include" \
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
# The compiler that configure chose for the Unix side, which is Xcode's, not llvm-mingw's.
unix_cc=$(sed -n 's/^CC = //p' Makefile | head -n 1)
if [ -n "$unix_cc" ]; then
    # shellcheck disable=SC2086 # CC may carry flags after the compiler's name.
    say "the Unix side: $unix_cc, $($unix_cc --version 2>&1 | head -n 1)"
else
    say "the Makefile names no CC for the Unix side"
fi
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

# S4.2.1: the launcher and the loader start and print the version. bin/wine answers --version in
# ntdll.so, inside its own process, before it would exec the loader, so the loader in
# lib/wine/aarch64-unix, which macOS can refuse for its link alone, runs on its own as well.
for program in bin/wine lib/wine/aarch64-unix/wine; do
    version=$("$install/$program" --version) || fail "the installed $program does not start."
    [ "$version" = "wine-$wine_version" ] \
        || fail "the installed $program reports \"$version\", not wine-$wine_version."
    say "$program reports $version"
done

# S4.3.2: single-process mode loads the wineserver as a library from Wine's bin folder, and
# finds its entry points by name.
server_library="$install/bin/wineserver.so"
[ -f "$server_library" ] || fail "bin/wineserver.so is missing."
for entry in init run connect stop terminate; do
    nm -gU "$server_library" | grep -q " ___wine_paneport_server_$entry\$" \
        || fail "bin/wineserver.so does not export __wine_paneport_server_$entry."
done
say "bin/wineserver.so exports the five entry points of single-process mode"

# bin/wine is Wine's launcher, which execs the loader in lib/wine/aarch64-unix as the program's
# process. macOS kills an arm64 executable whose segments are not aligned to its 16 KiB pages.
# It zeroes x18 on every return to user mode, as iOS does, unless the main executable names the
# macOS 12 SDK or an older one. The loader must not name such an SDK: the fork keeps the TEB
# pointer in the thread's TSD, and the cases of multi mode must run under the rule of the device,
# so that a use of x18 that is left in a PE module shows (docs/design/runtime-ios.md section 6).
for loader in "$install/bin/wine" "$install/lib/wine/aarch64-unix/wine"; do
    [ -f "$loader" ] || fail "${loader#"$install/"} is missing."
    name=${loader#"$install/"}
    sdk=$(vtool -show-build "$loader" 2>/dev/null | awk '$1 == "sdk" { print $2 }' | head -n 1)
    # The list is captured first, because a loop over a command substitution ignores its status:
    # an otool that fails or lists nothing would leave nothing to check.
    segments=$(otool -l "$loader" | awk '$1 == "vmaddr" { print $2 }') \
        || fail "otool cannot read the segments of $name."
    [ -n "$segments" ] || fail "otool lists no segment of $name."
    misaligned=0
    for address in $segments; do
        [ $((address % 16384)) -eq 0 ] || misaligned=$((misaligned + 1))
    done
    say "$name: SDK version ${sdk:-unknown}, $misaligned segments not aligned to 16 KiB"
    [ "$misaligned" -eq 0 ] || fail "$name has segments that macOS will not map."
done
case ${sdk%%.*} in
    '' | *[!0-9]*) fail "vtool shows no SDK version of lib/wine/aarch64-unix/wine." ;;
esac
[ "${sdk%%.*}" -ge 13 ] \
    || fail "lib/wine/aarch64-unix/wine names SDK $sdk, so macOS would keep x18 for it, which iOS never does."

# S4.2.2: every module that the build made for a PE architecture is installed in that
# architecture's folder, and a sample of modules has the machine type of its folder. The folders
# are those of the architectures that configure's --enable-archs names: arm64ec and aarch64
# share aarch64-windows, whose libraries are ARM64X files, and i386 has i386-windows. A folder
# that the install left out fails the check.
readobj="$LLVM_MINGW/bin/llvm-readobj"
problems=0
for arch in aarch64-windows i386-windows; do
    folder="$install/lib/wine/$arch"
    if [ ! -d "$folder" ]; then
        say "$arch: the folder is missing" >&2
        problems=1
        continue
    fi
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
    # The 64-bit libraries are hybrid ARM64X files, which hold the ARM64EC code that FEX runs;
    # the programs, such as wineboot.exe, are plain ARM64 and exist for that architecture only.
    samples="ntdll.dll kernel32.dll kernelbase.dll ucrtbase.dll user32.dll"
    programs=""
    case "$arch" in
        aarch64-windows) expected=IMAGE_FILE_MACHINE_ARM64X programs="wineboot.exe" ;;
        i386-windows) expected=IMAGE_FILE_MACHINE_I386 ;;
        *) expected="" ;;
    esac
    # shellcheck disable=SC2086 # The sample names are words of their own.
    for module in $samples $programs; do
        if [ ! -f "$folder/$module" ]; then
            say "$arch: $module is missing" >&2
            problems=1
            continue
        fi
        headers=$("$readobj" --file-headers "$folder/$module")
        machine=$(printf '%s\n' "$headers" | sed -n 's/^ *Machine: \([A-Z0-9_]*\).*/\1/p' | head -n 1)
        # The dump is read whole before it is searched, because a pipe into grep -q would end
        # llvm-readobj early under pipefail.
        load_config=$("$readobj" --coff-load-config "$folder/$module" 2>/dev/null || true)
        hybrid=""
        if [[ $load_config =~ CHPEMetadata(Pointer)?:\ 0x0*[1-9A-Fa-f] ]]; then
            hybrid=", with ARM64EC code (CHPE metadata)"
        fi
        say "$arch: $module is $machine$hybrid"
        wanted=$expected
        case " $programs " in
            *" $module "*) wanted=IMAGE_FILE_MACHINE_ARM64 ;;
        esac
        if [ -n "$wanted" ] && [ "$machine" != "$wanted" ]; then
            say "$arch: $module should be $wanted" >&2
            problems=1
        fi
    done
done
[ "$problems" -eq 0 ] || fail "the installed PE modules are incomplete or of the wrong machine type; see above."

# S4.4.5: no native PE module names x18 or has a TLS directory, since iOS zeroes x18 and the
# fork keeps the TEB pointer in the thread's TSD (docs/design/runtime-ios.md section 6). The one
# function that is allowed saves x18 with the other registers of a context. And no object,
# archive or module holds the symbol of FFmpeg's crc.S for ARM64, which is under the GPL and
# which the fork leaves out (docs/design/runtime-macos-harness.md section 10).
checked=$(date +%s)
"$pe_check" --allow 'ntdll.dll:RtlCaptureContext' \
    --forbid-symbol ff_crc32_aarch64 --objects "$work" "$install/lib/wine/aarch64-windows" \
    || fail "the PE modules failed the checks of eng/mac/check-pe-modules.sh; see above."
say "checked the native PE modules in $(($(date +%s) - checked)) s"
say "ready"
