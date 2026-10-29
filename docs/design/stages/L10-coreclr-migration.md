# L10 Runtime migration to CoreCLR

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L10 | Runtime migration to CoreCLR | Large | Planned | | |
| S10.0.1 | Mono leftovers sweep | Small | Planned | | |
| S10.0.2 | Evidence record and gate table | Small | Planned | | |
| M10.1 | Signals and threads on CoreCLR | Medium | Planned | | |
| S10.1.1 | CoreCLR signal survey | Small | Planned | | |
| S10.1.2 | Contract change proposal | Small | Planned | | |
| S10.1.3 | Dispatcher changes | Small | Planned | | |
| S10.1.4 | macOS CoreCLR interplay tests | Small | Planned | | |
| S10.1.5 | Device signal test update | Small | Planned | | |
| M10.2 | Build and runtime migration | Medium | Planned | | |
| S10.2.1 | Project and toolchain move | Small | Planned | | |
| S10.2.2 | Avalonia upgrade | Small | Planned | | |
| S10.2.3 | SteamKit2 check | Small | Planned | | |
| S10.2.4 | Interop changes | Small | Planned | | |
| S10.2.5 | CI move | Small | Planned | | |
| S10.2.6 | Runtime line on the diagnostics page | Small | Planned | | |

## L10 Runtime migration to CoreCLR

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L2, and .NET 11 for iOS released |
| C-0012 gate | Runtime migration, before 2027-05-11 (C-0012 section 3) |
| Size | S (the integration session only; M10.1 is M and M10.2 is L) |
| Branch | `feat/l10-coreclr-migration` |
| Decisions needed | None from plan section 4.2. recelilious approves the contract changes of S10.1.2 (C-0012 section 6) and accepts the stage. |

**Goal.** Move Paneport's managed code from Mono to CoreCLR before 2027-05-11. .NET for iOS 10 is the last release with Mono, and its support ends on that date (C-0010 section 1). CoreCLR uses `SIGUSR1` on Apple platforms, so C-0005 section 4 changes first, and the signal dispatcher gate must then pass again on CoreCLR (C-0012 section 3).

- The work runs in this order: M10.1, then M10.2, then this stage's integration session (plan sections 5.2 and 6.3).
- M10.1 can start once L2 is Accepted and a stable .NET 11 SDK with its iOS workload is released. Release candidates do not count. M10.2 also needs an Avalonia release that ships iOS assets for .NET 11. A stage whose condition does not hold yet is Blocked with that reason (plan section 2.4).
- The release dates of both are to be confirmed in this stage. The research expected .NET 11 to reach general availability on 2026-11-10. On 2026-10-27 the latest Avalonia release, 12.1.4, shipped iOS assets for .NET 10 only (`avalonia_ui_layer.md` sections 2.1 and 2.2).
- Aim to finish the acceptance by mid-April 2027. A failed gate then still leaves time for recelilious to choose a fallback.
- The research notes give November 2028, the end of support for .NET 10, as the end of support for Mono on `net10.0-ios` (`roadmap-2026-10.md`, "Key risks", risk 1). C-0010 section 1 sets 2027-05-11 for .NET for iOS 10, and the contract wins.

**What recelilious will see.**

- On the iPhone, Paneport works as before, now on CoreCLR. The diagnostics page names CoreCLR and a .NET 11 version. The four-hour signal test passes again. The overlay test and the Steam sign-in behave as in L2, and every later Large stage that was already Accepted still works.
- On Windows, the desktop head runs on .NET 11, and CI is green on .NET 11.
- In the contracts index, C-0005, C-0010 and any other contract the migration needed show new Accepted versions.

**Acceptance steps.** This stage is the Runtime migration gate of C-0012 section 3, which passes when:

> The app runs on CoreCLR instead of Mono, because .NET for iOS 10, the last release with Mono, loses support on 2027-05-11 (C-0010 section 1). The signal dispatcher gate passes again on CoreCLR, with C-0005 section 4 changed first to handle CoreCLR's use of `SIGUSR1`.

The Signal dispatcher gate that it repeats passes when:

> Over at least 4 hours of continuous running on the device, managed null-reference faults are caught as exceptions, guest faults take the guest path, and there are no native crashes and no garbage-collector hangs. The record confirms the call that turns off Mono's crash chaining (C-0005 section 4).

CoreCLR has no Mono call to confirm. S10.1.2 proposes the text that replaces that sentence in C-0012 section 3, and the steps check, in its place, the start-up checks that the changed C-0005 section 4 requires. The tables after the steps list the regression checks and map every gate criterion to its steps.

Use the builds of the integration commit that the evidence record template names. Steps 1 to 4 run on Windows. Steps 5 to 12 use the device test app: the full app plus the developer screens, which replaces the main app when installed because both share one bundle identifier (plan section 7.3). Step 13 uses the main app.

