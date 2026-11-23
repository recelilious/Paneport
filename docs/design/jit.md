# JIT Provider

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-23T04:10Z |

Back to the [design index](README.md).

This document describes the app side of the JIT request protocol of C-0006 as stage M3.1 built it (see the [L3 brief](stages/L3-jit-on-iphone.md)): the provider in `src/native/host/jit/` and its two backends, the states and their transitions, the pool's size and regions, the debugger flags and the broker-recognition rule, the commands and the dual mapping, the verification probe, the threads and the JIT group of the host interface, the list of verified iOS builds, and the tests. The broker script that StikDebug runs is described in [jit-broker.md](jit-broker.md). [signals.md](signals.md) describes the signal dispatcher whose probe route the probe uses, and [native-host.md](native-host.md) the rest of the host. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

Nothing here has run on a device yet: everything that only the iPhone can show is **not yet verified on device**, and L3's acceptance verifies it.

## 1. Scope

The provider obtains the executable code pool through the debugger that StikDebug attaches, maps it so that no mapping is ever writable and executable at once, verifies it with no debugger attached, and reports its state through the JIT group of C-0005 section 2 (`paneport/jit.h`). M3.1 builds the provider with an iOS backend and a macOS backend, the CTest suite that drives the whole life cycle on macOS, the bindings in Paneport.Interop, and the list of verified iOS builds with its check. Out of scope: the JIT setup screen, the URL opener and the self-test of the device test flavor (L3), any Wine session or translator (L4 and later), the per-title request flow (L6 and later), and an in-app enabler (C-0006 section 2).

## 2. Parts and the two backends

| Part | Files | What it does |
|---|---|---|
| Provider | `jit_provider.h`, `jit_provider.c` | The four exports, the two threads, the events, the registry of regions, and `pp_jit_is_ready` and `pp_jit_pool_copy` for the session group and the Wine bridge |
| State machine | `jit_machine.h`, `jit_machine.c` | Every transition and both timeouts, with no thread, no lock and no system call (section 3); the pool sizes (section 4) |
| Platform seam | `jit_platform.h` | Reading the two flags, command 1, command 0 and the clock, as function pointers |
| Flags | `jit_flags.c` | `CS_DEBUGGED` and `P_TRACED` from the system, and the monotonic clock (section 5) |
| iOS backend | `jit_backend_ios.c` | The system's flags, and the commands as `brk #0xf00d` (section 6) |
| macOS backend | `jit_backend_macos.c` | A simulated broker, for the tests and the runtime harness |
| Mapping | `jit_map.h`, `jit_map.c` | The writable alias, and the protection checks (section 6) |
| Probe | `jit_probe.h`, `jit_probe.c`, `jit_probe_handler.c` | The verification probe and its signal handler (section 7) |

**The seam.** `pp_jit_platform_t` holds the five functions in which the backends differ, and a polling interval. Everything else, the machine, the threads, the alias, the dispatcher's ranges and the probe, is the same code on both platforms, so the CTest suite on macOS runs the code that ships, except the two commands and the system's flags.

**The iOS backend** reads the flags of section 5 and sends the commands of section 6. CMake builds it for `CMAKE_SYSTEM_NAME` iOS only.

**The macOS backend** stands in for StikDebug, which does not exist on macOS. Its simulated broker attaches when the app opens the enabler, sets the debugged flag, which then stays set as `CS_DEBUGGED` does, maps each region as anonymous memory that is readable and executable, and detaches on command 0. The test builds, the binding tests and the runtime harness use it; M4.4 replaces M4.3's test hook with it on the harness (L4 brief).

