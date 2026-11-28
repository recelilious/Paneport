// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The child role of single mode (docs/design/runtime-macos-harness.md section 5.1): one run of a
// case in a fresh harness process, which the harness's parent starts for each run with
//
//   paneport_harness --run-single <case file> --prefix <folder> --runtime <folder> --logs <folder>
//
// The child is the process's main executable and runs Wine's code itself, through the native
// host's session API: pp_init on its main thread, the fork and exec trap armed, the JIT test hook
// ready, or in pool mode the JIT code pool requested from the macOS backend's simulated broker
// (docs/design/jit.md), pp_session_start with the case's variables, and for a program case
// pp_process_launch of
// C:\paneport-tests\<program> with its output captured. The program's output lines go to the
// child's standard output, one per line, which the parent compares; its error lines and the
// runtime's and the host's lines go to standard error, the run's error output. Once the session
// has ended and stopped, the child shuts the host down, checks itself and writes its result into
// <logs>/run-result.txt (run_result.h), with the program's exit code. It exits with 0 when the
// harness's side of the run worked, and with 1 when the result names a failure or a refused call.

#ifndef PP_HARNESS_SINGLE_CHILD_H
#define PP_HARNESS_SINGLE_CHILD_H

#include <stdbool.h>

// What the parent tells the child, as absolute paths: the case file; the run's prefix, a fresh
// copy of the template prefix; the runtime folder, Wine's install, which holds
// lib/wine/aarch64-unix/ntdll.so; and the case's log folder, for the result and the host's crash
// records. pool says that the run is one of pool mode ("--jit pool" on the child's command line):
// the child then gets the JIT state ready with a real pool instead of the answer of the test
// builds, and checks the pool's counters after a program.
typedef struct pp_single_child_t {
    const char *case_path;
    const char *prefix;
    const char *runtime;
    const char *logs;
    bool pool;
} pp_single_child_t;

// Runs the child role on the process's main thread and returns its exit status.
int pp_single_child_run(const pp_single_child_t *paths);

#endif
