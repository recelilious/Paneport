#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Selects the Xcode that eng/toolchain.json pins on a GitHub-hosted runner image, for the
# later steps of a job: it writes DEVELOPER_DIR to $GITHUB_ENV.
#
#   eng/mac/select-xcode.sh
#
# The image keeps several releases side by side, each in a version-named folder such as
# /Applications/Xcode_27.1.app, and its default Xcode can move past the pin while the pinned
# one is still installed. Some of those folders are links to other releases, such as a
# release candidate. The bootstrap's check compares only the version number, which a release
# candidate shares with its final release, so this script refuses a folder that resolves to
# a release candidate or a beta. A local Mac selects its Xcode with xcode-select instead
# (docs/design/dev-environment.md section 4). Bash 3.2 (C-0010 section 1).

set -euo pipefail

cd "$(dirname "$0")/../.."

pin=$(plutil -extract tools.xcode.version raw -o - eng/toolchain.json)
app="/Applications/Xcode_$pin.app"
if [ ! -d "$app" ]; then
    echo "::error title=Xcode::The image has no Xcode $pin."
    exit 1
fi

resolved=$(cd "$app" && pwd -P)
case "$(printf '%s' "$resolved" | tr '[:upper:]' '[:lower:]')" in
    *release_candidate*|*beta*|*_rc*)
        echo "::error title=Xcode::Xcode $pin on the image is $resolved, not a final release."
        exit 1
        ;;
esac

developer_dir="$app/Contents/Developer"
if [ -n "${GITHUB_ENV:-}" ]; then
    echo "DEVELOPER_DIR=$developer_dir" >> "$GITHUB_ENV"
fi
echo "Selected $developer_dir for the later steps."
