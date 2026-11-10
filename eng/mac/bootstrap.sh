#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Prepares a Mac to build Paneport's iOS side and its native host, in CI and on a local Mac
# alike. It checks the .NET SDK and Xcode against their pins, installs what the chosen parts
# need, and checks that too. tools/CheckToolchain.cs makes every comparison, so the pins are
# read in one place. The script stops with a non-zero exit code, after saying what to do,
# when something is missing or does not match. It never touches certificates, provisioning
# profiles or pairing files (C-0002 section 6, C-0009 section 5).
#
#   eng/mac/bootstrap.sh [--ios] [--native]
#
#   --ios     the iOS build: the iOS workload from the pinned workload set, and CMake for the
#             host framework
#   --native  the native checks: CMake, and the LLVM release of clang-format and clang-tidy
#
# With neither option it prepares both. CMake and LLVM come from the downloads that
# eng/toolchain.json pins, each checked against its SHA-256 before it is unpacked, into
# $PANEPORT_TOOLS_DIR, by default ~/Library/Caches/paneport-tools, outside the repository.
# Under GitHub Actions their folders join the PATH of the later steps; elsewhere the script
# prints the line that puts them on the PATH.
#
# Bash 3.2, which macOS ships (C-0010 section 1). docs/design/dev-environment.md describes
# how to use it.

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

want_ios=false
want_native=false
for argument in "$@"; do
    case "$argument" in
        --ios) want_ios=true ;;
        --native) want_native=true ;;
        *) fail "unknown option $argument. Usage: eng/mac/bootstrap.sh [--ios] [--native]" ;;
    esac
done
if [ "$want_ios" = false ] && [ "$want_native" = false ]; then
    want_ios=true
    want_native=true
fi

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

# The options of tools/CheckToolchain.cs that the final check passes, and the folders that
# join the PATH.
check_options="--xcode"
tool_paths=""

if [ "$want_ios" = true ]; then
    # The packs come only from the package source that eng/nuget.config pins. The SDK refuses
    # a plain install in a folder this user cannot write before it looks at what is
    # installed, so a workload that the folder's owner has installed is recognized first.
    workload_set=$(plutil -extract tools.ios-workload-set.version raw -o - eng/toolchain.json) \
        || fail "eng/toolchain.json has no ios-workload-set pin that plutil can read."
    if dotnet run tools/CheckToolchain.cs -- --ios-workload >/dev/null 2>&1; then
        say "the ios workload from workload set $workload_set is already installed"
    else
        say "installing the ios workload from workload set $workload_set"
        install_command=$(printf 'dotnet workload install ios --version %q --configfile %q' \
            "$workload_set" "$repo/eng/nuget.config")
        dotnet workload install ios --version "$workload_set" --configfile eng/nuget.config \
            || fail "dotnet workload install failed; see the lines above. If this user cannot write to the .NET \
installation, its owner runs this command, and then this script runs again: $install_command"
    fi
    check_options="$check_options --ios-workload"
fi

tools_dir=${PANEPORT_TOOLS_DIR:-$HOME/Library/Caches/paneport-tools}

# Reads one value of a pinned download from eng/toolchain.json.
pin_value() {
    plutil -extract "tools.$1.$2" raw -o - eng/toolchain.json 2>/dev/null \
        || fail "eng/toolchain.json has no $2 for $1 that plutil can read."
}

# Installs the pinned download of a tool into $tools_dir/<tool>-<version>, unless a finished
# install of the same download is there, and adds its program folder to tool_paths. The
# second argument is that folder inside the unpacked archive; the others are the archive's
# paths to unpack, all of it when none is given.
install_download() {
    tool=$1
    bin=$2
    shift 2
    version=$(pin_value "$tool" version)
    url=$(pin_value "$tool" downloads.osx-arm64.url)
    sha256=$(pin_value "$tool" downloads.osx-arm64.sha256)
    target="$tools_dir/$tool-$version"

    if [ -f "$target/.paneport-sha256" ] && [ "$(cat "$target/.paneport-sha256")" = "$sha256" ]; then
        say "$tool $version is already installed"
    else
        say "installing $tool $version from $url"
        mkdir -p "$tools_dir"
        work=$(mktemp -d "$tools_dir/.download.XXXXXX")
        curl --fail --silent --show-error --location --retry 3 --output "$work/archive" "$url" \
            || { rm -rf "$work"; fail "the download of $tool $version failed; see the line above."; }
        found=$(shasum -a 256 "$work/archive" | cut -d ' ' -f 1)
        if [ "$found" != "$sha256" ]; then
            rm -rf "$work"
            fail "the download of $tool $version has the SHA-256 $found, but eng/toolchain.json pins $sha256."
        fi

        # The archive holds one folder, named after the release; only what the patterns name
        # is unpacked from it, or everything when there is no pattern.
        mkdir "$work/unpacked"
        if [ $# -gt 0 ]; then
            unpacked=true
            tar -x -f "$work/archive" -C "$work/unpacked" "$@" || unpacked=false
        else
            unpacked=true
            tar -x -f "$work/archive" -C "$work/unpacked" || unpacked=false
        fi
        [ "$unpacked" = true ] \
            || { rm -rf "$work"; fail "the archive of $tool $version could not be unpacked."; }
        top=$(ls "$work/unpacked")
        if [ "$(printf '%s\n' "$top" | wc -l | tr -d ' ')" != 1 ]; then
            rm -rf "$work"
            fail "the archive of $tool $version does not hold exactly one folder."
        fi
        rm -rf "$target"
        mv "$work/unpacked/$top" "$target"
        printf '%s\n' "$sha256" > "$target/.paneport-sha256"
        rm -rf "$work"
    fi

    [ -d "$target/$bin" ] || fail "$tool $version has no folder $bin."
    tool_paths="$tool_paths:$target/$bin"
}

# CMake builds the host framework for --ios and the test builds for --native. Of the LLVM
# release, the native checks need only clang-format, clang-tidy, the libraries they may load
# and clang's own headers, which clang-tidy reads. A pattern's * also matches a /.
install_download cmake CMake.app/Contents/bin
check_options="$check_options --cmake"
installed="CMake"
if [ "$want_native" = true ]; then
    install_download llvm bin \
        --include '*/bin/clang-format' --include '*/bin/clang-tidy' \
        --include '*/lib/clang/*/include' --include '*/lib/*.dylib'
    check_options="$check_options --llvm"
    installed="CMake, clang-format and clang-tidy"
fi
tool_paths=${tool_paths#:}

say "checking what is installed against its pins"
# shellcheck disable=SC2086 # The options are words of their own.
PATH="$tool_paths:$PATH" dotnet run tools/CheckToolchain.cs -- $check_options \
    || fail "something installed does not match its pin; see the lines above."

if [ -n "${GITHUB_PATH:-}" ]; then
    printf '%s\n' "$tool_paths" | tr ':' '\n' >> "$GITHUB_PATH"
    say "the later steps find the pinned $installed first on the PATH"
else
    say "put the pinned tools first on the PATH of this shell with:"
    printf '  export PATH=%q:"$PATH"\n' "$tool_paths"
fi

say "ready"
