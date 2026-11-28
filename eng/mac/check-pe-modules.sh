#!/bin/bash
# SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
# SPDX-License-Identifier: MIT

# Checks PE modules that run natively on Apple silicon, which are the ARM64, ARM64EC and ARM64X
# modules of the Wine and FEX builds (docs/design/runtime-ios.md section 6):
#
#   - no instruction names x18. iOS zeroes x18 on every return to user mode, and macOS does for
#     a program linked with the macOS 13 SDK or a later one, so the forks keep the TEB pointer in
#     the thread's TSD, and code that still read x18 would read zero. Functions that only save
#     or restore a whole register context are allowed by name, with --allow;
#   - no TLS directory. Code that a compiler emits for a __thread or thread_local variable loads
#     the thread's TLS array through x18, which no header of the forks can change;
#   - none of the symbols that --forbid-symbol names, in the modules and in the object files and
#     archives of the folders that --objects names.
#
#   eng/mac/check-pe-modules.sh [--allow <module>:<symbol>]... [--forbid-symbol <name>]...
#       [--objects <folder>]... <module or folder>...
#   eng/mac/check-pe-modules.sh --self-test
#
# A folder stands for every file directly in it. Modules of other machine types are counted and
# skipped. <module> in --allow is a file name, or * for any module. Every finding is printed,
# and the script exits with 1 when there is one. --self-test builds small modules with the
# pinned llvm-mingw and passes only if the checks fail on each planted one, with its name, and
# pass on a clean one. The tools are llvm-mingw's llvm-objdump, llvm-readobj and llvm-nm, in
# LLVM_MINGW, from eng/mac/bootstrap.sh --forks. Bash 3.2 (C-0010 section 1).

set -euo pipefail

say() {
    printf 'check-pe-modules: %s\n' "$*"
}

fail() {
    printf 'check-pe-modules: %s\n' "$*" >&2
    exit 1
}

[ -n "${LLVM_MINGW:-}" ] || fail "LLVM_MINGW is not set. Run eng/mac/bootstrap.sh --forks and the \
export lines it prints."
objdump="$LLVM_MINGW/bin/llvm-objdump"
readobj="$LLVM_MINGW/bin/llvm-readobj"
nm="$LLVM_MINGW/bin/llvm-nm"
for tool in "$objdump" "$readobj" "$nm"; do
    [ -x "$tool" ] || fail "LLVM_MINGW does not name an llvm-mingw folder: $tool is missing."
done

# Prints llvm-objdump's disassembly of a module. llvm-objdump reads a module's export table for
# its symbols and stops, before it disassembles anything, on a table in which no export has a
# name ("export ordinal table not found"); Wine has such modules, msnet32.dll among them. For a
# module on which it fails, a copy is disassembled whose header names no export table: the 8
# bytes of the first data directory of a PE32+ optional header, 112 bytes into it, are zeroed.
# The symbols of the COFF symbol table remain.
disassemble() {
    local file=$1 copy offset magic status=0
    if "$objdump" -d --no-show-raw-insn "$file" 2> /dev/null; then
        return 0
    fi
    offset=$(od -An -tu4 -j60 -N4 "$file" | tr -d ' ')
    case "$offset" in
        '' | *[!0-9]*) return 1 ;;
    esac
    magic=$(od -An -tx1 -j$((offset + 24)) -N2 "$file" | tr -d ' ')
    [ "$magic" = 0b02 ] || return 1
    copy=$(mktemp "${TMPDIR:-/tmp}/paneport-pe-copy.XXXXXX") || return 1
    cp "$file" "$copy"
    printf '\0\0\0\0\0\0\0\0' | dd of="$copy" bs=1 seek=$((offset + 24 + 112)) conv=notrunc 2> /dev/null
    "$objdump" -d --no-show-raw-insn "$copy" 2> /dev/null || status=$?
    rm -f "$copy"
    return "$status"
}

