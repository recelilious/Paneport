<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/native

The CTest suite of the native host (C-0011 section 1). The presets `asan-ubsan` and `tsan` of [src/native](../../src/native/README.md) build it with AddressSanitizer and UndefinedBehaviorSanitizer, and with ThreadSanitizer, and the `native` CI job runs both on macOS.

| File | What it holds |
|---|---|
| `CMakeLists.txt` | Registers each case of each test program as a test of its own, `<program>.<case>`, generates the sources of the header test, and registers the checks of async-signal-safe calls: of the crash record writer and of the signal dispatcher's handler with `thread_role.c`, each on the objects of the test build and of the shipped build, and of the harness's guest fault handler |
| `pp_check.h` | The small assertion header: `PP_CHECK`, `PP_CHECK_EQUAL`, `PP_CHECK_TEXT`, and the runner of a program's cases |
| `test_support.h` | Helpers that several programs share: starting the host, waiting, threads with a context and a number, numbers in text, and temporary folders |
| `test_status.c` | S1.2.1: the interface version, `pp_last_error` per thread, and `pp_free`, whose freed bytes the sanitizers' allocator count must show; and, for M1.3, `pp_device_get_model` with a large buffer, no buffer and one too small (case `device_model`) |
| `test_event_queue.c` | S1.2.2: a full queue refuses and counts, and eight producers move 1,000,000 entries through one consumer |
| `test_event_thread.c` | S1.2.3: callbacks on the event thread only, none after `pp_shutdown`, a post in progress still delivered when the host stops, the event queue's first overflow logged once, one `pp_init` per process, the main-thread rule of test builds, and replacing a callback |
| `test_main_thread.c` | S1.2.3: the main-thread rule that ships, on the host built without `PANEPORT_TEST_BUILD` |
| `test_log.c` | S1.2.4: lines whole and in order per thread, early lines buffered, overlong lines cut, the log queue's first overflow logged once, and its notice kept for a callback when the buffer is full |
| `test_memory_budget.c` | S1.2.5: the limit as a sum, the Game Mode level, and a smaller `struct_size` |
| `test_crash_record.c` | S1.2.6: the documented bytes, the message cut, a record written in a signal handler, and the test flag |
| `check_signal_safe_calls.cmake` | S1.2.6 and S2.2.1: lists with `nm` the undefined symbols of a group of the host's objects that the group does not define itself, for the crash record writer and the signal dispatcher's handler with `thread_role.c`, as the test build and the shipped build compile them, and for the harness's guest fault handler, and fails on a call that is not on the group's list of async-signal-safe functions ([signals.md](../../docs/design/signals.md) section 10) |
| `signal_test_support.h` | Helpers of the signal tests: a stand-in for the runtime's handler, which also ends a SIGILL and a fault in managed code in a crash path like Mono's, pages to fault on, a fault of each of the six signals, a loop that runs a thread out of stack, child processes that are expected to end by a signal, and the crash record they leave |
| `test_signal_chain.c` | S2.2.1: each of the six signals reaches the stand-in exactly once with its original `siginfo_t`; a plain handler runs with its mask and its own signal blocked; ignored signals; the record before the runtime's crash path and the 4 KiB limit; the records of the default action, of the last-resort handler and of both crash paths; simulated and real stack overflows; and a second fault that waits for a record in progress |
| `test_signal_routing.c` | S2.2.2: every fault takes its route while writers change the registries, the probe route only in probe mode, routing by the program counter, a thread's mark ends with it, and full tables refuse |
| `test_signal_guest.c` | S2.2.3: the guest fault handler sees only guest-path faults; a declined fault, one with no handler, a marked thread's stack overflow and a fault inside the guest or the probe handler end the app with a record; removing a handler waits for a fault inside it |
| `test_thread_control.c` | S2.2.4: a suspended thread's register changed and seen after it resumes, an ended thread's error, and a thread that cannot suspend itself |
| `test_signal_installed.c` | S2.2.5: the installed check, the alternate stacks of the main thread and of marked threads and their release, and the last-resort handler at load, which leaves an ignored signal alone |
| `test_signal_harness.c` | S2.2.6: the native stress harness for 60 seconds beside a stand-in for the managed loop, the conditions of its start, and its two structs' `struct_size` |
| `test_headers.c`, `test_headers_linkage.cpp` | The header test of C-0005 section 5: one C17 and one C++20 source per header that CMake generates, all headers together, and a C++20 source that calls the host, so the program fails to link when a header loses its `extern "C"` guards |

Each case runs in a process of its own, because `pp_init` runs once per process. A test keeps its state in a context that it passes to its callbacks and threads, not in file-scope variables; only the outputs of the signal handler in `test_crash_record.c`, and the stand-in and the program's path in the signal tests, which signal handlers and child processes reach without an argument, are static, as those tests' documented singletons (C-0010 section 3). A case that ends by a signal runs that part in a child process of its own program, which it starts with `posix_spawn`. A case that cannot run in one of the builds prints `Skipped: ` and its reason as the first line of its output, and CTest reports it as skipped: the two cases of `test_signal_guest.c` that need a fault inside a handler, which ThreadSanitizer's run cannot deliver. The tests keep no personal data, and make their temporary folders at run time.