1. Open `docs/contracts/README.md` on GitHub, then C-0005, C-0010 and C-0012. Expected: C-0005 and C-0010 show a new Accepted version. C-0005 section 4 says how the dispatcher coexists with CoreCLR's use of `SIGUSR1`. C-0010 section 1 names CoreCLR as the .NET runtime on iOS. If recelilious accepted that part of the proposal, C-0012 section 3 says what the signal dispatcher record confirms on CoreCLR.
2. On GitHub, compare the history of `docs/contracts/C-0005-native-host-interface.md` with the pull requests of M10.1 and M10.2. Expected: the C-0005 change reached `main` before both pull requests were merged.
3. Open the CI checks of the integration commit on GitHub. Expected: `dotnet`, `native`, `ios` and `checks` are green, and their logs show the .NET 11 SDK.
4. Run the desktop head from the integration commit as in L0. Expected: it starts with sample data, and its diagnostics page shows CoreCLR with a .NET 11 version.
5. Install the device test app on the iPhone with AltStore, or through a Mac (C-0009 section 5). Expected: it installs over the previous build and opens without a crash.
6. Open the diagnostics page. Expected: the runtime line says CoreCLR with a .NET 11 version, not Mono. The native host's ABI version and the memory limit appear as in L1, which shows that the native host framework resolves under CoreCLR (C-0009 section 4). The build commit matches the template.
7. Run the interface conformance test on the device (C-0005 section 5). Expected: it passes.
8. Open the overlay gate page from L2 and repeat its overlay steps: the blend check of a screenshot with `tools/CheckOverlayBlend.cs` on Windows, the taps outside and on the controls, and the multi-touch steps. Expected: the results of the L2 overlay record. Every probe region is reported as PASS, every touch outside the controls reaches the game view while the swallowed counter stays at 0, and every finger keeps its owner from start to end.
9. Open the Steam gate page from L2. Sign in by approving the sign-in in the Steam mobile app, read the license list and app information, and download a manifest and some chunks. If a second device with the Steam mobile app is at hand, also sign in with a QR code (C-0007 section 3). Finish with Delete test data and Sign out, as in L2. Expected: every step succeeds. The record notes the sign-in and manifest download times next to the L2 figures. A time more than twice the L2 figure is reported to recelilious as a risk, not as a failure.
10. Close the app in the app switcher, put the iPhone on a charger, turn Low Power Mode off and open the app from the home screen, with no debugger attached (C-0006 section 1). Open the signal gate page from L2 and start the four-hour run. Leave the app in the foreground without touching it for at least 4 hours; the page keeps the screen on. If the app leaves the foreground, the run is invalid and starts again. Then read the result screen. Expected:
    - the verdict PASS, a valid run and a duration of at least 4:00:00, with the app still running, so there was no native crash;
    - managed NullReferenceExceptions thrown equal those caught, and their count rose throughout;
    - guest faults raised equal guest faults handled by the guest path, and unexpected faults are 0;
    - garbage collections are above 0, and the longest heartbeat gap is below the hang threshold that `docs/design/signals.md` sets, so there was no garbage-collector hang;
    - the counters of the scenarios that S10.1.5 added show no failure;
    - every start-up check that the changed C-0005 section 4 requires has passed, for example that the dispatcher still owns the fault signals and the runtime still owns `SIGUSR1`;
    - the peak memory footprint and the thermal state over time are shown for the record (C-0011 section 1).
11. Close the app in the app switcher and open it again. Expected: the app reports no crash record from the run (C-0005 section 2). Then open the iPhone's analytics data in Settings, at the menu path that the L2 acceptance steps give. Expected: no crash report from Paneport dated during the run.
12. For each Large stage in the first table below that is Accepted when this acceptance runs, do its check in the device test app. Each check that starts a Windows program gets its own launch of the app and its own JIT authorization, because a launch runs at most one Wine session (C-0004 section 1). Expected: each check gives the result in the table.
13. Install the main app from the same commit; it replaces the device test app. Open its diagnostics page, and its open-source licenses screen if L9 is Accepted. Expected: the page shows CoreCLR with a .NET 11 version, and the licenses screen still lists the .NET runtime and .NET for iOS.
14. Fill in the evidence record. Expected: every field listed below is filled, nothing from the forbidden list appears, and the record's date is before 2027-05-11.

Regression checks of step 12:

| Stage | Check on the CoreCLR build | Expected result |
|---|---|---|
| L3 | Enable JIT through StikDebug and run the JIT self-test. | The JIT state reaches `ready` and the self-test passes. |
| L4 | Run the Windows x86-64 test program from the in-app console. | Its output matches the L4 record. |
| L5 | Run the Direct3D 11 triangle program. | A rotating triangle fills the screen, as in the L5 record. |
| L6 | Play the L6 test game for 10 minutes, then summarize the session export with `tools/SessionMetrics.cs`. | It is not killed for memory, and it meets the frame-rate thresholds of the phase 2 gate of C-0012 section 3 again. |
| L7 | Open the library, signing in if the app asks, then download a small DRM-free game and start it. | The library lists the owned games, the download completes and the game starts. |
| L8 | Launch the L8 SteamStub-protected game through the Dock host. This runs on the iPhone only (plan section 7.6). | It reaches gameplay, as in the L8 record. |

Gate criteria and the steps that check them:

| Criterion (C-0012 section 3) | Checked in steps |
|---|---|
| Runtime migration: the app runs on CoreCLR instead of Mono | 6, 13 |
| Runtime migration: before 2027-05-11 | 14 |
| Runtime migration: C-0005 section 4 changed first to handle CoreCLR's use of `SIGUSR1` | 1, 2 |
| Signal dispatcher on CoreCLR: at least 4 hours of continuous running on the device | 10 |
| Signal dispatcher on CoreCLR: managed null-reference faults are caught as exceptions | 10 |
| Signal dispatcher on CoreCLR: guest faults take the guest path | 10 |
| Signal dispatcher on CoreCLR: no native crashes | 10, 11 |
| Signal dispatcher on CoreCLR: no garbage-collector hangs | 10 |
| Signal dispatcher on CoreCLR: in place of the Mono crash-chaining call, the start-up checks of the changed C-0005 section 4 | 1, 10 |

Steps 3, 4, 5, 7, 8, 9 and 12 are not gate criteria. They check that the app still works on CoreCLR, which is what plan section 5.1 says recelilious sees in this stage.

**Evidence record.** The file is `docs/evidence/YYYY-MM-DD-l10-coreclr.md`, with the date in UTC (C-0012 section 4). The integration session prepares it as a template (S10.0.2), and recelilious fills in the results or attaches the logs (C-0012 section 2). It holds:

