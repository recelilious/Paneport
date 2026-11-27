#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT
# Makes the archive of the exact corresponding source of each LGPL component that is neither this
# repository nor a fork (C-0003 section 3, C-0009 section 6), as the component's "source" in
# eng/release/bundle-components.json names it and tools/GenerateNotices.cs --list-sources prints it.
# For each one it checks that the package which the lock files of the iOS heads resolve names the
# pinned commit and repository in its nuspec, fetches the upstream's release tag at depth 1 and
# checks that the tag points to the pinned commit, archives the listed paths at that commit with git
# archive, adds the files of each listed submodule at the commit that the upstream's tree records,
# and checks that the archive holds nothing outside those paths. Each archive goes to
# <folder>/<archive>.
#
#   eng/mac/make-source-archives.sh <folder>      from the repository root, after the restore
#   eng/mac/make-source-archives.sh --self-test   on repositories of its own, without the network
#
# It fetches over https only, and never uses GitHub's generated archives, which are not guaranteed
# to stay byte-identical. bsdtar joins the archives: macOS has it as tar, and TAR names another one.
# NUGET_PACKAGES, or else ~/.nuget/packages, holds the restored packages. Exit codes: 0 every archive
# is made, 1 a check failed, 2 usage. Bash 3.2 (C-0010 section 1).
set -euo pipefail

me=make-source-archives
tar_tool=${TAR:-tar}
packages=${NUGET_PACKAGES:-$HOME/.nuget/packages}
locks=(src/Paneport.iOS/packages.lock.json tests/device/packages.lock.json)
scheme=https
annotate=1
export GIT_TERMINAL_PROMPT=0

scratch=$(mktemp -d "${RUNNER_TEMP:-${TMPDIR:-/tmp}}/make-source-archives.XXXXXX")
trap 'rm -rf "$scratch"' EXIT

error() {
    echo "$me: $*" >&2
    if [ "$annotate" -eq 1 ] && [ "${GITHUB_ACTIONS:-}" = true ]; then
        echo "::error title=Source archives::$*"
    fi
}

lower() {
    printf '%s' "$1" | tr '[:upper:]' '[:lower:]'
}

# A repository URL without a final slash or .git, to compare two of them.
plain_url() {
    local url=${1%/}
    printf '%s' "${url%.git}"
}

# Makes an empty repository whose archives hold each file as the commit stores it. The archived
# tree's attributes must not leave a file out (export-ignore), rewrite one (export-subst, ident,
# filters), convert its line endings (text, eol) or re-encode it (working-tree-encoding), and
# neither may core.autocrlf, which Git for Windows turns on. So .git/info/attributes, which wins
# over the tree's .gitattributes and over the runner's global and system attributes files, unsets
# them all; archive also runs with core.autocrlf off, which unsetting text already makes moot.
init_repository() {
    git -c init.defaultBranch=main init -q "$1"
    mkdir -p "$1/.git/info"
    printf '* -text -ident -filter -working-tree-encoding -export-ignore -export-subst\n' > "$1/.git/info/attributes"
}

# git with only the scheme of this run allowed, and no prompt.
git_fetch() {
    git -c protocol.allow=never -c "protocol.$scheme.allow=always" "$@"
}

# The versions to which the lock files resolve a package, one per line.
resolved() {
    local id=$1
    shift
    awk -v key="\"$id\": {" '
        index($0, key) { inside = 1; next }
        inside && /"resolved":/ { line = $0; sub(/.*"resolved": *"/, "", line); sub(/".*/, "", line); print line; inside = 0; next }
        inside && /^[ \t]*}/ { inside = 0 }
    ' "$@" | sort -u
}

# An attribute of the nuspec's <repository> element, or nothing.
repository_attribute() {
    sed -n 's/.*<repository [^>]*'"$2"'="\([^"]*\)".*/\1/p' "$1" | head -n 1
}

check_tar() {
    if ! "$tar_tool" --version 2> /dev/null | grep -q bsdtar; then
        error "$tar_tool is not bsdtar, which joins the archives; TAR can name it."
        return 1
    fi
}

