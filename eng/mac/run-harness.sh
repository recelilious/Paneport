#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Runs the cases of tests/runtime on the macOS harness (docs/design/runtime-macos-harness.md):
# builds what is missing, prepares a template prefix, then runs each case in a fresh harness
# process, writes a summary, and exits non-zero on any failure.
#
#   eng/mac/run-harness.sh [--mode single|multi] [--planted] [--no-build] [<case> ...]
#
#   --mode single  the default: the wineserver as a thread and the program as a group of threads
#                  in one process, the harness's child, started through the native host's session
#                  API, with a fresh copy of the template prefix for each run (M4.3)
#   --mode multi   Wine as upstream runs it, with the wineserver and every Windows process as
#                  processes of their own and a copy of the template prefix for each case, for
#                  comparisons; it skips the session cases
#   --planted      run the planted cases of tests/runtime/planted instead: each has one wrong
#                  expectation, so the run fails and names them, which CI checks
#   --no-build     use what is built, and fail when something is missing
#   <case>         the names of the cases to run, such as console; all of them by default
#
# Build output lives under $PANEPORT_BUILD_DIR, by default build/ in the repository, and the
# logs under $PANEPORT_BUILD_DIR/runtime/logs. The fork tools come from eng/mac/bootstrap.sh
# --forks, as for the build scripts. Bash 3.2 (C-0010 section 1).

set -euo pipefail

repo=$(cd "$(dirname "$0")/../.." && pwd)
cd "$repo"

say() {
    printf 'run-harness: %s\n' "$*"
}

fail() {
    printf 'run-harness: %s\n' "$*" >&2
    exit 1
}

mode=single
planted=false
build=true
names=()
while [ $# -gt 0 ]; do
    case "$1" in
        --mode)
            [ $# -ge 2 ] || fail "--mode needs a value."
            mode=$2
            shift 2
            ;;
        --planted) planted=true; shift ;;
        --no-build) build=false; shift ;;
        -*) fail "unknown option $1." ;;
        *) names+=("$1"); shift ;;
    esac
done
case "$mode" in
    single | multi) ;;
    pool) fail "pool mode comes with M4.4." ;;
    *) fail "unknown mode $mode." ;;
esac