- the gate, Runtime migration (C-0012 section 3), including the re-run of the Signal dispatcher gate on CoreCLR; and the build, as commit hash and app version;
- the runtime and version that the diagnostics page shows, and the .NET SDK, iOS workload, Xcode and Avalonia versions of the build;
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app (C-0009 section 4);
- the JIT enabler and its version, or "not used", whether Game Mode was active, and the memory limit read during the run;
- each step with its expected and observed result, and scrubbed logs;
- the metrics: the length of the signal run; the counts of NullReferenceExceptions thrown and caught, guest faults raised and handled by the guest path, unexpected faults and garbage collections; the longest heartbeat gap and the hang threshold; the peak memory footprint and the thermal state over time (C-0011 section 1); and the Steam sign-in and manifest download times next to the L2 figures;
- in place of the confirmation of Mono's crash-chaining call, the start-up checks that the changed C-0005 section 4 asks for, and a note that the Mono call is gone (S10.2.4);
- the results of the regression table and of the main-app check of step 13;
- the verdict (Pass, Fail or Waived), the tester as a project handle (`recelilious` or `Claude`, never a real name), and the date.

The record and its attachments never contain a UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4).

**Integration session.** It starts when M10.1 and M10.2 are Done (plan section 3.1). On `feat/l10-coreclr-migration` it:

1. confirms that `main` builds on .NET 11 with CoreCLR, and that `dotnet`, `native`, `ios` and `checks` are green on the integration commit;
2. does S10.0.1 and prepares the template of S10.0.2;
3. runs `eng/run-signal-interplay.sh` (from M10.1) for 60 minutes on .NET 11 on the integration commit, on a local Mac (D1) or once in the `native` job with a raised duration, and puts the summary in the pull request;
4. writes into the template the integration commit, the builds recelilious installs, and one regression row for each Large stage that is Accepted at that time;
5. opens the pull request, and gives recelilious the acceptance steps in the Chinese summary (plan section 3.3);
6. after recelilious has run the steps and filled in the record, continues in a new session: it commits the record, updates the gate table (S10.0.2), and sets L10 to Accepted once recelilious accepts it and the record is merged (plan section 2.4).

The session also proposes to recelilious the plan updates that the migration makes stale, such as the .NET 10 SDK in plan section 4.1 and the Mono risk in plan section 9, because a stage session changes only its own stage. If a step fails, the verdict is Fail and the record says why. recelilious then chooses a fix, a fallback or a waiver (C-0012 section 3), and any contract change comes first. If fewer than four weeks remain before 2027-05-11, the session says so at the top of its summary.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S10.0.1 | Mono leftovers sweep | `src/`, `tests/`, `eng/`, `tools/`, `.github/workflows/`, the design documents in `docs/design/` and the folder `README.md` files | A repository search for `mono_`, `MONO_`, `MtouchInterpreter`, `UseMonoRuntime`, `net10.0` and `.NET 10` finds nothing in `src/`, `tests/`, `eng/`, `tools/` or `.github/workflows/`. Every other hit is updated (design documents and READMEs), proposed to recelilious (stage briefs, the plan and contract text), or kept as history (`docs/research/`, `docs/evidence/` and contract change logs). |
| S10.0.2 | Evidence record and gate table | `docs/evidence/YYYY-MM-DD-l10-coreclr.md`, `docs/evidence/README.md` | The personal-data and forbidden-file check and lychee pass on both files (C-0011 section 2). The gate table keeps one row per gate, and its Runtime migration and Signal dispatcher rows link the new record. |

**Session log.**

_No sessions yet._

## M10.1 Signals and threads on CoreCLR

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L10 |
| Depends on | L2, and .NET 11 for iOS released |
| Decisions needed | None from plan section 4.2. recelilious approves the proposal of S10.1.2 before S10.1.3 merges (C-0012 section 6). The answer to D1 decides where the long macOS run happens. |
| Size | M |
| Branch | `feat/m10-1-coreclr-signals` |
| Design documents | `docs/design/coreclr-signals.md` (new); `docs/design/signals.md`, the signal dispatcher design document of M2.2 (updated) |

**Goal.** Learn exactly how CoreCLR uses signals, Mach exception ports and thread suspension on Apple platforms, from Microsoft's documentation and the runtime source. Design how the single signal dispatcher coexists with it, get the contract changes approved, change the dispatcher to match, and prove the interplay with automated tests on macOS, where .NET runs on CoreCLR. The app still runs on Mono when this stage ends; M10.2 switches it.

**Scope.**

In:

- S10.1.1, the survey, in `docs/design/coreclr-signals.md`. It reads Microsoft's documentation and the dotnet/runtime source at the .NET 11 release tag. That source is MIT and registered as the .NET runtime in C-0003 section 6. Each fact cites its file or page, and the document lists every source it consulted (C-0001 section 6). The survey answers at least these questions:
  - Which signals does the runtime install handlers for on iOS, when, and does each handler chain to the one present before it? The research lists `SIGILL`, `SIGFPE`, `SIGBUS`, `SIGABRT`, `SIGINT`, `SIGQUIT`, `SIGSEGV` and `SIGTERM`, plus `SIGTRAP` only without Mach exceptions, each chaining to the handler present when the runtime started (`avalonia_ui_layer.md` section 2.2, part 6).
  - How does the runtime use `SIGUSR1` as its activation signal (for garbage-collector suspension and hijacking, the debugger and thread abort), and which threads does it send it to? The research found that it claims every `SIGUSR1` sent from inside the process, so no install order lets it share the signal.
  - Does the iOS build take faults on its own threads through per-thread Mach exception ports, as on macOS? The research could not confirm that this code is compiled in for the iOS SDK.
  - Which threads does the runtime register, including a native thread that calls a managed callback, such as the host event thread (C-0004 section 1, C-0005 section 1)? Do threads that native code creates inherit any of its exception ports?
  - Where does a native fault on a registered thread go, for example inside a host call made from the main thread? Can the host still write its crash record (C-0005 section 2), and can the JIT probe still run where M3.1 put it (C-0006 section 3)?
  - Does the runtime enumerate, suspend or signal threads it did not create or register?
  - Which signal mask does a native thread inherit when a registered thread creates it?
  - Does the runtime's fault handling change anything while the JIT broker's debugger is attached (C-0006 section 1)?
  - Which settings change this behavior, and what do they cost? The research found one documented setting, `DOTNET_INTERNAL_ThreadSuspendInjection`, with an unmeasured garbage-collection cost.
  - How does NativeAOT differ? This is for reference only.
