<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/win32

Small Windows console programs of our own, in C17 under MIT, that the runtime runs to show that it works (plan section 7.2). They need no third-party binary: no Microsoft, Valve or game file is used (C-0001 section 7, C-0011 section 1). The harness in [tests/runtime](../runtime/README.md) runs them under Wine and compares their output with its expected outputs; [runtime-macos-harness.md](../../docs/design/runtime-macos-harness.md) describes both.

| Program | Source | What it checks |
|---|---|---|
| `console.exe` | [programs/console.c](programs/console.c) | The console test program and L4's sample program: a greeting naming its architecture, then the CPU feature, checksum, structured exception handling and thread checks, each on one line, and the result line. An argument from 0 to 255 sets the exit code of a passing run. |
| `cpu_features.exe` | [programs/cpu_features.c](programs/cpu_features.c) | The vendor and feature flags that CPUID reports, that `IsProcessorFeaturePresent` agrees about SSE, SSE2 and SSE3, and that `GetLargePageMinimum` gives 0 or a power of two. |
| `checksum.exe` | [programs/checksum.c](programs/checksum.c) | A checksum over a fixed 64 MiB buffer beside the expected value, and the throughput of the fastest of four passes. |
| `seh.exe` | [programs/seh.c](programs/seh.c) | An access violation, an integer divide by zero and a `RaiseException` call, each caught by its own `__try` handler; a `__finally` block during an unwind; vectored handlers that run first and that continue execution; and divisions by a divisor in memory that another thread flips between 0 and 1, each of which must raise the divide error or divide by 1, for 20,000 divisions after the first divide error and at most 10 seconds. |
| `threads.exe` | [programs/threads.c](programs/threads.c) | Interlocked updates from four threads, a critical section, auto-reset and manual-reset events, the clocks `GetTickCount`, `GetTickCount64` and `QueryUnbiasedInterruptTime` across a sleep, stores and loads of a second thread that cross 16 bytes, a worker that is suspended, has its context changed with `SetThreadContext` and is resumed, and a thread that loops on `QueryPerformanceCounter`, a syscall under Wine, suspended 1000 times with its context read. |
| `file_io.exe` | [programs/file_io.c](programs/file_io.c) | `GetLogicalDrives` reporting `C:`, and creating, writing, reading back, mapping, truncating, copying, renaming, listing and deleting files in a folder on `C:`. |
| `shared_data.exe` | [programs/shared_data.c](programs/shared_data.c) | The shared user data at its fixed address, `0x7ffe0000`: `NtQueryPerformanceCounter`, `NtQuerySystemTime` and `NtDelayExecution` called through the imports of ntdll and through `GetProcAddress`, whose x64 thunks test a byte of the page; the version fields of the page read in 24 instruction forms and compared with `RtlGetVersion`, one of them a `movlps` that must keep the register's upper half; its interrupt time, system time and tick count compared with `QueryUnbiasedInterruptTime`, `GetSystemTimeAsFileTime` and `GetTickCount64`, and checked to move across a sleep of 100 ms; and writes in 6 forms, each of which must raise an access violation that names a write at its address. It has no i686 version before M7.3. |

The checks live in [checks/](checks/), one file per area, and the programs share them: `console.exe` runs the basic form of four of them.

## Output

Every line has the form `<area>: <text>`, and the last one is `result: pass` or `result: fail, <n> checks failed`. A program exits with 0, or with the code its argument gives, only when every check passed, and with 1 otherwise. A failed check also prints a line that names it, so that the log says why. Lines go to the standard output handle through `WriteFile`, one call per line, so that nothing waits in a buffer when a later check crashes the program. No line holds a path, because paths hold the user's name; only the CPU line and the throughput change from one machine to another, and the expected outputs match them with patterns.

## Building

[eng/mac/build-win32-tests.sh](../../eng/mac/build-win32-tests.sh) builds them with the pinned llvm-mingw, which `eng/mac/bootstrap.sh --forks` installs and names in `LLVM_MINGW`:

```bash
eng/mac/build-win32-tests.sh x86_64
```

The programs land in `build/win32/x86_64/`, with a linker map beside each one and the `compile_commands.json` that the analysis reads. The flags are those of C-0010 section 3, `-Wall -Wextra -Wpedantic -Werror`, plus:

- `-fms-extensions`, for `__try`, `__except` and `__finally`. clang accepts them for MinGW targets with this flag. It has no asynchronous exceptions there, so it catches only exceptions raised inside a call: every fault happens in a function of its own that the `__try` block calls. The keyword warning that `-Wpedantic` gives for `__try` is turned off around those functions alone. Only `checks/seh_check.c` gets the flag: with it, clang-tidy's `readability-duplicate-include` reports includes that are not repeated, which that file alone tells it to ignore.
- `-D__USE_MINGW_ANSI_STDIO=0`, so that the UCRT formats the output, as in the C program of the mingw-w64 runtime review ([forks.md](../../docs/design/forks.md) section 9).
- `-Wl,--section-alignment,16384`, so that no 16 KB page of Apple silicon holds both code and writable data.
- `-lntdll`, the import library of ntdll, for the syscall exports that `shared_data.exe` imports.
- `-Wl,--strip-debug`, and no `-g`, so that the programs hold no debug info: llvm-mingw's runtime objects carry the paths of the machine that built them. `-ffile-prefix-map` keeps the paths of the checkout and of llvm-mingw out of `__FILE__`, and the build fails if a program holds the path of the checkout, of llvm-mingw, of the home folder, or of any home or temporary folder (C-0009 section 6).

The target is a parameter. This stage builds x86-64; M7.3 adds the i686 builds, whose code paths the checks already have but no build has run yet, except those of `checks/shared_data_check.c`: for i686 each of its five checks fails with a line that names M7.3, because the thunks of the i386 ntdll have another form and FEX's WoW64 module is another translator, which M7.3 covers.

## On Windows

The programs are meant to run natively on Windows 11 too, where they should print the same lines, except the CPU line and the throughput, and exit with 0. The native runs so far are local checks, outside CI, of programs that WSL's clang built for the MSVC target against the Windows SDK and the MSVC runtime of the workstation: M4.2's fix round ran `seh.exe` and `threads.exe`, and M4.5 ran all seven programs, where the run of `shared_data.exe` is the check of S4.5.2 ([L4 brief](../../docs/design/stages/L4-first-windows-program.md)). No native run of the programs that the pinned llvm-mingw builds has been done yet; lld warns on every link that an image with `/align` but without `/driver` may not run, which such a run would settle. `eng/mac/build-win32-tests.sh` lets that one link warning through and fails on any other.