# Makes one archive in <out> from one line of --list-sources, in the empty folder <work>.
make_archive() {
    local component=$1 package=$2 repository=$3 tag=$4 commit=$5 archive=$6 paths=$7 submodules=$8 work=$9 out=${10}
    local versions version nuspec named url tagged prefix entries outside count n=0
    local repo=$work/repository sub mode type subcommit rest name subdir part
    local list=()
    local subs=()
    local parts=(0.tar)
    local joined=()

    versions=$(resolved "$package" "${locks[@]}")
    if [ -z "$versions" ] || [ "$(printf '%s\n' "$versions" | wc -l | tr -d ' ')" -ne 1 ]; then
        error "$component: the lock files resolve $package to '$(printf '%s' "$versions" | tr '\n' ' ')', not to one version."
        return 1
    fi

    version=$versions
    nuspec=$packages/$(lower "$package")/$(lower "$version")/$(lower "$package").nuspec
    if [ ! -f "$nuspec" ]; then
        error "$component: $nuspec is missing; restore the iOS heads first."
        return 1
    fi

    named=$(repository_attribute "$nuspec" commit)
    url=$(repository_attribute "$nuspec" url)
    if [ "$named" != "$commit" ]; then
        error "$component: $package $version names the commit ${named:-(none)} in its nuspec, but eng/release/bundle-components.json pins $commit. Move the pin in a reviewed change of its own (C-0009 section 6)."
        return 1
    fi

    if [ "$(plain_url "$url")" != "$(plain_url "$repository")" ]; then
        error "$component: $package $version names the repository ${url:-(none)} in its nuspec, but the pin names $repository."
        return 1
    fi

    init_repository "$repo"
    if ! git_fetch -C "$repo" fetch -q --depth 1 --no-tags "$repository" "+refs/tags/$tag:refs/tags/$tag"; then
        error "$component: the tag $tag cannot be fetched from $repository."
        return 1
    fi

    tagged=$(git -C "$repo" rev-parse -q --verify "refs/tags/$tag^{commit}" || true)
    if [ "$tagged" != "$commit" ]; then
        error "$component: the tag $tag of $repository points to ${tagged:-nothing}, not to the pinned commit $commit."
        return 1
    fi

    prefix=${archive%.tar.gz}/
    read -r -a list <<< "$paths"
    if ! git -c core.autocrlf=false -C "$repo" archive --format=tar --prefix="$prefix" -o "$work/0.tar" "$commit" -- "${list[@]}"; then
        error "$component: git archive of $commit failed; each of its paths must exist there: $paths"
        return 1
    fi

    if [ -n "$submodules" ]; then
        read -r -a subs <<< "$submodules"
        if ! git -C "$repo" show "$commit:.gitmodules" > "$work/gitmodules"; then
            error "$component: $commit has no .gitmodules, so it records no submodule."
            return 1
        fi
    fi

    for sub in ${subs[@]+"${subs[@]}"}; do
        n=$((n + 1))
        read -r mode type subcommit rest <<< "$(git -C "$repo" ls-tree "$commit" -- "$sub")" || true
        if [ "${mode:-}" != 160000 ] || [ "${type:-}" != commit ]; then
            error "$component: $sub is not a submodule at $commit."
            return 1
        fi

        name=$(git config -f "$work/gitmodules" --get-regexp '^submodule\..*\.path$' \
            | awk -v path="$sub" '$2 == path { sub(/^submodule\./, "", $1); sub(/\.path$/, "", $1); print $1; exit }')
        url=$(git config -f "$work/gitmodules" --get "submodule.$name.url" || true)
        case "$url" in
            "$scheme"://*) ;;
            *)
                error "$component: the submodule $sub has the URL '${url:-(none)}', not one over $scheme."
                return 1
                ;;
        esac

        subdir=$work/submodule-$n
        init_repository "$subdir"
        if ! git_fetch -C "$subdir" fetch -q --depth 1 --no-tags "$url" "$subcommit" \
            || [ "$(git -C "$subdir" rev-parse -q --verify "$subcommit^{commit}" || true)" != "$subcommit" ]; then
            error "$component: the commit $subcommit of $sub cannot be fetched from $url."
            return 1
        fi

        if ! git -c core.autocrlf=false -C "$subdir" archive --format=tar --prefix="$prefix$sub/" -o "$work/$n.tar" "$subcommit"; then
            error "$component: git archive of $sub at $subcommit failed."
            return 1
        fi

        parts+=("$n.tar")
        echo "$me: $component: $sub at $subcommit from $url."
    done

    for part in "${parts[@]}"; do
        joined+=("@$part")
    done

    if ! (cd "$work" && "$tar_tool" -c -f joined.tar "${joined[@]}") || ! gzip -n -9 < "$work/joined.tar" > "$out/$archive"; then
        error "$component: the archives cannot be joined into $out/$archive."
        return 1
    fi

    if ! entries=$("$tar_tool" -t -f "$out/$archive"); then
        error "$component: $out/$archive cannot be read."
        return 1
    fi

    outside=$(printf '%s\n' "$entries" | awk -v prefix="$prefix" -v allowed="$paths $submodules" '
        BEGIN { count = split(allowed, list, " ") }
        {
            if (index($0, prefix) != 1) { print; next }
            rest = substr($0, length(prefix) + 1)
            sub(/\/$/, "", rest)
            if (rest == "") next
            for (i = 1; i <= count; i++) {
                if (rest == list[i] || index(rest, list[i] "/") == 1 || index(list[i] "/", rest "/") == 1) next
            }
            print
        }')
    if [ -n "$outside" ]; then
        error "$component: $archive holds files outside its paths, such as $(printf '%s\n' "$outside" | head -n 1)."
        return 1
    fi

    count=$(printf '%s\n' "$entries" | grep -c -v '/$' || true)
    echo "$me: wrote $out/$archive, $count files of $component from $repository at $commit (tag $tag)."
}

