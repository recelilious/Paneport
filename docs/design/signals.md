# Signals

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-17T01:06Z |

Back to the [design index](README.md).

This document describes the signal dispatcher of C-0005 section 4 as stage M2.2 built it (see the [L2 brief](stages/L2-phase-0-gates.md)): how it is installed and chains to the runtime's handler, how it routes a fault and the registries it reads, how it works with Mono, the guest and probe handlers, the alternate signal stacks and the installed check, the Mach thread control helpers, the stress harness of the signal gate with its hang threshold, the tests and the sanitizer settings. [native-host.md](native-host.md) describes the rest of the host and the crash record, and [interop.md](interop.md) the .NET bindings. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Scope

The host installs the process's single handler of SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT in `pp_init` (C-0005 section 4). It sends a fault to the guest path when the faulting thread carries a guest or translator mark, or when the fault lies in a registered JIT pool range or guest reservation, to the JIT probe while the probe mode is on (C-0006 section 3), and every other fault to the action that the signal had before, which is the .NET runtime's. M2.2 builds the dispatcher, its registries and the registration functions that later stages use, the Mach thread control helpers, the call that turns off Mono's crash chaining, the installed check, and the stress harness and page of the signal gate (C-0012 section 3). The Wine fork's handler and the bridge table (M4.3), the JIT provider (M3.1) and CoreCLR (M10.1) are out of scope.

## 2. Parts

| Part | Where | What it holds |
|---|---|---|
| Internal interface | `src/native/host/signals/signals.h` | Installing, the installed check, thread marks, pool ranges, reservations, the probe mode, the guest and probe handlers, and the counters |
| Handler | `signals/signal_handler.c` | Everything that runs in a signal handler: the dispatcher's handler, the routing reads, the calls of the guest and probe handlers, the chain to the saved actions, the end of the app with a crash record, and the last-resort handler (section 5) |
| Installing | `signals/signal_install.c` | `pp_signals_install`, `pp_signals_uninstall`, the installed check and the alternate signal stacks |
| Registries | `signals/signal_registry.c`, `signals/signal_state.h` | The writers of the routing tables, and the state that the handler reads |
| Machine context | `signals/signal_context.h` | The program counter of a signal's context on arm64, and the single instructions with which the harness and the tests plant faults |
| Thread control | `signals/thread_control.h`, `signals/thread_control.c` | The Mach helpers of section 8 |
| Stress harness | `src/native/host/signal_harness.c` | The native part of the signal gate and its `pp_test_` exports, in test builds only (section 9) |
| Bindings | `src/Paneport.Interop/` | `MonoRuntime.TurnOffCrashChaining`, and the signal functions of `HostTestExports` |
| Startup | `src/Paneport.iOS/CompositionRoot.cs` | The crash-chaining call before `pp_init` |
| Verdict | `src/Paneport.Core/Gates/`, `src/Paneport.Core/Diagnostics/CrashChainingState.cs` | The criteria of L2 step 13, the heartbeat, and what became of the crash-chaining call |
| Gate page | `tests/device/SignalGateRun.cs`, `tests/device/SignalGateViewModel.cs` | The signal gate page of the device test flavor |

The internal interface follows the conventions of C-0005 section 1, so that the Wine bridge can offer its registration functions unchanged (section 6). None of it is exported: the shipped framework exports only the functions of the public headers, and test builds add the `pp_test_` functions of `paneport/testing.h`, which raised the interface version to 1.2.

## 3. Installation and chaining

**In `pp_init`.** The host prepares the crash record folder first, so that a fault that ends the app leaves a record, then installs the dispatcher, then starts the event thread; test builds then run the installed check of section 7. If a later step fails, `pp_init` puts the saved actions back and takes the folder away again, so a failed call leaves nothing behind and may be retried. The dispatcher stays installed for the life of the process: `pp_shutdown` leaves it, because a fault after the host stopped still needs a record.

**Saving the previous actions.** `pp_signals_install` reads the action of each signal and saves it before it installs anything, because the handler may run on another thread as soon as one signal is installed. It then installs the dispatcher with `SA_SIGINFO`, `SA_ONSTACK` and an empty mask; `sigaction` returns the action it replaced, and when that differs from the saved one, another thread changed the action in between, so the host puts everything back and `pp_init` fails with `PP_E_SYSTEM`, naming the signal. Last, the calling thread, the main thread, gets an alternate signal stack (section 7).

