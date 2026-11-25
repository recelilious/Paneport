<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# include/paneport

The public C17 headers of the host interface (C-0005 section 1). They compile as C17 and as C++20, wrap their declarations in `extern "C"` for C++ callers, and give every function a comment with its purpose, its thread rule and who owns the memory it takes or returns (C-0010 section 3). Only the function groups that the host implements are declared; later groups come with their stages and raise the minor version.

| Header | What it declares |
|---|---|
| `common.h` | The interface version (`PP_ABI_VERSION_MAJOR`, `PP_ABI_VERSION_MINOR`), `pp_status_t` and its codes, and the export and linkage macros |
| `host.h` | The life cycle and diagnostics groups: `pp_abi_version`, `pp_last_error`, `pp_free`, `pp_init`, `pp_shutdown`, the event and log callbacks, their structs, and `pp_device_get_model`, which version 1.1 added; since version 1.4 a log line names its source, the host, the runtime or a Windows program's output or errors, and the program's process |
| `jit.h` | The JIT group, which version 1.3 added: `pp_jit_get_state`, `pp_jit_set_enabler_opened`, `pp_jit_note_foreground` and `pp_jit_request_pool`, the states and failure reasons of C-0006, `pp_jit_status_t`, the pool sizes and timeouts, `PP_E_JIT_STATE` and the event kind `PP_EVENT_JIT_STATE_CHANGED` |
| `memory.h` | The memory group: `pp_memory_get_budget` and `pp_memory_budget_t` |
| `session.h` | The session and process groups, which version 1.4 added: `pp_session_start`, `pp_session_stop`, `pp_process_launch`, `pp_process_get_state` and `pp_process_terminate`, their three structs, the architectures and process states, the status codes `PP_E_JIT_NOT_READY`, `PP_E_SESSION_EXISTS` and `PP_E_NO_SESSION`, and the event kinds of the session and its process |
| `wine_bridge.h` | The Wine bridge of C-0005 section 3: `pp_wine_bridge_t`, the table of the host's functions that the Wine fork calls, and the types of the guest fault handler. It includes no other Paneport header, declares no export, and the fork's build reads it from this folder |
| `testing.h` | The exports of the host's test builds only: the layout table of C-0005 section 5, a test crash record, the memory stub, helpers for the binding tests, since version 1.2 the signal dispatcher's installed check and the stress harness of the signal gate, and since version 1.4 the JIT state that the macOS harness sets ready and a log line with a source and a process for the binding tests |
| `paneport.h` | Includes every header of the shipped interface, which leaves out `testing.h` and `wine_bridge.h` |

[native-host.md](../../../../docs/design/native-host.md) describes the conventions, and [single-process-wine.md](../../../../docs/design/single-process-wine.md) the session and the bridge.
