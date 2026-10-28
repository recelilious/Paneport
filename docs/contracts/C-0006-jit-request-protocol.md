# C-0006 JIT Request Protocol

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines how Paneport obtains executable memory for the x86 translator on iPhone 17 and 18 class devices running iOS 26 or 27. The facts behind it are collected in [ios_platform_constraints.md](../research/ios_platform_constraints.md) and [process_and_memory_model.md](../research/process_and_memory_model.md).

## 1. Platform facts this protocol relies on

- Paneport cannot use `MAP_JIT`. On iOS it needs an entitlement that Apple keeps for its own system processes and a few special cases, such as alternative browser engines; Paneport is neither.
- On iOS 26 and later, on devices where the Trusted Execution Monitor is active (A15 and M2 chips and newer, which includes every iPhone 17 and 18), a page the app writes code into does not execute until an attached debugger has written to that page. Apple does not document this; it is observed by several projects.
- The convention is documented in `INTEGRATION.md` of StikDebug/StikJIT, an MPL-2.0 repository (<https://github.com/StikDebug/StikJIT/blob/main/INTEGRATION.md>, checked 2026-10-28). The app executes `brk #0xf00d` with a command in register `x16` and arguments in `x0` and `x1`:
  - command 1, `JIT26PrepareRegion` (`x0` = address, `x1` = length), returns the prepared region in `x0`;
  - command 0, `JIT26Detach`, makes the debugger detach.
- StikDebug runs the script chosen for an app, either the shared `universal.js` or the app's own script. An app can pass its own script, base64-encoded, in the `script-data` parameter of the `stikdebug://enable-jit` URL.
- One project found that a region the app had mapped itself never became executable, so Paneport always requests regions with a NULL address hint and lets the debugger allocate them. The region comes back executable and not writable, and the app builds a writable alias of it with `vm_remap` and `vm_protect`.
- Executing `brk #0xf00d` when the broker script is not attached crashes the process. A debugger without the script, such as Xcode's, does not handle it.
- Authorization is reported to last for the life of the process after the debugger detaches; one project reports the opposite, and nobody has tested what happens when the app goes to the background. The phase 1 gate checks both on a device (C-0012). After every full relaunch, authorization starts again.
- An attached debugger intercepts exceptions that Wine's own handlers need, so the debugger must detach before the Wine session starts.

## 2. Roles

| Role | Who | Notes |
|---|---|---|
| Enabler | StikDebug, a separate app under AGPL-3.0 | Paneport uses only the convention and the URL scheme described in StikJIT's `INTEGRATION.md`, and never links, embeds or copies StikDebug code. C-0003 section 9 allows this. |
| Broker script | Our own script, MIT, in `src/jit-broker/` | Written from `INTEGRATION.md` only. Bundled in the app and handed to StikDebug through the URL scheme. It handles only the two commands above. |
| App side | The native host, in `src/native/host/jit/` | Requests, maps and verifies the code pool, hands it to the translator, and reports its state through C-0005. |

If the broker script needs parts of StikDebug's script interface that `INTEGRATION.md` does not document, recelilious decides before the script is written, because those parts may be documented only in StikDebug's source. The published research summarizes StikDebug's scripts; that summary may be used only for observable behavior, never as a code outline.

No code from StikDebug, its bundled or community scripts (such as `universal.js`), or any other product's JIT script or helper (for example Madeira's, iCube's, Play!'s, LiveContainer's or UTM's) is copied, translated or rewritten into the broker script or the host (C-0001 section 6). An in-app enabler built on idevice (MIT) would replace StikDebug later; that needs a change to this contract and to C-0004 section 1.

## 3. Sequence

The pool is requested after the user has chosen a game, because each process runs one title (C-0004 section 1).

1. **Know the debugger state.** The host reads two things and reads them again whenever the app returns to the foreground:
   - "debugged since launch", from the code-signing status flag `CS_DEBUGGED`, which XNU never clears once set;
   - "debugger attached now", from the process's traced flag `P_TRACED`, read with `sysctl`.
