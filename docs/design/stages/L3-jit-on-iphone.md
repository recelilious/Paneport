# L3 JIT on the iPhone

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L3 | JIT on the iPhone | Large | Planned | | |
| M3.1 | JIT provider and broker script | Medium | In progress: S3.1.6 follows in a pull request of its own | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |
| S3.0.1 | JIT setup screen | Small | Planned | | |
| S3.0.2 | JIT self-test | Small | Planned | | |
| S3.0.3 | First verified iOS build entry | Small | Planned | | |
| S3.1.1 | Debugger state detection | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-23 |
| S3.1.2 | State machine and events | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |
| S3.1.3 | Region request and mapping | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |
| S3.1.4 | Verification probe | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |
| S3.1.5 | macOS backend with a simulated broker | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |
| S3.1.6 | Broker script | Small | Ready: C-0006 1.1 names StikDebug's built-in script ([jit-broker.md](../jit-broker.md) section 4) | | 2026-11-24 |
| S3.1.7 | iOS build list and schema | Small | Done | [#40](https://github.com/recelilious/Paneport/pull/40) | 2026-11-24 |

## L3 JIT on the iPhone

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L1, M2.2 |
| C-0012 gate | None. The phase 1 gate at L5 (C-0012 section 3) is where JIT is checked in full, including the no-RWX scan and the 10-minute run across backgrounding. |
| Size | M |
| Branch | `feat/l3-jit-on-iphone` |
| Decisions needed | None new. D2 (free or paid Apple account, decided before L1) sets the entitlement values recorded in the evidence record. The pool sizes are fixed by C-0006 section 4 and are confirmed or corrected at L5, not here. |

**Goal.** Prove on a real iPhone 17 or 18 Pro that Paneport can obtain an authorized, verified executable code pool through StikDebug, and that code written into the pool runs. This is the first time the JIT request protocol (C-0006) runs end to end on the device. No Wine session and no translator run yet. L3 only shows that the pool is real and usable.

**What recelilious will see.** A JIT setup screen inside the app with an Enable JIT button. Tapping it opens StikDebug, which runs its built-in script, `universal.js`, as Paneport's broker and returns control to the app. The screen then shows the state reaching `ready`. A JIT self-test writes a small ARM64 routine into the pool and runs it, and the screen reports that it passed.

**Acceptance steps.** These are manual steps on the device. L3 is not a C-0012 gate, so the steps prove the feature works and record the evidence; they do not have to satisfy gate criteria. Each step has an observable result.

1. **Preconditions.** StikDebug is installed and paired (pairing file imported, LocalDevVPN running), and Paneport is installed from a development-signed or re-signed build so that `get-task-allow` is present (C-0009 section 4). Expected: StikDebug lists Paneport in its JIT list.
2. **Open the JIT screen.** Launch Paneport and open the JIT setup screen. Expected: the screen shows the steps and an Enable JIT button, the state reads `not_debugged` (C-0006 section 5), and if the current iOS build is not in the verified list the screen shows the warning but still lets the user continue (C-0006 section 6).
3. **Enable JIT.** Tap Enable JIT. Expected: Paneport opens StikDebug through `stikdebug://enable-jit`, naming StikDebug's built-in script with `script-name=universal.js` and carrying Paneport's `bundle-id` and `pid` (C-0006 section 3 step 2, [jit-broker.md](../jit-broker.md) section 5); StikDebug runs and control returns to Paneport.
4. **Watch the state.** Expected: the state moves `debugger_attached`, then `requesting`, then `waiting_detach`, then `verifying`, then `ready` (C-0006 section 5), with a progress indicator per state. No Wine session is attempted (there is none until L4).
5. **Run the self-test.** Trigger the JIT self-test. Expected: it writes a small ARM64 routine through the writable alias, runs it from the executable view, and reports the expected return value (pass). No fault occurs and no mapping is written and executed at once (C-0006 section 3 step 4).
6. **Background and return.** Send the app to the background for at least one minute, return to the foreground, and run the self-test again. Expected: the host re-reads the debugger state on return (C-0006 section 3 step 1) and the self-test still passes. This records the behavior that C-0006 section 1 leaves open for backgrounding.
7. **Relaunch.** Fully quit and relaunch the app, then open the JIT screen. Expected: the state is `not_debugged` again, which confirms authorization is per launch (C-0006 section 1); enabling JIT again reaches `ready`.
8. **Foreign debugger (optional).** Instead of StikDebug, attach Xcode's debugger. Expected: the state stays `not_debugged` because the launch did not open the URL and the debugger is foreign (C-0006 section 3 step 2), the screen shows the foreign-debugger message, and the app never executes `brk #0xf00d`.
9. **Record and list.** Fill in the evidence record below. On a pass, add the verified iOS build to `compat/ios-builds.json` (C-0006 section 6, see S3.0.3).

**Evidence record.** File `docs/evidence/YYYY-MM-DD-l3-jit.md`, date in UTC (C-0012 section 4). It records:

- the feature (L3 JIT on the iPhone) and the build (commit hash and app version);
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `com.apple.developer.kernel.increased-memory-limit` and `com.apple.developer.kernel.extended-virtual-addressing` in the installed app (C-0009 section 4);
- the JIT enabler (StikDebug) and its version, whether Game Mode was active, and the memory limit read during the run;
- the steps above with expected and observed results, and these stage metrics: the pool size requested, the authorization time (and the time spent in each state), the state timeline, and whether the self-test still passed after backgrounding;
- scrubbed logs;
- the verdict, the tester as a project handle (`recelilious` or `Claude`), and the date.

The record and its attachments must never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name or Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). The device model identifier is allowed. This stage involves no Steam and no Valve binaries.

