# Single-Process Wine

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-24T15:00Z |

Back to the [design index](README.md).

This document describes how stage M4.3 runs Wine inside one process, as an iOS app must (C-0004 section 1): the wineserver as a thread of the host, a Windows process as a group of threads, and nothing that forks, execs or sends another thread SIGUSR1 or SIGQUIT (C-0005 section 4). It covers the Wine fork's single-process mode on `paneport/M4.3`, the native host's session API and the bridge between them, and how the harness checks them on macOS ([L4 brief](stages/L4-first-windows-program.md), M4.3). It builds on [runtime-macos-harness.md](runtime-macos-harness.md), which describes the builds, the harness and its single mode, on [native-host.md](native-host.md) and on [signals.md](signals.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win. The design comes from Wine's and FEX's own source and Apple's public documentation; of other products only public descriptions were read (C-0001 section 6).

## 1. Scope

- **In this stage:** one Windows process per session, an x86-64 program under FEX's ARM64EC module, on arm64 macOS: the wineserver as a library on a thread that the host creates (S4.3.2), the start of the process on a thread of its own (S4.3.3), the fault signals through the host's dispatcher and the dispatcher check (S4.3.4), thread control by Mach calls (S4.3.5), the process's end without `exit` (S4.3.6), the prefix and the drive letters (S4.3.7), the bridge and the host's session API (S4.3.8), and the harness's trap (S4.3.9).
- **Later stages:** iOS builds, the JIT pool and x18 on iOS (M4.4), the shared user data at its fixed address (M4.5), graphics (M5.1, M5.2), 32-bit guests (M7.3), and more than one Windows process (M8.1), for which section 11 lists the process-wide state.

## 2. The parts

| Part | Where | What it does |
|---|---|---|
| The entry header | the fork's `include/wine/paneport.h` | The fork's four entry points, the session and launch structs and their constants: the only Wine header that the host includes (C-0005 section 3) |
| The bridge | `src/native/include/paneport/wine_bridge.h` | The table of the host's functions that the fork calls: the log, a thread for the wineserver, thread marks, reservations, the guest fault handler, and the reports of a process's start and end. MIT, and it includes no other Paneport header |
| ntdll's single-process mode | the fork's `dlls/ntdll/unix/paneport.c`, with hooks in `loader.c`, `server.c`, `env.c`, `file.c`, `thread.c` and `signal_arm64.c` | The entry points, the start of a process, the boot step that takes the place of `wineboot`, and thread control |
| The wineserver as a library | the fork's `server/paneport.c` and `server/paneport.h`, `include/wine/server_paneport.h`, with changes in `fd.c`, `mach.c`, `mapping.c`, `process.c`, `registry.c`, `request.c`, `change.c` and `unicode.c` | `wineserver.so`, built from the server's objects, and its five entry points, which only ntdll calls |
| The session API | `src/native/include/paneport/session.h`, `src/native/host/session.c` and `guest_output.c` | `pp_session_start`, `pp_session_stop`, `pp_process_launch`, `pp_process_get_state` and `pp_process_terminate` (C-0005 section 2), the captured output and the events |
| The harness's single mode | `tests/runtime/harness/single_child.c` and `tests/runtime/trap/` | A child process per run that acts as the app, and the fork and exec trap (runtime-macos-harness.md sections 5.1 to 5.3) |

## 3. Threads

Every thread of a session lives in the host's process. None of them attaches to the .NET runtime (C-0004 section 1).

| Thread | Who creates it | Its work |
|---|---|---|
| The host's main thread | iOS and .NET; in the harness, the child's main thread | `pp_session_start` and `pp_session_stop`. The session's start runs the fork's `__wine_paneport_session_start` here, which prepares the prefix, loads `wineserver.so` and sets the server up |
| The host's event thread | The host, in `pp_init` | Delivers the session's events and its log lines, the program's output among them (C-0005 section 1) |
| The wineserver | The host, through the bridge's `start_thread`, with an 8 MiB stack | `__wine_paneport_server_run`: the server's main loop, until the server ends |
| The process's first thread | The fork, in `__wine_paneport_process_launch`, with an 8 MiB stack | `__wine_main`'s steps for the process, then the PE loader and the program's main thread. Wine uses the bottom of this stack as the thread's kernel stack, as upstream uses the stack of the loader's main thread |
| The process's other threads | Wine, as in multi mode | The program's threads and FEX's |
| The output reader | The host, in `pp_process_launch` with `capture_output` | Reads the pipes of the program's standard output and error, and hands each line to the event thread |

The session ends when the last thread that the bridge's `start_thread` created returns, which is the wineserver's: the host then reports the session's end, after the process's end (section 8).

## 4. The wineserver as a library (S4.3.2)

**The build.** The server's `Makefile.in` builds `wineserver.so` beside the `wineserver` program, from the same objects plus `paneport.c`, compiled as position-independent code, and links it with `-dynamiclib -install_name @rpath/wineserver.so` on macOS (`WINESERVER_SO_LDFLAGS` of `configure.ac`; `configure` changed by hand to match, as runtime-macos-harness.md section 2.1 describes). It installs into Wine's `bin/`, where ntdll finds it from its own path. `eng/mac/build-wine.sh` checks that the installed library exports the five entry points. The program behaves as upstream's: only the library's initialization switches single-process mode on.

**The entry points** (`include/wine/server_paneport.h`, internal to Wine):

- `__wine_paneport_server_init`, on the host's main thread, during the session's start: what the program's `main` does, without its master socket, its signal handlers and its limits. It takes the prefix's and its own folder as descriptors that ntdll opened, the NLS folder, the debug level and three callbacks: the interruption of a thread (section 7), the log, and the report that a process ended. It makes a control pipe, whose read end the main loop polls, and loads the NLS tables, the object directories and the registry. A second call, or a failure, returns -1 and leaves the server unusable; a failure inside the loading of the NLS file or the registry logs and aborts, as upstream's fatal errors end the program.
- `__wine_paneport_server_run`, the body of the server's thread: it blocks every signal except the six fault signals, runs the main loop, and when the loop ends refuses the clients still on their way, saves the registry if nothing did, and returns.
- `__wine_paneport_server_connect`, from any thread: hands the server one end of a connected socket pair for a new client, which the server's thread accepts as `master_socket_poll_event` accepts a connection: it creates the process and its first thread on that socket. It returns 0 when the server took the descriptor, and -1 when it did not or the server is ending; the caller closes it then.
- `__wine_paneport_server_terminate`, from any thread: terminates one process by its Windows id, or every process for 0, as `TerminateProcess` from another process does.
- `__wine_paneport_server_stop`, from any thread: does what SIGINT does to the program, `shutdown_master_socket`: it terminates every process with exit code 1, refuses new clients and saves the registry; the server ends 2 s later, as upstream's `close_socket_timeout` would.

Each call from another thread writes one small message into the control pipe with one `write`, so that calls from different threads never interleave, and the pipe never blocks the caller.

**What changes inside the server.** The findings behind each change are in Wine's own source:

- **No master socket, no lock file and no daemon.** The server never calls `open_master_socket`, so it never changes into the prefix's or its own folder, never binds a socket in the file system, and never forks. The first client arrives through `connect` instead, as a child process does upstream through the socket that its parent passed.
- **No signals of its own.** It never calls `init_signals`, so it installs no handler for SIGCHLD, SIGHUP, SIGINT, SIGALRM, SIGQUIT, SIGTERM, SIGXFSZ or SIGSEGV, and it no longer blocks and unblocks SIGIO around changes of its notifications, because no handler of SIGIO reads that list in this mode and the mask belongs to the host.
- **No change of the current folder.** Upstream's server changes into a file's root folder to open it, and into the prefix to save and load the registry and to make temporary files. The host's current folder belongs to the host, and other threads use relative paths, so the server opens, makes and renames those files relative to the folder's descriptor with `openat`, `mkdirat`, `renameat` and `unlinkat`, in both modes.
- **An end of its own.** Upstream's main loop runs while it has users, and the program exits 3 s after its last process. In single mode the loop runs until a flag ends it, and `master_socket_timeout` is 0, so the server saves the registry and ends right after its last process, instead of calling `exit`.
- **No SIGKILL.** Upstream kills a process's Unix process half a second after its Windows process ended, by its Unix pid, which in single mode is the host's. In single mode a process whose last thread ended is dead at once.
- **The task port.** Every client is the host's own task, which sends the server no task port: the server takes `mach_task_self()` with a send right of its own for each process, and never suspends its own task to write a process's memory.
- **Thread control.** Where the server would send SIGUSR1 or SIGQUIT to a thread, it asks ntdll's interruption callback instead (section 7).
- **The end of each process** reaches ntdll through the third callback, once for each process, with its exit code, also for a process that never finished its start or that was killed.
- **Fatal errors** go to the host's log, and the server aborts, which the host's dispatcher turns into a crash record (C-0005 section 2).

Not handled yet, because nothing of this stage needs it: the server's Linux-only effects on its process (priorities, `ptrace`), the close-on-exec flag of the descriptors that clients receive, which matters only once something execs, and more than one server per process.

## 5. The start of a process (S4.3.3)

`pp_process_launch` hands the fork a launch: the descriptors of the program's standard input, output and error, the host's handle of the process, an optional program folder, the program and its arguments. `__wine_paneport_process_launch` then:

1. duplicates the three descriptors, or opens `/dev/null` for a missing one;
2. makes the program's Windows path: when the launch names a program folder, `dosdevices/d:` points to it and the program runs as `D:\<program>` (section 9); otherwise the program is a Windows path;
3. makes a socket pair, hands one end to the server through `connect` and keeps the other as the process's server socket, which upstream passes in `WINESERVERSOCKET`;
4. starts the process's first thread, with an 8 MiB stack, and returns.

That thread runs what `__wine_main` does for a process, without what only a process of its own may do: it raises the descriptor limit, runs `virtual_init` and `init_environment`, and `start_main_thread`. It does not check the command line, which may exec the loader, and it does not park the host's main thread in a run loop as `apple_main_thread` does. The paths of the install (`init_paths`) were set once, at the session's start. `start_main_thread` then runs upstream's code, with these changes for single mode:

- **The server's socket** is the launch's, so `server_init_process` connects nowhere, makes no prefix and changes no folder.
- **Each thread** that Wine starts joins thread control (section 7) and is marked as a guest thread for the host's dispatcher before it tells the server its Unix thread id.
- **The program's image.** Upstream maps it at its preferred base, and a parentless process whose image cannot be mapped there falls back to `start.exe`, which starts the program again in a process of its own. In single mode the host's own mappings may hold that base, so the program runs where it was mapped, and ntdll's PE loader relocates it when it builds the main module, as it relocates a DLL, which on 16 KiB host pages needed the fork's `45216b4` (runtime-macos-harness.md section 11); any other failure to load it ends the process with its status, since `start.exe` could only start it in another process.
- **The process's parameters.** Its environment is the host's, which holds the session's variables and `WINEPREFIX` (section 9). Its current folder is the program's folder, not the host's current folder. The boot step runs in place of `run_wineboot` (section 9). The standard handles wrap the launch's descriptors instead of 0, 1 and 2, which are the host's, and the launch's copies are closed once the server holds its own. A console program with pipes as its handles gets no console: its console handle is `CONSOLE_HANDLE_SHELL_NO_WINDOW`, so no `conhost.exe` starts.
- **The end of the start.** The task port is not sent, and the host's main thread is not made a thread of Wine's (`apple_spawn_main_thread`). The signal handling of section 6 is set up. Right after the server confirmed the start, before the PE loader runs, the bridge's `process_started` reports the start, so a program that cannot load its DLLs starts and then ends with its NTSTATUS as its exit code.

## 6. Signals (S4.3.4)

The host's dispatcher owns the six fault signals (C-0005 section 4). Every signal that upstream Wine uses has its replacement:

| Signal | Upstream | Single mode |
|---|---|---|
| SIGSEGV, SIGBUS | `segv_handler`: access faults | The host's dispatcher. A fault of a thread marked as a guest thread goes to the guest fault handler that the fork registered through the bridge, `paneport_guest_fault`, which runs Wine's own handler on the faulting thread's signal context; a thread without Wine's data declines, and the dispatcher ends the app with a crash record |
| SIGILL | `ill_handler` | The same |
| SIGTRAP | `trap_handler`: breakpoints, single steps, and FEX's `brk` of the cooperative suspension | The same |
| SIGFPE | `fpe_handler` | The same |
| SIGABRT | `abrt_handler`: an assertion becomes `EXCEPTION_WINE_ASSERTION` | The same |
| SIGUSR1 | `usr1_handler`: suspension, contexts and system APCs, sent by the wineserver | Never sent. Thread control runs `usr1_handler` on the thread through SIGUSR2 (section 7) |
| SIGQUIT | `quit_handler`: termination, sent by the wineserver | Never sent. Thread control runs `quit_handler` the same way |
| SIGUSR2 | `usr2_handler`: a full context set on the return from a system call | Kept, with the same flags and mask, and it also brings thread control's interruptions. On a thread of the host, which has no Wine data, it goes to the handler that was there before Wine's |
| SIGINT | `int_handler`: a console's Ctrl-C | Not installed. No terminal or console sends it, and the server's Ctrl-C event for a console, which upstream sends as SIGINT, does nothing |
| SIGPIPE | Ignored by ntdll for the whole process | Ignored, as upstream does: a write to a closed pipe fails with `EPIPE` |
| SIGIO, SIGALRM, SIGHUP, SIGCHLD | Blocked during Wine's server calls (`server_block_set`) | The same; Wine installs no handler for them |
| The server's SIGCHLD, SIGHUP, SIGINT, SIGALRM, SIGQUIT, SIGTERM, SIGXFSZ and SIGSEGV | Handled or ignored by `init_signals` | Never installed; the server's thread blocks every signal except the six fault signals |
| SIGKILL | Sent by the server to a process's Unix process after its end | Never sent (section 4) |

**The dispatcher check.** Test builds check, right after the fork started the session, that the dispatcher still holds all six signals (`pp_signals_check_installed`), and fail the session with `PP_E_SYSTEM` and a message that names the first signal that the fork replaced (C-0005 section 4). The failed session counts as having run, so a second start returns `PP_E_SESSION_EXISTS`. The harness's case `session_dispatcher_check` plants a SIGSEGV handler through the host's test hook to show it (runtime-macos-harness.md section 5.3).

**x18.** Wine's handlers take the TEB from x18, which macOS keeps across context switches for the harness, because the harness is linked as built against the macOS 12 SDK (runtime-macos-harness.md sections 2.1 and 5.1). iOS keeps no such compatibility, so M4.4 settles x18 there before anything of Wine's runs on the device (L4 brief, notes of M4.3 and the risks of M4.4).

## 7. Thread control (S4.3.5)

The wineserver suspends a thread, reads and sets its context, has it run system APCs and terminates it by sending it SIGUSR1 or SIGQUIT, whose handlers run on the thread itself, on the state at which it stopped. C-0005 section 4 keeps both signals out of the process, so the server's `send_thread_signal` calls the interruption callback instead, `paneport_interrupt_thread` in ntdll, which brings the same handler to the thread with Mach calls:

1. **Stop.** On the server's thread, it finds the thread by the Unix thread id that the thread told the server, its Mach port name, and suspends it with `thread_suspend`. A slot of a table of 4096 threads holds each thread's state; the thread took a send right of its own when it joined, so that its port's name never stands for another thread while the server may still use it.
2. **Note.** It sets the signal's bit in the thread's pending bits. A thread on whose way an interruption already is gets only the bit.
3. **Move.** Otherwise it aborts the thread's system call safely (`thread_abort_safely`), reads its state, keeps its pc and sp, and moves its pc to a trampoline in ntdll, with its sp below the old one and below the 128 bytes of its red zone, room for the trampoline's save area. Then it resumes the thread. The server never waits for the thread, allocates nothing and takes no lock while the thread is suspended, since the thread may hold any lock, the allocator's among them (L4 brief, notes of M2.2).
4. **Save.** The trampoline saves every general and vector register, the flags and the floating-point control and status there, and calls `paneport_interrupt_entry`, which notes the save area, unblocks SIGUSR2 and raises it on its own thread.
5. **Deliver.** `usr2_handler` takes the save area and puts the saved registers, the stopped pc and sp and the thread's signal mask at that point into the signal's context, so that the context is the state at which the thread stopped. It then takes the pending bits and runs `quit_handler` or `usr1_handler` on that context, as if the server's signal had come there, and the handler's return goes back to it.

**A system call in progress.** A thread stopped in a system call finishes it once it runs again: the kernel writes the call's results into x0, x1 and the carry flag, keeping the new pc, or, for a call that it restarts, moves that pc back one instruction. The trampoline therefore starts one instruction after a slot of its own that a restart reaches, and that path records the restart, so the delivered context points back at the call's instruction; otherwise it holds the call's results. Wine's waits restart after an interruption as they do after a signal.

**Sections that must not be interrupted.** Where upstream blocks SIGUSR1 (Wine's `server_block_set`, around its calls to the server), that set also holds SIGUSR2. When the saved mask has SIGUSR2 blocked, or the thread was on its signal stack, running a handler, `usr2_handler` restores the stopped state with SIGUSR2 blocked in it and raises SIGUSR2 again: the kernel keeps it pending until the thread unblocks it, as it would keep the server's SIGUSR1, and that SIGUSR2 then brings the pending bits alone.

**ARM64EC threads.** A thread that runs in FEX's simulation is suspended cooperatively by `usr1_handler`'s doorbell path, as upstream suspends it, so thread control changes nothing there. M4.2 found a race in the suspension of ARM64EC threads that crashed the check of a thread suspended in a syscall loop, which set the context that it had read back (runtime-macos-harness.md section 11). Its likely cause was FEX's `ExitFunctionEC`, which kept a value in x23 while neither `InSimulation` nor `InSyscallCallback` was set, so that a suspension there took the plain signal path, and the round trip of an x86-64 context zeroed x23. The FEX fork's `cb6781e` keeps that value in x5, which a round trip keeps, and threads.exe sets the context back again in its check of the syscall loop.

**SIGINT and other signals.** Only SIGUSR1 and SIGQUIT are interrupted. The server's SIGINT for a console's Ctrl-C reports success and does nothing, since single mode has no console.

## 8. The end of a process and of the session (S4.3.6)

**No `exit` or `_exit`.** Upstream ends a process's Unix process when its last thread ends. In single mode, `exit_process` and `abort_process` end the thread instead: they close what the process kept of the server's descriptors, the descriptors of its handle cache and its server socket, so that their other ends see the end as when a process exits, and the thread leaves through Wine's own `pthread_exit_wrapper`, which closes its pipes. A process's other threads end as upstream: `NtTerminateProcess` has the server terminate them, which the server does with SIGQUIT's handler through thread control.

**The reports.** The server reports the end of a process with the code that `GetExitCodeProcess` gives, once its last thread is gone, and ntdll hands it to the bridge's `process_exited`. The host posts the process-exited event once the output reader has queued the program's last line, so the lines always come first. After its last process the server saves the registry and ends; its thread returns, and the host posts the session-ended event with `PP_OK`, after the process-exited event.

**`pp_session_stop`** tells the server to stop: every process ends with exit code 1, and the server saves the registry and ends 2 s later. The host joins the server's thread and the output reader, waits a moment for the process's end, counts a process that the fork never reported as ended with exit code 1, and posts the session-ended event. A launch runs at most one session: a second `pp_session_start` returns `PP_E_SESSION_EXISTS`, while the session runs and after it ended (C-0004 section 1), and the fork itself refuses a second start.

**Fatal errors.** Where Wine's Unix side would print a fatal error and exit, single mode prints it to standard error, as upstream does, hands it to the host's log, takes the thread's guest mark away and aborts, so that the dispatcher ends the app with a crash record (C-0005 section 2) instead of handing the abort to Wine's own SIGABRT handler. An assertion of Wine's that fails on a guest thread still takes the guest path, which turns it into an exception, as upstream's handler does. Some paths that only a broken start reaches still call `exit`: the failure of `virtual_init`'s mappings of the shared user data and of the first thread's data, and the help texts that `WINEDEBUG=help` and `WINEDLLOVERRIDES=help` print. The trap of the harness does not intercept `exit`; M4.4 replaces them before they can be reached on the device.

## 9. The prefix and the drive letters (S4.3.7)

**The template.** The stage chose between running `wineboot`'s steps in the process and a template prefix (plan section 7.4). A template wins: `wineboot --init` installs about a hundred DLLs through `setupapi` in bootstrap mode, writes fake DLLs, starts `services.exe` and `rundll32.exe` for each step, and needs a 32-bit `rundll32.exe` for WoW64 installs, all of it slow and none of it needed on every start. CI makes the template with multi-mode `wineboot`, as the harness's runner does, and the runner removes `dosdevices/z:` and writes `disable` into `.update-timestamp`, so that a later `wineboot` leaves the prefix alone (runtime-macos-harness.md section 6). M4.4 adds a fixed user name and removes the host's name, and the app installs the template on the device (S4.4.6).

**The session's start** sets the session's variables and `WINEPREFIX` in the host's environment, where Wine's Unix side reads its settings and from which a process takes its environment, and prepares the prefix as `setup_config_dir` does for a new one: `drive_c`, `dosdevices` and `dosdevices/c:` pointing to `../drive_c`, without the drive that upstream maps to the host's root. It removes every drive letter that maps `/` and the program's letter of an earlier launch. The server's own folder is `.paneport-server` inside the prefix, which holds the temporary files that back anonymous sections, as upstream's server folder does.

**The boot step**, `paneport_boot`, runs where the first process of a server session would run `wineboot`: in `build_initial_params`, in place of `run_wineboot`. It does what the template cannot hold, because `wineboot` does it at every boot and nothing saves it:

- initializes the shared user data, `virtual_init_user_shared_data`, and sets its version fields to those of Wine's default version, Windows 10 build 19045, with `C:\windows` as the system root;
- publishes each letter of `dosdevices` as a symbolic link `\??\X:` to `\Device\HarddiskVolumeN`, which `GetLogicalDrives` lists, as mountmgr would with `DefineDosDeviceW`'s raw target;
- writes the values of `HKCU\Volatile Environment` that the process's environment takes: `APPDATA`, `LOCALAPPDATA`, `USERPROFILE`, `HOMEDRIVE`, `HOMEPATH`, `USERNAME`, `CLIENTNAME`, `SESSIONNAME` and `HOMESHARE`;
- writes the volatile keys of `HKLM\Hardware\Description\System` as `wineboot` writes them, from the SMBIOS table that ntdll makes (`create_hardware_registry_keys`): the BIOS values, and a key for each processor below `CentralProcessor` with its identifier, vendor, name, speed and feature set and, on an ARM64 host, the ID registers that the table holds, as `CP xxxx` values; and the processor's values of the saved environment, `PROCESSOR_ARCHITECTURE` and the others, which follow the host. FEX's ARM64EC module takes the host's processor from `CentralProcessor\0` and ends the process before any x86-64 code runs when the key is missing, as the first runs of single mode showed (section 13). The wineserver creates only the last level of a key's path, and `HKLM\Hardware` is volatile, so the template never holds it: the boot step makes the missing levels one at a time, as `RegCreateKeyExW` does. On macOS Wine's table holds no ID registers yet, in either mode;
- creates `\KernelObjects\__wineboot_event` signaled, so that a later process that runs `run_wineboot` finds the boot done.

`wineboot`'s other steps at every boot are left out, since no program of this stage needs them: the `DYN_DATA` keys, the computer name's volatile `ActiveComputerName` and the host name, the sections of `\KnownDlls`, the services and the `RunOnce` and `RunServices` commands. A later stage that runs such a program adds the step to the boot step.

**A drive for the program.** A launch that names a program folder maps it as `D:` through `dosdevices/d:` before the process starts, and the program runs from there; the harness names none and copies its programs into `C:\paneport-tests` instead (runtime-macos-harness.md section 5). With the links, programs see `C:` and `D:` from `GetLogicalDrives` and `GetLogicalDriveStringsW`, their targets from `QueryDosDeviceW`, `DRIVE_FIXED` from `GetDriveTypeW`, and an empty label, serial number 0 and NTFS from `GetVolumeInformationW`. The calls that only mountmgr serves, such as `FindFirstVolumeW` and `GetVolumeNameForVolumeMountPointW`, fail. Wine reaches any host path through `\??\unix\`, which it relies on itself, so removing the drive of the host's root hides `/` from the list of drives and restricts nothing.

## 10. The helpers that are not started

| Helper | Upstream | Single mode |
|---|---|---|
| `wineboot.exe --init` | Started by the first process of each server session | The template and the boot step (section 9) |
| `services.exe`, with the services that `wine.inf` starts: MountMgr, PlugPlay, NDIS and nsiproxy in `winedevice.exe`, EventLog, and RpcSs on demand | Started by `wineboot` | Not started. No program of this stage uses a service; the drive letters, which mountmgr publishes upstream, come from the boot step |
| `rundll32.exe` and `winemenubuilder.exe` | Started by `wineboot`'s steps and the Run keys | Not started; the template ran those steps in CI |
| `explorer.exe /desktop` | Started by win32u for a process's first window | Not started: console programs open no window. The graphics stages decide its replacement (M5.1, M5.2) |
| `conhost.exe` | Started for a console on a terminal | Not started: the launch's handles are pipes (section 5) |
| `start.exe /exec` | Started in a parentless process whose program is no PE image or is not at its base | Not started: a program not at its base runs relocated, and any other failure to load the program ends the process (section 5) |
| `winedbg.exe --auto` | Started through the AeDebug key when an exception is unhandled | Not started: in this stage a process cannot start another. `NtCreateUserProcess` still forks, which fails in an iOS app and which the harness's trap refuses and reports; M8.1 gives processes their place in the one process |

## 11. The address space and the process-wide state

**The address-space plan**, which M7.3 reads. Single mode shares the host's address space, and the host maps first: Wine's `virtual_init` runs on the process's first thread when the program is launched, and reserves what is still free of the ranges that merge request 11638 reserves on Apple silicon, 64 KiB to 64 GiB and 448 GiB to 96 TiB, plus the top-down area at 0x7ff000000000 to 0x7ff600000000. The harness exports no `wine_main_preload_info`, so nothing is reserved before the host's own mappings.

- **The low 4 GB** is the main executable's page zero, which macOS keeps at 4 GB (runtime-macos-harness.md section 2.1), so nothing is ever mapped there.
- **The guest's images** load at their preferred bases when those are free, such as 0x140000000 for an x86-64 executable, and are relocated otherwise; the test programs are relocatable.
- **The shared user data** lies where macOS places it, as merge request 11638 maps it, and the PE side reads its address through ntdll's `__wine_get_user_shared_data`; guest code that reads it at 0x7ffe0000 itself is M4.5's work.
- **The TEBs, the threads' data and their kernel stacks** come from Wine's own views inside the reservations, as in multi mode.
- **Guest reservations registered through the bridge:** none in this stage. Every thread that runs guest or translator code is one of Wine's, marked as a guest thread before it runs any (section 5), so its faults take the guest path by its mark (C-0005 section 4). A stage whose threads that Wine did not create may touch guest memory, such as the graphics layers' render threads, registers Wine's reservations through the bridge's `add_reservation`.
- **32-bit guests** need addresses below 4 GB, which the page zero takes on macOS and iOS (wine_on_ios.md KQ1). Nothing of this stage uses that range, so M7.3 decides how 32-bit guests run, with the WoW64 module or Box64, without moving what this plan places.

**Process-wide state**, for M8.1, which runs more than one Windows process: ntdll's Unix side holds one process's state in globals, such as the PEB, the arguments, the handle cache and the server socket, the thread count, the views of the address space and the mapping of the shared user data, and `paneport.c` holds one launch: its standard descriptors, its program, its process id and handle. The server's state is the server's, one per process. Wine also sets state of the host's process: the environment, which holds the session's variables and `WINEPREFIX`; the locale, which `init_environment` sets with `setlocale`; the descriptor limit; the umask, which the session's start reads once, since reading it means setting it; SIGPIPE ignored and the SIGUSR2 handler; and unbuffered standard output and error, which Wine's debug channels set on their first use. M4.4 checks each against the app's own use.

## 12. Checks

- **CTest**, in the `native` job: the session API against a fake fork, the output capture, the event order, and the harness's parts, the trap among them (native-host.md and runtime-macos-harness.md section 5).
- **The harness's single mode**, in the `runtime` job: every program case of multi mode with the same expected lines and exit codes, `console.exe` 20 times in a row in fresh harness processes, the session cases, and the trap, which must refuse no call (runtime-macos-harness.md sections 5.1 to 5.3).

## 13. Confirmed and to be confirmed

| Question of the brief | Answer |
|---|---|
| Does single mode pass the brief's verification | Yes, on macOS. In run [45453104970](https://github.com/recelilious/Paneport/actions/runs/45453104970) on `2005b89`, with the Wine fork at `45216b4` and the FEX fork at `cb6781e`, all 14 cases of the `runtime` job passed in single mode: the nine program cases of multi mode with the same expected lines and exit codes (`checksum`, `console`, `console_exit_code`, `cpu_features`, `file_io`, `seh`, `seh_multiblock_off` with its 3 runs, `threads` and `threads_multiblock_off` with its 5 runs, which set the context back), `console_repeated` with its 20 runs in fresh harness processes, in 52.3 s, and the four session cases (S4.3.2, S4.3.4 and S4.3.6). The trap refused no call, and every session start passed the dispatcher check, which would have failed it with `PP_E_SYSTEM`, except the one in which `session_dispatcher_check` plants a handler to fail it. The planted cases failed with exactly their two lines. A case of one run took 0.4 to 2.9 s, where multi mode's took 8.6 to 11.2 s in run [45183281260](https://github.com/recelilious/Paneport/actions/runs/45183281260), whose first process of each case also runs `wineboot` (runtime-macos-harness.md section 6); only `console_repeated` took longer in single mode, 52.3 s against multi mode's 37.6 s, because each of its runs starts a fresh harness process on a fresh prefix, where multi mode's runs share one wineserver. Nothing of it ran on an iPhone, which M4.4 brings, so the pull request marks it "not yet verified on device" (C-0012 section 1) |
| Do Wine's request channels and descriptor passing work unchanged inside one process | Yes: the client and the server pass descriptors over the socket pair with `SCM_RIGHTS`, which dups them within the process, and every case of run 45453104970 made its requests that way. In the first run of single mode in the `runtime` job, run [45418731618](https://github.com/recelilious/Paneport/actions/runs/45418731618) on `b8b1b4f` with the fork at `d7044ab`, every session case passed, `session_start_stop` with its 20 starts and stops of the server, and each program started in the harness's process, made its requests and ran PE code: `start.exe` printed its message through the launch's pipe. The program cases failed on causes that the fork's `6f8ad14`, `c5bd94e`, `6e36f4a` and `45216b4` removed (sections 5 and 9). In run [45436791313](https://github.com/recelilious/Paneport/actions/runs/45436791313), on the fork's `6e36f4a`, 11 of the 14 cases passed, `threads` and `threads_multiblock_off` among them, and the other three failed on the relocation that `45216b4` fixes (runtime-macos-harness.md section 11) |
| How the trap intercepts calls on macOS | dyld's interposing (runtime-macos-harness.md section 5.2). `test_trap` shows it for calls from the test's own image, and the first run of single mode for Wine's Unix library, which the host loads with `dlopen`: the trap refused the fork of `start.exe`'s `CreateProcess`, named as `fork from ntdll.so!spawn_process+0xd4` |
| Does thread control by Mach calls settle M4.2's race in the suspension of ARM64EC threads | Not by itself: a thread in FEX's simulation is still suspended through `usr1_handler`'s doorbell path, which thread control runs on the thread's stopped state as the server's SIGUSR1 would (section 7). The FEX fork's `cb6781e` removes the race's likely cause, and threads.exe's check of the syscall loop, which sets the context back again, passed its 5 runs of `threads_multiblock_off` in multi mode (run [45183281260](https://github.com/recelilious/Paneport/actions/runs/45183281260)) and in single mode (runs 45436791313 and 45453104970) |
| SIGINT and SIGUSR2 against the dispatcher's design | SIGINT is not installed, and SIGUSR2 stays Wine's, chained for the host's threads (section 6). The dispatcher owns neither (C-0005 section 4) |
| Does lldb on the harness need signals passed through | Not needed so far: CI runs the harness without a debugger |

## 14. Sources

Public material consulted (C-0001 section 6):

- Wine 11.20's source: the wineserver's `main.c`, `request.c`, `fd.c`, `signal.c`, `mach.c`, `process.c`, `thread.c`, `registry.c`, `mapping.c`, `change.c` and `unicode.c`; ntdll's `loader.c`, `server.c`, `env.c`, `file.c`, `process.c`, `thread.c`, `virtual.c`, `signal_arm64.c` and `signal_arm64ec.c`; kernelbase's `console.c` and `volume.c`; `programs/wineboot/wineboot.c`, `dlls/mountmgr.sys` and `loader/wine.inf.in`: <https://github.com/wine-mirror/wine/tree/wine-11.20>
- Merge request 11638, as its author keeps it on GitHub, and its discussion on Wine's mailing list: <https://github.com/trcrsired/wine/commit/458eb1a48180> and <https://list.winehq.org/hyperkitty/list/wine-gitlab@list.winehq.org/thread/KFIKY4FPELQQMU45N65AJ4IETWPSZIN7/>
- FEX's ARM64EC module, `Source/Windows/ARM64EC/Module.cpp` and `Module.S`, at FEX-2610: <https://github.com/FEX-Emu/FEX/tree/FEX-2610>
- Apple's Kernel Programming Guide, Mach overview, on tasks, threads and ports: <https://developer.apple.com/library/archive/documentation/Darwin/Conceptual/KernelProgramming/Mach/Mach.html>
- Apple's open-source XNU, tag `xnu-12377.121.6`, as [signals.md](signals.md) section 8 reads it for the Mach thread calls and for a thread stopped in a system call, which the kernel finishes or restarts when the thread runs again: <https://github.com/apple-oss-distributions/xnu/tree/xnu-12377.121.6>
- Writing ARM64 code for Apple platforms, on x18: <https://developer.apple.com/documentation/xcode/writing-arm64-code-for-apple-platforms>
- Research notes [wine_on_ios.md](../research/wine_on_ios.md) KQ1 and KQ4, [process_and_memory_model.md](../research/process_and_memory_model.md) Q1, and [ios_platform_constraints.md](../research/ios_platform_constraints.md) section 3