**Chaining.** A fault that goes to the runtime reaches the saved action with its original arguments:

| Saved action | What the dispatcher does |
|---|---|
| A handler with `SA_SIGINFO` | Calls it with the signal number, the kernel's `siginfo_t` and the context, with the action's mask added to the blocked signals and, for `SA_NODEFER`, the signal itself unblocked; the mask is put back when it returns |
| A plain handler | Calls it with the signal number, under the same mask |
| The default action | Writes a crash record and ends the app |
| Ignore | Returns for a signal that a thread sent; a fault of an instruction would fault again for ever, so it counts as the default action |

The flags that act on delivery rather than on the call, `SA_RESETHAND` and `SA_RESTART`, are not reproduced: the dispatcher stays the installed handler. A saved action that is the dispatcher itself counts as the default action.

**A fault of an instruction.** The kernel gives a fault of the faulting instruction one of the small positive codes of its signal, such as `SEGV_MAPERR`; a signal that a thread sent carries `SI_USER` or one of its kin, which Darwin numbers from 0x10001, or 0. Only the address of a fault of an instruction means anything, so a sent signal is routed by the thread's mark and the program counter alone.

**Stack overflow.** A SIGSEGV or SIGBUS of an instruction whose address lies at most 1 MiB below the lowest address of the faulting thread's stack, as `pthread_get_stackaddr_np` and `pthread_get_stacksize_np` give it, is a stack overflow. On the runtime route the dispatcher ends the app with a record at once instead of chaining: Mono on arm64 has no recovery from a stack overflow, and handing the fault on from the alternate stack could make Mono resume on the exhausted stack and fault again in a loop. Apple's two functions read the calling thread's own record without a lock; `platform_darwin.c` calls them behind `pp_platform_stack_bounds`.

**Ending the app.** The dispatcher writes the record through M1.2's async-signal-safe writer, with the signal, `si_code`, `si_addr`, the program counter of the context, the thread's role (section 6) and a message; puts the default action back; and raises the signal again, which is delivered with the default action once the handler returns. The app therefore ends by the same signal, and iOS writes its own crash report too. A second fault while the first record is being written finds the writer busy and ends the app without a second record.

| Message of the record | When |
|---|---|
| A fault outside the guest path that nothing handled. | The saved action is the default action, or ignore for a fault of an instruction |
| A fault that the runtime did not handle. | The last-resort handler (section 5) |
| The guest fault handler declined a fault. | A guest-path fault that the handler declined, or with no handler set |
| The JIT probe handler declined a fault. | The same on the probe route |
| A stack overflow outside the guest path. | A stack overflow on the runtime route |

**Counters.** The dispatcher counts the faults it passed to the runtime and the faults that the guest and probe handlers handled or declined, with relaxed atomic additions; `pp_signals_read_counters` reads them.

## 4. Routing and the registries

**The route** of C-0005 section 4, checked in this order:

1. The probe mode is on, and the fault's address or program counter lies in a pool range: the probe route.
2. The thread carries a guest or translator mark: the guest path, by its mark.
3. The address or the program counter lies in a pool range: the guest path, by the pool. The program counter counts because translated code runs from the pool, so a load it makes may fault anywhere.
4. The address lies in a guest reservation: the guest path, by the reservation.
5. Anything else: the runtime.

**Lock-free reads.** The handler reads every registry without a lock and without allocating; each registry has a writer mutex, and writers never run in signal context, guest fault handlers included. Writers publish in the order that the readers expect, with release stores, and the readers load with acquire order.

**Thread marks.** A table of 1024 slots (`PP_SIGNAL_MARKED_THREADS`), each with the thread's `pthread_self()` as an integer and its mark. Only the thread itself claims, changes and frees its slot, through `pp_signals_mark_thread` and `pp_signals_unmark_thread`; the handler compares each used slot with `pthread_self()`, which POSIX lists as async-signal-safe, and stops at the highest slot ever claimed. A thread that never marked itself therefore never finds a slot, whatever other threads do meanwhile. The mark is not kept in thread-local storage, because Darwin may allocate a thread's thread-local variables on first use, which a handler must not do; the fixed table is the safe choice that the brief asked for. A pthread key whose destructor frees the slot when the thread ends keeps a reused `pthread_t` from inheriting a mark.

