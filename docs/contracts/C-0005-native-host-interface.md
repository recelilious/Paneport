# C-0005 Native Runtime Host Interface

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines the C interface between the .NET side (`Paneport.Interop`) and the native host (`src/native/host`), the bridge between the native host and the Wine fork, and how signals are owned in the process. It fixes conventions and the initial function groups. Exact parameter lists are settled in the headers and may still change while this contract is a draft.

## 1. Conventions

| Topic | Rule |
|---|---|
| Language | C17 headers in `src/native/include/paneport/`, usable from C, C++ and Objective-C, wrapped in `extern "C"` guards. |
| Names | Exported functions start with `pp_`, types end in `_t`, macros and enum constants start with `PP_`. |
| Results | Every function returns a `pp_status_t` (`int32_t`), with three exceptions: `pp_abi_version` returns a `uint32_t` (major in the high 16 bits, minor in the low 16 bits), `pp_last_error` returns a `const char *` that the host owns and that stays valid until the calling thread's next call into the host, and `pp_free` returns nothing. `PP_OK` is 0 and every error is negative. |
| Types | Integers use fixed-width types. `float` and `double` are allowed for coordinates, scales and rates. Pointers are allowed for strings, buffers, callbacks, `user_data` and the layer handle. Booleans are `uint8_t` holding 0 or 1. Structs are blittable and start with a `uint32_t struct_size` member so they can grow. |
| Strings | UTF-8 and NUL-terminated. The host copies every input string it keeps. Strings passed to a callback are valid only until the callback returns. |
| Handles | Opaque `uint64_t` values; 0 is never a valid handle. |
| Memory | Whoever allocates frees. Memory the host returns is freed with `pp_free`. |
| Callbacks | Plain C function pointers with a `void *user_data` argument. They run **only on the host event thread**. Events and log lines produced on any other thread are queued and delivered by that thread. A callback must return quickly and must not call back into the host. |
| Threads | Every function is marked `[main]` (main thread only) or `[any]`. |
| Errors | No C++ exception, Objective-C exception, .NET exception or `longjmp` ever crosses the interface. Every .NET callback catches all exceptions and reports failure through its return value or the log. |

**On the .NET side**, bindings use `LibraryImport` (source-generated, compatible with AOT) and the assembly sets `DisableRuntimeMarshalling`. Only blittable structs cross the interface. Callbacks are static methods marked `[UnmanagedCallersOnly]`, passed as function pointers; managed delegates are never marshalled. The library the bindings import is fixed by C-0009 section 4.

**Versioning.** The headers define `PP_ABI_VERSION_MAJOR` and `PP_ABI_VERSION_MINOR`. At startup the .NET side compares the major version and refuses to start on a mismatch. Adding a function or appending a struct member raises the minor version. Changing or removing anything raises the major version and needs a change to this contract.

## 2. Initial function groups

| Group | Functions | Purpose |
|---|---|---|
| Life cycle | `pp_abi_version` [any], `pp_init` [main], `pp_shutdown` [main], `pp_set_event_callback` [main] | Start and stop the host and receive its events. |
| Diagnostics | `pp_set_log_callback` [main], `pp_last_error` [any], `pp_free` [any] | Forward host logs to .NET, read error messages, free host memory. |
| JIT | `pp_jit_get_state` [any], `pp_jit_request_pool` [main] | Read the JIT state, and start the pool request with a size in MiB chosen by .NET (C-0006). |
| Memory | `pp_memory_get_budget` [any] | Report the **memory limit**, available memory, the current footprint, and whether the limit has reached the Game Mode level. |
| Session | `pp_session_start` [main], `pp_session_stop` [main] | Start and stop the Wine session. |
| Process | `pp_process_launch` [any], `pp_process_get_state` [any], `pp_process_terminate` [any] | Launch a Windows program inside the session and follow its state. |
| Steam | `pp_steam_set_token` [any] | Hand the Steam refresh token to the host for the Dock host (C-0007 section 3). |
| Display | `pp_display_attach` [main], `pp_display_resize` [main], `pp_display_detach` [main] | Hand the game view's `CAMetalLayer` (as a `void *`) to the graphics layers, and report size, scale and refresh rate. |
| Input | `pp_input_submit` [any] | Submit batches of fixed-size input events: gamepad state, keys, mouse movement and buttons, touches. |
| Audio | `pp_audio_set_active` [main] | Pause or resume audio when iOS interrupts the session. |

