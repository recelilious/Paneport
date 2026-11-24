#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Strips the Mach-O files of an app bundle of their debugging and local symbols, before the bundle is
# signed (M9.1; C-0009 section 6: release device builds are stripped of symbols). dotnet build in
# Release, as the ios and release jobs run it, already leaves every Mach-O file of both apps
# stripped, as .NET for iOS does for device builds by default; this step makes sure of it whatever
# the SDK, a package's prebuilt framework or a build property does, so that no debug map, which
# names the object files of a build and their folders, reaches a release.
#
#   eng/release/strip-binaries.sh <app>
#
# It runs Xcode's strip with -S -x on the main executable and on the binary of each framework and
# library in Frameworks/: -S removes the debugging symbols, -x the local ones. The global symbols
# stay, among them the executable's export of mono_set_crash_chaining, which eng/mac/check-app.sh
# checks afterwards. A file that already has a signature loses it here, and eng/mac/sign-adhoc.sh
# signs every file again. It counts each file's debugging entries before and after, as nm -a lists
# them, and fails once every file is done if one keeps any. Strip's own marker does not count: into
# a file that it leaves with indirect symbols but no local symbol, strip writes one N_OPT entry
# named radr://5614542 as the first symbol, so that no indirect symbol gets the index 0 (cctools
# strip.c, radar 5614542). It holds no debug information, and tools/ScanPackage.cs lets it pass too
# (docs/design/release-engineering.md section 9.7). Bash 3.2 (C-0010 section 1).

set -euo pipefail

if [ $# -ne 1 ]; then
    echo "usage: eng/release/strip-binaries.sh <app>" >&2
    exit 2
fi
app=$1

report() {
    if [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Strip::$*"
    fi
    echo "strip-binaries: $*" >&2
}

fail() {
    report "$@"
    exit 1
}

# The number of debugging entries (STABS) in the symbol tables of a Mach-O file, besides strip's
# marker: nm -a lists every entry whose type has a bit of N_STAB set with the type "-", then its
# section, description, kind and name, and strip's marker as "- 00 0000 OPT radr://5614542". The
# package scan checks the marker's value and place as well.
debug_entries() {
    local listing
    listing=$(xcrun nm -a -p "$1" 2>/dev/null) || return 1
    printf '%s\n' "$listing" | awk '
        $2 == "-" && !(NF == 6 && $3 == "00" && $4 == "0000" && $5 == "OPT" &&
            $6 == "radr://5614542") { count++ }
        END { print count + 0 }'
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

status=0
for file in "${files[@]}"; do
    name=${file#"$app"/}
    before=$(wc -c < "$file" | tr -d ' ')
    entries_before=$(debug_entries "$file") || fail "nm cannot read $name."
    xcrun strip -S -x "$file" || fail "strip failed on $name."
    after=$(wc -c < "$file" | tr -d ' ')
    entries_after=$(debug_entries "$file") || fail "nm cannot read $name after strip."
    echo "strip-binaries: $name: $before -> $after bytes, $entries_before -> $entries_after" \
        "debugging entries besides strip's marker."
    if [ "$entries_after" -ne 0 ]; then
        report "$name keeps $entries_after debugging entries after strip -S -x."
        status=1
    fi
done
exit "$status"