**Ranges.** Two tables, 64 JIT pool ranges (`PP_SIGNAL_POOL_RANGES`) and 256 guest reservations (`PP_SIGNAL_RESERVATIONS`), share one writer mutex. Each slot holds a start, an exclusive end and a sequence number. A writer makes the sequence odd, stores the bounds, and makes it even again with release order; a reader loads the sequence with acquire order, skips the slot while it is odd, loads the bounds, and keeps them only when the sequence is the same after an acquire fence (a sequence lock, section 14). A slot that changes under the reader four times counts as empty, so the handler never waits for a writer, not even for one that the signal interrupted on the same thread. A range must therefore be registered before anything can fault in it, and removed only after. Ranges may overlap; a fault matches when any range holds it.

**Handles.** Registering a range returns a handle that is never 0 (C-0005 section 1): the slot's index plus one in bits 0 to 15, the table in bits 16 to 23, and the slot's generation, which grows with each registration, in bits 32 to 63. A handle of a removed range, or of the other table, names nothing, and removing it fails with `PP_E_INVALID_ARGUMENT`.

**Capacities.** A registration that finds its table full fails with `PP_E_OUT_OF_MEMORY`, and so does marking a thread when its alternate stack cannot be allocated. The capacities are compile-time constants in `signals.h`; a stage that needs more raises them there and in this section. A range needs a start above 0 and a size above 0 that ends inside the address space.

**The probe mode.** `pp_signals_set_probe_mode` turns it on with 1 and off with 0. M3.1 turns it on while the JIT state is `verifying` (C-0006 section 3).

## 5. Mono, crash chaining and the last-resort handler

**Which faults end the app.** The brief left open how the dispatcher tells a fault that the runtime resolves, such as a managed null reference, from one that ends the app. It does not tell them apart itself: it hands every fault outside the guest path to the runtime's handler, and the runtime decides. Mono resolves a fault in managed code by changing the context so that the thread throws a `NullReferenceException`, and returns. With crash chaining off, Mono hands a fault that it does not take, such as one in native code or on a thread it does not know, to the handler that it found when it installed its own, and returns; signal chaining is on in .NET for iOS. Only when it found no handler does it run its native crash path, which ends the app without Paneport's record (research note, section 2.2 item 6a).

**The last-resort handler.** So that Mono finds a handler that writes the record, the host installs one when its library is loaded: a constructor in `signal_handler.c` installs `pp_signal_last_resort` for each of the six signals that still has the default action. dyld runs the constructor before `main`, because the app's executable links the framework (C-0009 section 4), so it runs before `mono_jit_init` installs Mono's handlers and saves what it found. Mono then calls it for a fault that it does not take, and it writes the record, "A fault that the runtime did not handle.", and ends the app as section 3 describes. A signal for which the runtime installs no handler keeps the last-resort handler until `pp_init`, and the dispatcher then saves it as the runtime's action. A signal that already has another action when the library is loaded keeps it: in the test host of `tests/Paneport.Interop.Tests`, CoreCLR's handlers are in place before the test library is loaded, and the dispatcher that `pp_init` installs chains to them.

**The crash-chaining call.** C-0005 section 4 has Paneport.Interop call `mono_set_crash_chaining(FALSE)` after the runtime starts. `MonoRuntime.TurnOffCrashChaining` imports `void mono_set_crash_chaining(mono_bool)`, where `mono_bool` is a 32-bit integer, from `__Internal`: on the device Mono is linked into the app's executable, and an `__Internal` import resolves in the main program ([interop.md](interop.md) section 2). The `ios` job's `eng/mac/check-app.sh` checks that both flavors' executables export the symbol. Paneport.iOS makes the call in `CompositionRoot`, after Avalonia's platform is set up and before `HostStartup.Start` calls `pp_init`. `CrashChainingState.Run` records whether it resolved and returned, did not resolve (`EntryPointNotFoundException` or `DllNotFoundException`) or failed, logs it, and never ends the startup; Diagnostics lists it as "Crash chaining" beside the build stamp, and the signal gate page shows it as its startup check (L2 step 11). The desktop head and the macOS tests run on CoreCLR, which has no such function, and never make the call; their Diagnostics says so.

