<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# include/paneport

The public C17 headers of the host interface (C-0005 section 1). They compile as C17 and as C++20, wrap their declarations in `extern "C"` for C++ callers, and give every function a comment with its purpose, its thread rule and who owns the memory it takes or returns (C-0010 section 3). Only the function groups that the host implements are declared; later groups come with their stages and raise the minor version.

| Header | What it declares |
|---|---|
| `common.h` | The interface version (`PP_ABI_VERSION_MAJOR`, `PP_ABI_VERSION_MINOR`), `pp_status_t` and its codes, and the export and linkage macros |
| `host.h` | The life cycle and diagnostics groups: `pp_abi_version`, `pp_last_error`, `pp_free`, `pp_init`, `pp_shutdown`, the event and log callbacks, and their structs |
| `memory.h` | The memory group: `pp_memory_get_budget` and `pp_memory_budget_t` |
| `testing.h` | The exports of the host's test builds only: the layout table of C-0005 section 5, a test crash record, the memory stub, and helpers for the binding tests |
| `paneport.h` | Includes every header of the shipped interface, which leaves out `testing.h` |

The Wine bridge header `wine_bridge.h` of C-0005 section 3 comes with the Wine session work. [native-host.md](../../../../docs/design/native-host.md) describes the conventions.