build_dir=${PANEPORT_BUILD_DIR:-$repo/build}
case $build_dir in
    /*) ;;
    *) fail "PANEPORT_BUILD_DIR must be an absolute path, not $build_dir." ;;
esac
wine_install="$build_dir/wine/install"
fex_install="$build_dir/fex/install"
programs="$build_dir/win32/x86_64"
harness="$build_dir/native/macos-harness/runtime/paneport_harness"
runtime="$build_dir/runtime"
logs="$runtime/logs"

# Ends the wineserver of the current case's prefix, with every Wine process that uses it.
end_case_server() {
    WINEPREFIX="$prefix" "$server" -k > /dev/null 2>&1 || true
}

# Ends the harness child of the current case in single mode, by its command line, which names
# the case's file.
end_case_child() {
    pkill -KILL -f -- "--run-single $case_file " > /dev/null 2>&1 || true
}

# Runs a command and ends it after the given number of seconds; returns 124 then.
with_timeout() {
    local seconds=$1
    shift
    "$@" &
    local child=$!
    local waited=0
    while kill -0 "$child" 2>/dev/null; do
        if [ "$waited" -ge "$seconds" ]; then
            kill -KILL "$child" 2>/dev/null || true
            wait "$child" 2>/dev/null || true
            return 124
        fi
        sleep 1
        waited=$((waited + 1))
    done
    wait "$child"
}

# --- Build what is missing ------------------------------------------------------------------

if [ "$build" = true ]; then
    [ -x "$wine_install/bin/wine" ] || eng/mac/build-wine.sh
    [ -f "$fex_install/libarm64ecfex.dll" ] || eng/mac/build-fex.sh
    # The test programs and the harness take seconds, so they are always built again.
    eng/mac/build-win32-tests.sh x86_64
    # The preset builds into build/native/macos-harness; -B moves that under PANEPORT_BUILD_DIR.
    (cd src/native && cmake --preset macos-harness -B "$build_dir/native/macos-harness" > /dev/null &&
        cmake --build "$build_dir/native/macos-harness" --parallel > /dev/null) ||
        fail "the harness does not build; run cmake --preset macos-harness -B $build_dir/native/macos-harness in src/native to see why."
fi
[ -x "$wine_install/bin/wine" ] || fail "Wine is not built; run eng/mac/build-wine.sh."
[ -f "$fex_install/libarm64ecfex.dll" ] || fail "FEX is not built; run eng/mac/build-fex.sh."
[ -f "$programs/console.exe" ] || fail "the test programs are not built; run eng/mac/build-win32-tests.sh x86_64."
[ -x "$harness" ] || fail "the harness is not built; run this script without --no-build, which builds it in $build_dir/native/macos-harness."

rm -rf "$logs"
mkdir -p "$logs"

# --- The runtime: Wine's install with FEX beside its modules --------------------------------

# A clone of Wine's install, which APFS makes without copying the data, receives FEX's module
# among Wine's own PE modules, where Wine's loader finds it by name. Wine's install itself
# stays as its build left it.
wine="$runtime/wine"
rm -rf "$wine"
mkdir -p "$runtime"
cp -Rc "$wine_install" "$wine" 2>/dev/null || cp -R "$wine_install" "$wine"
cp "$fex_install/libarm64ecfex.dll" "$wine/lib/wine/aarch64-windows/libarm64ecfex.dll"
loader="$wine/bin/wine"
server="$wine/bin/wineserver"
say "$("$loader" --version), with libarm64ecfex.dll $(shasum -a 256 "$fex_install/libarm64ecfex.dll" | cut -c1-12)"

# No Mono and no Gecko installers, and no display driver: console programs need none
# (docs/design/runtime-macos-harness.md).
export WINEDLLOVERRIDES="mscoree,mshtml=d;winemac.drv=d"
export WINEDEBUG=${WINEDEBUG:-err+all,fixme-all}
unset DISPLAY

# --- The template prefix --------------------------------------------------------------------

template="$runtime/prefix-template"
rm -rf "$template"
say "creating the template prefix with wineboot"
started=$(date +%s)
status=0
WINEPREFIX="$template" with_timeout 900 "$loader" wineboot --init > "$logs/prefix.log" 2>&1 || status=$?
WINEPREFIX="$template" with_timeout 120 "$server" -w >> "$logs/prefix.log" 2>&1 || true
if [ "$status" -ne 0 ] || [ ! -d "$template/drive_c/windows/system32" ]; then
    tail -n 40 "$logs/prefix.log" >&2
    fail "wineboot did not create the prefix (status $status); the end of its log is above."
fi
# FEX's module is the emulator of x86-64 code, as Wine reads it from the registry
# (dlls/ntdll/loader.c, load_arm64ec_module), and the null driver is the display driver.
WINEPREFIX="$template" "$loader" reg add 'HKLM\Software\Microsoft\Wow64\amd64' /ve /t REG_SZ \
    /d libarm64ecfex.dll /f >> "$logs/prefix.log" 2>&1 || fail "reg add for the emulator failed."
WINEPREFIX="$template" "$loader" reg add 'HKCU\Software\Wine\Drivers' /v Graphics /t REG_SZ \
    /d null /f >> "$logs/prefix.log" 2>&1 || fail "reg add for the display driver failed."
WINEPREFIX="$template" with_timeout 120 "$server" -w >> "$logs/prefix.log" 2>&1 || true
if [ "$mode" = single ]; then
    # Single mode runs no wineboot and no wineserver of its own. The fork publishes the drive
    # letters itself, C: from dosdevices/c:, and no drive maps the host's root (S4.3.7); a
    # .update-timestamp that says disable keeps any later wineboot from updating the prefix.
    rm -f "$template/dosdevices/z:"
    printf 'disable\n' > "$template/.update-timestamp"
fi
say "template prefix ready in $(($(date +%s) - started)) s"

# --- The cases ------------------------------------------------------------------------------

if [ "$planted" = true ]; then
    case_folder="$repo/tests/runtime/planted"
    # The planted expected outputs come from the real ones, so that they cannot drift apart:
    # console_wrong_line.txt is console.txt with another architecture in its greeting.
    expected="$runtime/planted-expected"
    rm -rf "$expected"
    mkdir -p "$expected"
    cp "$repo"/tests/runtime/expected/*.txt "$expected/"
    sed '1s/x86-64/i686/' "$repo/tests/runtime/expected/console.txt" > "$expected/console_wrong_line.txt"
else
    case_folder="$repo/tests/runtime/cases"
    expected="$repo/tests/runtime/expected"
fi
if [ ${#names[@]} -eq 0 ]; then
    for file in "$case_folder"/*.json; do
        names+=("$(basename "$file" .json)")
    done
fi

summary="$logs/summary.md"
{
    printf '## Runtime harness, %s mode%s\n\n' "$mode" "$([ "$planted" = true ] && echo ', planted cases')"
    printf '| Case | Result |\n|---|---|\n'
} > "$summary"
failed=0
passed=0
for name in "${names[@]}"; do
    case_file="$case_folder/$name.json"
    [ -f "$case_file" ] || fail "there is no case $name in $case_folder."
    prefix="$runtime/prefixes/$name"
    rm -rf "$prefix"
    mkdir -p "$runtime/prefixes"
    if [ "$mode" = single ]; then
        # The harness makes the prefix a fresh copy of the template before each run, and its
        # child runs the session in a process of its own, with no wineserver beside it. The
        # child's process group, which Ctrl-C does not reach, ends here when the run is
        # interrupted.
        trap 'end_case_child; exit 130' INT
        trap 'end_case_child; exit 143' TERM
        line=$("$harness" --mode single --case "$case_file" --programs "$programs" \
            --expected "$expected" --template "$template" --prefix "$prefix" \
            --loader "$loader" --logs "$logs") || true
        trap - INT TERM
    else
        cp -Rc "$template" "$prefix" 2>/dev/null || cp -R "$template" "$prefix"
        # A wineserver of the case's own, started before the program and outside its process
        # group, so that the server does not hold the program's output, and ended with the
        # case. The session's first program runs wineboot --init, whose helpers keep the case's
        # error stream open until the server ends.
        WINEPREFIX="$prefix" "$server" -p > "$logs/$name.wineserver.log" 2>&1 || true
        # The server never ends by itself, and the program runs in a process group of its own
        # that Ctrl-C does not reach, so an interrupted run ends them here.
        trap end_case_server EXIT
        trap 'end_case_server; exit 130' INT
        trap 'end_case_server; exit 143' TERM
        line=$("$harness" --mode multi --case "$case_file" --programs "$programs" \
            --expected "$expected" --prefix "$prefix" --loader "$loader" --logs "$logs") || true
        end_case_server
        trap - EXIT INT TERM
    fi
    rm -rf "$prefix"
    printf '%s\n' "$line"
    # A session case has no session to run in multi mode, so it neither passes nor fails there.
    case "$line" in
        "PASS "*) passed=$((passed + 1)) ;;
        "SKIP "*) ;;
        *) failed=$((failed + 1)) ;;
    esac
    printf '| %s | %s |\n' "$name" "$(printf '%s' "$line" | sed 's/|/\\|/g')" >> "$summary"
done
printf '\n%s passed, %s failed.\n' "$passed" "$failed" >> "$summary"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
    cat "$summary" >> "$GITHUB_STEP_SUMMARY"
fi

say "$passed passed, $failed failed; the logs are in $logs"
[ "$failed" -eq 0 ]
