<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# host

The code of the native host (C-0004 section 2), in C17. [native-host.md](../../../docs/design/native-host.md) describes each part.

| File | What it holds |
|---|---|
| `host.c`, `host_state.h` | The life cycle: `pp_init`, `pp_shutdown`, the callback setters, `pp_abi_version`, `pp_free`, and the rule for the main thread |
| `error.c`, `error.h` | The per-thread message of `pp_last_error` |
| `event_queue.c`, `event_queue.h` | The bounded lock-free queue with many producers and one consumer |
| `event_thread.c`, `event_thread.h` | The host event thread, its two queues, the callbacks, the buffer for early log lines, and the overflow notices |
| `log.c`, `log.h` | The log pipeline, the only path for native log lines |
| `memory_budget.c`, `memory_budget.h` | `pp_memory_get_budget`: the system's readings on iOS, a stub that tests set on macOS, and the Game Mode level |
| `crash_record.h`, `crash_record.c`, `crash_record_writer.c` | The crash record's layout; the preparation of its folder; and its async-signal-safe writer, in a file of its own so that a test can list the calls of its object |
| `platform.h`, `platform_darwin.c` | The calls whose spelling is Apple's: the thread identifier, the main thread and thread names |
| `test_exports.c` | The `pp_test_` exports of `paneport/testing.h`, compiled into test builds only |
| `Info.plist.in` | The template of the framework's Info.plist |

Everything here is our own code under the MIT License (C-0003 section 4). It holds no mutable global state except the documented process-wide singleton of each part.
