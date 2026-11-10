<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/native

The CTest suite of the native host (C-0011 section 1). The presets `asan-ubsan` and `tsan` of [src/native](../../src/native/README.md) build it with AddressSanitizer and UndefinedBehaviorSanitizer, and with ThreadSanitizer, and the `native` CI job runs both on macOS.

| File | What it holds |
|---|---|
| `CMakeLists.txt` | Registers each case of each test program as a test of its own, `<program>.<case>`, and generates the sources of the header test |
| `pp_check.h` | The small assertion header: `PP_CHECK`, `PP_CHECK_EQUAL`, `PP_CHECK_TEXT`, and the runner of a program's cases |
| `test_support.h` | Helpers that several programs share: starting the host, waiting, threads, numbers in text, and temporary folders |
| `test_status.c` | S1.2.1: the interface version, `pp_last_error` per thread, and `pp_free` |
| `test_event_queue.c` | S1.2.2: a full queue refuses and counts, and eight producers move 1,000,000 entries through one consumer |
| `test_event_thread.c` | S1.2.3: callbacks on the event thread only, none after `pp_shutdown`, one `pp_init` per process, the main-thread rule, and replacing a callback |
| `test_log.c` | S1.2.4: lines whole and in order per thread, early lines buffered, overlong lines cut, and the first overflow logged once |
| `test_memory_budget.c` | S1.2.5: the limit as a sum, the Game Mode level, and a smaller `struct_size` |
| `test_crash_record.c` | S1.2.6: the documented bytes, the message cut, a record written in a signal handler, and the test flag |
| `test_headers.c` | The header test of C-0005 section 5, with one C17 and one C++20 source per header that CMake generates |

Each case runs in a process of its own, because `pp_init` runs once per process. The tests keep no personal data, and make their temporary folders at run time.