2. **Ask for JIT.** When the user starts a game, the app opens StikDebug with `stikdebug://enable-jit`, passing Paneport's broker script. The host treats an attached debugger as the broker only if this launch opened that URL and `P_TRACED` became set afterwards. Any other debugger, such as Xcode's, is foreign: the state stays `not_debugged` and the host never executes `brk #0xf00d`. No Wine session can start before the state is `ready`.
3. **Request the pool.** When the broker attaches, the state becomes `debugger_attached` and .NET receives a state-change event. .NET then calls `pp_jit_request_pool` with the size from section 4, and the state becomes `requesting`. The host executes `brk #0xf00d`, for commands 1 and 0, only in the `requesting` state and only while a debugger is attached now. It sends command 1 with a NULL address for each region until the pool is complete.
4. **Map it.** For every region the host keeps the executable view and creates a writable alias. The translator writes code only through the alias and runs it only through the executable view, so no mapping is ever both writable and executable.
5. **Detach.** The host sends command 0, and the broker script detaches the debugger. When command 0 returns, the state becomes `waiting_detach`, and the host waits until `P_TRACED` is clear.
6. **Verify.** With no debugger attached, the host writes a small test routine through the alias into the first page, the last page and one page in every 16 MiB of each region, and runs it from the executable view under the dispatcher's JIT probe route (C-0005 section 4). Any fault means authorization failed.
7. **Hand over.** The host passes the regions to the translator through the Wine bridge (C-0005 section 3). The translator allocates executable memory only from these regions and refuses any other executable allocation.
8. **No growth.** The pool is fixed for the life of the process. If the translator's code cache fills up, it evicts and recompiles inside the pool. The host never asks for more memory after the session has started.

Timeouts: if the pool is not complete within 60 seconds of entering `requesting`, or the debugger has not detached within 30 seconds of command 0, the state becomes `failed`.

## 4. Pool size

| Setting | Value |
|---|---|
| Default | 512 MiB |
| Minimum | 256 MiB |
| Maximum | 896 MiB, and never more than one eighth of the memory limit (C-0005 section 2) at the time of the request |
| Per-title override | `jitPoolMiB` in the title's compatibility entry (C-0008), clamped to the minimum and maximum |

If the memory limit allows less than the minimum, the state becomes `failed`. The whole pool counts against the memory limit once it is authorized.

Shipped emulators use 64 to 288 MB. The default and the maximum above are Paneport's own choice, following a Windows-games project that measured authorization at about 2.0 s for 256 MiB, 2.5 s for 512 MiB and 3.1 to 3.3 s for 896 MiB. recelilious set these values on 2026-10-28, and the phase 1 gate confirms or corrects them (C-0012).

## 5. States

The host reports one of these states, the enum `pp_jit_state_t`, through `pp_jit_get_state` and the event callback.

| State | Meaning | What the user sees |
|---|---|---|
| `not_debugged` | No debugger is attached (`P_TRACED` is clear), or only a foreign one is. | The JIT setup screen. |
| `debugger_attached` | The broker that this launch asked for is attached; no pool has been requested yet. | A progress indicator. |
| `requesting` | The pool is being requested. | A progress indicator. |
| `waiting_detach` | The pool is mapped; the debugger is detaching. | A short wait message. |
| `verifying` | The probe is running with no debugger attached. | A progress indicator. |
| `ready` | The pool is authorized and verified. | Nothing; the game starts. |
| `failed` | A request, the detach, the probe or a timeout failed. | The reason and the advice to relaunch the app and enable JIT again. |

Leaving `failed` always needs a relaunch.

## 6. Keeping up with iOS releases

- `compat/ios-builds.json`, validated by `compat/schema/ios-builds.schema.json`, lists the iOS builds on which this protocol has been verified. Each entry holds the iOS version and build, the device model identifier and the path of its evidence record (C-0012). The list ships in the app.
- On an iOS build that is not on the list, the app warns but does not refuse to run.
- Any change Apple makes to this mechanism is handled by a change to this contract first.

## 7. Safety

- The broker script handles only commands 0 and 1, only for Paneport's own process, and performs no other operation.
- Pairing files are never committed (C-0002 section 6).

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
