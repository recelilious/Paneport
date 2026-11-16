#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Signs an app bundle ad hoc (S1.3.9), as C-0009 section 6 describes for releases and plan
# section 5.2 asks for the builds of the ios job, so that a sideloading tool can read the
# entitlements it must request when it re-signs the app with the user's own Apple ID.
#
#   eng/mac/sign-adhoc.sh <app> <entitlements>
#
# Each embedded framework and library first, with `codesign --force --sign -`, then the app with
# `codesign --force --sign - --entitlements <entitlements>`. An ad-hoc signature holds no Apple
# identity and no provisioning profile (C-0009 section 5). With
# PANEPORT_EXTENDED_VIRTUAL_ADDRESSING=true, which the build property
# PaneportExtendedVirtualAddressing stands for, the app also declares
# extended-virtual-addressing (C-0009 section 4); the default build does not. Bash 3.2
# (C-0010 section 1).

set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: eng/mac/sign-adhoc.sh <app> <entitlements>" >&2
    exit 2
fi
app=$1
entitlements=$2

fail() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Ad-hoc signing::$*"
    fi
    echo "sign-adhoc: $*" >&2
    exit 1
}

[ -d "$app" ] || fail "$app is not an app bundle."
[ -f "$entitlements" ] || fail "$entitlements does not exist."
plutil -lint "$entitlements" >/dev/null || fail "$entitlements is not a valid property list."

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
cp "$entitlements" "$work/entitlements.plist"
if [ "${PANEPORT_EXTENDED_VIRTUAL_ADDRESSING:-false}" = true ]; then
    plutil -insert com.apple.developer.kernel.extended-virtual-addressing -bool true "$work/entitlements.plist"
    echo "sign-adhoc: the app also declares extended-virtual-addressing."
fi

# A framework or library inside the bundle is signed before the bundle that contains it, because
# the outer signature seals the inner ones.
if [ -d "$app/Frameworks" ]; then
    find "$app/Frameworks" -mindepth 1 -maxdepth 1 \( -name '*.framework' -o -name '*.dylib' \) -print |
        sort | while IFS= read -r item; do
            echo "sign-adhoc: signing $item"
            codesign --force --sign - --timestamp=none "$item"
        done
fi

echo "sign-adhoc: signing $app"
codesign --force --sign - --timestamp=none --generate-entitlement-der \
    --entitlements "$work/entitlements.plist" "$app"
codesign --verify --strict --verbose=2 "$app"
