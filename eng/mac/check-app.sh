#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks a signed app bundle of the ios job (S1.3.2, S1.3.3, S1.3.9, S1.3.10).
#
#   eng/mac/check-app.sh <app> <main|test>
#
# - Info.plist: the bundle identifier of D3, the flavor's display name, iOS 26.0, the scene
#   delegate, and every key of C-0009 section 4 with its value.
# - The executable: arm64 code only, linked against @rpath/paneport_host.framework/paneport_host
#   with @executable_path/Frameworks among its run paths.
# - Frameworks/paneport_host.framework: present, with the bundle identifier derived from D3, and
#   the host build of the flavor: no pp_test_ export in the main app, the test exports in the
#   device test flavor.
# - Signatures: every one ad hoc, with no team identifier, none merely the linker's, and each
#   verifies; no provisioning profile in the bundle.
# - Entitlements: exactly com.apple.developer.kernel.increased-memory-limit, set to true, and
#   com.apple.developer.kernel.extended-virtual-addressing, set to true, as well when
#   PANEPORT_EXTENDED_VIRTUAL_ADDRESSING is true, the switch that eng/mac/sign-adhoc.sh reads
#   (C-0009 section 4).
#
# Every problem is reported before the script fails. Bash 3.2 (C-0010 section 1).

set -euo pipefail

if [ $# -ne 2 ] || { [ "$2" != main ] && [ "$2" != test ]; }; then
    echo "usage: eng/mac/check-app.sh <app> <main|test>" >&2
    exit 2
fi
app=$1
flavor=$2

bundle_id=io.github.recelilious.paneport
framework_id=$bundle_id.host
if [ "$flavor" = main ]; then
    display_name=Paneport
else
    display_name="Paneport Test"
fi

problems=0
problem() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=App bundle::$*"
    fi
    echo "check-app: $*" >&2
    problems=$((problems + 1))
}

value() {
    plutil -extract "$2" raw -o - "$1" 2>/dev/null || echo "(none)"
}

expect() {
    found=$(value "$1" "$2")
    [ "$found" = "$3" ] || problem "$(basename "$(dirname "$1")")/$(basename "$1") sets $2 to $found, not $3."
}

echo "Checking $app ($flavor)"
[ -d "$app" ] || { problem "$app is not an app bundle."; exit 1; }
plist="$app/Info.plist"
plutil -lint "$plist" >/dev/null || problem "$plist is not a valid property list."

expect "$plist" CFBundleIdentifier "$bundle_id"
expect "$plist" CFBundleDisplayName "$display_name"
expect "$plist" MinimumOSVersion 26.0
expect "$plist" UIDeviceFamily.0 1
expect "$plist" UIApplicationSceneManifest.UISceneConfigurations.UIWindowSceneSessionRoleApplication.0.UISceneDelegateClassName \
    PaneportSceneDelegate
expect "$plist" LSApplicationCategoryType public.app-category.games
for key in LSSupportsGameMode GCSupportsGameMode CADisableMinimumFrameDurationOnPhone UIFileSharingEnabled \
    LSSupportsOpeningDocumentsInPlace; do
    expect "$plist" "$key" true
done
short_version=$(value "$plist" CFBundleShortVersionString)
build_number=$(value "$plist" CFBundleVersion)
echo "Version: $short_version ($build_number)"
[[ "$short_version" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || problem "CFBundleShortVersionString is $short_version, not MAJOR.MINOR.PATCH."
[[ "$build_number" =~ ^[0-9]+$ ]] || problem "CFBundleVersion is $build_number, not an integer."

executable="$app/$(value "$plist" CFBundleExecutable)"
[ -f "$executable" ] || problem "the bundle has no executable $executable."
if [ -f "$executable" ]; then
    if architectures=$(lipo -archs "$executable"); then
        [ "$architectures" = arm64 ] || problem "the executable holds $architectures, not arm64 alone."
    else
        problem "lipo cannot read the executable's architectures."
    fi
    otool -L "$executable" | grep -F -q "@rpath/paneport_host.framework/paneport_host" \
        || problem "the executable does not link @rpath/paneport_host.framework/paneport_host."
    otool -l "$executable" | awk '$1 == "path" { print $2 }' | grep -F -x -q "@executable_path/Frameworks" \
        || problem "the executable's run paths lack @executable_path/Frameworks."
fi

framework="$app/Frameworks/paneport_host.framework"
if [ -f "$framework/paneport_host" ]; then
    expect "$framework/Info.plist" CFBundleIdentifier "$framework_id"
    test_exports=$(nm -gU "$framework/paneport_host" | awk '{ print $NF }' | grep -c '^_pp_test_' || true)
    if [ "$flavor" = main ] && [ "$test_exports" -ne 0 ]; then
        problem "the main app embeds a host with $test_exports test exports; it must embed the ios-device build."
    elif [ "$flavor" = test ] && [ "$test_exports" -eq 0 ]; then
        problem "the device test flavor embeds a host without test exports; it must embed the ios-device-test build."
    fi
    echo "Host framework: $test_exports test exports."
else
    problem "the bundle has no Frameworks/paneport_host.framework."
fi

[ ! -e "$app/embedded.mobileprovision" ] || problem "the bundle holds a provisioning profile."

# Every signed item of the bundle: the app, and each framework or library it embeds.
signed=("$app")
if [ -d "$app/Frameworks" ]; then
    while IFS= read -r item; do
        signed+=("$item")
    done < <(find "$app/Frameworks" -mindepth 1 -maxdepth 1 \( -name '*.framework' -o -name '*.dylib' \) | sort)
fi
for item in "${signed[@]}"; do
    details=$(codesign -dv "$item" 2>&1 || true)
    if ! grep -F -q "Signature=adhoc" <<< "$details"; then
        problem "$(basename "$item") is not signed ad hoc."
    fi
    if ! grep -F -q "TeamIdentifier=not set" <<< "$details"; then
        problem "$(basename "$item") names a team identifier."
    fi
    if grep -F -q "linker-signed" <<< "$details"; then
        problem "$(basename "$item") carries only the linker's signature."
    fi
    codesign --verify --strict "$item" 2>/dev/null || problem "the signature of $(basename "$item") does not verify."
done

# The keys in jq's order, which sorts them, each followed by a space.
expected_keys="com.apple.developer.kernel.increased-memory-limit "
if [ "${PANEPORT_EXTENDED_VIRTUAL_ADDRESSING:-false}" = true ]; then
    expected_keys="com.apple.developer.kernel.extended-virtual-addressing $expected_keys"
fi

entitlements=$(mktemp)
trap 'rm -f "$entitlements"' EXIT
if codesign -d --entitlements - --xml "$app" > "$entitlements" 2>/dev/null && [ -s "$entitlements" ]; then
    if keys=$(plutil -convert json -o - "$entitlements" | jq -r 'keys[]' | tr '\n' ' '); then
        echo "Entitlements: $keys"
        [ "$keys" = "$expected_keys" ] || problem "the app's entitlements are ${keys% }, not exactly ${expected_keys% }."
        for key in $expected_keys; do
            [ "$(plutil -convert json -o - "$entitlements" | jq -r --arg key "$key" '.[$key]')" = true ] \
                || problem "$key is not true."
        done
    else
        problem "the app's entitlements cannot be read as a property list."
    fi
else
    problem "the app's signature holds no entitlements."
fi

if [ "$problems" -ne 0 ]; then
    echo "check-app: $problems problems in $app." >&2
    exit 1
fi
echo "$app passes."
