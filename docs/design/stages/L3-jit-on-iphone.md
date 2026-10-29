# L3 JIT on the iPhone

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L3 | JIT on the iPhone | Large | Planned | | |
| M3.1 | JIT provider and broker script | Medium | Planned | | |
| S3.0.1 | JIT setup screen | Small | Planned | | |
| S3.0.2 | JIT self-test | Small | Planned | | |
| S3.0.3 | First verified iOS build entry | Small | Planned | | |
| S3.1.1 | Debugger state detection | Small | Planned | | |
| S3.1.2 | State machine and events | Small | Planned | | |
| S3.1.3 | Region request and mapping | Small | Planned | | |
| S3.1.4 | Verification probe | Small | Planned | | |
| S3.1.5 | macOS MAP_JIT provider | Small | Planned | | |
| S3.1.6 | Broker script | Small | Planned | | |
| S3.1.7 | iOS build list and schema | Small | Planned | | |

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

**What recelilious will see.** A JIT setup screen inside the app with an Enable JIT button. Tapping it opens StikDebug, which runs Paneport's broker script and returns control to the app. The screen then shows the state reaching `ready`. A JIT self-test writes a small ARM64 routine into the pool and runs it, and the screen reports that it passed.

**Acceptance steps.** These are manual steps on the device. L3 is not a C-0012 gate, so the steps prove the feature works and record the evidence; they do not have to satisfy gate criteria. Each step has an observable result.

1. **Preconditions.** StikDebug is installed and paired (pairing file imported, LocalDevVPN running), and Paneport is installed from a development-signed or re-signed build so that `get-task-allow` is present (C-0009 section 4). Expected: StikDebug lists Paneport in its JIT list.
2. **Open the JIT screen.** Launch Paneport and open the JIT setup screen. Expected: the screen shows the steps and an Enable JIT button, the state reads `not_debugged` (C-0006 section 5), and if the current iOS build is not in the verified list the screen shows the warning but still lets the user continue (C-0006 section 6).
3. **Enable JIT.** Tap Enable JIT. Expected: Paneport opens StikDebug through `stikdebug://enable-jit`, carrying Paneport's broker script as base64 `script-data` (C-0006 section 1); StikDebug runs and control returns to Paneport.
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

**Integration session.** The L3 session starts once M3.1 is Done. It wires the JIT provider (M3.1) into the user-facing flow: the JIT setup screen (S3.0.1) reads the state through `pp_jit_get_state` and the event callback over Paneport.Interop (C-0005 section 2), and the Enable JIT action opens `stikdebug://enable-jit` with the bundled broker script through an iOS URL opener in Paneport.iOS. It builds the JIT self-test (S3.0.2) on top of the authorized pool. It confirms the macOS MAP_JIT path still passes CTest after the wiring, prepares the acceptance steps and the evidence record template (C-0012 section 2), and runs the checks that need the whole screen together. After recelilious runs the device acceptance and it passes, the session adds the verified build entry (S3.0.3). The session starts no Wine session; that begins at L4.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S3.0.1 | JIT setup screen: steps, an Enable JIT button that opens the URL with the base64 broker script, a progress indicator per state, failure reasons, the foreign-debugger message, and the warning for an iOS build not in the verified list | view model in `src/Paneport.Core`, view in `src/Paneport.UI`, URL opener behind a Core interface implemented in `src/Paneport.iOS` | Headless UI tests in `tests/Paneport.UI.Tests` drive the view model through each `pp_jit_state_t` value and assert the shown text, the button state and the unverified-build warning |
| S3.0.2 | JIT self-test: writes a small ARM64 routine through the writable alias, runs it from the executable view, and checks the result | native routine in `src/native/host/jit/`, surfaced through the device test app in `tests/device` | The same routine runs on the macOS MAP_JIT provider in `tests/native` and returns the expected value |
| S3.0.3 | First verified iOS build entry, added after the device acceptance passes | `compat/ios-builds.json` | The CI compatibility check validates the entry against `compat/schema/ios-builds.schema.json` (C-0011 section 1) |

**Session log.**

_No sessions yet._

## M3.1 JIT provider and broker script

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L3 |
| Depends on | M2.2, M1.3 |
| Size | L |
| Branch | `feat/m3-1-jit-provider` |
| Decisions needed | Whether the broker needs any part of StikDebug's script interface that StikJIT's `INTEGRATION.md` does not document. If so, recelilious decides before the script is written, because those parts may exist only in StikDebug's source, which Paneport must not read (C-0006 section 2). D1 (a local Apple silicon Mac) is helpful for running the CTest suite locally but is not required, because the `native` and `checks` CI jobs cover it (plan section 4.2). |
| Design documents | `docs/design/jit.md` (the JIT provider, the pool and its states) and `docs/design/jit-broker.md` (the broker script, recording its JavaScript language level per C-0010 section 1 and its sources per C-0001 section 6) |

