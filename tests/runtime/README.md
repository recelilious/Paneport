<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/runtime

The runtime harness: our own C17 code under MIT that runs the Win32 test programs of [tests/win32](../win32/README.md) under Wine on macOS and checks what they print (plan section 7.1). The Medium stages from M4.2 to M8.1 are verified with it. [runtime-macos-harness.md](../../docs/design/runtime-macos-harness.md) is its design.

| Folder | What it holds |
|---|---|
| [harness/](harness/) | The harness: the case reader, the expected outputs and their comparison, the child process, and `main.c` |
| [cases/](cases/) | One JSON file per case |
| [expected/](expected/) | The expected outputs that the cases name |
| [planted/](planted/) | Two cases that must fail, one with a wrong expected line and one with a wrong exit code |
| [unit/](unit/) | The CTest cases of the harness's own parts |

## Cases

A case names the program, its arguments, its timeout, its exit code and its expected output:

```json
{"program": "console.exe", "arguments": ["3"], "timeout_seconds": 300, "exit_code": 3, "expected": "console.txt"}
```

`arguments` is optional. Any other member, a missing one or a value of the wrong type fails the case, so that a typo does not go unnoticed. Names are file names without a folder.

## Expected outputs

Each line of an expected file stands for one line of the program's standard output. A line that starts with `= ` must match exactly; a line that starts with `~ ` is a POSIX extended regular expression that must match the whole line, as for the CPU feature line and the throughput. No other line may appear. An output line that ends in a carriage return matches without it. The files hold no host path and no user name.

## Running the cases

[eng/mac/run-harness.sh](../../eng/mac/run-harness.sh) builds what is missing, prepares a template prefix with `wineboot`, and runs each case in a fresh harness process with a fresh copy of that prefix:

```bash
eng/mac/run-harness.sh --mode multi
```

It needs a Mac with Apple silicon and the fork tools of `eng/mac/bootstrap.sh --forks`. Names of cases, such as `console threads`, run those alone. `--planted` runs the planted cases, which must fail and be named; the `runtime` job of CI runs every case and then the planted ones (C-0011 section 4). The logs of each case, its output, Wine's error output and the result, go to `build/runtime/logs/<case>/`, with a summary in `build/runtime/logs/summary.md`.

The harness itself is `paneport_harness`, which the CMake preset `macos-harness` of `src/native` builds:

```bash
paneport_harness --case <file> --programs <folder> --expected <folder> --prefix <folder> --loader <file> --logs <folder> [--mode multi]
```

It copies the program into the prefix at `C:\paneport-tests`, starts Wine's loader on it with `WINEPREFIX` naming the prefix, and prints `PASS <case>` or `FAIL <case>: <why>`. A case fails when a line differs, a line is missing or one more is printed, the exit code differs, the program runs past its timeout, or Wine reports an unhandled exception. It also fails when the output goes past 4 MiB, which is cut there, and when Wine's error output goes past 4 MiB: the harness keeps only its last 4 MiB, where Wine reports how the program ended, so a report in the part that was cut would be missed. Verbose `WINEDEBUG` channels can fill that much. It exits with 0 when the case passed, 1 when it failed and 2 when it could not run.

## Tests of the harness

The CTest presets `asan-ubsan` and `tsan` of `src/native` build the harness and run the cases of `unit/` with the native host's tests: the case reader, the comparison, the child process with its timeout, and a check that every case and expected output here parses and names a program and an expected output that exist.
