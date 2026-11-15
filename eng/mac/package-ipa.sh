#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Packages a signed app bundle as an .ipa (S1.3.9, C-0009 section 3 stage 4): a zip archive
# whose only top-level folder is Payload/, which holds the app.
#
#   eng/mac/package-ipa.sh <app> <ipa>
#
# ditto keeps the executable's file mode and the bundle's symbolic links, which a sideloading
# tool needs, and writes no resource forks. Bash 3.2 (C-0010 section 1).

set -euo pipefail

if [ $# -ne 2 ]; then
    echo "usage: eng/mac/package-ipa.sh <app> <ipa>" >&2
    exit 2
fi
app=$1
ipa=$2

if [ ! -d "$app" ]; then
    echo "package-ipa: $app is not an app bundle." >&2
    exit 1
fi

work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir "$work/Payload"
ditto "$app" "$work/Payload/$(basename "$app")"
rm -f "$ipa"
ditto -c -k --norsrc --keepParent "$work/Payload" "$ipa"
echo "package-ipa: wrote $ipa ($(wc -c < "$ipa" | tr -d ' ') bytes)."
