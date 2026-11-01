#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Prepares a Mac to build Paneport's iOS side, in CI and on a local Mac alike. It checks the
# .NET SDK and Xcode against their pins, installs the iOS workload from the pinned workload
# set, and checks that too. tools/CheckToolchain.cs makes every comparison, so the pins are
# read in one place. The script stops with a non-zero exit code, after saying what to do,
# when something is missing or does not match. It never touches certificates, provisioning
# profiles or pairing files (C-0002 section 6, C-0009 section 5).
#
#   eng/mac/bootstrap.sh
#
# Bash 3.2, which macOS ships (C-0010 section 1). docs/design/dev-environment.md describes
# how to use it, and M1.2 adds the CMake and LLVM steps.

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)
cd "$repo"

say() {
    printf 'bootstrap: %s\n' "$*"
}

fail() {
    printf 'bootstrap: %s\n' "$*" >&2
    exit 1
}

# The iOS toolchain runs only on macOS, and Paneport builds for arm64 alone. A shell under
# Rosetta reports x86_64 from uname, so ask the hardware.
[ "$(uname -s)" = Darwin ] || fail "this script needs macOS, not $(uname -s)."
[ "$(sysctl -n hw.optional.arm64 2>/dev/null || echo 0)" = 1 ] || fail "this script needs a Mac with Apple silicon."

command -v dotnet >/dev/null 2>&1 \
    || fail "dotnet is not on the PATH. Install the .NET SDK version that global.json pins \
(https://dotnet.microsoft.com/download/dotnet/10.0), then run this script again."

# Xcode first, so that a wrong Xcode stops the script before anything is installed.
say "checking the .NET SDK and Xcode against their pins"
dotnet run tools/CheckToolchain.cs -- --xcode \
    || fail "the SDK or Xcode does not match its pin; the lines above name both versions. Install the pinned \
version, or select the pinned Xcode with xcode-select or DEVELOPER_DIR, then run this script again."

# The packs come only from the package source that eng/nuget.config pins.
workload_set=$(plutil -extract tools.ios-workload-set.version raw -o - eng/toolchain.json) \
    || fail "eng/toolchain.json has no ios-workload-set pin that plutil can read."
say "installing the ios workload from workload set $workload_set"
dotnet workload install ios --version "$workload_set" --configfile eng/nuget.config \
    || fail "dotnet workload install failed; see the lines above. If the .NET installation is not writable \
by this user, install the workload as its owner with the same command, then run this script again."

say "checking the installed workload set against its pin"
dotnet run tools/CheckToolchain.cs -- --xcode --ios-workload \
    || fail "the installed workload set does not match its pin; see the lines above."

say "ready to build Paneport.iOS"