**Integration session.** The L3 session starts once M3.1 is Done. It wires the JIT provider (M3.1) into the user-facing flow: the JIT setup screen (S3.0.1) reads the state through `pp_jit_get_state` and the event callback over Paneport.Interop (C-0005 section 2), and the Enable JIT action opens the URL that `StikDebugRequest.Build` gives (S3.1.6), which names StikDebug's built-in script, through an iOS URL opener in Paneport.iOS. It builds the JIT self-test (S3.0.2) on top of the authorized pool. It confirms that the macOS backend with a simulated broker, whose regions are anonymous readable and executable memory rather than `MAP_JIT` memory ([jit.md](../jit.md) section 2), still passes CTest after the wiring, prepares the acceptance steps and the evidence record template (C-0012 section 2), and runs the checks that need the whole screen together. After recelilious runs the device acceptance and it passes, the session adds the verified build entry (S3.0.3). The session starts no Wine session; that begins at L4.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S3.0.1 | JIT setup screen: steps, an Enable JIT button that opens the URL of S3.1.6, which names StikDebug's built-in script, a progress indicator per state, failure reasons, the foreign-debugger message, and the warning for an iOS build not in the verified list | view model in `src/Paneport.Core`, view in `src/Paneport.UI`, URL opener behind a Core interface implemented in `src/Paneport.iOS` | Headless UI tests in `tests/Paneport.UI.Tests` drive the view model through each `pp_jit_state_t` value and assert the shown text, the button state and the unverified-build warning |
| S3.0.2 | JIT self-test: writes a small ARM64 routine through the writable alias, runs it from the executable view, and checks the result | native routine in `src/native/host/jit/`, surfaced through the device test app in `tests/device` | The same routine runs on the macOS backend with a simulated broker in `tests/native` ([jit.md](../jit.md) section 2) and returns the expected value |
| S3.0.3 | First verified iOS build entry, added after the device acceptance passes | `compat/ios-builds.json` | The CI compatibility check validates the entry against `compat/schema/ios-builds.schema.json` (C-0011 section 1) |

**Notes from earlier stages.**

