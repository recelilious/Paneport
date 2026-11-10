<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/native

The CTest suite of the native host (C-0011 section 1). The presets `asan-ubsan` and `tsan` of [src/native](../../src/native/README.md) build it with AddressSanitizer and UndefinedBehaviorSanitizer, and with ThreadSanitizer, and the `native` CI job runs both on macOS.

| File | What it holds |
|---|---|
| `CMakeLists.txt` | Registers each case of each test program as a test of its own, `<program>.<case>`, generates the sources of the header test, and registers the check of the crash record writer's calls |
| `pp_check.h` | The small assertion header: `PP_CHECK`, `PP_CHECK_EQUAL`, `PP_CHECK_TEXT`, and the runner of a program's cases |
| `test_support.h` | Helpers that several programs share: starting the host, waiting, threads with a context and a number, numbers in text, and temporary folders |
| `test_status.c` | S1.2.1: the interface version, `pp_last_error` per thread, and `pp_free`, whose freed bytes the sanitizers' allocator count must show |
| `test_event_queue.c` | S1.2.2: a full queue refuses and counts, and eight producers move 1,000,000 entries through one consumer |
| `test_event_thread.c` | S1.2.3: callbacks on the event thread only, none after `pp_shutdown`, a post in progress still delivered when the host stops, the event queue's first overflow logged once, one `pp_init` per process, the main-thread rule of test builds, and replacing a callback |
| `test_main_thread.c` | S1.2.3: the main-thread rule that ships, on the host built without `PANEPORT_TEST_BUILD` |
| `test_log.c` | S1.2.4: lines whole and in order per thread, early lines buffered, overlong lines cut, the log queue's first overflow logged once, and its notice kept for a callback when the buffer is full |
| `test_memory_budget.c` | S1.2.5: the limit as a sum, the Game Mode level, and a smaller `struct_size` |
| `test_crash_record.c` | S1.2.6: the documented bytes, the message cut, a record written in a signal handler, and the test flag |
| `check_writer_calls.cmake` | S1.2.6: lists the undefined symbols of the crash record writer's object with `nm`, and fails on a call that is not async-signal-safe |
| `test_headers.c`, `test_headers_linkage.cpp` | The header test of C-0005 section 5: one C17 and one C++20 source per header that CMake generates, all headers together, and a C++20 source that fails to compile when a header loses its `extern "C"` guards |

Each case runs in a process of its own, because `pp_init` runs once per process. A test keeps its state in a context that it passes to its callbacks and threads, not in file-scope variables; only the outputs of the signal handler in `test_crash_record.c`, which receives no argument, are static, as that test's documented singletons (C-0010 section 3). The tests keep no personal data, and make their temporary folders at run time.
