#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks the two builds of paneport_host.framework (S1.2.10): the one of the ios-device
# preset, which the main app embeds, and the one of ios-device-test, which the device test
# flavor embeds.
#
#   eng/mac/check-framework.sh <ios-device framework> <ios-device-test framework>
#
# For each build: the bundle's Info.plist, a binary with arm64 code for iOS devices only, and
# the install name @rpath/paneport_host.framework/paneport_host that the .NET bindings import
# (C-0009 section 4). For the exports: the ios-device build exports exactly the functions that
# the public headers declare with PP_EXPORT, outside paneport/testing.h; the ios-device-test
# build exports those and exactly the functions of paneport/testing.h. Bash 3.2 (C-0010
# section 1).

set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: eng/mac/check-framework.sh <ios-device framework> <ios-device-test framework>" >&2
    exit 2
fi
release=$1
test_build=$2
headers="$(cd "$(dirname "$0")/../.." && pwd)/src/native/include/paneport"

problems=0
problem() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Host framework::$*"
    fi
    echo "check-framework: $*" >&2
    problems=$((problems + 1))
}

# The functions that headers declare: a PP_EXPORT line names its function right before the
# opening parenthesis of its parameter list.
declared() {
    sed -n 's/^PP_EXPORT .*[ *]\(pp_[a-z0-9_]*\)(.*/\1/p' "$@" | sort -u
}

# The external symbols that a binary defines, without the leading underscore of C names.
exported() {
    nm -gU "$1" | awk '{ print $NF }' | sed 's/^_//' | sort -u
}

check_bundle() {
    framework=$1
    binary="$framework/paneport_host"
    plist="$framework/Info.plist"
    echo "Checking $framework"
    if [ ! -f "$binary" ] || [ ! -f "$plist" ]; then
        problem "$framework has no binary paneport_host or no Info.plist."
        return
    fi

    plutil -lint "$plist" >/dev/null || problem "$plist is not a valid property list."
    for pair in CFBundleExecutable=paneport_host CFBundlePackageType=FMWK MinimumOSVersion=26.0 \
        CFBundleSupportedPlatforms.0=iPhoneOS; do
        key=${pair%%=*}
        expected=${pair#*=}
        found=$(plutil -extract "$key" raw -o - "$plist" 2>/dev/null || echo "(none)")
        [ "$found" = "$expected" ] || problem "$plist sets $key to $found, not $expected."
    done

    install_name=$(otool -D "$binary" | sed -n '2p')
    echo "  install name: $install_name"
    [ "$install_name" = "@rpath/paneport_host.framework/paneport_host" ] \
        || problem "$binary has the install name $install_name."

    architectures=$(lipo -archs "$binary")
    echo "  architectures: $architectures"
    [ "$architectures" = arm64 ] || problem "$binary holds $architectures, not arm64 alone."

    # LC_BUILD_VERSION names the platform: 2, or IOS in newer tools, for iOS devices.
    platform=$(otool -l "$binary" | awk '/cmd LC_BUILD_VERSION/ { found = 1 } found && $1 == "platform" { print $2; exit }')
    echo "  platform: $platform"
    case "$platform" in
        2 | IOS | ios) ;;
        *) problem "$binary is built for platform $platform, not for iOS devices." ;;
    esac
}

check_bundle "$release"
check_bundle "$test_build"

expected_release=$(declared "$headers"/common.h "$headers"/host.h "$headers"/memory.h)
expected_test_only=$(declared "$headers"/testing.h)
[ -n "$expected_release" ] && [ -n "$expected_test_only" ] \
    || problem "no PP_EXPORT declaration was found in $headers."
expected_test=$(printf '%s\n%s\n' "$expected_release" "$expected_test_only" | sort -u)

# The test exports live in paneport/testing.h alone, and it holds nothing else.
for function in $expected_release; do
    case "$function" in
        pp_test_*) problem "$function is declared outside paneport/testing.h, where the test exports belong." ;;
    esac
done
for function in $expected_test_only; do
    case "$function" in
        pp_test_*) ;;
        *) problem "paneport/testing.h declares $function, which is not a pp_test_ function." ;;
    esac
done
other_headers=$(cd "$headers" && ls -- *.h | grep -v -x -e common.h -e host.h -e memory.h -e testing.h -e paneport.h || true)
[ -z "$other_headers" ] \
    || problem "add the headers $other_headers to eng/mac/check-framework.sh, which lists the headers of each build."

if [ -f "$release/paneport_host" ] && [ -f "$test_build/paneport_host" ]; then
    release_exports=$(exported "$release/paneport_host")
    test_exports=$(exported "$test_build/paneport_host")
    echo "Exports of the ios-device build:"
    printf '  %s\n' $release_exports
    echo "Exports that only the ios-device-test build adds:"
    printf '  %s\n' $(comm -13 <(printf '%s\n' "$release_exports") <(printf '%s\n' "$test_exports"))
    if [ "$release_exports" != "$expected_release" ]; then
        problem "the ios-device build does not export exactly the functions of the public headers:"
        diff <(printf '%s\n' "$expected_release") <(printf '%s\n' "$release_exports") >&2 || true
    fi
    if [ "$test_exports" != "$expected_test" ]; then
        problem "the ios-device-test build does not export exactly those and the pp_test_ functions:"
        diff <(printf '%s\n' "$expected_test") <(printf '%s\n' "$test_exports") >&2 || true
    fi
fi

if [ "$problems" -ne 0 ]; then
    echo "check-framework: $problems problems." >&2
    exit 1
fi
echo "Both frameworks pass."