- M3.1 (session log of 2026-11-23): the JIT group is `paneport/jit.h`, bound in Paneport.Interop as `NativeHost.GetJitStatus`, `SetJitEnablerOpened`, `NoteJitForeground` and `RequestJitPool`. The screen reads `GetJitStatus` [any] and the events of kind `HostJit.StateChangedEvent`, whose code is the new state; `ForeignDebugger` shows the foreign-debugger message, and `Failure` the failure's reason. The app calls `SetJitEnablerOpened(true)` on the main thread right before it opens the URL, and `SetJitEnablerOpened(false)` when iOS refuses it; the head calls `NoteJitForeground` on every return to the foreground; on the `debugger_attached` event the app calls `RequestJitPool(HostJit.DefaultPoolMiB)` on the main thread ([jit.md](../jit.md) sections 5 and 8).
- M3.1 (session logs of 2026-11-23 and 2026-11-24): the URL names StikDebug's built-in script, `script-name=universal.js`, with `bundle-id` and `pid`, as StikJIT's `INTEGRATION.md` describes. recelilious chose that on 2026-11-23, and C-0006 1.1 records it. S3.1.6 built it: `StikDebugRequest.Build(bundleIdentifier, processId)` in `src/Paneport.Core/Jit/` returns the URL's text, which the opener of S3.0.1 opens as it is; it refuses an empty bundle identifier and a process identifier that is not above 0 ([jit-broker.md](../jit-broker.md) section 5).
- M3.1 (session log of 2026-11-23): the record's metrics come from the status: `RequestedMiB`, `PoolMiB`, `AuthorizationMilliseconds` and the time in each state; the host also logs each change of the state with its time. The self-test of S3.0.2 can take the pool from `pp_jit_pool_copy` in `jit_provider.h` and run a routine with `pp_jit_probe_page`; after `ready` the probe mode is off, so a fault there ends the app with a crash record ([jit.md](../jit.md) sections 7 and 11). The CTest binaries on the runner need no entitlement and no signing of their own (jit.md section 12).
- M3.1 (session logs of 2026-11-23 and 2026-11-24): the macOS backend's simulated broker maps each region as anonymous readable and executable memory, not as `MAP_JIT` memory, which is writable and executable by design; the coordinator accepted this on 2026-11-24. The macOS path of this section and of S3.0.2 is that backend ([jit.md](../jit.md) section 2).

**Session log.**

_No sessions yet._