- S10.1.1 also writes the coexistence design. The expected direction, to be confirmed by the survey:
  - The dispatcher keeps the six fault signals of C-0005 section 4 and chains every fault outside the guest path to the runtime's saved handler.
  - `SIGUSR1`, and every other signal the survey shows the runtime needs, stays the runtime's. The host, the forks and the translator never install handlers for those signals and never send them.
  - Guest and translator threads never attach to the runtime (C-0004 section 1), so the runtime never sends them `SIGUSR1`. Thread control stays on Mach calls (C-0005 section 4).
  - Test builds check after `pp_session_start` that the fault signals still have the dispatcher's handler and that the runtime's signals still have the runtime's.
- S10.1.2, the contract change proposal, on its own branch `contract/coreclr-runtime`, never on the stage branch (plan section 3.3). Each changed contract gets its own `contract` commit with a new version, date, change-log entry and index row (C-0002 section 5, and rule 4 of the contracts index). It becomes Accepted only on recelilious's approval (C-0012 section 6), and the branch is pushed only after the review (C-0012 section 2). The proposal covers:
  - C-0005 section 4: the coexistence rules, the extended test-build check, what replaces "Mono's crash chaining is off", and the closing paragraph that assumes Mono;
  - C-0010 section 1: CoreCLR as the .NET runtime on iOS, and the .NET and C# versions for code and tools. If the Avalonia release that M10.2 needs has a new major version and it is already known, the Avalonia row too; otherwise M10.2 proposes that row;
  - other contract text that names Mono or .NET 10, found by reading the contracts: C-0009 section 2 (the .NET 10 SDK pin), section 3 (stage 3 builds on Mono with full ahead-of-time compilation and an interpreter list) and section 4 (the native host import "resolves under Mono"); C-0012 section 3 (the signal dispatcher gate asks the record to confirm Mono's crash-chaining call, and the SteamKit2 gate names a build on Mono); and C-0003 section 3, if its statement that .NET AOT compiles SteamKit2 statically into the executable no longer describes the CoreCLR build (to be confirmed in this stage);
  - the transition: `main` runs on Mono until M10.2 merges, so the proposal says which rules hold before and after that merge, and `main` never disagrees with its contracts.

  Plan section 8 names only C-0005 and C-0010. The others are listed because their text names Mono or .NET 10, and recelilious decides each.
- S10.1.3, the dispatcher changes in `src/native/host/` that the approved C-0005 section 4 requires. They merge only after that change is on `main`, and they keep the Mono build working until M10.2. If M3.1 is Done and the survey shows that the JIT probe's faults would not reach the dispatcher under CoreCLR, the probe moves to a thread whose faults do.
- S10.1.4, the interplay suite in `tests/Paneport.Interop.Tests/`, which the `native` job runs on macOS. The research found the same signal code in the .NET 10 and .NET 11 runtimes (`avalonia_ui_layer.md` section 2.2, part 6), so the suite means something before M10.2 moves CI to .NET 11. It needs no Wine session and is not the harness of plan section 7.1. These scenarios run at the same time:
  1. managed threads dereference null in a loop and catch every `NullReferenceException`;
  2. native threads, marked as guest threads through the host's test hooks, fault on guard pages and inside a registered guest reservation, and the guest path handles each fault;
  3. if M3.1's JIT provider exists, faults inside a test pool while the JIT state is `verifying` go to the probe route (C-0005 section 4);
  4. one thread allocates and forces garbage collections throughout, so the runtime keeps suspending managed threads;
  5. a native thread suspends, reads and writes the state of, and resumes the guest threads in a loop, with the Mach calls of C-0005 section 4;
  6. the host event thread delivers events and log lines to managed callbacks throughout (C-0005 section 1);
  7. the signal-ownership check that test builds run after `pp_session_start` confirms who owns each signal; where no session can start, a test hook calls the check directly;
  8. in a child process, a native fault inside a host call made from a managed thread ends the process and leaves the crash record of C-0005 section 2, unless the approved contract change says otherwise.

  The bindings for the test hooks live in Paneport.Interop and follow the pattern of the interface conformance test, because C-0010 section 2 allows `LibraryImport` only there. `eng/run-signal-interplay.sh` (Bash 3.2, C-0010 section 1) runs the suite for a given number of minutes and prints a summary.
- S10.1.5, the device signal test in `tests/device/`. It gains the scenarios of S10.1.4 that M2.2's test lacks, shows each count and each start-up check on screen, and exports a summary scrubbed of everything C-0012 section 4 forbids.
- If the Wine or FEX fork already exists (M4.1 onward), a check of its current branch for handlers on, or sends of, the runtime's signals. Small fixes go on `paneport/M10.1` in that fork, and the stage branch moves the submodule pointer (plan section 3.3).

Out:

- Moving any project, the CI jobs or the pins to .NET 11 (M10.2).
- Removing the Mono crash-chaining call (S10.2.4).
- Avalonia and SteamKit2 (M10.2).
- Device runs. The 30-minute smoke run belongs to M10.2 and the four-hour run to L10.
- Copying runtime code. The survey reads the source to learn behavior, and Paneport's code stays our own.
- Larger fork changes. They become a follow-up stage once recelilious agrees.
- NativeAOT as the app's runtime.

**Read first.**

- Contracts: C-0005 sections 1 to 5; C-0004 section 1; C-0006 sections 1 and 3; C-0010 sections 1 and 2; C-0009 sections 2 to 4; C-0003 sections 3 and 6; C-0011 sections 1 to 4; C-0012 sections 1 to 3 and 6; C-0002 section 5; C-0001 section 6.
- Research notes: `avalonia_ui_layer.md` section 2.2 (parts 1, 5 and 6, and claim T2), section 2.4 (part 3 and claim J3), section 3.3, and section 4 (experiments E16 and E17); `roadmap-2026-10.md`, "Key risks", risk 1; `wine_on_ios.md`, KQ1 (Wine's signal handlers).
- Design documents and records: `docs/design/signals.md`, the signal dispatcher design document of M2.2; `docs/design/jit.md`, the JIT design document of M3.1, if M3.1 is Done; the L2 evidence record of the Signal dispatcher gate, `docs/evidence/YYYY-MM-DD-l2-signal-dispatcher.md`.

**Deliverables.**

- `docs/design/coreclr-signals.md` (new) and its row in `docs/design/README.md`.
- `docs/design/signals.md`, the signal dispatcher design document of M2.2, updated.
- On `contract/coreclr-runtime`: `docs/contracts/C-0005-native-host-interface.md`, `docs/contracts/C-0010-languages-and-style.md`, the other contracts the proposal changes, and `docs/contracts/README.md`.
- `src/native/host/`: the dispatcher, its test-build check and the new test hooks.
- `src/Paneport.Interop/`: the bindings for the test hooks.
- `tests/native/`: new CTest cases.
- `tests/Paneport.Interop.Tests/`: the interplay suite.
- `eng/run-signal-interplay.sh` (new) and its entry in `eng/README.md`.
- `tests/device/`: the updated signal test.
- This file: the status and session log of M10.1 and its Small stages.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S10.1.1 | CoreCLR signal survey | `docs/design/coreclr-signals.md` | Review checklist: every survey question in this brief is answered or marked open with a plan, every fact cites Microsoft documentation or a dotnet/runtime file at the .NET 11 release tag, and lychee passes. |
| S10.1.2 | Contract change proposal | `docs/contracts/` on `contract/coreclr-runtime` | recelilious's review and approval (C-0012 section 6). Before that, the personal-data check and lychee pass locally, and each changed contract has a new version, date, change-log entry and index row. |
| S10.1.3 | Dispatcher changes | `src/native/host/`, `tests/native/` | CTest passes with the `asan-ubsan` and `tsan` presets on macOS. With a stand-in handler in the runtime's place, new cases show that the test-build check fails when a handler is installed on `SIGUSR1` or on a fault signal after session start, and passes otherwise. |
| S10.1.4 | macOS CoreCLR interplay tests | `tests/Paneport.Interop.Tests/`, test hooks in `src/native/host/` with bindings in `src/Paneport.Interop/`, `eng/run-signal-interplay.sh` | The suite passes in the `native` job, and a 60-minute run of the script passes on .NET 11. |
| S10.1.5 | Device signal test update | `tests/device/` | The device test app builds the way L2 builds it. Its run is L10's acceptance (device check). |

**Verification.** All of it runs without recelilious's iPhone.

1. Native checks, in the `native` job and on a local Mac if there is one (D1): clang-format in check mode, clang-tidy, and CTest with the `asan-ubsan` and `tsan` presets (C-0011 section 4). Pass: no format or analysis finding, every test passes, and no sanitizer report. The new cases of S10.1.3 pass, and M2.2's routing tests still pass unchanged.
2. Interplay suite, in the `native` job: `tests/Paneport.Interop.Tests` on CoreCLR. Pass: every test passes, and in each run:
   - the caught `NullReferenceException`s equal the null dereferences attempted;
   - the faults handled on each route equal the faults triggered for that route, and unexpected faults, those that took another route, are 0;
   - the longest heartbeat gap stays below the hang threshold, which this stage sets for macOS and records in `docs/design/coreclr-signals.md`, as `docs/design/signals.md` does for the device;
   - the ownership check passes;
   - the child process ends as scenario 8 expects.

   The default duration adds less than five minutes to the job.
3. Long run with the one script this stage creates, `eng/run-signal-interplay.sh`: 60 minutes on macOS with the .NET 11 runtime. Pass: the criteria of item 2, with the summary in the pull request. Without a local Mac (D1), this run moves to M10.2's verification, which has .NET 11 in CI, and the session log says so. How a run selects the .NET 11 runtime before M10.2 changes the pins is to be confirmed in this stage. If it needs a pin that C-0009 section 2 does not list, S10.1.2 proposes it.
4. `dotnet`, `ios` and `checks` are green on the branch's last commit (C-0011 section 3). The `ios` job still builds the Mono app with the changed dispatcher.
5. Contract branch: before recelilious's review, the personal-data, secrets and link checks pass locally (C-0011 section 2).
6. No device check in this stage. The pull request marks the dispatcher's device behavior "not yet verified on device" (C-0012 section 1), and L10 runs the updated device test.

**Done when.**

- `docs/design/coreclr-signals.md` answers every survey question, or marks it open with the device check in L10 that will answer it.
- recelilious approved the contract changes, and they reached `main` before the dispatcher change (C-0012 section 1 item 3).
- The dispatcher implements the changed C-0005 section 4, and the Mono build still works.
- Items 1, 2, 4 and 5 of the verification pass, and item 3 passed or was handed to M10.2 in the session log.
- The device signal test has the changes of S10.1.5.
- recelilious merged the branch, and M10.1 is Done.

**Risks and fallbacks.**

- The survey shows that leaving `SIGUSR1` to the runtime is not enough, for example because the runtime suspends threads it does not own, or because its Mach exception ports catch guest faults. The fallbacks, in order: change the dispatcher or the thread setup so that guest faults still reach it; set `DOTNET_INTERNAL_ThreadSuspendInjection=0` after measuring its garbage-collection cost, first in the macOS suite and then on the device as research experiment E17 proposes; the failed-gate fallback of C-0012 section 3, a Swift shell. recelilious decides, and the contracts change first.
- macOS is not iOS. On iOS the runtime is linked statically and runs ReadyToRun code with an interpreter (`avalonia_ui_layer.md` section 2.2), and the Mach exception question is open. The macOS suite can pass while the device fails. The device run in L10 decides, and the device test shows which route handled each kind of fault, so a difference is visible. Running the suite in the iOS simulator might come closer, but it needs a CI job that C-0011 section 4 does not list.
- The review takes time while the deadline approaches. The survey and the tests go ahead while the review is pending; only the merge of S10.1.3 waits.
- The dispatcher changes land while `main` still runs on Mono. They keep Mono's behavior, and M2.2's routing tests stay green.
- A fork needs a large change to stop handling or sending the runtime's signals. It becomes a follow-up stage once recelilious agrees, and L10 waits for it.

**Hands over to.**

- M10.2: the accepted contract changes, a dispatcher that works on both runtimes, the interplay suite, and the 60-minute run if it was handed over.
- L10: the updated device signal test, and the start-up checks that the changed C-0005 section 4 asks the evidence record to confirm.
- Every stage that changes the dispatcher, the JIT probe or the forks after this one merges, such as M3.1, M4.3, M4.4 or M8.1 if they are not Done yet, and later work in the forks: the changed C-0005 section 4, in particular the signals that only the runtime may handle or send, the extended test-build check, and the thread the JIT probe runs on.

**Session log.**

_No sessions yet._

## M10.2 Build and runtime migration

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L10 |
| Depends on | M10.1 |
| Decisions needed | None from plan section 4.2. A package that the register in C-0003 section 6 does not cover needs recelilious's approval of its registration first. D7 may be revisited if the `ios` job's run time changes a lot. |
| Size | L |
| Branch | `build/m10-2-coreclr-migration` |
| Design documents | `docs/design/coreclr-migration.md` (new); the design documents that name Mono or .NET 10 (updated), such as `docs/design/solution-and-build.md` (M0.1), `docs/design/repository-checks.md` (M0.2) and those of the iOS app head (M1.3) and the Steam service (M2.3) |

**Goal.** Move the whole solution to .NET 11 and the iOS app to CoreCLR. On iOS, .NET 11 compiles ahead of time with composite ReadyToRun and keeps an interpreter for code that is not precompiled (`avalonia_ui_layer.md` section 2.2). That is the mode this stage uses. Avalonia moves to a release that supports .NET 11 for iOS, SteamKit2 keeps working, the one Mono-specific call goes (plan section 9), every CI job turns green on .NET 11, and a device smoke check passes. Until an Avalonia release ships iOS assets for .NET 11, this stage is Blocked with that reason (plan section 2.4).

**Scope.**

In:

- S10.2.1, the project and toolchain move:
  - `global.json` pins the .NET 11 SDK and iOS workload set, and `eng/toolchain.json` pins the Xcode version that workload needs and the macOS runner image (C-0009 section 2, as changed). The research saw the .NET 11 RC2 workload need Xcode 27.0 (`avalonia_ui_layer.md` section 2.2). The version for the released workload is to be confirmed in this stage.
  - Paneport.iOS and the device test app target `net11.0-ios`. The other projects and the file-based tools target .NET 11 and use the C# version that the changed C-0010 section 1 names.
  - The iOS build uses CoreCLR in the mode above. Mono-only settings go, such as the interpreter list (C-0009 section 3) and any exception-marshaling mode that CoreCLR does not support; the research found that only Mono can unwind native frames (`avalonia_ui_layer.md` section 2.2, part 2). The exact build properties are to be confirmed in this stage.
  - `tools/CheckToolchain.cs` (M0.2) accepts the new pins, and the lock files are regenerated.
- S10.2.2, the Avalonia upgrade. Avalonia, and the Avalonia.Headless.XUnit test package with it, move to the first stable release that ships iOS assets for .NET 11, or a later one if it fixes something Paneport needs. Its version and date are to be confirmed in this stage. The session reads that release's notes for iOS changes, such as the 120 Hz and idle display-link changes that the research saw merged for 12.2 (`avalonia_ui_layer.md` section 2.1), and records their effect on the overlay. The overlay's transparency style and hit-test overrides from M2.1 keep working. Any new package is registered under C-0003 before it is added (C-0003 section 5).
- S10.2.3, the SteamKit2 check. On .NET 11 the interpreter runs protobuf-net's dynamically generated code without configuration, and the Mono interpreter settings have no effect (`avalonia_ui_layer.md` section 2.3, claim S2). So the interpreter list leaves `eng/toolchain.json`. The `[UnconditionalSuppressMessage]` suppressions in Paneport.Steam stay only where they are still needed (C-0010 section 2). The WebSocket connection that M2.3 set up keeps working; the research found that the default iOS HTTP handler cannot upgrade to WebSocket (`avalonia_ui_layer.md` section 3.4 and experiment E11).
- S10.2.4, the Interop changes. The Mono crash-chaining call and its binding leave Paneport.Interop. The start-up step that the changed C-0005 section 4 requires on CoreCLR, if any, comes in. CoreCLR aborts the process when a managed exception reaches native frames, and .NET 11 fails fast on a callback with a non-blittable signature (`avalonia_ui_layer.md` section 2.2, parts 2 and 5). C-0005 section 1 already forbids both, and this stage's test proves it. If the native host import does not resolve under CoreCLR on the device, the bindings use `__Internal`, as C-0009 section 4 says.
- S10.2.5, the CI move. Every workflow uses the pinned .NET 11 SDK and workload, the pinned macOS runner image and the pinned Xcode. Each job prints the SDK version it used. The `ios` job also checks that the app was built for CoreCLR; how it tells is to be confirmed in this stage. If M9.1 is Done, the `release` workflow's dry run runs on .NET 11.
- S10.2.6, the runtime line. The diagnostics page from L1 shows the managed runtime (CoreCLR or Mono) and its version, and the build's commit hash if the page does not show it yet. The build section of the L2 developer screen already names the runtime, so this module reuses its reader if there is one. How the runtime is detected is to be confirmed in this stage and recorded in the migration document.
- Every automated test, rerun on .NET 11, and M10.1's 60-minute run if it was handed over.
- `docs/design/coreclr-migration.md`: the versions chosen, the settings, what changed and why, the sources consulted (C-0001 section 6), and measurements next to the last Mono figures: `.ipa` size, cold start time, the shell's footprint at idle, and the Steam sign-in and manifest download times.
- The device smoke check in the verification, run by recelilious.

Out:

- The signal design, the dispatcher, the contract proposal and the interplay suite (M10.1).
- NativeAOT. The research found that it also uses `SIGUSR1` on Apple and has no interpreter for protobuf-net's dynamic code (`avalonia_ui_layer.md` sections 2.2 and 2.3).
- Upgrading SteamKit2, protobuf-net or other packages beyond what .NET 11 or the new Avalonia needs.
- The four-hour run and the evidence record (L10).
- Code in the forks, which does not use .NET.
- New features beyond the runtime line.

**Read first.**

- Contracts: C-0009 sections 2 to 6; C-0010 sections 1, 2 and 7; C-0011 sections 1 to 5; C-0003 sections 3, 5 and 6; C-0005 sections 1, 4 and 5; C-0004 sections 2 and 3; C-0007 sections 2 and 3; C-0012 sections 1 and 3; and the contract changes accepted in M10.1.
- Research notes: `avalonia_ui_layer.md` section 2.1 ("Versions and release train"), section 2.2 (parts 0, 1, 2, 3 and 5), section 2.3 (parts 1a, 5 and 7, and claims S1 and S2), sections 3.2, 3.4 and 3.5, and section 4 (experiments E6, E11, E20, E21 and E24); `roadmap-2026-10.md`, "Build and signing".
- Design documents: `docs/design/coreclr-signals.md` (M10.1), `docs/design/solution-and-build.md` (M0.1), `docs/design/repository-checks.md` (M0.2, the CI jobs and tools), and the design documents of the iOS app head (M1.3) and the Steam service (M2.3).

**Deliverables.**

- `global.json` and `eng/toolchain.json`.
- `Directory.Build.props`, `Directory.Packages.props` and every `packages.lock.json`.
- The project files under `src/` and `tests/`, and the package directives of the file-based tools in `tools/`.
- `src/Paneport.iOS/`, `src/Paneport.Interop/`, `src/Paneport.Steam/` where needed, and the runtime line in `src/Paneport.Core/` and `src/Paneport.UI/`.
- `tests/Paneport.Core.Tests/`, `tests/Paneport.Steam.Tests/`, `tests/Paneport.UI.Tests/`, `tests/Paneport.Interop.Tests/` and `tests/device/`.
- `.github/workflows/`.
- `docs/design/coreclr-migration.md` (new) and its row in `docs/design/README.md`; the design documents and `README.md` files that name Mono or .NET 10.
- This file: the status and session log of M10.2 and its Small stages.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S10.2.1 | Project and toolchain move | `global.json`, `eng/toolchain.json`, `Directory.Build.props`, the project files under `src/` and `tests/`, the tools in `tools/` | The `dotnet` job's steps for `eng/ci.slnf` pass locally on Windows with the pinned .NET 11 SDK, and `tools/CheckToolchain.cs` passes against the new pins (C-0009 section 2). |
| S10.2.2 | Avalonia upgrade | `Directory.Packages.props`, lock files, `src/Paneport.UI/`, `src/Paneport.iOS/`, `tests/Paneport.UI.Tests/` | The headless UI tests and the hit-test unit tests from M2.1 pass, and `tools/CheckLicenses.cs` passes over the new lock files. |
| S10.2.3 | SteamKit2 check | `src/Paneport.Steam/`, `tests/Paneport.Steam.Tests/`, `eng/toolchain.json` | The Steam tests with fakes pass on .NET 11, the trimmed test app `tests/Paneport.TrimTest` publishes with no trimming warning from our code, and `eng/toolchain.json` no longer lists interpreted assemblies. |
| S10.2.4 | Interop changes | `src/Paneport.Interop/`, `tests/Paneport.Interop.Tests/` | The interface conformance test passes on .NET 11, and no `mono_` symbol is left. A test makes every managed callback handler throw while the host's test hook delivers events, and sees each failure reported through the return value or the log while the process keeps running. |
| S10.2.5 | CI move | `.github/workflows/`, `eng/` | `dotnet`, `native`, `ios` and `checks` pass on the branch, and the `release` dry run passes if M9.1 is Done. |
| S10.2.6 | Runtime line on the diagnostics page | `src/Paneport.Core/` (view model), `src/Paneport.UI/` (view), `tests/Paneport.Core.Tests/` | A unit test with a fake runtime description passes, and on Windows the desktop head shows CoreCLR with a .NET 11 version. |

**Verification.** All of it runs without recelilious's iPhone, except item 6, which is a device check.

1. Windows, with the pinned .NET 11 SDK: the `dotnet` job's steps for `eng/ci.slnf`, which are a locked restore, the build, the tests, `dotnet format --verify-no-changes` and the trimmed test app (C-0011 sections 2 and 4). Pass: no error and no warning (warnings are errors, C-0010 section 2), every test passes, and each test project runs at least as many tests as it did on `main` before the move.
2. GitHub Actions on the branch's last commit: `dotnet` on Windows and Ubuntu, `native`, `ios` and `checks` (C-0011 sections 3 and 4). Pass: all are green, and the logs show the .NET 11 SDK. The `native` job runs M10.1's interplay suite and the interface conformance test on .NET 11, with M10.1's pass criteria. The `ios` job produces the `.ipa`, and its runtime check reports CoreCLR. The `checks` job's dependency license check passes over the new lock files.
3. If M10.1 handed it over: the 60-minute run of `eng/run-signal-interplay.sh` on .NET 11. Pass: M10.1's criteria.
4. If M9.1 is Done: the `release` workflow's dry run, following the dry-run and clean-up steps in `docs/releases/README.md` and the test-tag permission that M9.1 recorded. Pass: it produces every artifact that C-0009 section 6 lists, and the generated notices still list the .NET runtime and .NET for iOS.
5. This stage creates no new script. It changes the workflows, the pins and the project files.
6. Device check, run by recelilious. Until it is done, the pull request marks device behavior "not yet verified on device" (C-0012 section 1). recelilious installs the device test app built from the branch, then checks that:
   - the diagnostics page shows CoreCLR and the native host's ABI version;
   - the interface conformance test passes;
   - the overlay gate page of L2 passes its touch steps;
   - a 30-minute run on the signal gate page of L2 shows 0 unexpected faults, a longest heartbeat gap below the hang threshold of `docs/design/signals.md`, and no crash;
   - the Steam gate page of L2 signs in and downloads a manifest, and shows its sign-in and manifest download times;
   - the cold start time and the shell's footprint at idle are noted for the migration document.

   Pass: every item holds, and the results and figures go into the pull request. The four-hour run belongs to L10.

**Done when.**

- Every project targets .NET 11, and the app and the device test app run on CoreCLR.
- No Mono-specific call or setting is left in `src/`, `tests/`, `eng/` or `.github/workflows/`.
- Verification items 1 to 4 pass on the last commit, and the device check passed.
- `docs/design/coreclr-migration.md` records the versions, settings, measurements and sources.
- recelilious merged the branch, and M10.2 is Done.

**Risks and fallbacks.**

- No Avalonia release supports .NET 11 for iOS in time. The stage stays Blocked with that reason. recelilious may then choose a pinned Avalonia pre-release that ships those assets, or decide to stay on .NET for iOS 10 after 2027-05-11 without support, which needs a C-0010 change and a waiver of the gate (C-0012 section 3).
- The workload needs a newer Xcode than the runner images offer. In October 2026 the research found Xcode 27 on GitHub's runners only behind a preview label (`avalonia_ui_layer.md` section 2.2). A local Mac (D1) can build meanwhile, but the jobs must pass before the merge (C-0011 section 3). The research also found that apps built with Xcode 27 must adopt the UIScene life cycle or crash at launch, so the app head is checked for it.
- CoreCLR on iOS has defects. The research listed a release-candidate crash with mixed interpreter and ReadyToRun code (fixed for general availability), an open garbage-collector hole through an interpreted stub (dotnet/runtime issue 136472, milestone 12.0.0), an interpreter defect with NaN values (dotnet/macios issue 26269), and a debugger crash with ReadyToRun (`avalonia_ui_layer.md` section 2.2, parts 0 and 1). The session checks their status first. If one affects Paneport, the migration document records it, and the code that runs interpreted is kept small.
- SteamKit2 is slower under the interpreter. The session compares the times with the L2 figures. If they are much worse, a later stage can evaluate SteamKit2's compile-time protobuf model, which the research saw in review as SteamKit pull request 1651.
- A new package needs registration first (C-0003 section 5). That is a contract change and can delay the stage.
- Branches of parallel stages conflict with the move. recelilious picks the merge time, and the pull request lists what open branches must do: rebase, retarget and regenerate their lock files.
- The footprint, size or start time changes. The migration document records them. If the memory governor of M6.3 is already Done and budgets for the shell, the session log proposes a follow-up that adjusts the budget.
- Visual Studio, Rider and Pair to Mac (C-0009 section 3) must support the .NET 11 SDK on Windows. Which versions do is to be confirmed in this stage.

**Hands over to.**

- L10: `main` on .NET 11 with CoreCLR, the app and the device test app built for `net11.0-ios`, and the runtime line on the diagnostics page.
- Every stage that starts after the merge: the .NET 11 SDK, targets and lock files. Branches that were open at the merge rebase onto it.
- Stages that are not Done at the merge: M9.1 and L9 get the release workflow on .NET 11, with the .NET runtime and .NET for iOS still in the notices (C-0009 section 4); M6.3 gets the shell's footprint on CoreCLR from the migration document; M7.1 and M8.2 get SteamKit2 under CoreCLR, with its sign-in and download times. Where such a stage is already Done and a figure changed, the session log proposes a follow-up.
- A later stage: the move away from .NET 11, a standard-term release that the research lists as supported until 2028-11-09 (`avalonia_ui_layer.md` section 2.2).

**Session log.**

_No sessions yet._
