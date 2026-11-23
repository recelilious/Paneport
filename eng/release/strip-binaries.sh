#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Strips the Mach-O files of an app bundle of their debugging and local symbols, before the bundle is
# signed (M9.1; C-0009 section 6: release device builds are stripped of symbols). The .NET build as
# the ios and release jobs run it, dotnet build in Release, leaves the linker's debug map, STABS
# entries that name the object files it linked, in the executable and in every framework, as the full
# package scan of #37 showed; the prebuilt frameworks of packages arrive with them too.
#
#   eng/release/strip-binaries.sh <app>
#
# It runs Xcode's strip with -S -x on the main executable and on the binary of each framework and
# library in Frameworks/: -S removes the debugging symbols, -x the local ones. The global symbols
# stay, among them the executable's export of mono_set_crash_chaining, which eng/mac/check-app.sh
# checks afterwards. A file that already has a signature loses it here, and eng/mac/sign-adhoc.sh
# signs every file again. Bash 3.2 (C-0010 section 1).

set -euo pipefail

if [ $# -ne 1 ]; then
    echo "usage: eng/release/strip-binaries.sh <app>" >&2
    exit 2
fi
app=$1

fail() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Strip::$*"
    fi
    echo "strip-binaries: $*" >&2
    exit 1
}

[ -d "$app" ] || fail "$app is not an app bundle."
executable=$(plutil -extract CFBundleExecutable raw -o - "$app/Info.plist" 2>/dev/null) ||
    fail "$app/Info.plist names no executable."
[ -f "$app/$executable" ] || fail "$app lacks its executable $executable."

# The executable, then each framework's binary, which its Info.plist names, and each loose library.
files=("$app/$executable")
if [ -d "$app/Frameworks" ]; then
    while IFS= read -r framework; do
        binary=$(plutil -extract CFBundleExecutable raw -o - "$framework/Info.plist" 2>/dev/null) ||
            fail "$framework/Info.plist names no executable."
        [ -f "$framework/$binary" ] || fail "$framework lacks its binary $binary."
        files+=("$framework/$binary")
    done < <(find "$app/Frameworks" -mindepth 1 -maxdepth 1 -name '*.framework' -type d | sort)
    while IFS= read -r library; do
        files+=("$library")
    done < <(find "$app/Frameworks" -mindepth 1 -maxdepth 1 -name '*.dylib' -type f | sort)
fi

for file in "${files[@]}"; do
    before=$(wc -c < "$file" | tr -d ' ')
    xcrun strip -S -x "$file" || fail "strip failed on ${file#"$app"/}."
    after=$(wc -c < "$file" | tr -d ' ')
    echo "strip-binaries: ${file#"$app"/}: $before -> $after bytes."
done