# Makes every archive that the lines of --list-sources name.
make_all() {
    local out=$1 listing=$2 status=0 n=0 component package repository tag commit archive paths submodules work lock
    for lock in "${locks[@]}"; do
        if [ ! -f "$lock" ]; then
            error "$lock is missing; run from the repository root."
            return 1
        fi
    done

    mkdir -p "$out"
    while IFS='|' read -r component package repository tag commit archive paths submodules; do
        [ -n "$component" ] || continue
        n=$((n + 1))
        work=$(mktemp -d "$scratch/archive.XXXXXX")
        if ! make_archive "$component" "$package" "$repository" "$tag" "$commit" "$archive" "$paths" "$submodules" "$work" "$out"; then
            status=1
        fi
    done <<< "$listing"
    if [ "$n" -eq 0 ]; then
        error "eng/release/bundle-components.json names no source archive."
        return 1
    fi

    return "$status"
}

# A file URL of a folder, also from Git Bash on Windows, where the self-test can run too.
file_url() {
    if command -v cygpath > /dev/null 2>&1; then
        printf 'file:///%s' "$(cygpath -m "$1")"
    else
        printf 'file://%s' "$1"
    fi
}

# Builds an upstream with a submodule and a package that names its commit, then checks that the
# archive holds the listed paths and the submodule's files and nothing else, and that a pin which
# the nuspec or the tag does not name, a missing path and two versions in the lock files fail.
self_test() {
    local tmp=$scratch/self-test failures=0 defs up defs_commit up_commit other up_url line entries log archived
    local placeholder='$Format:%H$'
    defs=$tmp/defs
    up=$tmp/upstream
    packages=$tmp/packages
    locks=("$tmp/packages.lock.json")
    scheme=file
    annotate=0
    quiet() {
        git -c init.defaultBranch=main -c user.name=Paneport -c user.email=self-test -c commit.gpgsign=false \
            -c tag.gpgsign=false "$@"
    }

    check() {
        local description=$1
        shift
        if "$@"; then
            echo "$me: self-test: as expected: $description"
        else
            echo "$me: self-test: NOT as expected: $description" >&2
            failures=$((failures + 1))
        fi
    }

    # Passes when make_all fails and its messages name every given text.
    fails_naming() {
        local out=$1 listed=$2 text
        shift 2
        if log=$(make_all "$out" "$listed" 2>&1); then
            return 1
        fi

        for text in "$@"; do
            case "$log" in
                *"$text"*) ;;
                *) return 1 ;;
            esac
        done
    }

    nuspec() {
        mkdir -p "$packages/sample/1.0.0"
        printf '<?xml version="1.0"?>\n<package><metadata><id>Sample</id><version>1.0.0</version><repository type="git" url="%s" commit="%s" /></metadata></package>\n' \
            "$1" "$2" > "$packages/sample/1.0.0/sample.nuspec"
    }

    lock() {
        printf '{\n  "version": 2,\n  "dependencies": {\n    "net10.0": {\n      "Sample": {\n        "type": "Transitive",\n        "resolved": "%s"\n      }\n    }\n  }\n}\n' \
            "$1" > "$tmp/packages.lock.json"
    }

    holds() {
        printf '%s\n' "$entries" | grep -F -x -q "$1"
    }

    # Passes when the archive's file holds exactly the given line, with no carriage return.
    stores() {
        [ "$("$tar_tool" -x -O -f "$archived" "$1" | od -An -c | tr -s ' \n' ' ')" = \
            "$(printf '%s\n' "$2" | od -An -c | tr -s ' \n' ' ')" ]
    }

    # The fetches allow the run's scheme alone: one over git:// is refused before it connects.
    refuses_other_protocols() {
        local message
        if message=$(git_fetch -C "$up" fetch -q "git://localhost:9/none" 2>&1); then
            return 1
        fi

        case "$message" in
            *"transport 'git' not allowed"*) return 0 ;;
            *) return 1 ;;
        esac
    }

    mkdir -p "$defs" "$up/lib" "$up/tools/hook" "$up/samples"
    quiet init -q "$defs"
    echo 'message Defs {}' > "$defs/defs.proto"
    quiet -C "$defs" add defs.proto
    quiet -C "$defs" commit -q -m definitions
    quiet -C "$defs" commit -q --allow-empty -m later
    defs_commit=$(git -C "$defs" rev-parse HEAD~1)
    git -C "$defs" config uploadpack.allowAnySHA1InWant true

    quiet init -q "$up"
    echo 'class Library {}' > "$up/lib/library.cs"
    echo '<Project />' > "$up/build.props"
    echo '#define S_API' > "$up/tools/hook/sdk.h"
    echo 'class Sample {}' > "$up/samples/sample.cs"
    printf '[submodule "defs"]\n\tpath = defs\n\turl = %s\n' "$(file_url "$defs")" > "$up/.gitmodules"
    quiet -C "$up" add lib build.props tools samples .gitmodules
    quiet -C "$up" update-index --add --cacheinfo "160000,$defs_commit,defs"
    quiet -C "$up" commit -q -m upstream
    quiet -C "$up" tag -a 1.0.0 -m 1.0.0
    up_commit=$(git -C "$up" rev-parse HEAD)
    quiet -C "$up" commit -q --allow-empty -m after
    other=$(git -C "$up" rev-parse HEAD)
    git -C "$up" config uploadpack.allowAnySHA1InWant true
    up_url=$(file_url "$up")
    line="Sample|Sample|$up_url|1.0.0|$up_commit|Sample-1.0.0-source.tar.gz|lib build.props|defs"

    nuspec "$up_url" "$up_commit"
    lock 1.0.0
    if make_all "$tmp/out" "$line" && entries=$("$tar_tool" -t -f "$tmp/out/Sample-1.0.0-source.tar.gz"); then
        check "the archive holds the listed paths" holds Sample-1.0.0-source/lib/library.cs
        check "the archive holds the listed files" holds Sample-1.0.0-source/build.props
        check "the archive holds the submodule's files at the commit that the tree records" \
            holds Sample-1.0.0-source/defs/defs.proto
        check "the archive leaves out what its paths do not list" \
            eval '! printf "%s\n" "$entries" | grep -E -q "/(tools|samples)/"'
    else
        check "makes the archive of a pinned upstream" false
    fi

    nuspec "$up_url" "$other"
    check "fails when the nuspec names another commit than the pin" \
        fails_naming "$tmp/out-commit" "$line" "names the commit $other" "pins $up_commit"
    nuspec "$up_url.git" "$up_commit"
    check "accepts the nuspec's repository with .git at its end" make_all "$tmp/out-url" "$line"
    nuspec "$up_url-fork" "$up_commit"
    check "fails when the nuspec names another repository" \
        fails_naming "$tmp/out-repository" "$line" "names the repository"
    nuspec "$up_url" "$other"
    check "fails when the tag points to another commit than the pin" \
        fails_naming "$tmp/out-tag" "${line//$up_commit/$other}" "points to $up_commit, not to the pinned commit $other"
    nuspec "$up_url" "$up_commit"
    check "fails on a path that the commit lacks" \
        fails_naming "$tmp/out-path" "${line//lib build.props/lib missing.props}" "git archive of $up_commit failed"
    check "fails on a submodule that the tree does not record" \
        fails_naming "$tmp/out-submodule" "${line%|defs}|lib" "lib is not a submodule"
    printf '{ "dependencies": { "net10.0": { "Sample": {\n "resolved": "1.0.1"\n } } } }\n' > "$tmp/second.lock.json"
    locks=("$tmp/packages.lock.json" "$tmp/second.lock.json")
    check "fails when the lock files resolve two versions" \
        fails_naming "$tmp/out-versions" "$line" "not to one version"
    locks=("$tmp/packages.lock.json")

    check "fails on a file outside the listed paths" \
        fails_naming "$tmp/out-outside" "${line//lib build.props/li* build.props}" "holds files outside its paths"
    check "fetches over the run's scheme alone" refuses_other_protocols

    # A commit whose submodule has a URL over another scheme.
    quiet -C "$up" checkout -q "$up_commit"
    printf '[submodule "defs"]\n\tpath = defs\n\turl = git://localhost:9/defs\n' > "$up/.gitmodules"
    quiet -C "$up" add .gitmodules
    quiet -C "$up" commit -q -m "another scheme"
    quiet -C "$up" tag -a 1.0.0-scheme -m scheme
    other=$(git -C "$up" rev-parse HEAD)
    nuspec "$up_url" "$other"
    check "fails on a submodule URL over another scheme" \
        fails_naming "$tmp/out-scheme" "${line//|1.0.0|$up_commit|/|1.0.0-scheme|$other|}" "not one over file"

    # A commit whose attributes would leave out a file, rewrite one by substitution, ident or a
    # filter, convert line endings and re-encode a file, made into an archive with core.autocrlf on,
    # as Git for Windows has it, and with a filter driver that changes what it smudges. The encoded
    # file is added before its attribute exists, so that the commit stores it as it is, and leaves
    # the working tree, so that git does not try to read it in that encoding.
    quiet -C "$up" checkout -q "$up_commit"
    printf 'plain text\n' > "$up/lib/encoded.txt"
    quiet -C "$up" add lib/encoded.txt
    rm "$up/lib/encoded.txt"
    printf '%s\n' 'build.props export-ignore' 'lib/version.txt export-subst' 'lib/*.cs text eol=crlf' \
        'lib/ident.txt ident' 'lib/filtered.txt filter=upper' 'lib/encoded.txt working-tree-encoding=UTF-16LE' \
        > "$up/.gitattributes"
    printf '%s\n' "$placeholder" > "$up/lib/version.txt"
    printf '%s\n' '$Id$' > "$up/lib/ident.txt"
    printf 'lower case\n' > "$up/lib/filtered.txt"
    quiet -C "$up" add .gitattributes lib/version.txt lib/ident.txt lib/filtered.txt
    quiet -C "$up" commit -q -m attributes
    quiet -C "$up" tag -a 1.0.0-attributes -m attributes
    other=$(git -C "$up" rev-parse HEAD)
    nuspec "$up_url" "$other"
    archived=$tmp/out-attributes/Sample-1.0.0-source.tar.gz

    # The settings go to git through assignments before a function call, as make_all is called
    # below. Without them the case would pass whatever the guards do, so it first asserts that git
    # sees them, also in the Bash 3.2 of macOS.
    plant() {
        GIT_CONFIG_COUNT=2 GIT_CONFIG_KEY_0=core.autocrlf GIT_CONFIG_VALUE_0=true \
            GIT_CONFIG_KEY_1=filter.upper.smudge GIT_CONFIG_VALUE_1='tr a-z A-Z' "$@"
    }
    sees_plant() {
        [ "$(git config --get core.autocrlf)" = true ] && [ "$(git config --get filter.upper.smudge)" = 'tr a-z A-Z' ]
    }
    check "git sees the planted core.autocrlf and filter driver through a function call" plant sees_plant
    if plant make_all "$tmp/out-attributes" "${line//|1.0.0|$up_commit|/|1.0.0-attributes|$other|}" \
        && entries=$("$tar_tool" -t -f "$archived"); then
        check "keeps a file that the tree's attributes would leave out" holds Sample-1.0.0-source/build.props
        check "keeps a file as the commit stores it, unsubstituted" stores Sample-1.0.0-source/lib/version.txt "$placeholder"
        check "keeps a file as the commit stores it, with ident unexpanded" stores Sample-1.0.0-source/lib/ident.txt '$Id$'
        check "keeps a file as the commit stores it, unfiltered" stores Sample-1.0.0-source/lib/filtered.txt 'lower case'
        check "keeps a file in the encoding that the commit stores" stores Sample-1.0.0-source/lib/encoded.txt 'plain text'
        check "keeps the line endings that the commit stores, against eol=crlf and core.autocrlf on" \
            stores Sample-1.0.0-source/lib/library.cs 'class Library {}'
    else
        check "makes the archive of a commit with export attributes" false
    fi

    if [ "$failures" -ne 0 ]; then
        echo "$me: self-test: $failures checks were not as expected." >&2
        return 1
    fi

    echo "$me: self-test passed."
}

main() {
    if [ "$#" -eq 1 ] && [ "$1" = --self-test ]; then
        check_tar
        self_test
        return
    fi

    if [ "$#" -ne 1 ] || [ "${1#-}" != "$1" ]; then
        echo "usage: eng/mac/make-source-archives.sh <folder> | --self-test" >&2
        return 2
    fi

    local listing
    check_tar
    if ! listing=$(dotnet run tools/GenerateNotices.cs -- --list-sources); then
        error "tools/GenerateNotices.cs -- --list-sources failed."
        return 1
    fi

    make_all "$1" "$listing"
}

main "$@"