**Why not `MAP_JIT` on macOS.** The L3 brief planned a `MAP_JIT` backend, because `MAP_JIT` was thought to be the only way to execute generated code on a Mac. The probe of this stage (section 12) showed otherwise on the runner: anonymous memory made executable runs code with no entitlement, in a binary linked as CMake links the tests and in one signed with the hardened runtime, as M4.2 had found for the harness. A `MAP_JIT` region is writable and executable at once by design (the probe read protection 7 and maximum 7, and the runner's virtual M1 does not support the per-thread write protection), which is exactly what the pool must never be. A region that is mapped readable and executable from the start, with the maximum protection that anonymous memory keeps, is what the debugger hands over on iOS (C-0006 section 1), so the backend models the device more closely and never has a writable and executable page. The CTest suite plants writable memory, whose maximum protection still allows both permissions, as the counterexample that the protection check must find (section 10); a `MAP_JIT` mapping, which clang-tidy's analyzer refuses in our code, would be found the same way.

## 3. States and transitions

The states are those of C-0006 section 5, as the `PP_JIT_STATE_` values 0 to 6 of `pp_jit_state_t`, an `int32_t` so that the type crosses the interface with a fixed width (C-0005 section 1).

| From | Input | To |
|---|---|---|
| `not_debugged` | A reading with `P_TRACED` and `CS_DEBUGGED` set, after the enabler opened in this launch and a reading since then found `P_TRACED` clear | `debugger_attached` |
| `debugger_attached` | A reading with `P_TRACED` clear: the broker left before any command | `not_debugged` |
| `debugger_attached` | The app takes the enabler back: the attached debugger is then not the broker | `not_debugged` |
| `debugger_attached` | `pp_jit_request_pool`, with one eighth of the memory limit at least the minimum | `requesting` |
| `debugger_attached` | `pp_jit_request_pool`, with less | `failed` (memory limit) |
| `requesting` | Command 0 returned, after every region was mapped | `waiting_detach` |
| `requesting` | A reading with `P_TRACED` clear before command 0 | `failed` (debugger lost) |
| `requesting` | No region, a region that is not readable and executable only, or an alias that cannot be made | `failed` (region refused, mapping) |
| `requesting` | 60 s since it was entered, with the pool not complete | `failed` (pool timeout) |
| `requesting`, `waiting_detach` | 30 s since command 0 was sent, with `P_TRACED` still set | `failed` (detach timeout) |
| `waiting_detach` | A reading with `P_TRACED` clear | `verifying` |
| `verifying` | The probe passed, and every view kept its protection | `ready` |
| `verifying` | The probe failed | `failed` (verification) |
| any but `ready` and `failed` | A thread, a reading of the flags or the memory limit, or a dispatcher range that the system refuses | `failed` (system) |

- **One transition per input.** Each input of the machine makes at most one transition, and the provider compares the state before and after each input, so every change reaches the log and the event callback (C-0005 section 1). The event's kind is `PP_EVENT_JIT_STATE_CHANGED` (1) and its code the new state; `pp_jit_get_state` tells the rest.
- **The end states.** `ready` and `failed` take no input; leaving `failed` needs a relaunch (C-0006 section 5). In `ready`, a reading is only recorded: a pool once authorized stays authorized, and a debugger that attaches later is reported as foreign.
- **Times.** Every input carries the time at which it happened, from the platform's clock, so the machine books the time spent in each state, and the tests drive the timeouts with a clock of their own.

## 4. Pool size and regions

`pp_jit_request_pool` takes the size in MiB that .NET chose: the title's `jitPoolMiB` when its compatibility entry has one (C-0008 section 2), and `PP_JIT_POOL_DEFAULT_MIB` (512) otherwise. The host limits it to `PP_JIT_POOL_MIN_MIB` (256) and `PP_JIT_POOL_MAX_MIB` (896), and to one eighth of the memory limit that `pp_memory_get_budget` reads at that moment, in whole MiB (C-0006 section 4). When that eighth is below the minimum, the state becomes `failed`. A size inside the limits is kept as it is, so a title's override is honored.

The pool is requested in **regions of 128 MiB** (`PP_JIT_REGION_MIB`), the last one taking the rest: four for the default pool, seven for the largest. 128 MiB is the reach of an arm64 `B` or `BL` instruction, so translated code can always branch directly to any other code in its region. Smaller regions would cost one command each, which takes the debugger seconds for its page writes (C-0006 section 4), and larger ones would give the translator nothing. C-0006 leaves the region size open; the phase 1 gate's measurements may change it (C-0012 section 3).

## 5. The debugger flags and the broker-recognition rule

**The two flags** (C-0006 section 3 step 1):

- "Debugged since launch" is `CS_DEBUGGED`, bit `0x10000000` of the code-signing status word that `csops(getpid(), CS_OPS_STATUS, ...)` returns, with `CS_OPS_STATUS` 0. No public SDK header declares `csops`, although it is part of the system's C library on every Apple platform, so `jit_flags.c` declares the prototype and the two values itself, from XNU's published sources (`bsd/sys/codesign.h` and `osfmk/kern/cs_blobs.h`), as C-0003 section 5 asks for an interface definition.
- "Debugger attached now" is `P_TRACED` in the `p_flag` word of the process's `kinfo_proc`, which `sysctl` with `CTL_KERN`, `KERN_PROC` and `KERN_PROC_PID` returns, the way Apple's Technical Q&A QA1361 reads it.

On the runner, the probe read `0x22020001` and `0x4004` with no debugger, and `0x32020021` and `0x5804` under lldb: both flags set, as expected (section 12).

**The rule** (C-0006 section 3 step 2). The app calls `pp_jit_set_enabler_opened(1)` right before it asks iOS to open `stikdebug://enable-jit`, and `pp_jit_set_enabler_opened(0)` when iOS refuses to open it. The host reads the flags at that moment. It takes an attached debugger for the broker only when the enabler opened in this launch, has not been taken back, and a reading since then found `P_TRACED` clear, so that the debugger attached afterwards. A debugger that was attached when the enabler opened, such as Xcode's, is foreign until it detaches; a debugger that attaches without the enabler is foreign. The host sends a foreign debugger no command: `pp_jit_request_pool` needs `debugger_attached`, and the request thread checks the state and a fresh reading before each command (section 6). The status's `foreign_debugger` tells the screen to show the foreign-debugger message.

**`CS_DEBUGGED` too.** StikJIT's `INTEGRATION.md` has the app wait until `CS_DEBUGGED` appears before any command, so the transition to `debugger_attached` also needs it. XNU sets both flags when a debugger attaches; a reading that finds `P_TRACED` alone only waits for the next.

**Readings.** `pp_jit_note_foreground` reads the flags at once whenever the app returns to the foreground (C-0006 section 3 step 1); the app's head calls it from its scene delegate (L3). After the enabler opened, the supervisor also reads them every 50 ms (`PP_JIT_POLL_MS`), so the broker's attach and its detach are seen while the app is in the foreground.

## 6. Commands and the dual mapping

**The commands** follow the convention that StikJIT's `INTEGRATION.md` documents (C-0006 section 1): the app executes `brk #0xf00d` with the command in `x16` and the arguments in `x0` and `x1`, and the broker moves the program counter past the breakpoint. Command 1, `JIT26PrepareRegion`, goes out with a NULL address in `x0` and the region's length in `x1`, and returns the region's address in `x0`; command 0, `JIT26Detach`, makes the debugger detach. `jit_backend_ios.c` writes each as a few instructions of inline assembly, which declare `x0`, `x1`, `x16` and memory as changed. The request thread sends a command only in the `requesting` state and only right after a reading that a debugger is attached (C-0006 section 3); a breakpoint without the broker ends the process, and the moment between the reading and the breakpoint is the one risk that remains, as it does for every app of this convention.

**The alias** (C-0006 section 3 step 4). The region that command 1 returns must be page-aligned, mapped throughout, and readable and executable only; otherwise the state becomes `failed` (region refused). `vm_remap` with copy off and `VM_INHERIT_NONE` maps the same memory a second time, and `vm_protect` makes that alias readable and writable. Then `vm_protect` lowers the maximum protection of both views to their current one, so that neither can ever gain the other's permission; that step is best effort, logged when it fails, because the current protections are what C-0006 requires. Both views are checked with `vm_region_64`, page range by page range, before the region joins the registry. The translator writes only through the alias and runs only from the executable view; no mapping is ever writable and executable at once.

**The dispatcher's ranges.** Both views of each region are registered with `pp_signals_add_pool_range` before anything runs in them, and stay registered for the life of the process: later, a fault whose address or program counter lies in the pool takes the guest path (signals.md section 4).

**The registry** holds, for each region, the executable view, the writable alias, the size and the dispatcher's two handles (`pp_jit_pool_region_t`). `pp_jit_pool_copy` copies it; once the state is `ready`, it no longer changes. M4.4 hands it to the translator through a member of the Wine bridge (C-0005 section 3). `paneport/wine_bridge.h` includes no other Paneport header, so that member declares its own struct of the same addresses and size.

## 7. The verification probe

With no debugger attached (C-0006 section 3 step 6), the supervisor runs the probe over every region, in the `verifying` state, through the dispatcher's JIT probe route: it sets the probe handler and turns the probe mode on for its own run only (signals.md sections 4 and 6).

- **Pages.** The first page of every 16 MiB (`PP_JIT_PROBE_INTERVAL`) and the last page of each region: nine pages of a region of 128 MiB, and 36 of the default pool.
- **The routine.** Two instructions written through the alias at the start of the page, `movz w0, #value` and `ret`, with a value of its own for each page, so that a routine that ran from another page fails too. The probe makes them visible to instruction fetches with the sequence of the Arm architecture for modified code, by address and for one cache line: `dc cvau` through the alias, which was written, `dsb ish`, `ic ivau` through the view, which is run, `dsb ish` and `isb`. It calls the routine from the view with `blr`, checks the value, and writes zero words back, which are permanently undefined instructions, so that nothing of the probe stays in the pool.
- **Faults.** Every address stays an integer and every access is one instruction of inline assembly, so the probe handler knows each one: a window of atomics names the phase (store, flush or run), the alias address of the routine and the view's page. In the store and flush phases, a fault at those addresses is skipped by one instruction, which has no other effect. In the run phase, a fault whose program counter lies in the page returns from the routine: the routine is a leaf that leaves the link register alone, so the handler sets the program counter to the link register and `x0` to `PP_JIT_PROBE_FAULTED`. Each handled fault is counted, and the first one's signal recorded. Any other fault the handler declines, which ends the app with the crash record "The JIT probe handler declined a fault." (signals.md section 6). The handler uses atomics and the fault's context only; a CTest test lists its calls with `nm` in both builds.
- **The verdict.** The probe stops at the first page that faults or returns another value, and logs its region and offset. After a passing probe, the supervisor checks the protections of every view once more. Either failure makes the state `failed` (verification).

On the runner, a page of the view that may not run raises SIGBUS with the page's address as both the fault address and the program counter, and the link register still holds the return address; a write to a read-only page of the alias raises SIGBUS at the alias address (section 12). What iOS raises for a page that the debugger did not prepare is **not yet verified on device**; the handler takes any of the six signals.

## 8. Threads and the interface

**Threads.** Two threads of the provider's own, so that the main thread never waits for the debugger:

- The supervisor, `paneport.jit`, starts when the enabler opens or the pool is requested, reads the flags every 50 ms, applies the timeouts, runs the probe, and ends in `ready` or `failed`, or at `pp_shutdown`, which waits for it.
- The request thread, `paneport.jit-pool`, starts with `pp_jit_request_pool` and sends the commands, maps each region and registers it. A command may never return, if the debugger never answers; the supervisor's timeouts then make the state `failed`, nobody waits for the thread, and when its command comes back late, it sends nothing more and gives back the region's alias.

One mutex guards the provider; the commands and the probe run without it, so `pp_jit_get_state` answers while they take seconds.

**The JIT group** of `paneport/jit.h` raises the interface to version 1.3 (C-0005 section 1):

| Function | Thread | What it does |
|---|---|---|
| `pp_jit_get_state` | [any] | Fills `pp_jit_status_t`, within the caller's `struct_size`: the state, the failure's reason, the two flags, whether the enabler opened and whether a debugger is foreign, the sizes asked for and granted, the regions planned and mapped, the time in the current state, the time spent in each state of the request, and the authorization time from the request to `ready` |
| `pp_jit_set_enabler_opened` | [main] | 1 right before the app opens the enabler's URL, 0 when iOS refused to open it (section 5) |
| `pp_jit_note_foreground` | [main] | Reads the flags again on the return to the foreground |
| `pp_jit_request_pool` | [main] | Starts the request (section 4), and returns at once |

The group adds the status code `PP_E_JIT_STATE` (-10), which a call gets in a state that does not take it, with a message that names the state, and the event kind `PP_EVENT_JIT_STATE_CHANGED` (1). M4.3 adds the session group's codes and event kinds beside them; whichever of the two stages merges second renumbers its own. `pp_session_start` refuses with `PP_E_JIT_NOT_READY` until `pp_jit_is_ready` holds (C-0005 section 2), which M4.3 and M4.4 wire. A failure that the provider meets after a call returned, such as the memory limit, reaches the state, not the call's status.

**The device's flow**, which L3 wires and verifies: the JIT setup screen calls `pp_jit_set_enabler_opened(1)` and opens `stikdebug://enable-jit` with the bundle identifier, the process identifier and the broker script, as [jit-broker.md](jit-broker.md) says; iOS switches to StikDebug, which attaches and returns to Paneport; the head calls `pp_jit_note_foreground`, the state becomes `debugger_attached`, and on that event the app calls `pp_jit_request_pool`. Whether StikDebug serves the commands while Paneport is in the foreground, and what happens to the pool across backgrounding, is **not yet verified on device** (C-0006 section 1); acceptance steps 4 to 7 of L3 record it.

## 9. The list of verified iOS builds

`compat/ios-builds.json` lists the iOS builds on which the protocol has been verified (C-0006 section 6). It ships in the app with the `compat/` snapshot (C-0009 section 4); on an iOS build that it does not hold, the app warns and still runs. It is empty until L3's device acceptance passes (S3.0.3).

- **Schema.** `compat/schema/ios-builds.schema.json`, in JSON Schema draft 2020-12: `schemaVersion` 1, and `builds`, each entry with `iosVersion` (such as `26.1`), `iosBuild` (such as `23B85`), `deviceModel` (a model identifier such as `iPhone18,1`, never the device's name) and `evidence`, one or more paths under `docs/evidence/`, oldest first. The phase 1 gate adds its record to the entry of the build and model that L3 listed, which answers the open question of the L5 brief.
- **Rules beyond the schema.** A build and a model appear together in one entry only, and every evidence record that an entry names exists.
- **The check.** `tools/CheckCompat.cs` validates the list in the `checks` job, with a validator of its own for exactly the keywords that the schema uses, which refuses a schema with any other keyword, so that no keyword goes unchecked. It adds no dependency, which keeps decision D8 open for M8.3, whose title files join the same tool. Its self-test plants fifteen lists that each break one rule and a schema with an unknown keyword ([repository-checks.md](repository-checks.md) section 9).

## 10. Tests

| Program | Small stage | Cases |
|---|---|---|
| `test_jit_flags` | S3.1.1 | The decoding of both flags, with the words that the probe read; the system's reader with no debugger; the broker after the enabler; a debugger before the enabler, without it, and after a refused one, each foreign; the wait for `CS_DEBUGGED`; a reading on every return to the foreground, in `ready` too; the checks of the four calls |
| `test_jit_machine` | S3.1.2 | Every transition in order with its durations; the broker's leaving before the request; the foreign debuggers; both timeouts at their limits; the lost debugger; the failed probe; failed and ready as the ends; the request only in `debugger_attached`; the memory limit; the pool sizes and regions; the names |
| `test_jit_mapping` | S3.1.3 | Alias coherence, with a byte written through the alias read back through the view and code run from it; no view writable and executable, then or later; planted writable memory, whose maximum protection allows both, and a range with a hole, both found; command 1 once per region with its size and command 0 once; a title's 300 MiB; the limit of one eighth; the minimum; a refused region; a lost debugger |
| `test_jit_probe` | S3.1.4 | The probed offsets; a healthy pool; faults planted on the first page, the last page and a page at a 16 MiB interval, each named by region and offset; a fault off the probed pages, which passes; a fault on the alias; the failed state through the provider |
| `test_jit_provider` | S3.1.5 | The whole life cycle with the simulated broker, with its five events; an event on every change, in order, on the event thread; the pool timeout and the detach timeout with the fake clock; the refusals of `ready` and `failed`; the end of the supervisor at `pp_shutdown` |

The cases run in both sanitizer presets of the `native` job. `jit_test_support.h` holds the fake platform: a debugger, flags and a clock that the test controls, around the regions of the macOS backend, with switches for a held, refused or late command and a debugger that leaves. `tests/Paneport.Interop.Tests` adds `pp_jit_status_t` to the layout comparison and drives a request to `ready` through the bindings. The device's behavior is L3's acceptance.

## 11. Notes for later stages

- **L3** wires the JIT setup screen to `pp_jit_get_state` and the events, calls `pp_jit_set_enabler_opened` around the URL opener, which the iOS head's scene delegate handles (ios-app-head.md section 2), and calls `pp_jit_note_foreground` on every return to the foreground. Its self-test (S3.0.2) can reuse `pp_jit_probe_page` and `pp_jit_pool_copy` for a routine of its own in the pool. The broker script waits for recelilious's decision ([jit-broker.md](jit-broker.md)).
- **M4.4** offers `pp_jit_pool_copy`'s regions through a member of `paneport/wine_bridge.h` with a struct of its own, and has FEX write through `write_address` and run from `exec_address`. The views keep lowered maxima, so nothing can make the alias executable. On the harness, the macOS backend's simulated broker replaces M4.3's test hook; the harness sets the memory stub, because the backend reads the limit from it.
- **L5** adds its evidence record to the entry of `compat/ios-builds.json` that L3 made for the same build and model (section 9).

## 12. Confirmed in this stage

A throwaway probe on the runner image `xcode-27` (run 45101893218, macOS 27.0.1 on a virtual Apple M1 with System Integrity Protection disabled, Xcode 27.1), whose branch was deleted after the run, found:

- The flags as section 5 gives them, with and without lldb.
- Anonymous memory made executable runs code with no entitlement, in a binary signed as the linker signs it and in one signed with the hardened runtime, with or without `com.apple.security.cs.allow-jit` and `com.apple.security.cs.allow-unsigned-executable-memory`. `MAP_JIT` regions run code too, eight regions of 128 MiB included, with protection and maximum 7, and `pthread_jit_write_protect_supported_np` returns 0. So the CTest binaries need no entitlement and no signing of their own on the runner, which answers the open point of S3.1.5.
- `vm_remap` of a readable and executable region with copy off returns the current protection 5 and the maximum 7; `vm_protect` makes the alias readable and writable; lowering both maxima works, and raising a view's protection afterwards fails with `KERN_PROTECTION_FAILURE` (2). Code written through the alias runs from the view on its first, middle and last pages.
- The faults of section 7.
- The iPhoneOS SDK compiles every call that the iOS backend and the mapping use, with the host's warnings: `vm_remap`, `vm_protect`, `vm_region_64`, `vm_deallocate`, `csops` with its own declaration, `sysctl` with `kinfo_proc` and `P_TRACED`, and the inline assembly of both commands.

## 13. Sources

Public material consulted (C-0001 section 6):

- StikJIT's `INTEGRATION.md`, commit `a9ccad90c7a8cb547f77ff338890e6633a65ac43` of 2026-11-08, MPL-2.0, read for the convention of the two commands, the order of the app's steps and the URL's parameters, and for nothing else of StikDebug: <https://github.com/StikDebug/StikJIT/blob/a9ccad90c7a8cb547f77ff338890e6633a65ac43/INTEGRATION.md>
- Research notes `ios_platform_constraints.md` (section 1, JIT under TXM), `process_and_memory_model.md` and `avalonia_ui_layer.md` (section 6d), for observable behavior only: [ios_platform_constraints.md](../research/ios_platform_constraints.md), [process_and_memory_model.md](../research/process_and_memory_model.md), [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
- Apple, Technical Q&A QA1361, "Detecting the Debugger", for reading `P_TRACED` with `sysctl`: <https://developer.apple.com/library/archive/qa/qa1361/_index.html>
- Apple's open-source XNU, for the prototype of `csops` and the values of `CS_OPS_STATUS` and `CS_DEBUGGED`: `bsd/sys/codesign.h` and `osfmk/kern/cs_blobs.h` at <https://github.com/apple-oss-distributions/xnu>
- Apple, the Mach virtual memory interface of the iOS and macOS SDKs: `vm_remap`, `vm_protect`, `vm_region_64` and `vm_deallocate` in `mach/vm_map.h`
- Apple, "Porting just-in-time compilers to Apple silicon", on `MAP_JIT`, the hardened runtime and its entitlements: <https://developer.apple.com/documentation/apple-silicon/porting-just-in-time-compilers-to-apple-silicon>
- Arm, Arm Architecture Reference Manual for A-profile architecture (document DDI 0487), section B2.4.4, "Concurrent modification and execution of instructions", and the descriptions of `DC CVAU`, `IC IVAU`, `DSB` and `ISB`. Arm's site refuses link checkers, so the manual is named by its number.
- Arm, A64 Instruction Set Architecture (document DDI 0602): `MOVZ`, `RET`, `BLR` and `BRK`, and the range of `B` and `BL`.
- JSON Schema, draft 2020-12, core and validation: <https://json-schema.org/draft/2020-12>