**Confirmed only on the device.** The CTest stand-in for the runtime's handler models the documented behavior: it resolves faults in the pages it owns and hands any other fault to the handler it found. Whether Mono on the device returns, resets the action or aborts on a fault it does not own, whether the `__Internal` import resolves at run time, and whether the managed null-reference loop faults at all under full ahead-of-time compilation (Mono may check for null explicitly), are for the signal gate to show; the page counts the faults passed to the runtime's handler for the last one.

## 6. Guest and probe handlers

**The registration.** `pp_signals_set_guest_handler` sets the one guest fault handler, with its `user_data`, or removes it with NULL. A handler has the type `pp_guest_fault_handler_t`: it receives a `pp_guest_fault_t` with `struct_size`, the route, the thread's mark, the signal, `si_code`, the fault address, the program counter, and the kernel's `siginfo_t` and context as `void *`, and returns `PP_GUEST_FAULT_HANDLED` or `PP_GUEST_FAULT_DECLINED`. The shape follows C-0005 section 1, with fixed-width integers, `struct_size` first and pointers only for buffers and `user_data`, so that the bridge table of C-0005 section 3 can carry these function pointers unchanged; `paneport/wine_bridge.h` comes with M4.3.

**The rules for a handler.** It runs in the faulting thread's signal context, so it calls only async-signal-safe functions and must return. It may change the context, as Wine's handler moves a thread into its exception dispatch, and return `PP_GUEST_FAULT_HANDLED`; the thread then goes on from the context as the handler left it. A handler that declines leaves the context unchanged, and the dispatcher ends the app with the record "The guest fault handler declined a fault." A guest-path fault with no handler set counts as declined. A handler never calls the registration functions, which would wait for it.

**Replacing a handler.** The handler and its `user_data` sit in one of two boxes, and an index names the active box. A caller counts itself into the box it found active and checks the index again, with sequentially consistent order, before it reads the box, and leaves the box's count when the handler returns; a caller that finds the index changed backs out and tries again, at most four times, and then counts as finding no handler. A writer fills the box that is not active, once the callers that came too late have left it, switches the index, and waits until no caller is left in the box it replaced. When `pp_signals_set_guest_handler` returns, no fault is therefore still in the handler it replaced, as M1.2's callback setters guarantee (native-host.md section 3), and the old `user_data` may be released.

**The probe handler.** `pp_signals_set_probe_handler` sets the handler of the probe route, of the same type and with the same rules, in a box pair of its own. A declined probe fault ends the app with "The JIT probe handler declined a fault." M3.1's handler takes every fault of the probe, because any fault means that the authorization failed (C-0006 section 3), and moves the thread to its recovery point.

**Thread roles in the record.** A record of a marked thread carries the role 3 for a guest thread or 4 for a translator thread; any other thread carries main (1), the host event thread (2) or other (0), from `pp_host_thread_role`. [native-host.md](native-host.md) section 8 lists the values, and Paneport.Core's reader accepts them.

## 7. Alternate stacks and the installed check

**Alternate stacks.** The dispatcher is installed with `SA_ONSTACK`, and alternate stacks are per thread. `pp_init` gives the main thread one, and `pp_signals_mark_thread` gives each marked thread one: 256 KiB, more than `SIGSTKSZ` because the handler may chain into Mono's, with a guard page below it. A marked thread's stack is freed when its mark goes or the thread ends, but only taken away when it is still the thread's own. A thread that already has an alternate stack keeps it, because whoever set it frees it: AddressSanitizer gives every thread one and unmaps the stack it finds when the thread ends, and the Wine fork sets its own for its threads. Threads that are neither the main thread nor marked, such as the runtime's, get none, so a stack overflow on them still ends the app without a record, as the brief's risk says.

**The installed check.** `pp_signals_check_installed` checks that each of the six signals still has the dispatcher's handler, with `SA_SIGINFO`, and names the first one that does not: it fails with `PP_E_SYSTEM` and the message "SIGBUS no longer has the signal dispatcher's handler.", for example, and returns the signal's number. Test builds run it at the end of `pp_init`, which then fails; `pp_test_signals_check_installed` exports it for the signal gate page and the tests; and M4.3's test builds run it after `pp_session_start`, failing the session when a fork installed a handler of its own (C-0005 section 4).

## 8. Mach thread control

