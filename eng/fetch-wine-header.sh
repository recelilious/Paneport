#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Fetches the Wine fork's entry header, include/wine/paneport.h, at the commit that this
# repository records for external/Paneport-wine, into build/wine-header/include/wine/. The
# native host includes that header alone of Wine's (C-0005 section 3), and the builds of the
# host that do not check out the fork, such as the native and ios jobs, find it there
# (src/native/CMakeLists.txt). The header holds declarations only, under the fork's license.
#
#   eng/fetch-wine-header.sh
#
# It needs git and curl. Bash 3.2 (C-0010 section 1).

set -euo pipefail

repo=$(cd "$(dirname "$0")/.." && pwd)
commit=$(git -C "$repo" rev-parse HEAD:external/Paneport-wine)
case $commit in
    [0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f][0-9a-f]*) ;;
    *)
        printf 'fetch-wine-header: no commit is recorded for external/Paneport-wine.\n' >&2
        exit 1
        ;;
esac

target="$repo/build/wine-header/include/wine"
mkdir -p "$target"
url="https://raw.githubusercontent.com/recelilious/Paneport-wine/$commit/include/wine/paneport.h"
curl --fail --silent --show-error --location --retry 3 --output "$target/paneport.h.part" "$url"
# A header without its guard is not the one that the fork keeps.
grep -q '__WINE_WINE_PANEPORT_H' "$target/paneport.h.part" || {
    printf 'fetch-wine-header: %s is not the entry header.\n' "$url" >&2
    exit 1
}
mv "$target/paneport.h.part" "$target/paneport.h"
printf 'fetch-wine-header: include/wine/paneport.h of the Wine fork at %s\n' "$commit"
