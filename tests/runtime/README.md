<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/runtime

The runtime harness: our own C17 code under MIT that runs the Win32 test programs of [tests/win32](../win32/README.md) under Wine on macOS and checks what they print (plan section 7.1). The Medium stages from M4.2 to M8.1 are verified with it. [runtime-macos-harness.md](../../docs/design/runtime-macos-harness.md) is its design.

| Folder | What it holds |
|---|---|
| [harness/](harness/) | The harness: the case reader, the expected outputs and their comparison, the child process, the prefixes and the result file of single mode, its child role, and `main.c` |
| [trap/](trap/) | The fork and exec trap of single mode, `libpaneport_trap.dylib` |
| [cases/](cases/) | One JSON file per case |
| [expected/](expected/) | The expected outputs that the cases name |
| [planted/](planted/) | Two cases that must fail, one with a wrong expected line and one with a wrong exit code |
| [unit/](unit/) | The CTest cases of the harness's own parts |

## Cases

A case names the program, its arguments, the environment variables of its runs, how many times it runs, its timeout, its exit code and its expected output:

```json
{"program": "console.exe", "arguments": ["3"], "timeout_seconds": 300, "exit_code": 3, "expected": "console.txt"}
{"program": "seh.exe", "environment": {"FEX_MULTIBLOCK": "0"}, "runs": 3, "timeout_seconds": 300, "exit_code": 0, "expected": "seh.txt"}
```

`arguments`, `environment` and `runs` are optional. `environment` adds its variables to every run: multi mode puts them in the loader's environment, on top of the harness's own, and single mode hands them to the session as its environment block. A name is capital letters, digits and underscores, does not start with a digit, and is not `WINEPREFIX`, which the harness sets. `runs`, from 1 to 100 and 1 when it is left out, runs the program that many times, each run within the timeout and judged on its own, and the case fails at the first run that fails. Several cases use them: they run `seh.exe` and `threads.exe` several times under `FEX_MULTIBLOCK=0`, with which FEX translates one block at a time, so that a regression of the divisor race or of the syscall frame fails CI in at least 19 of 20 runs ([runtime-macos-harness.md](../../docs/design/runtime-macos-harness.md) section 4), and `console_repeated` runs `console.exe` 20 times. Any other member, a missing one or a value of the wrong type fails the case, so that a typo does not go unnoticed. Names are file names without a folder.

A session case checks the session API of single mode instead of running a program ([runtime-macos-harness.md](../../docs/design/runtime-macos-harness.md) section 5.3). Its `session` member names what it checks, `start_stop`, `second_start`, `jit_not_ready` or `dispatcher_check`, and it has no `program`, `arguments`, `exit_code` or `expected`:

```json
{"session": "start_stop", "runs": 20, "timeout_seconds": 120}
```

## Expected outputs

Each line of an expected file stands for one line of the program's standard output. A line that starts with `= ` must match exactly; a line that starts with `~ ` is a POSIX extended regular expression that must match the whole line, as for the CPU feature line and the throughput. No other line may appear. An output line that ends in a carriage return matches without it. The files hold no host path and no user name.

## Running the cases

[eng/mac/run-harness.sh](../../eng/mac/run-harness.sh) builds what is missing, prepares a template prefix with `wineboot`, and runs each case in a fresh harness process:

```bash
eng/mac/run-harness.sh                # single mode, the default
eng/mac/run-harness.sh --mode multi   # Wine as upstream runs it, for comparisons
```

It needs a Mac with Apple silicon and the fork tools of `eng/mac/bootstrap.sh --forks`. In single mode the harness makes a fresh copy of the template prefix for each run and runs the program in a process of its own through the native host's session API; in multi mode the runner makes a copy for each case and starts a wineserver for it, and the session cases are skipped. Names of cases, such as `console threads`, run those alone. `--planted` runs the planted cases, in either mode, which must fail and be named; the `runtime` job of CI runs every case and then the planted ones (C-0011 section 4). The logs of each case, its output, the error output and the result, go to `build/runtime/logs/<case>/`, with a summary in `build/runtime/logs/summary.md`.

The harness itself is `paneport_harness`, which the CMake preset `macos-harness` of `src/native` builds with the native host's test build:

```bash
paneport_harness --case <file> --programs <folder> --expected <folder> --prefix <folder> --loader <file> --logs <folder> [--mode single|multi] [--template <folder>]
```

It copies the program into the prefix at `C:\paneport-tests` and runs it there with the case's arguments and variables, once for each run, and prints `PASS <case>`, or `FAIL <case>: <why>`, which names the failed run of a case of more than one, or in multi mode `SKIP <case>` for a session case. A run fails when a line differs, a line is missing or one more is printed, the exit code differs, the program runs past its timeout, or Wine reports an unhandled exception. Single mode compares the program's whole Windows exit code, from the session's process-exited event; multi mode has only the low 8 bits that the loader's exit status holds. It also fails when the output goes past 4 MiB, which is cut there, and when the error output goes past 4 MiB: the harness keeps only its last 4 MiB, where Wine reports how the program ended, so a report in the part that was cut would be missed. Verbose `WINEDEBUG` channels can fill that much. It exits with 0 when the case passed, 1 when it failed, 2 when it could not run and 3 when it was skipped.

- **Multi mode** starts Wine's loader on the program with `WINEPREFIX` naming the prefix, which the runs share.
- **Single mode** needs `--template`, which names the template's folder itself rather than a symbolic link to it, and absolute paths. Before each run it makes `--prefix` a fresh copy of the template, then starts itself in its child role, `paneport_harness --run-single <case file> --prefix <folder> --runtime <folder> --logs <folder>`. The child calls `pp_init`, arms the fork and exec trap, starts the session with `pp_session_start` and launches the program with `pp_process_launch`, writes the program's output lines to its standard output and everything else to its standard error, and after the session's end checks itself and writes `run-result.txt` into the case's log folder, with the program's exit code, the calls that the trap refused and what it found wrong. A run of single mode also fails when the child does not exit with 0, writes no result or a malformed one, or reports a failure or a refused call.

## Tests of the harness

The CTest presets `asan-ubsan` and `tsan` of `src/native` build the harness and run the cases of `unit/` with the native host's tests: the case reader, the comparison, the child process with its timeout, the copy and removal of single mode's prefixes, the copy both as a run makes it, cloning each file on macOS, and without clones, as a file system that cannot clone takes it and the runner's APFS never does, and its refusal of a folder that it cannot read and of an entry that is not a folder, a file or a symbolic link, its result file, a check that every case and expected output here parses and names a program and an expected output that exist, with every kind of session case there, and the trap (S4.3.9). The trap and its test are built without the sanitizers, whose runtimes interpose some of the same calls.