**Goal.** Implement the app side and the broker side of the JIT request protocol exactly as C-0006 sections 3 to 7 define them, behind one internal provider interface with two backends: the iOS debugger-broker backend and a macOS `MAP_JIT` backend that lets the whole lifecycle be tested in CI without a device.

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
  - A macOS backend using `MAP_JIT` dual mapping behind the same provider interface, for tests.
  - The broker script in `src/jit-broker/`, written from StikJIT's `INTEGRATION.md` only, bundled as an app resource (C-0009 section 4).
  - `compat/ios-builds.json` and `compat/schema/ios-builds.schema.json` (C-0006 section 6).
- Out:
  - Any Wine session, translator or guest thread (L4 and later). `pp_session_start` stays refused with `PP_E_JIT_NOT_READY` until the state is `ready` (C-0005 section 2); M3.1 does not start a session.
  - The per-game request flow and the title compatibility entry (L6 and later); M3.1 exercises the default pool only.
  - Pool growth after the session starts; the pool is fixed for the life of the process (C-0006 section 3 step 8).
  - An in-app JIT enabler to replace StikDebug; that needs a change to C-0006 and C-0004 (C-0006 section 2).

**Read first.**

- C-0006, the whole contract, in particular section 1 (platform facts and the `brk #0xf00d` convention), section 2 (roles and the originality boundary), section 3 (the eight-step sequence and the timeouts), section 4 (pool size), section 5 (states), section 6 (the verified-build list) and section 7 (safety).
- C-0005 section 1 (conventions and the callback thread rule), section 2 (the JIT function group and the session-start precondition), section 3 (the Wine bridge and the pool hand-off) and section 4 (the signal dispatcher and the JIT probe route).
- C-0004 section 1 (one Wine session per launch, relaunch to switch), section 2 (the JIT broker script and native host components) and section 3 (repository layout and README rule).
- C-0003 section 9 (the AGPL boundary with StikDebug), section 5 (the forbidden and under-review categories) and section 6 (the component register).
- C-0001 section 6 (originality) and section 7 (compliance red lines).
- C-0009 section 4 (the app bundle, the `jit-broker/` location and the entitlements).
- C-0010 section 1 (the broker's JavaScript language level), section 3 (C and Objective-C style) and section 6 (license headers).
- C-0011 section 1 (native tests) and section 4 (the CI jobs).
- C-0012 section 1 (definition of done) and section 4 (evidence records).
- C-0008 section 2 (the `jitPoolMiB` field).
- Research notes: `ios_platform_constraints.md` (JIT under TXM, section 1), `process_and_memory_model.md` (pool sizing and the JIT pool in the footprint), `avalonia_ui_layer.md` (the protocol specifics and the signal interplay) and `roadmap-2026-10.md`.
- Design documents written by this stage: `docs/design/jit.md` and `docs/design/jit-broker.md`.

**Deliverables.**

- `src/native/host/jit/`: the provider, with debugger-state detection, the state machine and events, region request and mapping, the verification probe, the pool registry, and the macOS `MAP_JIT` backend.
- `src/native/include/paneport/`: the JIT entries the provider exposes through the host interface and the Wine bridge (the pool as a list of regions), consistent with C-0005 sections 2 and 3. `pp_jit_get_state` and `pp_jit_request_pool` already belong to the interface group in C-0005 section 2; appending members follows the versioning rule in C-0005 section 1.
- `src/jit-broker/` and its `README.md`: the broker script.
- `compat/ios-builds.json` and `compat/schema/ios-builds.schema.json`, plus `compat/schema/README.md` if not already present.
- `tests/native/`: the CTest suite, the fake debugger-state source and the `MAP_JIT` provider tests.
- `tools/`: the compatibility checker (the C# tool named in C-0011) extended to validate `compat/ios-builds.json` against its schema.
- `docs/design/jit.md` and `docs/design/jit-broker.md`.
- `eng/`: any CMake preset or toolchain addition the JIT tests need, if any.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S3.1.1 | Debugger state detection: `CS_DEBUGGED` via `csops`, `P_TRACED` via `sysctl`, re-read on foreground | `src/native/host/jit/` | CTest with the fake state source checks that both flags are read, that the broker-recognition rule holds, and that a foreground return triggers a re-read |
| S3.1.2 | State machine and events across the seven `pp_jit_state_t` values | `src/native/host/jit/` | CTest drives every transition and both timeouts, and asserts an event fires on each change |
| S3.1.3 | Region request and mapping: command 1 with a NULL address per region, execute-only view plus `vm_remap` writable alias, command 0 to detach | `src/native/host/jit/` | CTest on the `MAP_JIT` provider checks alias coherence (a byte written through the alias reads back through the execute view) and that no mapping is writable and executable at once |
| S3.1.4 | Verification probe on the first page, the last page and one page per 16 MiB, routed through the JIT probe while `verifying` | `src/native/host/jit/` | CTest checks the probe pass path and that an injected fault on a probed page is reported as a verification failure |
| S3.1.5 | macOS `MAP_JIT` provider behind the shared provider interface | `src/native/host/jit/` | The provider builds on macOS and backs the CTest suite; the exact entitlement and signing the test binary needs on the runner is to be confirmed in this stage |
| S3.1.6 | Broker script implementing only commands 0 and 1 | `src/jit-broker/` | A syntax parse in CI and a review that it performs no other operation and is written from `INTEGRATION.md` only; a protocol unit test against a fake debugger transport is to be confirmed |
| S3.1.7 | iOS build list and its schema | `compat/ios-builds.json`, `compat/schema/ios-builds.schema.json` | The CI compatibility check validates the file and example fixtures against the schema |

**Verification.** All of the following runs in CI without recelilious's iPhone. The device check is L3's acceptance, not part of M3.1.

- `native` CI job on macOS (C-0011 section 4): configure, build and run the CTest suite twice, with the `asan-ubsan` and `tsan` presets. The suite uses the macOS `MAP_JIT` provider (S3.1.5) and the fake debugger-state source (S3.1.1). Pass criteria:
  - alias coherence holds and no mapping is ever both writable and executable;
  - the verification probe passes on a healthy pool and reports a failure when a fault is injected on the first page, the last page or a 16 MiB-interval page;
  - every state transition in C-0006 section 5 occurs in order, an event is delivered on each change, and both the 60-second pool timeout and the 30-second detach timeout drive the state to `failed`;
  - requested sizes are clamped to 256 to 896 MiB and to one eighth of the simulated memory limit, and a below-minimum limit yields `failed` (C-0006 section 4);
  - the headers compile as C17 and C++20 and the interface conformance check passes (C-0005 section 5).
- `checks` CI job (C-0011 section 4): the compatibility checker validates `compat/ios-builds.json` against `compat/schema/ios-builds.schema.json`, including a failing fixture that the check must reject. REUSE, gitleaks and the personal-data check pass over the new files.
- Scripts this stage creates: the CTest suite and its fake debugger-state source in `tests/native/`, the `MAP_JIT` provider test backend, and the extension to the C# compatibility checker in `tools/` for `compat/ios-builds.json`.

**Done when.** The deliverables exist, the verification above passes in CI, the design documents are written (C-0012 section 1 item 8), the definition of done in C-0012 section 1 holds, and the branch is merged by recelilious after review. No device behavior is claimed; the pull request marks the device path as "not yet verified on device" (C-0012 section 1 item 9), which L3 then verifies.

**Risks and fallbacks.**

- Apple changes the JIT mechanism in an iOS point release (roadmap risk 2). The verified-build list (S3.1.7) warns on an unknown build without refusing to run (C-0006 section 6), and any change Apple makes is handled by a change to C-0006 first.
- Backgrounding behavior is untested in the field (C-0006 section 1). The state machine re-reads the debugger state on foreground return (S3.1.1); L3's acceptance records what happens, and the phase 1 gate at L5 confirms the longer run.
- `INTEGRATION.md` may not document everything the broker needs (C-0006 section 2). The fallback is a decision by recelilious before the script is written; nobody reads StikDebug's source, and no code from StikDebug, `universal.js` or any other product's JIT helper is copied, translated or rewritten (C-0001 section 6, C-0003 section 9).
- An attached debugger intercepts the exceptions Wine's handlers will later need, so the protocol detaches before any session (C-0006 section 1). Verification runs only with no debugger attached (C-0006 section 3 step 6).
- The macOS `MAP_JIT` backend models the iOS layout but is not identical: `MAP_JIT` on macOS needs the `com.apple.security.cs.allow-jit` entitlement and allows one region (research note `x86_to_arm_translation.md`). The exact entitlement and signing the CTest binary needs on the GitHub runner is to be confirmed in this stage; the backend's job is to exercise the same provider interface and the same dual-mapping discipline, not to reproduce the device byte for byte.

**Hands over to.**

- L3: the provider and the broker feed the JIT setup screen (S3.0.1) and the self-test (S3.0.2), and the device acceptance.
- M4.4: the pool registry (the list of regions with execute-only view, writable alias and size) is handed to the translator through the Wine bridge (C-0005 section 3), and `pp_session_start` refuses to start until the state is `ready` (C-0005 section 2).
- L5: the phase 1 gate relies on the authorization and verification proven here, including the guarantee that no mapping is writable and executable at once (C-0012 section 3).

**Session log.**

_No sessions yet._