`thread_control.h` holds the helpers that the guest work of M4.3 needs to suspend a guest thread, read and write its registers and resume it, through Mach calls on arm64; SIGUSR1 and SIGQUIT are never used for thread control (C-0005 section 4).

- `pp_thread_control_open` takes a send right to a running thread's port and returns its name, and `pp_thread_control_close` gives it back. Holding the right keeps the name valid after the thread ends: the calls on it then fail with `PP_E_INVALID_ARGUMENT`, because the kernel reports a dead name or a terminated thread, instead of reaching whatever port the name would otherwise come to stand for.
- `pp_thread_control_suspend` and `pp_thread_control_resume` wrap `thread_suspend` and `thread_resume`; suspensions count, and a thread cannot suspend or resume itself.
- `pp_thread_control_get_state` and `pp_thread_control_set_state` read and write the general registers of a suspended thread through `ARM_THREAD_STATE64`, as `pp_thread_state_t`: x0 to x28, fp, lr, sp, pc and cpsr. Writing reads the state first, so the members that the struct leaves out keep their values.

The app is an arm64 process, not arm64e, so the thread state holds the registers in plain fields; `signal_context.h` and `thread_control.c` refuse to build for anything else.

## 9. The stress harness and the signal gate

**The native harness.** In test builds, `pp_test_signal_harness_start` maps four pages: a guard page that cannot be read or written, a read-only page, and two pages that cannot be read or written, which it registers as a guest reservation. It sets its own guest fault handler, so it refuses to start while another is set, and starts up to 16 marked threads, which read the guard page and write the read-only page in turn, and up to 16 unmarked threads, which read the reservation; each pauses for the configured time between two faults. Every planted fault is a single 4-byte instruction. The handler counts a fault on the marked threads' pages that came by the thread's mark, or one in the reservation that came by its address, and moves the program counter past the instruction; a fault in its pages that came another way counts as unexpected and is skipped too, so that the run goes on to its verdict, and a fault outside its pages is declined, which ends the app with a record. `pp_test_signal_harness_stop` ends the threads and takes the handler, the reservation and the pages away, and `pp_test_signal_harness_read` returns the counters, with the faults that the dispatcher passed to the runtime.

**The signal gate page.** The device test flavor's developer menu opens it (L2 steps 11 to 13):

- The startup check shows the crash-chaining state of section 5 and whether the dispatcher has all six signals.
- A run lasts four hours for the gate, or ten minutes for the smoke run of the integration session and the optional device check of this stage. It runs the native harness with four threads of each kind and a pause of 200 microseconds; a managed loop that reads the length of a string through a reference that is always null and catches each `NullReferenceException`, with a pause of a millisecond after every 64; a thread that allocates a 64 KiB block every millisecond, keeps the last 16 and asks for a full collection every 5,000 blocks; and a heartbeat every 100 milliseconds.
- The counters refresh every second. The page turns the idle timer off for the run, so the screen stays on, and the run turns invalid if the app enters the background. At its target the run ends and the result screen shows the verdict; Stop ends it early, which fails the duration.

**The verdict.** `SignalGate.Evaluate` in Paneport.Core passes a run only when every criterion of L2 step 13 holds, and lists each one with what the run showed:

| Criterion | Holds when |
|---|---|
| Valid run | The app never left the foreground |
| Duration | The run lasted at least its target: 4:00:00 for the gate |
| Managed null references | As many exceptions were caught as were thrown, and more than none |
| Guest faults by mark | As many were handled by the guest path as were raised, and more than none |
| Guest faults by address | The same, for the reservation |
| Unexpected faults | None |
| Garbage collections | More than none |
| Longest heartbeat gap | Below the hang threshold |
| Dispatcher installed | All six signals still have the dispatcher's handler at the end |

A count of zero fails its criterion, because a run in which nothing happened proves nothing.

**The hang threshold is 2 seconds.** The heartbeat beats every 100 milliseconds, and the longest gap between two beats, an open one included, must stay below 2 seconds. A garbage collection on the device's small test heap stops the threads for milliseconds to tens of milliseconds, and the heartbeat's own interval is a tenth of the threshold, so a passing run has a wide margin; a stop of the world that waits for a thread inside a signal handler, a deadlock between a handler and the collector, or a hang of the dispatcher shows as seconds, or never ends. L2 uses the threshold for the gate and L10 for its runs; a change goes through the brief of the stage that needs it, with the reason.

