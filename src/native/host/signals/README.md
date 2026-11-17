<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# host/signals

The signal dispatcher of C-0005 section 4: the process's single handler of SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT, which `pp_init` installs, the registries it routes by, and the Mach thread control helpers. [signals.md](../../../../docs/design/signals.md) describes each part. Nothing here is exported; the rest of the host and later stages call it through `signals.h`.

| File | What it holds |
|---|---|
| `signals.h` | The internal interface: installing, the installed check, thread marks, JIT pool ranges, guest reservations, the probe mode, the guest and probe handlers, and the counters, with the capacities of the tables |
| `signal_state.h` | The state that the handler reads and the writers change, one process-wide singleton, and the helpers that read a signal's action |
| `signal_handler.c` | Everything that runs in a signal handler: the dispatcher's handler, the routing reads, the calls of the guest and probe handlers, the chain to the saved actions, the end of the app with a crash record, and the last-resort handler that the host installs when it is loaded. A CTest test checks its calls with `nm` |
| `signal_install.c` | Installing and uninstalling the dispatcher, the installed check, and the alternate signal stacks |
| `signal_registry.c` | The writers of the thread marks, the ranges, the probe mode and the handlers |
| `signal_context.h` | The program counter of a signal's context on arm64, and the single instructions with which the harness and the tests plant faults |
| `thread_control.h`, `thread_control.c` | Suspending, reading, writing and resuming an arm64 thread through Mach calls |

Everything here is our own code under the MIT License (C-0003 section 4). It holds no mutable global state except the documented process-wide singletons `pp_signal_state` and the registry's locks.