Rules for these groups:

- **Memory limit.** iOS has no public API for the jetsam limit or for Game Mode. The host estimates the limit as `os_proc_available_memory()` plus the current `phys_footprint`, samples it again over time, and reports when it reaches the Game Mode level of about 8,192 MB. Private calls such as `memorystatus_control` are not used. Wherever a contract says "memory limit", it means this reported value.
- **Session start.** `pp_session_start` takes a session configuration. From the title's compatibility entry (C-0008) come the architecture, translator options, environment variables, blocked helper programs and graphics settings. The prefix path is derived from the entry's `prefix` value and the title's identifier, and the locale comes from the app settings. The physical memory size reported to Windows is derived by the host from the memory limit. It fails with `PP_E_JIT_NOT_READY` unless the JIT state is `ready` (C-0006), and with `PP_E_SESSION_EXISTS` if a session has already run in this process (C-0004 section 1).
- **Steam token.** The host keeps the token only in memory, hands it to the Dock host through the bridge (section 3), and overwrites its copy with zeros once the Dock host has read it or the session ends. The token never travels as a command-line argument, an environment variable, a registry value or a log line.

**Events** reach .NET through the event callback: a process started or exited (with its exit code); a guest process ended by an unhandled Windows exception (exception code, address and module name); the session ended; memory pressure (with a level); a thermal state change; and a JIT state change. A fatal fault outside the guest path ends the whole app, so it is never delivered as an event. The host writes it to a crash record in `Library/Caches/Paneport/`, and the app reads that record on its next launch.

## 3. Bridge between the host and the Wine fork

- The host passes the Wine fork a table of C function pointers, `pp_wine_bridge_t`, when the session starts. Through it the fork:
  - reaches the game view's layer, polls the input queue and reports events;
  - reads the memory limit;
  - obtains the authorized JIT code pool, as a list of regions each with its execute-only view, writable alias and size, and passes it to the translator (C-0006);
  - marks guest and translator threads, and registers guest address reservations;
  - registers its guest fault handler with the signal dispatcher (section 4);
  - lets the Dock host take the Steam token once.
- The table is declared in `paneport/wine_bridge.h`, an MIT header that includes no other Paneport header. The Wine fork's build adds this repository's `src/native/include` to its include path; the header is never copied into the fork.
- The Wine fork declares its entry points (start and stop a session, launch, terminate and query a process) in a header that lives in the fork, under the fork's license. That header contains only declarations, constants, struct layouts and small macros. It is the only Wine header the host includes.

## 4. Signals

- **The dispatcher owns the fault signals.** The host installs the process's single signal dispatcher in `pp_init`, after the .NET runtime has started and before any Wine thread exists. It owns SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT.
- **The forks never install handlers for them.** The Wine fork, the FEX fork and any other translator never call `sigaction` or `signal` for those signals. They register their fault handler through the bridge instead. Test builds check after `pp_session_start` that the dispatcher is still installed for each of them, and fail the session if it is not.
- **Routing.** A fault goes to the guest path when the faulting thread is marked as a guest or translator thread, when the address is inside the JIT code pool, or when it is inside a registered guest reservation. While the JIT state is `verifying`, a fault inside the pool goes to the JIT probe instead (C-0006). Every other fault goes back to the handler the .NET runtime installed.
- **No `SIGUSR1` or `SIGQUIT` for thread control.** Guest threads are suspended, have their context read and written, are woken for system calls the wineserver queues, and are terminated through Mach thread calls (`thread_suspend`, `thread_get_state`, `thread_set_state`, `thread_resume`) and in-process wake-ups. The in-process wineserver never sends `SIGUSR1` or `SIGQUIT`.
- **Mono's crash chaining is off.** After the runtime starts, `Paneport.Interop` calls `mono_set_crash_chaining(FALSE)`. The phase 0 signal gate confirms that this call works on the device (C-0012).
- This design assumes the Mono runtime, which C-0010 section 1 fixes. Mono for iOS ends with .NET 10, whose support ends on 2027-05-11. Moving to CoreCLR, which uses `SIGUSR1` itself, needs a change to this section before then (C-0012 section 3).

## 5. Checks

- A test in `tests/native` compiles every public header as C17 and as C++20.
- In test builds the host exports a table of struct sizes and member offsets. `tests/Paneport.Interop.Tests` compares it with the C# definitions in the macOS CI job (C-0011 section 4) and on the device.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