## 10. Tests

| Program | Small stage | What it checks |
|---|---|---|
| `test_signal_chain` | S2.2.1 | With a stand-in for the runtime's handler installed before `pp_init`, each of the six signals, raised on an unmarked thread, reaches it exactly once, with the same `siginfo_t` members and program counter that it receives without the dispatcher; a plain handler gets its mask; an ignored sent signal returns; the default action, a fault that the runtime hands to the last-resort handler, and a stack overflow end the app with their records |
| `test_signal_routing` | S2.2.2 | Under ThreadSanitizer, two writers register and remove pool ranges and reservations, mark and unmark themselves and swap the guest fault handler while marked and unmarked threads fault 20,000 times each, and every fault takes its route; a pool fault goes to the probe only while the probe mode is on; a thread that ends loses its mark; full tables and stale handles are refused, and the thread table holds exactly 1,024 threads |
| `test_signal_guest` | S2.2.3 | The guest fault handler sees only guest-path faults; a declined fault and a fault with no handler end the app with a record of the guest role; removing a handler waits for a fault inside it |
| `test_thread_control` | S2.2.4 | A suspended thread has one register changed and, once resumed, sees the new value; an ended thread gives an error; a thread cannot suspend itself |
| `test_signal_installed` | S2.2.5 | The check passes after `pp_init`, fails naming SIGBUS when a test replaces its handler, and fails before `pp_init`; the last-resort handler holds each signal at load, but not one that was ignored when the process started |
| `test_signal_harness` | S2.2.6 | The native harness runs for 60 seconds beside a thread that faults for the stand-in, as the managed loop does for Mono: every planted fault is handled on its route, none is unexpected, and the runtime saw exactly the stand-in's faults; its start checks its conditions |

**Cases that end by a signal** run that part in a child process of their own program, started with `posix_spawn` as `<program> child <case> <folder>`. The case checks that the child ended by the expected signal and reads the crash record that it left in the folder, at the offsets of native-host.md section 8, with the address that the child wrote before it faulted. CTest's `WILL_FAIL` is not used, because CTest may count a crash as a failure even with it.

**The calls of the handler.** The CTest check of async-signal-safe calls (`tests/native/check_signal_safe_calls.cmake`) lists the undefined symbols of `signal_handler.c`'s object with `nm`, as it does for the crash record writer, and fails on any call outside its list: `sigaction`, `sigemptyset`, `sigaddset`, `pthread_sigmask`, `pthread_self`, `pthread_kill`, `raise`, `memcpy`, `memmove` and `memset`, which POSIX lists as async-signal-safe; errno's accessor; the host's own `pp_crash_record_write`, `pp_host_thread_role` and `pp_platform_stack_bounds`; and what the compiler and the sanitizers add.

**The .NET side.** `tests/Paneport.Core.Tests` checks on synthetic counters that the verdict is PASS only when every criterion holds, and that each criterion alone fails it; the heartbeat; and every outcome of the crash-chaining call. `tests/Paneport.Interop.Tests` compares the two new structs with the host's layout table, checks that the dispatcher has all six signals in its test host, and starts, reads and stops the harness without threads; its faults are planted by the CTest suite. The binding of `mono_set_crash_chaining` builds in the `dotnet` job, and the call is checked on the device.

## 11. Sanitizer settings

The test presets of `src/native/CMakePresets.json` leave the six signals to the host:

- `ASAN_OPTIONS` adds `handle_segv=0:handle_sigbus=0:handle_sigill=0:handle_sigfpe=0:handle_abort=0:allow_user_segv_handler=1`.
- `TSAN_OPTIONS` adds `handle_segv=0:handle_sigbus=0:handle_sigill=0:handle_sigfpe=0:handle_abort=0`.

Otherwise the sanitizers install handlers of their own for these signals at startup: the last-resort handler would find no default action to take over, and the dispatcher would hand a fault to a sanitizer's report instead of to the test's stand-in. With the options, the tests see the device's arrangement. What the sanitizers report themselves, memory errors, data races and undefined behavior, is unaffected; only a real crash of a test now ends through the dispatcher, with its record and its signal, instead of with a sanitizer's stack trace. AddressSanitizer still gives every thread its alternate stack, so under it the dispatcher sets none (section 7). ThreadSanitizer delivers the six signals at once, as synchronous signals, passes the handler the kernel's `siginfo_t` and context, answers a query of `sigaction` with the program's handler, and reports an allocation inside a handler, which the handler's code never makes.

