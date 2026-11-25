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
| `event_queue.c`, `event_queue.h` | The bounded lock-free queue with many producers and one consumer, and the hook of the test builds between a push's claim of a slot and its publication |
| `event_thread.c`, `event_thread.h` | The host event thread, its two queues, the callbacks, the buffer for early log lines, the overflow notices, the post that waits for room, and each event delivered after the lines posted before it, with a bounded wait for a producer inside its push |
| `log.c`, `log.h` | The log pipeline, the only path for native log lines, and the entries of the runtime's lines and a Windows program's, with their source and process |
| `session.c`, `wine_session.h` | The session and process groups of `paneport/session.h` and the Wine bridge of `paneport/wine_bridge.h`: loading the Wine fork's Unix library and its five entry points, the bridge table, the query of a process that `pp_process_get_state` asks until the host recorded its end, the launch's last check that the session runs before it calls the fork, the stop's bounded wait, the launch's one session and its process, their events, the dispatcher check after a session's start, and its hook in test builds |
| `guest_output.c`, `guest_output.h` | The capture of a Windows program's standard output and error: the pipes, the reader thread, its wake pipe, through which the session's stop ends it although a write end stays open, and the splitter of lines |
| `jit_state.c`, `jit_state.h` | The JIT precondition of `pp_session_start`: M3.1's state, and in test builds the state that `pp_test_jit_set_ready` sets |
| `memory_budget.c`, `memory_budget.h` | `pp_memory_get_budget`: the system's readings on iOS, a stub that tests set on macOS, and the Game Mode level |
| `crash_record.h`, `crash_record.c`, `crash_record_writer.c` | The crash record's layout; the preparation of its folder; and its async-signal-safe writer, in a file of its own so that a test can list the calls of its object |
| `device.c` | `pp_device_get_model`: the device's model identifier, into the caller's buffer |
| `platform.h`, `platform_darwin.c` | The calls whose spelling is Apple's: the thread identifier, the main thread, thread names and the model identifier (`hw.machine`) |
| `thread_role.c`, `thread_role.h` | The calling thread's role for a crash record and the bounds of its stack, which the signal dispatcher's handler reads; async-signal-safe, with its calls checked together with the handler's |
| [`jit/`](jit/README.md) | The JIT provider of C-0006: the JIT group of `paneport/jit.h`, the state machine, the backends of iOS and macOS, the pool's dual mapping and the verification probe ([jit.md](../../../docs/design/jit.md)) |
| [`signals/`](signals/README.md) | The signal dispatcher of C-0005 section 4, its registries and the Mach thread control helpers ([signals.md](../../../docs/design/signals.md)) |
| `test_exports.c` | The `pp_test_` exports of `paneport/testing.h`, compiled into test builds only |
| `signal_harness.c`, `signal_harness.h`, `signal_harness_handler.c` | The native part of the signal gate's stress harness and its `pp_test_` exports, and its guest fault handler in a file of its own, so that a test can list the handler's calls; compiled into test builds only |
| `Info.plist.in` | The template of the framework's Info.plist |

Everything here is our own code under the MIT License (C-0003 section 4). It holds no mutable global state except the documented process-wide singleton of each part.