# One module: prints a line per finding, tab-separated, whose first field is its kind:
#   skipped <module> <machine>          not a native module
#   native  <module> <machine>          a module that was checked
#   tls     <module> <rva>              it has a TLS directory
#   x18     <module> <symbol> <address> <instruction>
#   symbol  <file>   <name>             it holds a forbidden symbol
#   error   <module> <text>             a tool failed on it
# The forbidden symbols are the arguments after the file.
check_module() {
    local file=$1 name headers machine tls base load_config ranges symbol listing
    shift
    name=${file##*/}
    if ! headers=$("$readobj" --file-headers "$file" 2> /dev/null); then
        printf 'skipped\t%s\t%s\n' "$name" "not a PE file"
        return 0
    fi
    # An object file or an archive, such as the import libraries that Wine installs beside its
    # modules, has no optional header and is no module.
    case "$headers" in
        *ImageOptionalHeader*) ;;
        *)
            printf 'skipped\t%s\t%s\n' "$name" "not a PE image"
            return 0
            ;;
    esac
    machine=$(printf '%s\n' "$headers" | sed -n 's/^ *Machine: IMAGE_FILE_MACHINE_\([A-Z0-9_]*\).*/\1/p' | head -n 1)
    case "$machine" in
        ARM64 | ARM64EC | ARM64X) ;;
        *)
            printf 'skipped\t%s\t%s\n' "$name" "${machine:-unknown}"
            return 0
            ;;
    esac
    printf 'native\t%s\t%s\n' "$name" "$machine"

    tls=$(printf '%s\n' "$headers" | sed -n 's/^ *TLSTableRVA: \(0x[0-9A-Fa-f]*\).*/\1/p' | head -n 1)
    case "$tls" in
        '') printf 'error\t%s\t%s\n' "$name" "llvm-readobj prints no TLSTableRVA" ;;
        0x0 | 0x00000000) ;;
        *) printf 'tls\t%s\t%s\n' "$name" "$tls" ;;
    esac

    # The x86-64 code of a hybrid module, such as the thunks of an ARM64X library, would
    # disassemble as arbitrary ARM64 instructions. The code map of the CHPE metadata names its
    # ranges, as "<start> - <end>  X64", by their offsets from the image's base.
    base=$(printf '%s\n' "$headers" | sed -n 's/^ *ImageBase: \(0x[0-9A-Fa-f]*\).*/\1/p' | head -n 1)
    load_config=$("$readobj" --coff-load-config "$file" 2> /dev/null || true)
    ranges=$(printf '%s\n' "$load_config" \
        | sed -n 's/^ *\(0x[0-9A-Fa-f]*\) - \(0x[0-9A-Fa-f]*\)  *X64 *$/\1-\2/p' | tr '\n' ',')

    # The filter keeps the lines that name a symbol and those that name x18 or w18 as a word, so
    # that awk reads little; #0x18 is not such a word. A failing llvm-objdump leaves a marker,
    # because the pipe's status is grep's and awk's.
    listing=$({ disassemble "$file" || echo 'OBJDUMP-FAILED'; } \
        | { LC_ALL=C grep -E '^[0-9a-f]+ <|^OBJDUMP-FAILED$|(^|[^0-9A-Za-z_.$#])[xw]18([^0-9A-Za-z_]|$)' || true; } \
        | LC_ALL=C awk -v module="$name" -v base="${base:-0x0}" -v ranges="$ranges" '
            function hex(text,    i, digit, value) {
                value = 0
                text = tolower(text)
                sub(/^0x/, "", text)
                for (i = 1; i <= length(text); i++) {
                    digit = index("0123456789abcdef", substr(text, i, 1))
                    if (digit == 0) break
                    value = value * 16 + digit - 1
                }
                return value
            }
            BEGIN {
                count = split(ranges, list, ",")
                image = hex(base)
                skip = 0
                for (i = 1; i <= count; i++) {
                    if (list[i] == "") continue
                    split(list[i], ends, "-")
                    skip++
                    from[skip] = image + hex(ends[1])
                    to[skip] = image + hex(ends[2])
                }
                symbol = "(no symbol)"
            }
            /^OBJDUMP-FAILED$/ { printf "error\t%s\tllvm-objdump failed\n", module; next }
            /^[0-9a-f]+ <.*>:$/ {
                symbol = $0
                sub(/^[0-9a-f]+ </, "", symbol)
                sub(/>:$/, "", symbol)
                next
            }
            {
                line = $0
                colon = index(line, ":")
                if (colon == 0) next
                address = substr(line, 1, colon - 1)
                gsub(/[ \t]/, "", address)
                if (address !~ /^[0-9a-f]+$/) next
                text = substr(line, colon + 1)
                # What follows an instruction is a comment or the name of a branch target.
                sub(/[ \t]*\/\/.*$/, "", text)
                gsub(/<[^>]*>/, "", text)
                gsub(/^[ \t]+/, "", text)
                gsub(/\t/, " ", text)
                if (text !~ /(^|[^0-9A-Za-z_.$#])[xw]18([^0-9A-Za-z_]|$)/) next
                value = hex(address)
                for (i = 1; i <= skip; i++) if (value >= from[i] && value < to[i]) next
                printf "x18\t%s\t%s\t%s\t%s\n", module, symbol, address, text
            }')
    [ -z "$listing" ] || printf '%s\n' "$listing"

    if [ $# -gt 0 ]; then
        check_symbols "$file" "$@"
    fi
}

# An object file, an archive or a module: a "symbol" line for each forbidden symbol that it
# defines or refers to. llvm-nm prints nothing for a module without a symbol table.
check_symbols() {
    local file=$1 names wanted
    shift
    names=$("$nm" "$file" 2> /dev/null | awk '{ print $NF }' | sort -u || true)
    for wanted in "$@"; do
        # A here-string, because a pipe into grep -q would end printf early under pipefail.
        if grep -qxF -e "$wanted" -e "_$wanted" -e "#$wanted" <<< "$names"; then
            printf 'symbol\t%s\t%s\n' "$file" "$wanted"
        fi
    done
}

# The workers that xargs starts: --worker-module or --worker-object, a folder for the results,
# the forbidden symbols up to "--", then files.
case "${1:-}" in
    --worker-module | --worker-object)
        worker=$1
        results=$2
        shift 2
        forbidden=()
        while [ "$1" != -- ]; do
            forbidden+=("$1")
            shift
        done
        shift
        for file in "$@"; do
            if [ "$worker" = --worker-module ]; then
                check_module "$file" ${forbidden[@]+"${forbidden[@]}"}
            else
                check_symbols "$file" ${forbidden[@]+"${forbidden[@]}"}
            fi
        done > "$results/$$.txt"
        exit 0
        ;;
esac

# Counts a finding of run_checks, and prints it unless many came before it.
shown=200
report() {
    findings=$((findings + 1))
    if [ "$findings" -le "$shown" ]; then
        say "$1" >&2
    fi
}

# Runs the checks and prints the report; returns 1 when there is a finding. The arguments are
# those of the script.
run_checks() {
    local allow=() forbidden=() object_folders=() paths=() path results jobs findings=0
    local kind module first second third allowed entry total skipped
    while [ $# -gt 0 ]; do
        case "$1" in
            --allow)
                [ $# -ge 2 ] || fail "--allow needs <module>:<symbol>."
                case "$2" in
                    *:*) allow+=("$2") ;;
                    *) fail "--allow needs <module>:<symbol>, not $2." ;;
                esac
                shift 2
                ;;
            --forbid-symbol)
                [ $# -ge 2 ] || fail "--forbid-symbol needs a name."
                forbidden+=("$2")
                shift 2
                ;;
            --objects)
                [ $# -ge 2 ] || fail "--objects needs a folder."
                [ -d "$2" ] || fail "$2 is not a folder."
                object_folders+=("$2")
                shift 2
                ;;
            -*) fail "unknown option $1." ;;
            *)
                [ -e "$1" ] || fail "$1 does not exist."
                paths+=("$1")
                shift
                ;;
        esac
    done
    [ ${#paths[@]} -gt 0 ] || fail "name at least one module or folder."
    if [ ${#object_folders[@]} -gt 0 ] && [ ${#forbidden[@]} -eq 0 ]; then
        fail "--objects needs a --forbid-symbol."
    fi

    results=$(mktemp -d "${TMPDIR:-/tmp}/paneport-pe-check.XXXXXX")
    jobs=$(sysctl -n hw.ncpu 2> /dev/null || getconf _NPROCESSORS_ONLN 2> /dev/null || echo 2)
    for path in "${paths[@]}"; do
        if [ -d "$path" ]; then
            find "$path" -maxdepth 1 -type f -print0
        else
            printf '%s\0' "$path"
        fi
    done | xargs -0 -n 16 -P "$jobs" "$0" --worker-module "$results" \
        ${forbidden[@]+"${forbidden[@]}"} -- || fail "a worker failed."
    if [ ${#object_folders[@]} -gt 0 ]; then
        find "${object_folders[@]}" -type f \( -name '*.o' -o -name '*.a' \) -print0 \
            | xargs -0 -n 64 -P "$jobs" "$0" --worker-object "$results" "${forbidden[@]}" -- \
            || fail "a worker failed."
    fi
    find "$results" -type f -name '*.txt' -exec cat {} + | LC_ALL=C sort > "$results/all"

    total=$(awk -F '\t' '$1 == "native"' "$results/all" | wc -l | tr -d ' ')
    skipped=$(awk -F '\t' '$1 == "skipped"' "$results/all" | wc -l | tr -d ' ')
    say "$total native modules checked, $skipped other files skipped"
    [ "$total" -gt 0 ] || {
        rm -rf "$results"
        fail "no ARM64, ARM64EC or ARM64X module was found."
    }
    while IFS=$'\t' read -r kind module first second third; do
        case "$kind" in
            native | skipped) ;;
            tls)
                report "$module has a TLS directory (at $first): its code would load the TLS array through x18"
                ;;
            x18)
                allowed=false
                for entry in ${allow[@]+"${allow[@]}"}; do
                    if [ "$entry" = "$module:$first" ] || [ "$entry" = "*:$first" ]; then
                        allowed=true
                    fi
                done
                if [ "$allowed" = false ]; then
                    report "$module uses x18 in $first at $second: $third"
                fi
                ;;
            symbol)
                report "$module holds the symbol $first"
                ;;
            *)
                report "$module: ${first:-$kind}"
                ;;
        esac
    done < "$results/all"
    rm -rf "$results"
    if [ "$findings" -gt "$shown" ]; then
        say "$findings findings, of which the first $shown are above" >&2
        return 1
    elif [ "$findings" -gt 0 ]; then
        say "$findings findings" >&2
        return 1
    fi
    say "no x18 use, no TLS directory${forbidden[0]+, none of the forbidden symbols}"
}

# Builds a module from a C source with the pinned llvm-mingw, without the C runtime, whose
# start-up code brings a TLS directory of its own.
self_test_module() {
    local source=$1 output=$2
    shift 2
    "$LLVM_MINGW/bin/aarch64-w64-mingw32-clang" -O2 -shared -nostdlib -Wl,--entry=entry \
        "$@" -o "$output" "$source" || fail "the self-test could not build ${output##*/}."
}

# Runs the checks on one planted module; they must fail and print the line that names it.
self_test_fails() {
    local wanted=$1 output status=0
    shift
    output=$(run_checks "$@" 2>&1) || status=$?
    if [ "$status" -eq 0 ]; then
        printf '%s\n' "$output" >&2
        fail "self-test: the checks passed a planted module ($wanted)."
    fi
    if ! grep -qF -- "$wanted" <<< "$output"; then
        printf '%s\n' "$output" >&2
        fail "self-test: the checks did not print: $wanted"
    fi
    say "self-test: failed as expected: $wanted"
}

self_test() {
    local folder
    folder=$(mktemp -d "${TMPDIR:-/tmp}/paneport-pe-selftest.XXXXXX")
    mkdir "$folder/clean" "$folder/reads" "$folder/copies" "$folder/tls" "$folder/saves" \
        "$folder/crc" "$folder/objects"

    # A clean module, which reads the TEB as the forks do.
    cat > "$folder/clean.c" << 'EOF'
void *teb(void)
{
    unsigned long long tsd;
    __asm__("mrs %0, tpidrro_el0" : "=r" (tsd));
    return *(void **)((tsd & ~7ull) + 0x30);
}
int entry(void *instance, unsigned int reason, void *reserved) { return (unsigned long long)teb() != 0x18; }
EOF
    # x18 as a base register.
    cat > "$folder/reads.c" << 'EOF'
__asm__(".text\n\t.globl planted_read\nplanted_read:\n\tldr x0, [x18, #0x60]\n\tret\n");
int entry(void *instance, unsigned int reason, void *reserved) { return 1; }
EOF
    # x18 copied to another register.
    cat > "$folder/copies.c" << 'EOF'
__asm__(".text\n\t.globl planted_copy\nplanted_copy:\n\tmov x0, x18\n\tret\n");
int entry(void *instance, unsigned int reason, void *reserved) { return 1; }
EOF
    # A context save, which --allow lets pass by its name, and only by its name.
    cat > "$folder/saves.c" << 'EOF'
__asm__(".text\n\t.globl planted_save\nplanted_save:\n\tstp x18, x19, [x0, #0x90]\n\tret\n");
int entry(void *instance, unsigned int reason, void *reserved) { return 1; }
EOF
    # Implicit TLS, built with the TLS model of Windows whatever the default of the toolchain:
    # the compiler's own load of the TLS array, and a TLS directory.
    cat > "$folder/tls.c" << 'EOF'
__thread int planted_variable = 1;
unsigned int _tls_index;
extern char __tls_start__[], __tls_end__[];
__asm__(".section .tls,\"dw\"\n\t.globl __tls_start__\n__tls_start__:\n\t.quad 0\n"
        ".section .tls$ZZZ,\"dw\"\n\t.globl __tls_end__\n__tls_end__:\n\t.quad 0\n\t.text\n");
const struct { void *start, *end, *index, *callbacks; unsigned int zero, flags; } _tls_used =
    { __tls_start__, __tls_end__, &_tls_index, 0, 0, 0 };
int entry(void *instance, unsigned int reason, void *reserved) { return planted_variable; }
EOF
    # Modules whose exports all lack a name, on which llvm-objdump fails by itself: a clean one
    # and one that reads x18.
    printf 'EXPORTS\n    teb @1 NONAME\n' > "$folder/noname.def"
    printf 'EXPORTS\n    planted_read @1 NONAME\n' > "$folder/nonameread.def"
    # The symbol of FFmpeg's crc.S, in a module and in an object file.
    cat > "$folder/crc.c" << 'EOF'
unsigned int ff_crc32_aarch64(void) { return 0; }
int entry(void *instance, unsigned int reason, void *reserved) { return ff_crc32_aarch64(); }
EOF

    self_test_module "$folder/clean.c" "$folder/clean/clean.dll"
    self_test_module "$folder/reads.c" "$folder/reads/reads.dll"
    self_test_module "$folder/copies.c" "$folder/copies/copies.dll"
    self_test_module "$folder/saves.c" "$folder/saves/saves.dll"
    self_test_module "$folder/tls.c" "$folder/tls/tls.dll" -fno-emulated-tls
    self_test_module "$folder/crc.c" "$folder/crc/crc.dll"
    mkdir "$folder/nonameread"
    self_test_module "$folder/clean.c" "$folder/clean/noname.dll" "$folder/noname.def"
    self_test_module "$folder/reads.c" "$folder/nonameread/nonameread.dll" "$folder/nonameread.def"
    if "$objdump" -d "$folder/clean/noname.dll" > /dev/null 2>&1; then
        say "self-test: llvm-objdump reads a module whose exports all lack a name; the copy without an export table is no longer needed"
    fi
    "$LLVM_MINGW/bin/aarch64-w64-mingw32-clang" -O2 -c -o "$folder/objects/crc.o" "$folder/crc.c" \
        || fail "the self-test could not build crc.o."
    # An object file beside the clean module, as an import library lies beside Wine's modules:
    # it is skipped, though it reads x18.
    "$LLVM_MINGW/bin/aarch64-w64-mingw32-clang" -O2 -c -o "$folder/clean/libreads.a" "$folder/reads.c" \
        || fail "the self-test could not build libreads.a."

    run_checks --forbid-symbol ff_crc32_aarch64 --objects "$folder/clean" "$folder/clean" \
        || fail "self-test: the checks failed on a clean module."
    run_checks --allow 'saves.dll:planted_save' "$folder/saves" "$folder/clean" \
        || fail "self-test: the checks failed on an allowed function."
    self_test_fails "reads.dll uses x18 in planted_read at" "$folder/reads"
    self_test_fails "copies.dll uses x18 in planted_copy at" "$folder/copies/copies.dll"
    self_test_fails "saves.dll uses x18 in planted_save at" "$folder/saves"
    self_test_fails "saves.dll uses x18 in planted_save at" --allow 'saves.dll:planted_other' \
        --allow 'other.dll:planted_save' "$folder/saves"
    self_test_fails "nonameread.dll uses x18 in planted_read at" "$folder/nonameread"
    self_test_fails "tls.dll has a TLS directory" "$folder/tls"
    self_test_fails "tls.dll uses x18 in " "$folder/tls"
    self_test_fails "crc.dll holds the symbol ff_crc32_aarch64" --forbid-symbol ff_crc32_aarch64 \
        "$folder/crc"
    self_test_fails "crc.o holds the symbol ff_crc32_aarch64" --forbid-symbol ff_crc32_aarch64 \
        --objects "$folder/objects" "$folder/clean"
    rm -rf "$folder"
    say "self-test: the checks failed on every planted module and passed the clean one"
}

if [ "${1:-}" = --self-test ]; then
    [ $# -eq 1 ] || fail "--self-test takes no other argument."
    self_test
else
    run_checks "$@"
fi
