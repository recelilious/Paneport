<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# host/jit

The JIT provider of C-0006: the app side of the JIT request protocol, which obtains, maps and verifies the executable code pool and reports its state through the JIT group of C-0005 section 2 (`paneport/jit.h`). [jit.md](../../../../docs/design/jit.md) describes each part. Only the four functions of `paneport/jit.h` are exported; the rest of the host and later stages reach the pool through `jit_provider.h`.

| File | What it holds |
|---|---|
| `jit_provider.h`, `jit_provider.c` | The exports of `paneport/jit.h`, the supervisor and request threads, the events, the pool registry, and what the session group and the Wine bridge need: `pp_jit_is_ready` and `pp_jit_pool_copy` |
| `jit_machine.h`, `jit_machine.c` | The state machine of C-0006 sections 3 to 5 with no thread and no system call, the pool sizes of section 4, and the regions of 128 MiB |
| `jit_platform.h` | The seam between the protocol and the system: reading the flags, command 1, command 0 and the clock |
| `jit_flags.c` | `CS_DEBUGGED` read with `csops` and `P_TRACED` read with `sysctl`, and the monotonic clock |
| `jit_backend_ios.c` | The iOS backend: the system's flags, and the commands as `brk #0xf00d` with the command in `x16`. Built for iOS only |
| `jit_backend_macos.c` | The macOS backend of the tests and the harness: a simulated broker, and regions of anonymous memory that is readable and executable. Built for macOS only |
| `jit_map.h`, `jit_map.c` | The writable alias of each region, made with `vm_remap` and `vm_protect`, and the check that no view is ever writable and executable |
| `jit_probe.h`, `jit_probe.c` | The verification probe of C-0006 section 3 step 6, through the signal dispatcher's JIT probe route |
| `jit_probe_handler.c` | The probe handler, which runs in signal context, in a file of its own so that a CTest test can list its calls with `nm` |

Everything here is our own code under the MIT License (C-0003 section 4), written from C-0006, StikJIT's `INTEGRATION.md` for the convention of the two commands, and Apple's and Arm's documentation; nothing comes from StikDebug or any other product's JIT helper (C-0001 section 6, C-0003 section 9). It holds no mutable global state except the documented process-wide singletons: the provider's state in `jit_provider.c` (`s_jit`), the probe's window `pp_jit_probe_window`, and the simulated broker of `jit_backend_macos.c` (`s_broker`).