## 12. Notes for later stages

- **M3.1** registers the pool's executable views, and their writable aliases where the probe writes, with `pp_signals_add_pool_range`; sets the probe handler, which takes every fault of the probe and moves the thread to its recovery point; and turns the probe mode on for the `verifying` state only. A pool fault counts by its address or its program counter (section 4).
- **M4.3** offers `pp_signals_set_guest_handler`, `pp_signals_mark_thread`, `pp_signals_unmark_thread`, `pp_signals_add_reservation` and `pp_signals_remove_reservation` through the bridge table unchanged, uses the Mach helpers of section 8 for the in-process wineserver, and runs the installed check after `pp_session_start` in test builds. Wine sets alternate stacks for its own threads, which the dispatcher keeps. Wine's handler must return, never call the registration functions, and leave the context unchanged when it declines.
- **M6.3** registers the guest reservations of the file-backed tier. The table holds 256; if the tier needs more, M6.3 raises `PP_SIGNAL_RESERVATIONS` and section 4.
- **M10.1** moves to CoreCLR, which needs a change to C-0005 section 4 first. CoreCLR uses SIGUSR1 to inject activations on Unix, which is not one of the six signals, and installs its own handlers of them; the dispatcher would then chain to CoreCLR's handlers as it does in the macOS test host. The routing tests of section 10, the harness and this document must keep passing and holding; the last-resort handler depends on the runtime saving and calling a previous handler, which M10.1 confirms for CoreCLR.
- **L2** uses the signal gate page for steps 11 to 13, the ten-minute run for the smoke run of the integration session, and the hang threshold of section 9.

## 13. Confirmed in this stage

- **Local runs before CI.** The CTest cases that do not depend on Darwin passed under AddressSanitizer with UndefinedBehaviorSanitizer and under ThreadSanitizer in a Linux build with local stand-ins for the machine context, the stack bounds and the Darwin platform calls, which were never committed: the routing under writers that change the registries, the guest fault handler and its records, the installed check and the last-resort handler, and the 60-second harness, which raised and handled about 60 million guest faults beside 16 million runtime faults under ThreadSanitizer. ThreadSanitizer found a data race in the test's stand-in, which wrote the last `siginfo_t` from several threads at once; the stand-in now keeps it only for the test that faults on one thread at a time. clang-tidy 22 found boolean conversions in C and widening multiplications, which the code no longer has.
- **Formatting.** clang-format 22 on the workstation and the pinned 23.1.5 in CI agree on every new file.

## 14. Sources

Public material consulted (C-0001 section 6):

- Research note `avalonia_ui_layer.md`, sections 2.2, 3.3 and 4, on Mono's handlers, signal and crash chaining, the dispatcher design, and experiments E5 and E16: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
- POSIX.1-2024, section 2.4.3, the async-signal-safe functions: <https://pubs.opengroup.org/onlinepubs/9799919799/functions/V2_chap02.html>
- POSIX `sigaction`, `sigaltstack`, `pthread_self` and `posix_spawn`: <https://pubs.opengroup.org/onlinepubs/9799919799/functions/sigaction.html>, <https://pubs.opengroup.org/onlinepubs/9799919799/functions/sigaltstack.html>, <https://pubs.opengroup.org/onlinepubs/9799919799/functions/pthread_self.html> and <https://pubs.opengroup.org/onlinepubs/9799919799/functions/posix_spawn.html>
- Apple's Kernel Programming Guide, Mach overview, on tasks, threads and ports: <https://developer.apple.com/library/archive/documentation/Darwin/Conceptual/KernelProgramming/Mach/Mach.html>
- Sequence locks, as the Linux kernel documents them: <https://docs.kernel.org/locking/seqlock.html>
- Mono's embedding API: <https://www.mono-project.com/docs/advanced/embedding/>
- The sanitizers' common flags and ThreadSanitizer's flags: <https://github.com/google/sanitizers/wiki/SanitizerCommonFlags> and <https://github.com/google/sanitizers/wiki/ThreadSanitizerFlags>