## M3.1 JIT provider and broker script

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L3 |
| Depends on | M2.2, M1.3 |
| Size | L |
| Branch | `feat/m3-1-jit-provider`; S3.1.6 on `feat/m3-1-stikdebug-request` |
| Decisions needed | Whether the broker needs any part of StikDebug's script interface that StikJIT's `INTEGRATION.md` does not document. It did, and recelilious decided on 2026-11-23 that the request names StikDebug's built-in script, which C-0006 1.1 records ([jit-broker.md](../jit-broker.md) section 4). D1 (a local Apple silicon Mac) is helpful for running the CTest suite locally but is not required, because the `native` and `checks` CI jobs cover it (plan section 4.2). |
| Design documents | `docs/design/jit.md` (the JIT provider, the pool and its states) and `docs/design/jit-broker.md` (StikDebug's built-in script, the request that names it, and the sources per C-0001 section 6) |

**Goal.** Implement the app side of the JIT request protocol exactly as C-0006 sections 3 to 7 define them, with the request that names StikDebug's built-in script as the broker, behind one internal provider interface with two backends: the iOS debugger-broker backend and a macOS backend with a simulated broker that lets the whole lifecycle be tested in CI without a device, whose regions are anonymous readable and executable memory rather than `MAP_JIT` memory ([jit.md](../jit.md) section 2).

**Scope.**

- In:
  - Debugger-state detection: debugged-since-launch from `CS_DEBUGGED` (read with `csops`) and attached-now from `P_TRACED` (read with `sysctl`), re-read on every return to the foreground (C-0006 section 3 step 1).
  - The broker-recognition rule: an attached debugger counts as the broker only if this launch opened `stikdebug://enable-jit` and `P_TRACED` became set afterwards; any other debugger is foreign and the host never executes `brk #0xf00d` (C-0006 section 3 step 2).
  - The state machine and the events for `pp_jit_state_t`: `not_debugged`, `debugger_attached`, `requesting`, `waiting_detach`, `verifying`, `ready`, `failed` (C-0006 section 5), delivered through the event callback on the host event thread (C-0005 section 1).
  - `pp_jit_get_state` and `pp_jit_request_pool` (C-0005 section 2), with the size chosen by .NET and clamped to 256 to 896 MiB and to at most one eighth of the memory limit at the time of the request (C-0006 section 4); a per-title `jitPoolMiB` override is honored when present (C-0008 section 2).
  - The protocol itself: `brk #0xf00d` with command 1 (`JIT26PrepareRegion`, `x0` address, `x1` length) using a NULL address hint per region, and command 0 (`JIT26Detach`) to detach, executed only in the `requesting` state and only while a debugger is attached now (C-0006 sections 1 and 3).
  - Mapping each region as an execute-only view plus a writable alias built with `vm_remap` and `vm_protect`, so no mapping is ever writable and executable at once (C-0006 sections 1 and 3 step 4).
  - The 60-second pool timeout and the 30-second detach timeout, both leading to `failed` (C-0006 section 3).
  - Verification after detach, with no debugger attached, on the first page, the last page and one page in every 16 MiB of each region, run through the dispatcher's JIT probe route while the state is `verifying` (C-0006 section 3 step 6, C-0005 section 4).
  - A pool registry: the authorized regions as a list, each with its execute-only view, writable alias and size, ready for the later hand-off to the translator through the Wine bridge (C-0005 section 3).
  - A macOS backend with a simulated broker behind the same provider interface, for tests, with the same dual mapping: readable and executable regions and their writable aliases ([jit.md](../jit.md) section 2).
  - The request that names StikDebug's built-in script: `stikdebug://enable-jit` with `bundle-id`, `pid` and `script-name=universal.js`, from a pure builder in `src/Paneport.Core` (C-0006 sections 1 to 3). Paneport writes, bundles and reads no script (C-0006 section 2).
  - `compat/ios-builds.json` and `compat/schema/ios-builds.schema.json` (C-0006 section 6).
- Out:
  - Any Wine session, translator or guest thread (L4 and later). `pp_session_start` stays refused with `PP_E_JIT_NOT_READY` until the state is `ready` (C-0005 section 2); M3.1 does not start a session.
  - The per-game request flow and the title compatibility entry (L6 and later); M3.1 exercises the default pool only.
  - Pool growth after the session starts; the pool is fixed for the life of the process (C-0006 section 3 step 8).
  - An in-app JIT enabler to replace StikDebug; that needs a change to C-0006 and C-0004 (C-0006 section 2).

**Read first.**

- C-0006, the whole contract, in particular section 1 (platform facts and the `brk #0xf00d` convention), section 2 (roles and the originality boundary), section 3 (the eight-step sequence and the timeouts), section 4 (pool size), section 5 (states), section 6 (the verified-build list) and section 7 (safety).
- C-0005 section 1 (conventions and the callback thread rule), section 2 (the JIT function group and the session-start precondition), section 3 (the Wine bridge and the pool hand-off) and section 4 (the signal dispatcher and the JIT probe route).
- C-0004 section 1 (one Wine session per launch, relaunch to switch), section 2 (the native host and Paneport.Core components, with no platform API in Core) and section 3 (repository layout and README rule).
- C-0003 section 9 (the AGPL boundary with StikDebug), section 5 (the forbidden and under-review categories) and section 6 (the component register).
- C-0001 section 6 (originality) and section 7 (compliance red lines).
- C-0009 section 4 (the app bundle, which holds no script, and the entitlements).
- C-0010 section 1 (the languages, with no JavaScript), section 2 (C# style), section 3 (C and Objective-C style) and section 6 (license headers).
- C-0011 section 1 (native tests) and section 4 (the CI jobs).
- C-0012 section 1 (definition of done) and section 4 (evidence records).
- C-0008 section 2 (the `jitPoolMiB` field).
- Research notes: `ios_platform_constraints.md` (JIT under TXM, section 1), `process_and_memory_model.md` (pool sizing and the JIT pool in the footprint), `avalonia_ui_layer.md` (the protocol specifics and the signal interplay) and `roadmap-2026-10.md`.
- Design documents written by this stage: `docs/design/jit.md` and `docs/design/jit-broker.md`.

**Deliverables.**

- `src/native/host/jit/`: the provider, with debugger-state detection, the state machine and events, region request and mapping, the verification probe, the pool registry, and the macOS backend with a simulated broker.
- `src/native/include/paneport/`: the JIT entries the provider exposes through the host interface and the Wine bridge (the pool as a list of regions), consistent with C-0005 sections 2 and 3. `pp_jit_get_state` and `pp_jit_request_pool` already belong to the interface group in C-0005 section 2; appending members follows the versioning rule in C-0005 section 1.
- `src/Paneport.Core/Jit/`: the builder of the request (S3.1.6), with its tests in `tests/Paneport.Core.Tests/Jit/`.
- `compat/ios-builds.json` and `compat/schema/ios-builds.schema.json`, plus `compat/schema/README.md` if not already present.
- `tests/native/`: the CTest suite, the fake debugger-state source and the tests on the macOS backend.
- `tools/`: the compatibility checker (the C# tool named in C-0011) extended to validate `compat/ios-builds.json` against its schema.
- `docs/design/jit.md` and `docs/design/jit-broker.md`.
- `eng/`: any CMake preset or toolchain addition the JIT tests need, if any.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S3.1.1 | Debugger state detection: `CS_DEBUGGED` via `csops`, `P_TRACED` via `sysctl`, re-read on foreground | `src/native/host/jit/` | CTest with the fake state source checks that both flags are read, that the broker-recognition rule holds, and that a foreground return triggers a re-read |
| S3.1.2 | State machine and events across the seven `pp_jit_state_t` values | `src/native/host/jit/` | CTest drives every transition and both timeouts, and asserts an event fires on each change |
| S3.1.3 | Region request and mapping: command 1 with a NULL address per region, execute-only view plus `vm_remap` writable alias, command 0 to detach | `src/native/host/jit/` | CTest on the macOS backend checks alias coherence (a byte written through the alias reads back through the execute view) and that no mapping is writable and executable at once |
| S3.1.4 | Verification probe on the first page, the last page and one page per 16 MiB, routed through the JIT probe while `verifying` | `src/native/host/jit/` | CTest checks the probe pass path and that an injected fault on a probed page is reported as a verification failure |
| S3.1.5 | macOS backend with a simulated broker behind the shared provider interface ([jit.md](../jit.md) section 2) | `src/native/host/jit/` | The backend builds on macOS and backs the CTest suite; the exact entitlement and signing the test binary needs on the runner is to be confirmed in this stage |
| S3.1.6 | The request that names StikDebug's built-in script as the broker: `stikdebug://enable-jit?bundle-id=…&pid=…&script-name=universal.js` (C-0006 section 3 step 2). Paneport writes, bundles and reads no script | `src/Paneport.Core/Jit/` | Unit tests in `tests/Paneport.Core.Tests` check the exact URL with its query items in order, the escaping of the bundle identifier, and the refusal of an empty bundle identifier and of a process identifier that is not above 0 |
| S3.1.7 | iOS build list and its schema | `compat/ios-builds.json`, `compat/schema/ios-builds.schema.json` | The CI compatibility check validates the file and example fixtures against the schema |

**Verification.** All of the following runs in CI without recelilious's iPhone. The device check is L3's acceptance, not part of M3.1.

- `native` CI job on macOS (C-0011 section 4): configure, build and run the CTest suite twice, with the `asan-ubsan` and `tsan` presets. The suite uses the macOS backend with a simulated broker (S3.1.5, [jit.md](../jit.md) section 2) and the fake debugger-state source (S3.1.1). Pass criteria:
  - alias coherence holds and no mapping is ever both writable and executable;
  - the verification probe passes on a healthy pool and reports a failure when a fault is injected on the first page, the last page or a 16 MiB-interval page;
  - every state transition in C-0006 section 5 occurs in order, an event is delivered on each change, and both the 60-second pool timeout and the 30-second detach timeout drive the state to `failed`;
  - requested sizes are clamped to 256 to 896 MiB and to one eighth of the simulated memory limit, and a below-minimum limit yields `failed` (C-0006 section 4);
  - the headers compile as C17 and C++20 and the interface conformance check passes (C-0005 section 5).
- `checks` CI job (C-0011 section 4): the compatibility checker validates `compat/ios-builds.json` against `compat/schema/ios-builds.schema.json`, including a failing fixture that the check must reject. REUSE, gitleaks and the personal-data check pass over the new files.
- Scripts this stage creates: the CTest suite and its fake debugger-state source in `tests/native/`, the macOS test backend with a simulated broker, and the extension to the C# compatibility checker in `tools/` for `compat/ios-builds.json`.

**Done when.** The deliverables exist, the verification above passes in CI, the design documents are written (C-0012 section 1 item 8), the definition of done in C-0012 section 1 holds, and the branch is merged by recelilious after review. No device behavior is claimed; the pull request marks the device path as "not yet verified on device" (C-0012 section 1 item 9), which L3 then verifies.

**Risks and fallbacks.**

- Apple changes the JIT mechanism in an iOS point release (roadmap risk 2). The verified-build list (S3.1.7) warns on an unknown build without refusing to run (C-0006 section 6), and any change Apple makes is handled by a change to C-0006 first.
- Backgrounding behavior is untested in the field (C-0006 section 1). The state machine re-reads the debugger state on foreground return (S3.1.1); L3's acceptance records what happens, and the phase 1 gate at L5 confirms the longer run.
- `INTEGRATION.md` did not document the interface that a script uses (C-0006 section 2), so recelilious decided on 2026-11-23 that the request names StikDebug's built-in script; nobody reads StikDebug's source, and no code from StikDebug, `universal.js` or any other product's JIT helper is copied, translated or rewritten (C-0001 section 6, C-0003 section 9). The script is StikDebug's, so an update of StikDebug can change it: every evidence record names StikDebug's version (C-0006 section 7).
- An attached debugger intercepts the exceptions Wine's handlers will later need, so the protocol detaches before any session (C-0006 section 1). Verification runs only with no debugger attached (C-0006 section 3 step 6).
- The macOS backend models the iOS layout but is not identical. The brief planned it on `MAP_JIT`, which the research note `x86_to_arm_translation.md` expected to need the `com.apple.security.cs.allow-jit` entitlement and to allow one region under the hardened runtime. On the runner the probe found neither ([jit.md](../jit.md) section 12), but a `MAP_JIT` region is writable and executable by design, so M3.1 built the backend with a simulated broker and anonymous readable and executable memory, which never maps a page that is writable and executable ([jit.md](../jit.md) sections 2 and 12). The backend's job is to exercise the same provider interface and the same dual-mapping discipline, not to reproduce the device byte for byte.

**Hands over to.**

- L3: the provider and the request feed the JIT setup screen (S3.0.1) and the self-test (S3.0.2), and the device acceptance.
- M4.4: the pool registry (the list of regions with execute-only view, writable alias and size) is handed to the translator through the Wine bridge (C-0005 section 3), and `pp_session_start` refuses to start until the state is `ready` (C-0005 section 2).
- L5: the phase 1 gate relies on the authorization and verification proven here, including the guarantee that no mapping is writable and executable at once (C-0012 section 3).

**Notes from earlier stages.**

- M1.2 ([L1 brief](L1-app-on-iphone.md), session log of 2026-11-10): a new public header, such as the JIT group's, goes into the header lists of `eng/mac/check-framework.sh`, which fails on a header it does not know; each new struct goes into the layout table of `src/native/host/test_exports.c` and into `LayoutConformance.ManagedLayouts`; and the group raises `PP_ABI_VERSION_MINOR` and `HostAbi.Minor` together (`docs/design/native-host.md` sections 2 and 9).
- M1.3 ([L1 brief](L1-app-on-iphone.md), session log of 2026-11-15): the iOS head starts Avalonia with `UseiOS()` and no app delegate of Avalonia's, so Avalonia binds no `IActivatableLifetime` and opens no URL by itself; the URL opener handles the URL in the head's own app or scene delegate. The developer menu of `tests/device` takes the JIT self-test (`docs/design/ios-app-head.md` sections 2 and 10).
- M2.2 ([L2 brief](L2-phase-0-gates.md), session log of 2026-11-17): the dispatcher's internal interface is `src/native/host/signals/signals.h`. Register each pool range with `pp_signals_add_pool_range` before anything runs in it, and remove it after; a pool fault counts by its address or its program counter. The probe route needs `pp_signals_set_probe_handler` and `pp_signals_set_probe_mode(1)`, for the `verifying` state only. A probe handler runs in the faulting thread's signal context, must return, and moves the thread to its recovery point; one that declines ends the app with a crash record, and so does a fault inside it on a marked thread. Its `brk #0xf00d` is routed by its program counter, because Darwin reports a breakpoint with the code of a sent signal. `.clang-tidy` turns off `portability-no-assembler`, so `brk #0xf00d` may be inline assembly ([signals.md](../signals.md) sections 3, 4, 6 and 12).

**Session log.**

- **2026-11-23 (session 1).** Draft pull request [#40](https://github.com/recelilious/Paneport/pull/40).
  - **Done.**
    - The JIT group of `paneport/jit.h`, interface version 1.3: `pp_jit_get_state`, `pp_jit_set_enabler_opened`, `pp_jit_note_foreground`, `pp_jit_request_pool`, `pp_jit_status_t`, `PP_E_JIT_STATE` (-10) and `PP_EVENT_JIT_STATE_CHANGED` (1).
    - The provider in `src/native/host/jit/`. It holds the state machine without threads (S3.1.2), the flags and the broker-recognition rule (S3.1.1), the commands, the alias and the dispatcher's ranges (S3.1.3), and the probe through the probe route (S3.1.4).
    - The iOS backend, and a macOS backend with a simulated broker (S3.1.5). Its regions are not `MAP_JIT` memory, which is writable and executable by design ([jit.md](../jit.md) section 2).
    - The bindings in Paneport.Interop.
    - `compat/ios-builds.json`, its schema and `tools/CheckCompat.cs` in the `checks` job (S3.1.7).
    - [jit.md](../jit.md) and [jit-broker.md](../jit-broker.md).
    - A throwaway probe on the runner (run 45101893218) answered S3.1.5's open point: the CTest binaries need no entitlement and no signing of their own (jit.md section 12).
  - **Verification.**
    - First CI run 45109808452. `dotnet` passed on both systems.
    - `native`: `asan-ubsan` and `tsan` passed 159 of 159, the 45 JIT cases and the two `nm` checks of the probe handler among them, with the same two cases skipped under `tsan` as before. `tests/Paneport.Interop.Tests` passed, including the JIT binding test. clang-tidy 23 found five things, which a commit three later fixes: signed bitwise operands, a parameter name, two trailing commas, and a planted `MAP_JIT` mapping that the analyzer refuses, now replaced by plain writable memory.
    - `checks` failed on the links to jit.md, which a later commit added, and on a transient 429 from www.gnu.org. The runs of the next two commits failed `checks` on developer.arm.com, which answers lychee with 403, so a later commit names Arm's manuals by number.
    - `ios` and `runtime` run on the pull request.
    - On `9f4d3e4`, the head before the statuses, push run 45135622981 passed `dotnet` on both systems, `checks` and `native`, and skipped `ios` and `runtime`, which a push to a branch skips (D7, D14); pull_request run 45135626354 passed all six jobs. That head also declares x0 to x17, the vector registers that a call does not save, and the flags as changed by the two commands, because `INTEGRATION.md` presents them as C functions; fix round 1 adds v8 to v15, whose upper halves a call may change too.
  - **Decisions.**
    - The broker script: `INTEGRATION.md` does not document the interface that a script uses, so the coordinator put the options of jit-broker.md section 4 to recelilious, recommending A. Until the answer, D: S3.1.6 is Blocked and `src/jit-broker/` holds only its README.
    - The coordinator accepted three choices: the numbers of the JIT group, a validator of our own in `tools/CheckCompat.cs` (D8 stays open for M8.3), and regions of 128 MiB.
    - M4.3's #39 also uses -10 to -12, event kinds 1 to 3 and minor version 3, so whichever of the two merges second renumbers its own.
  - **Left.**
    - S3.1.6. recelilious chose option A on 2026-11-23 ("Yes, option A"), and approved the contract changes as drafted ("Approve as drafted"): C-0003 1.9, C-0004 1.2, C-0006 1.1, C-0009 1.3 and C-0010 1.2, in [#41](https://github.com/recelilious/Paneport/pull/41). The pull request of S3.1.6 follows [#41](https://github.com/recelilious/Paneport/pull/41): the request names `script-name=universal.js` with `bundle-id` and `pid`, `src/jit-broker/` goes, and jit-broker.md, this brief and the plan follow.
    - Everything on the device is not yet verified; that is L3.
    - M3.1 cannot be Done before S3.1.6. The coordinator decided that [#40](https://github.com/recelilious/Paneport/pull/40) merges first, after its independent review, with S3.1.1 to S3.1.5 and S3.1.7 Done; M3.1 stays In progress and S3.1.6 Blocked until [#41](https://github.com/recelilious/Paneport/pull/41) merges. M4.4, which depends on M3.1, waits for the script's pull request. If [#40](https://github.com/recelilious/Paneport/pull/40) merges before M4.3's #39, #39 renumbers its status codes, event kinds and version.
  - **For later sessions.** Notes left in the sections of L3, M4.3, M4.4 and L5. jit.md section 11 lists the same points.
- **2026-11-24 (session 2).** Fix round 1 of [#40](https://github.com/recelilious/Paneport/pull/40). The coordinator's independent review of `7d7582a` (three checkers and three skeptics) found 58 claims that hold, 16 that hold in part and 2 that do not, and only low defects. The branch is rebased onto main `078bbfa`, which holds [#41](https://github.com/recelilious/Paneport/pull/41) and [#42](https://github.com/recelilious/Paneport/pull/42).
  - **Done.**
    - From `pp_shutdown` on, no command passes its check and no region that a command returns is kept. A command that passed its check just before may still go out, to the debugger that the check found attached, and its region's alias is then given back.
    - New cases: `commands_after_shutdown`, `shutdown_between_commands` and `enabler_taken_back_is_reported` in `test_jit_provider`, and `routine_of_another_page_fails` and `handler_takes_only_its_window` in `test_jit_probe`; `failed_refuses_everything` checks its events, and `writable_executable_is_found` a range that only its hole fails. 50 JIT cases in all.
    - The probe's page capacity follows the region size, and the two commands declare every vector register.
    - `pp_test_jit_plant_status` and a second JIT binding test, which tells every member of the status apart.
    - `tools/CheckCompat.cs` implements exactly the keywords that the schema uses, checks the kinds of the values of those that validate and refuses a repeated member; its self-test has 48 cases. M8.3 adds the keywords that the title schema needs.
    - `src/jit-broker/` is gone, S3.1.5 is renamed, and the stale documents are corrected.
  - **Verification.** In a Linux stand-in of the host, the 50 JIT cases pass under ASan and TSan, and each new case fails under the mutant that it targets. The runs of CI on the final head are in [#40](https://github.com/recelilious/Paneport/pull/40).
  - **Decisions.**
    - The coordinator accepted on 2026-11-24 the macOS backend with a simulated broker, whose regions are anonymous readable and executable memory, in place of the `MAP_JIT` backend that this brief planned: the brief's risk text asks only that the test backend exercise the same provider interface and dual-mapping discipline, and the runner then needs no mapping that is writable and executable. S3.1.5 is renamed to match ([jit.md](../jit.md) section 2).
    - Under C-0006 1.1 Paneport writes no broker script, so the coordinator dropped `src/jit-broker/` from [#40](https://github.com/recelilious/Paneport/pull/40). S3.1.6, now Ready, follows in a pull request of its own right after [#40](https://github.com/recelilious/Paneport/pull/40) merges: it builds the request that names `script-name=universal.js` with `bundle-id` and `pid`. M3.1 stays In progress until then.
    - A lone surrogate escape in `compat/ios-builds.json` crashes the check. M8.3 takes it: its pull request routes the file through its strict `JsonText.Read`, which refuses it.
  - **Left.** S3.1.6, after [#40](https://github.com/recelilious/Paneport/pull/40) merges. Everything on the device is L3's.
  - **For later sessions.** A note left in L3's section on the macOS backend, and the M4.4 note in the L4 brief corrected (best-effort maxima, region sizes).
- **2026-11-24 (session 3).** Fix round 2 of [#40](https://github.com/recelilious/Paneport/pull/40). The coordinator's fix check of round 1 on `2d28016` (three checkers and three skeptics) found that the native fixes hold, that R40ID2, R40ID4 and the pull request's description hold in part, and only new low defects.
  - **Done.**
    - The commits of round 1 carry `Stage: M3.1`, rewritten with their trees unchanged: `a54c2b4` is `ae62baf`, `6a90b11` is `1beed0e`, `7220484` is `b406036` and `2d28016` is `144c5e8`.
    - `jit_provider.h` says what `pp_jit_provider_stop` promises: from the call on, no command passes its check and no region that a command returns is kept, while a command that passed its check just before may still go out.
    - New cases: `nothing_kept_after_shutdown` in `test_jit_provider`, in which the region that a command returns after `pp_shutdown` leaves no range in the dispatcher and no alias, and `routine_of_another_region_fails` in `test_jit_probe`; `handler_takes_only_its_window` checks that the first fault's signal is kept. 52 JIT cases in all. The region hook of test builds passes a context, so the test keeps no file-scope state.
    - A third planted status, whose flags are those of the first inverted, so that each flag takes both values.
    - `tools/CheckCompat.cs` loads the schema through one method for the check and the self-test, whose refusal names the file; it checks the annotations and refuses a `required` list that names a member twice, and its self-test has 60 cases.
  - **Verification.** In the Linux stand-in of the host, the 52 JIT cases pass under ASan and TSan, and each new case fails under the mutant that it targets. Against a Windows stand-in of the host's planting functions, the binding test fails under each of 21 mutants of the status mapping. Each of 13 mutants of `tools/CheckCompat.cs` fails a case of its self-test by name. The runs of CI on the final head are in [#40](https://github.com/recelilious/Paneport/pull/40).
  - **Decisions.**
    - `jit_probe.h` says that the probe's result keeps the signal of the page's first fault, so a case pins it. A probe that ignores its count of faults is nearly equivalent to this one, and the coordinator left it.
    - The lone surrogate escape stays with M8.3.
  - **Left.** S3.1.6, after [#40](https://github.com/recelilious/Paneport/pull/40) merges. Everything on the device is L3's.
  - **For later sessions.** The M4.4 note in the L4 brief now says that the pool's regions on the harness are not `MAP_JIT` memory.
