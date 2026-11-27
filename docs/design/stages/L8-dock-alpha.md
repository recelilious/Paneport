# L8 Dock alpha

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L8 | Dock alpha | Large | Planned | | |
| M8.1 | Multiple pseudo-processes | Medium | Planned | | |
| S8.1.1 | State inventory and design | Small | Planned | | |
| S8.1.2 | PEB and TEB re-indexing | Small | Planned | | |
| S8.1.3 | POSIX state emulation | Small | Planned | | |
| S8.1.4 | CreateProcess path | Small | Planned | | |
| S8.1.5 | Cross-process handles and waits | Small | Planned | | |
| S8.1.6 | Helper policy | Small | Planned | | |
| S8.1.7 | Multi-process test programs | Small | Planned | | |
| M8.2 | Dock host | Medium | Planned | | |
| S8.2.1 | Design document and interface records | Small | Planned | | |
| S8.2.2 | Package downloader and pin verifier | Small | Planned | | |
| S8.2.3 | Dock host core | Small | Planned | | |
| S8.2.4 | Token channel | Small | Planned | | |
| S8.2.5 | Cache cleanup | Small | Planned | | |
| S8.2.6 | Steam service coordination | Small | Planned | | |
| S8.2.7 | CEG preparation | Small | Planned | | |
| M8.3 | Compatibility checks and reports | Medium | Done | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-27 |
| S8.3.1 | Validator | Small | Done | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-27 |
| S8.3.2 | Rule checks | Small | Done | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-27 |
| S8.3.3 | Rating computation | Small | Done | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-27 |
| S8.3.4 | Failing fixtures | Small | Done | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-27 |
| S8.3.5 | First entries | Small | Done: no title has been tested on the device yet, so there is no entry; M6.6 and S7.0.3 add the first ones | [#44](https://github.com/recelilious/Paneport/pull/44) | 2026-11-24 |
| M8.4 | AAA performance and launcher-service titles | Medium | Planned | | |
| S8.4.1 | MetalFX spatial upscaling and render scale | Small | Planned | | |
| S8.4.2 | Frame rate cap | Small | Planned | | |
| S8.4.3 | Per-title translator tuning | Small | Planned | | |
| S8.4.4 | Thermal governor and memory-limit use | Small | Planned | | |
| S8.4.5 | Launcher-service session under the helper policy | Small | Planned | | |
| S8.0.1 | Dock session launch wiring | Small | Planned | | |
| S8.0.2 | L8 evidence template and gate update | Small | Planned | | |

## L8 Dock alpha

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L7 |
| C-0012 gate | Phase 4, "Alpha with Dock" (C-0012 section 3) |
| Size | L (the integration session's own work; the Medium stages carry their own sizes) |
| Branch | `feat/l8-dock-alpha` (for the integration session) |
| Decisions needed | D6 (the SteamStub AAA title and the launcher-service title, from recelilious's own library) |

**Goal.** Reach the phase 4 gate of C-0012 section 3. A SteamStub-protected AAA game from the 2010s starts through the Dock host, which loads Valve's genuine `steamclient64.dll` so that Valve's own code performs the sign-in, the ownership check and the launch, and the game plays at 720p. A game that needs a resident launcher service gets in-game, with the launcher and the game running as pseudo-processes under the helper policy. Nothing in Paneport bypasses, removes or emulates any DRM (C-0001 section 7, C-0007 section 1): the DRM runs unchanged inside Valve's own client.

**What recelilious will see.** On the iPhone, a SteamStub-protected AAA game from the 2010s launched through the Dock host and playable at 720p, and a second title that needs a launcher service reaching gameplay with its launcher running beside it.

**Acceptance steps.** recelilious performs these on the iPhone. They check every criterion of the phase 4 gate in C-0012 section 3.

1. Install the L8 device build (the test app in `tests/device`, which shares the main bundle identifier under D12, plan section 7.3). Launch it and open the diagnostics screen. Expected result: the app installs and launches, and the diagnostics screen shows the native host and the memory limit from `pp_memory_get_budget` (C-0005 section 2). Record the fields C-0012 section 4 requires (build commit and version, device model identifier, iOS version and build, signing route, sideloading tool and version, the three entitlement values, the JIT enabler and version).
2. Enable JIT through StikDebug and wait for the JIT state to become `ready` (C-0006 section 5). Expected result: the state reaches `ready`, so a session may start (C-0005 section 2 rejects a session start with `PP_E_JIT_NOT_READY` otherwise).
3. Sign in to Steam with your own account and open the library. Expected result: the library lists the D6 SteamStub AAA title and the D6 launcher-service title, and both show the launch path `dock` (C-0008 section 2, C-0007 section 5).
4. Launch the SteamStub-protected AAA game. Expected result: Paneport.Steam pauses its downloads and signs off before the Dock host signs in (C-0007 section 4); the Dock host starts inside the Wine session, loads Valve's `steamclient64.dll`, signs in with the refresh token, Valve's client checks the license, and Valve's own launch function starts the title; the game reaches gameplay. This is criterion 1 (starts through the Dock host).
5. Play the AAA title at 720p for a sustained run and read the median frame rate from the in-app counter and the native host's frame-time counters (C-0011 section 1). Expected result: the median is at least 30 fps at 720p. This completes criterion 1.
6. Launch the game that needs a launcher service. Expected result: the launcher and the game run as pseudo-processes under the helper policy (M8.1, M8.4), and the title reaches gameplay. This is criterion 2 (a launcher-service title gets in-game).
7. During each session, watch the peak memory footprint against the memory limit the host read during that session (`pp_memory_get_budget`), and note whether Game Mode was active (C-0009 section 4, C-0005 section 2). Expected result: the peak footprint stays below the limit; record the limit value and the Game Mode state. This is criterion 3.
8. After each session ends, and again after signing out, confirm that Paneport deleted Valve's cached login state from the prefix, and record the list of files Valve's client wrote into the prefix, taken from the enumeration in the Dock host design document (M8.2) and confirmed on the device. Expected result: the recorded file list matches the design document, and the cached files are gone after the session ends and after sign-out (C-0007 section 3, C-0001 section 7 item 3). This is criterion 4.
9. Fill in the evidence record for each criterion and give the gate a verdict of Pass, Fail or Waived (C-0012 section 3). Only recelilious can waive it.

**Evidence record.** `docs/evidence/YYYY-MM-DD-l8-dock-alpha.md`, with the UTC date of the run in the file name (C-0012 section 4). It records: the gate (phase 4) and the build (commit hash and version); the device model identifier and the iOS version and build; the signing route (free or paid team), the sideloading tool and its version, and the installed values of `get-task-allow`, `com.apple.developer.kernel.increased-memory-limit` and `com.apple.developer.kernel.extended-virtual-addressing` (C-0009 section 4); the JIT enabler and its version, whether Game Mode was active, and the memory limit read during the run; for each criterion the steps, the expected and observed results, the metrics (median and 1% low frame rate, peak footprint, thermal state over time) and scrubbed logs; the list of files Valve's client wrote into the prefix; and the verdict, the tester as a project handle (`recelilious` or `Claude`, never a real name), and the date. It must never contain any personal data or secret listed in C-0012 section 4: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path. No Valve binary and no game file are ever attached (C-0001 section 7, C-0007 section 6).

**Integration session.** Once M8.1 to M8.4 are done, the L8 session wires their results together: it makes the library manager start a Dock session for a `dock` title (S8.0.1), connects the token channel (M8.2) and the helper policy (M8.1) to the session that M8.4 tunes, prepares the acceptance steps above and the evidence template (S8.0.2), and runs the checks that need everything together on the macOS harness (the Dock host against our stub DLL, plus the multi-process and launcher-service regressions). The full real-client path is verified only on recelilious's device, because Valve binaries never enter CI (plan section 7.6, C-0007 section 6). The session updates `docs/evidence/README.md` so the phase 4 row points at the new record (plan section 7.7).

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S8.0.1 | Dock session launch wiring | `src/Paneport.Core`, `src/Paneport.UI`, `src/native/host` | Logic and UI tests: a `dock` title starts a session that launches the Dock host first; a `direct` title still takes the direct path (C-0007 section 5) |
| S8.0.2 | L8 evidence template and gate update | `docs/evidence/` | Review checklist: the template carries every C-0012 section 4 field and the "never record" list, and the phase 4 row in `docs/evidence/README.md` is updated |

**Notes from earlier stages.**

- M8.3 (session log of 2026-11-23): the first title entries come with M6.6 and S7.0.3 rather than with M8.3, because no title had been tested on the device yet. The entries of the D6 titles that the gate uses pass `tools/CheckCompat.cs` in the `checks` job: a report names the gate's evidence record in the same change, and the rating counts only the reports of a release tag on a target device (`compat-db.md` sections 2.2 and 8.1).

**Session log.**

_No sessions yet._

## M8.1 Multiple pseudo-processes

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L8 |
| Depends on | M4.3 |
| Size | XL |
| Branch | `feat/m8-1-multiple-pseudo-processes` |
| Decisions needed | None |
| Design documents | `docs/design/pseudo-processes.md` (written in this stage) |

**Goal.** Let more than one Windows process live at once in the single iOS process, so that a title and the helper programs it starts run together as pseudo-processes (C-0004 section 1). M4.3 brought up one Windows process in single-process Wine; this stage makes `CreateProcess` build a second pseudo-process with its own wineserver connection, and makes cross-process handles, waits, pipes, shared memory and exit codes work between them. There is no crash isolation, and the stage documents that.

**Scope.**

- In: an inventory of the per-process state in Wine's Unix-side libraries and a re-indexing of that state by PEB or TEB; emulated POSIX state for environment, working directory, file descriptors, `exit` and `kill`, held per pseudo-process; a `CreateProcess` path that creates a pseudo-process with its own wineserver connection; cross-process handles, waits, pipes, shared memory and exit codes; the helper policy layer that reads `blockedExecutables` from the title's compatibility entry (C-0008 section 2) and refuses crash reporters and updaters, and forces Chromium-based helpers into single-process mode; our own multi-process Win32 test programs; the fork and exec trap kept on.
- Out: the Dock host itself (M8.2); performance tuning and launcher-service titles (M8.4); any crash isolation between pseudo-processes; running a second iOS process or app extension (C-0004 section 1 forbids child processes and reserves extensions for research).

**Read first.**

- C-0004 section 1 (one iOS process, pseudo-processes, one session per launch) and section 2 (the Wine fork owns the single-process wineserver and pseudo-processes, and the dependency directions).
- C-0005 section 3 (the host and Wine fork bridge) and section 4 (the dispatcher owns the fault signals; the fork registers its fault handler through the bridge and never installs its own).
- C-0001 section 6 (originality: no code copied from Madeira, Playport or the other named products) and section 7 (compliance).
- C-0010 section 4 (code style inside the forks) and C-0003 section 4 (fork changes stay LGPL in `external/Paneport-wine`).
- Research: `docs/research/process_and_memory_model.md` (per-process state, address-space conflicts, the helper classes) and `docs/research/wine_on_ios.md`.
- Design document: `docs/design/pseudo-processes.md`, written in this stage.

**Deliverables.**

- `docs/design/pseudo-processes.md`: the inventory of per-process Unix-side state, the re-indexing plan, the POSIX state model, the `CreateProcess` design, and a statement that a fault in any pseudo-process ends the whole app, with the helper policy as the mitigation.
- `external/Paneport-wine` (fork changes on `paneport/M8.1`, with the submodule pointer moved on the stage branch, plan section 3.3): per-process re-indexing of Unix-side globals, POSIX state emulation, the `CreateProcess` path with a per-process wineserver connection, cross-process handles, waits, pipes, shared memory and exit codes, and the helper policy layer.
- `tests/win32/`: multi-process test programs that we write and build with llvm-mingw for x86-64 (a parent and children, pipes, named events, shared memory, and a 25-child stress test), plan section 7.2.
- `tests/runtime/`: harness cases that run the multi-process programs and assert that no `fork`, `exec` or `posix_spawn` was called (plan section 7.1).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S8.1.1 | State inventory and design | `docs/design/pseudo-processes.md` | Review checklist: every per-process Unix-side global is listed with its re-indexing plan and its owning library |
| S8.1.2 | PEB and TEB re-indexing | `external/Paneport-wine` | Harness unit: two pseudo-processes report distinct PEB and TEB identity and distinct process ids |
| S8.1.3 | POSIX state emulation | `external/Paneport-wine` | Harness: environment, working directory, file descriptor table, `exit` and `kill` behave per pseudo-process, not process-wide |
| S8.1.4 | CreateProcess path | `external/Paneport-wine` | Harness: a parent starts a child pseudo-process with its own wineserver connection, with no `fork` or `exec` |
| S8.1.5 | Cross-process handles and waits | `external/Paneport-wine` | A `tests/win32` program shows a pipe, a named event, a shared-memory mapping and a wait on the child's exit code working across pseudo-processes |
| S8.1.6 | Helper policy | `external/Paneport-wine` | Unit: a program named in `blockedExecutables` is refused, a crash reporter and an updater are refused, and a Chromium-based helper is forced into single-process mode |
| S8.1.7 | Multi-process test programs | `tests/win32` | The programs build with llvm-mingw and run under the harness; the 25-child stress test completes |

**Verification.** All automated and runnable without the iPhone, on the macOS `runtime` CI job (C-0011 section 4, added to C-0011 in M4.2 per plan section 8). This stage creates the `tests/runtime` multi-process harness cases and the `tests/win32` multi-process programs. Pass criteria: every multi-process program exits with code 0 and the expected output; the 25-child stress test holds 25 live child pseudo-processes and each reaches its expected exit code; the harness fails if any `fork`, `exec` or `posix_spawn` is observed (plan section 7.1); the AddressSanitizer-with-UndefinedBehaviorSanitizer and ThreadSanitizer builds are clean (C-0011 section 1).

**Done when.** The deliverables are merged, the verification passes in CI, `docs/design/pseudo-processes.md` exists (C-0012 section 1 item 8), and the change meets the definition of done in C-0012 section 1.

**Risks and fallbacks.** The list of per-process Unix-side globals is a long tail and some of it surfaces only under a real title; the design document keeps the inventory and the harness grows a case for each one found. There is no crash isolation, so one faulting pseudo-process ends the app; the helper policy removes the common crash sources (crash reporters, updaters), and this is documented, not solved, here. A fixed-base x64 image can have only one live instance at a time; this stage records the limit, and M8.4 keeps launcher-service titles within it.

**Hands over to.** M8.2 (the Dock host runs as a pseudo-process and starts the game as another), M8.4 (launcher-service titles run a launcher and the game as pseudo-processes under the helper policy), and the L8 integration session.

**Notes from earlier stages.**

- M4.3 ([L4 brief](L4-first-windows-program.md), session logs of 2026-11-24 and 2026-11-25): [single-process-wine.md](../single-process-wine.md) section 11 lists the process-wide state of ntdll's Unix side, of the fork's `paneport.c`, which holds one launch, and what Wine sets in the host's process, among it the current folder, which each listing of a folder changes, and the scheduling policies of the guest's threads. `NtCreateUserProcess` still forks, which the harness's trap refuses and names, as in `fork from ntdll.so!spawn_process+0xd4`; the wineserver's own descriptors, those that it opens and those that it receives from clients, lack close-on-exec, while the copies that ntdll receives have it; each guest thread keeps a send right on its own Mach port for the life of the app; and a process runs one wineserver and one session (sections 4, 10 and 11).

**Session log.**

_No sessions yet._

## M8.2 Dock host

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L8 |
| Depends on | M8.1, M7.1, M7.2 |
| Size | XL |
| Branch | `feat/m8-2-dock-host` |
| Decisions needed | None (the device check uses the D6 titles) |
| Design documents | `docs/design/dock-host.md` (written in this stage) |

**Goal.** Build the headless Dock host so that games needing a running Steam client can start while Valve's own code performs every ownership and DRM check (C-0007 sections 3, 5 and 6, C-0001 section 7). The Dock host loads Valve's genuine `steamclient64.dll`, signs in with the refresh token taken once through the bridge, checks the license, prepares CEG executables through Valve's client, and calls Valve's own launch function. Paneport touches neither the game files nor the DRM.

**Scope.**

- In: a design document that records the interface basis for each client build, written from permitted public sources only, with no disassembly and no decompiled code; a package downloader that fetches Valve's client packages on the device from Valve's official client update servers; the pin table `src/guest/dockhost/client-pins.json` keyed by SHA-256 and size, failing closed on an unknown build; `dockhost.exe`, a C17 x86-64 program built with llvm-mingw, that loads `steamclient64.dll` with `LoadLibraryExW`, signs in, checks the license, prepares CEG executables and calls Valve's launch function; the token channel through `pp_steam_set_token`, the bridge take-once entry and a Wine Unix call, with the host's copy zeroed after it is read or the session ends; deletion of Valve's cached login state at every app launch, at the end of a Dock session and at sign-out, with the file list enumerated; Paneport.Steam pausing its downloads and signing off while the Dock host is signed in, and reconnecting at session end.
- Out: loading `steam.exe` or the Steam web helper (never, C-0007 section 6); 32-bit titles that use the Steam API (unsupported at first, C-0007 section 6); the Steam overlay, Workshop and family sharing (C-0007 section 7); any Valve binary in CI (plan section 7.6); downloading or writing game content (the Dock host never does, C-0007 section 4).

**Read first.**

- C-0007 section 3 (sign-in, the refresh token, the token path through `pp_steam_set_token` and the bridge, deletion of Valve's cached login state), section 5 (classification and the Dock launch path, including CEG preparation) and section 6 (Dock host rules: pins, fail closed, interface definitions written by us, the reading ban on the named reference products, never `steam.exe` or the web helper, 32-bit Steam API unsupported).
- C-0005 section 2 (`pp_steam_set_token`, the in-memory token and its zeroing) and section 3 (the bridge take-once entry).
- C-0004 section 2 (the Dock host component and its dependency rule: it reaches the native host only through a call the Wine fork forwards over the bridge) and section 4 (the prefix path and the `runtime/` folder for downloaded client packages).
- C-0003 section 5 (vendor SDK headers and decompiled code forbidden), section 7 (reference-only products, including madeira-dock, Madeira and SteamRE/open-steamworks) and section 8 (Valve client components downloaded at run time, never committed).
- C-0001 section 6 (originality) and section 7 (compliance red lines).
- C-0009 section 4 (the `dockhost/` folder in the app bundle).
- Research: `docs/research/steam_launch_single_process.md` and `docs/research/steam_integration.md`.
- Design document: `docs/design/dock-host.md`, written in this stage.

**Deliverables.**

- `docs/design/dock-host.md`: the interface basis for each supported client build (every interface slot and signature, and how it was established from permitted public sources), the sign-in, license, CEG and launch call sequence, the enumerated list of files Valve's client writes into the prefix, and the statement that nobody who wrote it read the source of madeira-dock, Madeira or their forks, SteamRE/open-steamworks or the Steamworks SDK headers (C-0007 section 6, C-0003 sections 5 and 7). It contains no disassembly.
- `src/guest/dockhost/`: the `dockhost.exe` source in C17, and `client-pins.json` with the SHA-256 and size of each supported client build.
- `src/native/host/` and `src/native/include/paneport/wine_bridge.h`: the host side of the token channel (the in-memory token, the bridge take-once entry, zeroing) and the session-end cache cleanup hook.
- `src/Paneport.Interop/`: the `pp_steam_set_token` binding.
- `src/Paneport.Steam/`: pausing downloads and signing off before the Dock host signs in and reconnecting afterward, and the cache deletion at app launch and at sign-out.
- `src/Paneport.Core/`: the on-device package download and pin verification for the client packages placed in `Library/Application Support/Paneport/runtime/` (C-0004 section 4).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S8.2.1 | Design document and interface records | `docs/design/dock-host.md` | Review checklist: the interface basis per build is recorded from permitted sources only, with no disassembly and no code from the reference products (C-0007 section 6, C-0003 sections 5 and 7) |
| S8.2.2 | Package downloader and pin verifier | `src/Paneport.Core`, `src/guest/dockhost/client-pins.json` | Unit with fake packages: a package matching the pinned SHA-256 and size is accepted, any mismatch or unknown build is refused (fail closed) |
| S8.2.3 | Dock host core | `src/guest/dockhost` | Harness: `dockhost.exe`, built with llvm-mingw, loads a stub DLL we write that exports only test entry points, runs the sign-in, license and launch sequence against it, and refuses an unknown pin |
| S8.2.4 | Token channel | `src/native/host`, `src/native/include/paneport/wine_bridge.h`, `src/Paneport.Interop` | Unit: the take-once entry yields the token exactly once, the host copy reads as zero afterward, and the token never appears in a log line, a command-line argument, an environment variable or a registry value (C-0005 section 2) |
| S8.2.5 | Cache cleanup | `src/Paneport.Steam`, `src/native/host` | Unit on a fake prefix: planted login-cache files are deleted at app launch, at session end and at sign-out (C-0007 section 3) |
| S8.2.6 | Steam service coordination | `src/Paneport.Steam` | Unit with fakes: downloads pause and the service signs off before the Dock host signs in, and reconnect when the session ends (C-0007 section 4) |
| S8.2.7 | CEG preparation | `src/guest/dockhost` | Harness against the stub: the CEG-preparation request is issued through the client interface and the stub records it; real CEG is a device check |

**Verification.** Automated and runnable without the iPhone. The `dotnet` job runs the Paneport.Core, Paneport.Steam and Paneport.Interop unit tests with hand-written fakes (C-0011 section 1). The `runtime` job builds `dockhost.exe` with llvm-mingw and runs it against a stub `steamclient64`-shaped DLL that we write, which exports only test entry points and carries no Valve interface definitions copied from anywhere (C-0001 section 6, C-0003 section 5). This stage creates: the fake client packages and the pin-verifier tests, the token-zeroing tests, the cache-cleanup tests on a fake prefix, the Steam-coordination tests, and the stub DLL and its harness case. Pass criteria: the pin verifier accepts the matching fake package and fails closed on any mismatch or unknown build; the token is readable once and then zero; the planted cache files are removed at launch, session end and sign-out; `dockhost.exe` loads the stub, runs the call sequence and refuses an unknown pin; and no Valve binary is present anywhere in CI (plan section 7.6). The real `steamclient64.dll` runs only on recelilious's device (a device check), recorded in the L8 evidence, together with the confirmed list of files Valve's client wrote into the prefix. Adding a client build to `client-pins.json` is a reviewed change with its own evidence record of a Dock launch on that build (C-0007 section 6).

**Done when.** The deliverables are merged, the automated verification passes in CI, `docs/design/dock-host.md` exists and meets C-0007 section 6, and the change meets the definition of done in C-0012 section 1. The real-client path is marked "not yet verified on device" in the pull request until the L8 evidence record exists (C-0012 section 1 item 9).

**Risks and fallbacks.** The client interface layout is undocumented and Valve can change it on any client update, so the Dock host is pinned per build and fails closed on an unknown one; the design document must establish each build's interface basis from permitted public sources only. The exact client-update download endpoint and package file names, the exact set of files Valve's client writes into the prefix, and the session-root arrangement that launches `dockhost.exe` are to be confirmed in this stage and recorded in `docs/design/dock-host.md`. Whether Paneport.Steam and the Dock host can both stay signed in at once is to be confirmed by a device test and then by a change to C-0007 (C-0007 section 4); the default remains pause and sign-off. If the Dock approach fails on the device, recelilious decides on a fallback and C-0007 and C-0004 change first (C-0007 section 2).

**Hands over to.** M8.4 (performance tuning and launcher-service titles run through the Dock session) and the L8 integration session and its phase 4 gate.

**Notes from earlier stages.**

- S9.1.1 ([L9 brief](L9-first-release.md), M9.1 session log of 2026-11-19): every file that this stage adds to the bundle needs a rule in `eng/release/bundle-components.json`, or the `ios` job warns, and a rule for a new component needs its texts there, such as a fork's license files as `{ "repository": "external/..." }`, or the job fails; a new NuGet package of the app needs a row of `eng/license-map.json` and texts too. `licenses/` at the root of the bundle belongs to the notices generator (`docs/design/release-engineering.md` sections 1 and 5).

**Session log.**

_No sessions yet._

## M8.3 Compatibility checks and reports

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L8 |
| Depends on | M6.4; D8 |
| Size | M |
| Branch | `feat/m8-3-compatibility-checks` |
| Decisions needed | D8 (whether the JsonSchema.Net license terms are acceptable; if not, another validator is chosen, C-0011 section 5). D8, decided 2026-11-23: a validator of our own; C-0003 1.10 and C-0011 1.5 record it ([#49](https://github.com/recelilious/Paneport/pull/49)) |
| Design documents | None new; extends the compatibility database core from M6.4 |

**Goal.** Add the CI compatibility check that enforces C-0008 and the reports that link evidence records, so that every title file in `compat/titles/` is validated and the launch, allow-list and rating rules hold before a change merges (C-0008 section 5, C-0011 section 1).

**Scope.**

- In: the validator, built on JsonSchema.Net or an alternative chosen under D8 (C-0011 section 5; D8 chose a validator of our own, in `tools/CheckCompat.cs`), that validates every `compat/titles/*.json` against the title schema; the rule checks of C-0008 section 5 (file names match the `id`, and the launch and allow-list rules); the rating computation of C-0008 section 3 using `targetDevices`; failing fixtures that each break exactly one rule; reports that link evidence records; adding the check to the `checks` CI job; and the first title entries for the titles tested so far.
- Out: changing the title schema or adding fields (that belongs with M6.4 or a schema change under C-0008 section 5); device testing of titles (that is the L8 gate and M8.4); the `ios-builds.json` schema check (M6.4 and C-0006 section 6).

**Read first.**

- C-0008 section 2 (the fields), section 3 (ratings and the `targetDevices` computation) and section 5 (the launch, allow-list, file-name and CI rules).
- C-0011 section 1 (the compatibility-data test level), section 4 (the `checks` job on Ubuntu) and section 5 (JsonSchema.Net registered as under review until D8). Since C-0011 1.5 ([#49](https://github.com/recelilious/Paneport/pull/49)), section 5 says that the compatibility check uses no schema library.
- C-0003 section 6 (the register entry for JsonSchema.Net and its license terms). C-0003 1.10 ([#49](https://github.com/recelilious/Paneport/pull/49)) dropped that entry, since Paneport does not use the package.
- C-0007 section 5 (the classification the `drm` and `launch` fields record).
- Research: none specific; the schema and loader from M6.4 are the basis.

**Deliverables.**

- `tools/`: the C# file-based compatibility validator that implements C-0008 section 5 and the rating rule of C-0008 section 3, run with `dotnet run`.
- `tests/`: failing fixtures, each breaking exactly one rule (a bad schema value, a file name that does not match its `id`, an illegal `launch` value, a forbidden allow-list entry, a `rating` that differs from the computed one).
- `.github/workflows/`: the compatibility check added to the `checks` job (C-0011 section 4).
- `compat/titles/`: the first entries for the titles tested so far, each linking its evidence record.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S8.3.1 | Validator | `tools/` | Unit: a valid title file passes schema validation against `compat/schema/title.schema.json` |
| S8.3.2 | Rule checks | `tools/` | Unit: the file-name, launch and allow-list rules of C-0008 section 5 are enforced, including refusing a `local-` file that shows a Steam marker |
| S8.3.3 | Rating computation | `tools/` | Unit: the rating is computed from the reports using `targetDevices` (C-0008 section 3), and a file whose `rating` differs is rejected |
| S8.3.4 | Failing fixtures | `tests/` | Each fixture fails the check for exactly one rule and for no other |
| S8.3.5 | First entries | `compat/titles/` | The entries validate, the file names match their `id`, and each report links an evidence record |

**Verification.** Automated and runnable without the iPhone, on the `checks` CI job (Ubuntu, C-0011 section 4). This stage creates the validator and the failing fixtures. Pass criteria: the job passes on the valid set (every `compat/titles/*.json` is schema-valid, each file name matches its `id`, the launch and allow-list rules hold, and each `rating` equals the computed result) and fails on each failing fixture, with the failure naming the one rule that fixture breaks. The check uses only synthetic data; no game file or Valve binary is committed (C-0011 section 1, plan section 7.6).

**Done when.** The validator and fixtures are merged, the `checks` job runs the compatibility check and passes on the valid set and fails on every fixture, D8 is recorded (and the chosen validator's license is registered under C-0003), and the change meets the definition of done in C-0012 section 1.

**Risks and fallbacks.** D8 may rule the JsonSchema.Net NuGet terms unacceptable; then the validator uses a library whose package is under an allowed license, chosen under C-0011 section 5, and C-0003 is updated (plan section 8). D8 chose a validator of our own instead, on 2026-11-23, and C-0003 1.10 and C-0011 1.5 record it ([#49](https://github.com/recelilious/Paneport/pull/49)). The rating rule in C-0008 section 3 is precise about ties (the lower rating wins when target devices disagree on the highest release); the fixtures must cover that case.

**Hands over to.** The L8 integration session (the first title entries back the gate) and every later title entry and report.

**Notes from earlier stages.**

- M6.4 ([L6 brief](L6-2d-game.md#m64-compatibility-database-core), session log of 2026-10-31): `compat-db.md` section 9 lists where the schema and the app's loader differ: `512.0` is an integer to JSON Schema, `format: date` asserts only in validators that assert formats, and `$` in .NET patterns also matches before a final line break. The models in Paneport.Core and `$defs/targetDevices` are ready for the rating check.

**Session log.**

- **2026-11-23 (session 1).** The title check, on #40's head `9f4d3e4`, before #40 merged.
  - **Done.**
    - D8: asked "For D8, use our own small JSON Schema validator instead of JsonSchema.Net?", recelilious answered "Use our own" on 2026-11-23. Plan section 4.2 records it, and plan section 8 the contract changes it brings.
    - `tools/CheckCompat.cs`, which M3.1 wrote for `compat/ios-builds.json`, also checks every title file of `compat/titles/` (S8.3.1 to S8.3.3). It reads each file as the app's reader does, validates it against the title schema with the validator, which gains the keywords that the title schema uses and gives each pattern its ECMA-262 meaning, and checks in code the file name, the launch and allow-list rules, the rating and the evidence records ([compat-db.md](../compat-db.md) sections 2.2, 8.1 and 9).
    - `tests/compat/` (S8.3.4): 9 valid title files and 36 fixtures that each fail exactly one rule.
    - S8.3.5: no title has been tested on the device yet, so `compat/titles/` holds no entry. M6.6 and S7.0.3 add the first ones; the L6 and L7 briefs and this brief's notes for L8 say what the check asks of them.
    - compat-db.md sections 2.2, 8.1 and 9, the READMEs of `compat/titles/`, `tests/` and `tests/compat/`, and two test comments of M6.4 that waited for M8.3.
  - **Verification.** On the workstation: the self-test, 379 cases after the review's fixes; the check of the repository; `dotnet format whitespace tools`; the personal-data scan; and the license check. Each of 25 mutants of the check fails a case. An independent review of the tool found no serious defect and seven small ones. Six are fixed in `97ed608`, which 12 more mutants pin; the seventh, R40IV1, is in the reading of `compat/ios-builds.json`, which #40 owned.
  - **Decisions.**
    - The rating ([compat-db.md](../compat-db.md) section 2.2): for each target device its latest report on the highest release counts, the one with the latest date and, of two on one date, the later in the list; the lowest of those wins. A pre-release tag counts as a release tag, ordered by Semantic Versioning 2.0.0. Each model identifier counts as a device, so the two identifiers of the iPhone 18 Pro Max count as two, and where they disagree the lower rating wins.
    - The check does not use Paneport.Core, though C-0004 section 2 would allow it, so that it and the app's reader check each other ([compat-db.md](../compat-db.md) section 8.1).
  - **Left.** The contract draft for D8 and `tests/compat/`, on the local branch `contract/m8-3-own-validator`, which recelilious reviews: C-0003 1.10, C-0011 1.4 and C-0004 1.3.
- **2026-11-24 (session 2).** Pull request [#44](https://github.com/recelilious/Paneport/pull/44), rebased onto main `301471e`, which holds [#40](https://github.com/recelilious/Paneport/pull/40) and [#43](https://github.com/recelilious/Paneport/pull/43).
  - **Done.**
    - The rebase keeps #40's rules of the validator and its 60 cases. Each schema has a vocabulary, the keywords and annotations that it uses now, and may use no other: the list's schema still refuses `enum` and `$comment`, which the title schema uses. A case checks that each schema uses all of its vocabulary and that the validator implements nothing that no schema uses. The title schema loads through #40's loader, and `$comment` must be a string. The rebase merged #40's two fix rounds into the first and third commits, so no commit drops one of its rules.
    - R40IV1, which the coordinator gave this stage: `compat/ios-builds.json` is read through `JsonText.Read`, as the title files are, so half a surrogate pair no longer crashes the tool, and a syntax error is named by its line and byte, never by the file's text (`3577c5c`).
    - The documents that #40 wrote for the check: tools/README.md, [repository-checks.md](../repository-checks.md), [jit.md](../jit.md) section 9, the READMEs of `compat/`, `compat/schema/` and `.github/workflows/`, and the comments of ci.yml. [compat-db.md](../compat-db.md) section 8.1 says that the validator is #40's, with a vocabulary for each schema.
    - S8.3.1, S8.3.2, S8.3.3 and S8.3.5 are Done.
  - **Verification.** On the workstation, the self-test passes 427 cases, the check of the repository passes, and `dotnet format whitespace tools`, the personal-data scan and the license check are clean. Each of 7 mutants of the merge with #40's rules and 2 of R40IV1's fix fails a case. The runs of CI on the final head are in [#44](https://github.com/recelilious/Paneport/pull/44).
  - **Decisions.** The coordinator decided that R40IV1 goes in this pull request, and that counting each model identifier as a device stays as documented, for recelilious to see.
  - **Left.**
    - The contract draft of session 1. The merge needs the whole draft: it records D8 in C-0003 and C-0011, as plan section 8 and the Done-when list expect, and adds `tests/compat/` to C-0004's tree. S8.3.4 is Blocked and M8.3 In progress until it is accepted.
    - The `paneportVersion` pattern of the title schema accepts `v1.0.0-01`, which is no release tag; the check does not count such a report. A later change of the schema can close it.
  - **For later sessions.** The notes for M6.6, S7.0.3 and L8 from session 1 stand.
- **2026-11-25 (session 3).** Fix round 1 of the independent review of [#44](https://github.com/recelilious/Paneport/pull/44), on the same base `301471e`. Main has moved on to `4ad40c8` with [#38](https://github.com/recelilious/Paneport/pull/38), which touches neither the tool nor `compat/`, and the pull request does not conflict with it.
  - **Done.**
    - The rating counts the four target devices of C-0001 section 2, as C-0008 section 3 does, not model identifiers. The check names a report's device through a map of the table of [compat-db.md](../compat-db.md) section 2.1, so `iPhone19,3` and `iPhone19,7` are one device, the iPhone 18 Pro Max, whose latest report on the release counts. This replaces the counting in session 1's Decisions. Two cases compare the map with the table and with the schema.
    - Valid fixtures that tell the rating rule apart: the lower device reporting first, a later report listed first, both identifiers of the iPhone 18 Pro Max, and a device written in another letter case, which does not count.
    - The launch rule: two invalid fixtures, a Direct title with `steamApi` true and a local title with `steamStub` true, and four valid Dock titles, three whose `drm` closes the Direct path in each way and one without `drm`.
    - The validator:
      - An exclusive minimum is compared also against the double that the reader reads, so `1e-400` fails it.
      - An escaped hyphen in a class may start a range, as in ECMA-262.
      - The exponent of a number is clamped, so that a huge one cannot overflow.
      - A pattern that does not finish within 1 s fails `pattern` with its file and pointer.
      - `uniqueItems` compares numbers, `true`, `false` and `null` through sets.
      - A schema that escapes half of a surrogate pair, or whose reference leads back to itself without a step into the value, is refused with exit 2.
      - Patterns are read by UTF-16 code units, as ECMA-262 reads them without the `u` flag, and the documents say so.
    - The rules in code test the type of each value that they read. A schema that drops a type then makes the rule fail the value instead of throwing, an allow-list of `true` fails `allow-list`, and a target device that is not a text is refused.
    - More cases:
      - each Steam variable of C-0008 section 5, and `steamappid`, in the environment allow-list;
      - the release tags `v1.0.0-0a`, `v1.0.0-RC.1`, `v2.1.0` and `v2.1.1`;
      - the missing record in a second report;
      - a `readme.md`, and a file of neither name form;
      - the size limit at 1 MiB, and two byte-order marks;
      - the 64 characters of a plain name;
      - the refusals of the loader below `anyOf`, `oneOf`, `if`, `then` and `else`, and the forms of keywords that no schema uses.
    - The documents:
      - [compat-db.md](../compat-db.md) sections 2.2, 8.1, 9 and 11;
      - [repository-checks.md](../repository-checks.md) sections 6 and 9;
      - [jit.md](../jit.md) section 13 and the design index;
      - the READMEs of `compat/titles/` and `tests/compat/`, the L7 note and a comment of ci.yml;
      - why the merge waits for the whole contract draft.
  - **Verification.** On the workstation:
    - The self-test passes 572 cases, and the check of the repository passes.
    - `dotnet format whitespace tools`, the personal-data scan and the license check are clean.
    - Each of 63 mutants of this round fails a case or crashes the self-test. The pull request lists them.
    - The 25 and 12 mutants of session 1 were not kept, so they are not listed.
  - **Decisions.** The coordinator decided fix round 1:
    - The rating counts the four devices, as the contract does, so how devices count is no longer a question for recelilious.
    - The reading that a device's latest report on the release replaces its earlier ones goes to recelilious, with the recommendation to accept it.
  - **Left.** The contract draft of session 1 and the latest-report reading wait for recelilious.
- **2026-11-26 (session 4).** Fix round 2 of [#44](https://github.com/recelilious/Paneport/pull/44), after the fix check of round 1, which found no medium or high defect and 18 low ones. Rebased from `301471e` onto main `bb29223`, which adds [#38](https://github.com/recelilious/Paneport/pull/38), [#45](https://github.com/recelilious/Paneport/pull/45), [#46](https://github.com/recelilious/Paneport/pull/46), [#47](https://github.com/recelilious/Paneport/pull/47), [#49](https://github.com/recelilious/Paneport/pull/49), [#50](https://github.com/recelilious/Paneport/pull/50) and [#39](https://github.com/recelilious/Paneport/pull/39).
  - **Done.**
    - recelilious approved the contract draft on 2026-11-26; it merged as [#49](https://github.com/recelilious/Paneport/pull/49) (`17aec4d`), so C-0003 1.10, C-0004 1.3 and C-0011 1.5 are in force. recelilious accepted the reading on 2026-11-26: on the highest release, a device's newer report replaces its older ones ([compat-db.md](../compat-db.md) section 2.2).
    - The rebase conflicted only in `.github/workflows/README.md`, which keeps #39's rows of `native`, `ios` and `runtime` and this pull request's row of `checks`. [repository-checks.md](../repository-checks.md) section 6 still matches the `checks` job step by step.
    - The validator:
      - `uniqueItems`, `const` and `enum` compare through a key of each value. A nested number whose exponent is beyond an int no longer crashes the check, and a 1 MiB list of 128,841 distinct one-item lists takes 1.5 s instead of 327 s.
      - A schema of `true` or `false` loads again.
      - A pattern that times out below `not`, `anyOf`, `oneOf` or `if` names the value's pointer and its place in the schema.
      - Half of a surrogate pair in a schema names its place in full.
      - `\-` outside a class is refused, as the `u` flag refuses it.
    - The launch rule also tests the type of `executable`.
    - Fixtures: three invalid Direct titles, with `ceg` `unknown`, without `steamApi` and without `steamStub`; and four valid titles: Dock titles with `ceg` `yes` and without `ceg`, a local title with `ceg` `unknown` and no `launch`, and a Direct title with `steamAppIdFile` true. The app's reader loads the four and rejects each of the three with `Launch` alone.
    - More cases:
      - cycles through `anyOf`, `oneOf`, `if`, `then` and `else`, and definitions shared without a cycle;
      - a timeout below `not`, `anyOf`, `oneOf` and `if`, in `properties` and `items`, and a problem found before a timeout;
      - `true` and `false`, numbers by their exact value, and nested values under `uniqueItems`;
      - an escaped hyphen inside a class, and the year 0000;
      - each type test of the rules in code;
      - a walk of both schemas that finds none of the validator's stricter readings below `not`, in an `if` or in a `oneOf` branch, and finds those planted in a copy of each.
    - The documents: [compat-db.md](../compat-db.md) sections 2.2, 8.1 and 9, [repository-checks.md](../repository-checks.md) section 9, [jit.md](../jit.md) section 9, the READMEs of `tools/` and `tests/compat/`, and M8.3's section of this brief, which #49 made stale.
    - S8.3.4 and M8.3 are Done: only the merge is left.
  - **Verification.** On the workstation:
    - The self-test passes 621 cases, and the check of the repository passes.
    - `dotnet format whitespace tools`, the personal-data scan and the license check are clean.
    - Every mutant of fix rounds 1 and 2, 98 in all, was run in a copy that keeps `Directory.Build.props` and `.editorconfig`, so that each builds as CI builds the tool. 96 fail a case and 2 crash the self-test; none survives or fails to build. 11 of round 1's 63 were written again for the same fault, because this round changed their code or because they did not build so. The pull request lists them.
    - The runs of CI on the final head are in [#44](https://github.com/recelilious/Paneport/pull/44).
  - **Decisions.** The coordinator decided fix round 2, taking each skeptic's fix of the fix check.
  - **Left.** The merge.
- **2026-11-27 (session 5).** Fix round 3 of [#44](https://github.com/recelilious/Paneport/pull/44), after the fix check of round 2, which found no medium or high defect and 12 low ones, and one item of round 2 that held only in part. Main is still `bb29223`, so there was no rebase.
  - **Done.**
    - The pattern translation refuses what the `u` flag refuses and ECMA-262 takes without it: a `]` outside a class, a `{` or `}` that is no part of a quantifier, and a quantifier after a lookahead. It also refuses a quantifier after `^` or `$`, which .NET takes and ECMA-262 refuses. [compat-db.md](../compat-db.md) section 9's sentence on the `u` flag and the validator's comment then hold as written. Neither schema has such a pattern: both load in the self-test.
    - More cases:
      - each part of a value's key under `uniqueItems`: the count of a list's items and of an object's members, the letter case of a member name, a number's sign, `null` apart from `false`, and the ordinal sort of the members;
      - the walk of the stricter readings through `additionalProperties`, `then`, `else`, `allOf` and `anyOf`, planted in the title schema's copy;
      - the segment rule of `JsonText.Scan`: half a pair in a `$comment` below `properties` and below `allOf`, named in full, and a member twice whose name is not plain, read as the data is, named `/<name not shown>`;
      - a timeout below `allOf`, `then` and `else`, so that the cases place it directly, in `properties` and `items`, and below `not`, `anyOf`, `oneOf`, `allOf`, `if`, `then` and `else`;
      - a report whose date is a number, in a title without a rating;
      - each refusal of the translation, and the quantifiers `{n,}` and `{n,m}`;
      - an empty class before another class, and a property escape inside a class, which only the refusal of an empty class and of `\p` now refuse.
    - A valid local title, `local-fixture-shoal`, whose `drm` has no `ceg` and sets `steamAppIdFile` true, with no `launch`. The app's reader loads it, and without its `executable` rejects it.
    - The documents: [compat-db.md](../compat-db.md) section 8.1, [repository-checks.md](../repository-checks.md) section 9, the README of `tests/compat/`, and session 4's entry above, whose timeout cases it names exactly.
  - **Verification.** On the workstation:
    - The self-test passes 648 cases, and the check of the repository passes.
    - `dotnet format whitespace tools`, the personal-data scan and the license check are clean.
    - A fuzz of the translation against node 24.19.0: of 150,000 random patterns, each that the translation takes compiles with the `u` flag, matches as node matches it without the flag on 60 texts, and on those without half of a surrogate pair matches alike with it.
    - Every mutant of fix rounds 1 to 3, 138 in all, was run in a copy that keeps `Directory.Build.props` and `.editorconfig`, so that each builds as CI builds the tool. 133 fail a case, 2 crash the self-test, and 3 stop it with exit 2 at a pattern that they refuse wrongly; none survives or fails to build. Round 1's mutant that drops the refusal of an empty class survived the first run, since the refusal of a `]` outside a class now refuses its cases too; the case `[][a]` kills it. The pull request lists them.
    - The refusal of a `[` inside a class is redundant since this round: .NET reads a `[` in a class otherwise than ECMA-262 only in a subtraction, which ends in `]]`, and the second `]` is now refused. The fuzz, with that refusal dropped, finds no difference in 270,000 patterns, so no case can catch its removal; with the refusal of an empty class dropped instead, it finds eight. It stays, for its clearer message.
    - The runs of CI on the final head are in [#44](https://github.com/recelilious/Paneport/pull/44).
  - **Decisions.** The coordinator decided fix round 3, refusing the patterns that the `u` flag refuses, as round 2 refused `\-`. The refusal of a quantifier after `^` or `$`, which the fuzz found, goes beyond the decisions, and the coordinator checks it with the round.
  - **Left.** The merge.

## M8.4 AAA performance and launcher-service titles

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L8 |
| Depends on | M8.1, M8.2, M6.3; D6 |
| Size | L |
| Branch | `feat/m8-4-aaa-performance` |
| Decisions needed | D6 (the SteamStub AAA title and the launcher-service title) |
| Design documents | None new; uses the graphics, governor and pseudo-process designs from M5.2, M6.3 and M8.1 |

**Goal.** Make a SteamStub AAA game from the 2010s playable at 720p through the Dock host, and make a launcher-service title reach gameplay. This stage adds MetalFX spatial upscaling, render scale and frame caps through DXMT, per-title translator tuning, the thermal governor's use of the memory limit, and launcher-service sessions that run a launcher and the game as pseudo-processes under the helper policy, then measures the result.

**Scope.**

- In: applying `graphics.renderScale`, `graphics.metalFx` and `graphics.frameRateCap` from the title's compatibility entry through DXMT and the display bridge (C-0008 section 2); per-title translator options from the entry's `translator` allow-list (C-0008 section 2) passed into the session configuration; the thermal and memory governor using `pp_memory_get_budget` and the thermal state events (C-0005 section 2, C-0004 section 2); launcher-service titles running a launcher and the game as pseudo-processes under the helper policy (M8.1); and the measurements that feed the L8 evidence.
- Out: the gate run itself and its verdict (the L8 integration session and recelilious); new graphics features beyond what DXMT and MetalFX already provide; MetalFX frame interpolation unless its availability is confirmed in this stage (the baseline is spatial upscaling); any Valve binary in CI (plan section 7.6).

**Read first.**

- C-0008 section 2 (the `graphics`, `translator`, `memoryEstimateMiB` and `helperPolicy` fields and their allow-lists).
- C-0005 section 2 (the memory limit and the Game Mode level from `pp_memory_get_budget`, and the thermal-state and memory-pressure events).
- C-0004 section 1 (pseudo-processes and one session per launch) and section 2 (the native host's memory and thermal governor).
- C-0001 section 7 and C-0007 section 1 (no DRM is touched; the governor and the graphics settings never change game files).
- Research: `docs/research/graphics_directx_to_metal.md` (DXMT and MetalFX, spatial upscaling from iOS 16, frame interpolation from iOS 26), `docs/research/precedents_and_ux_layer.md` (MetalFX at present time on the final image), `docs/research/process_and_memory_model.md` (launcher-service classes, the memory budget and the file-backed tier), and `docs/research/x86_to_arm_translation.md` (translator tuning and the memory-ordering cost on iOS).

**Deliverables.**

- `external/Paneport-dxmt` (fork changes on `paneport/M8.4`, submodule pointer moved on the stage branch) and `src/native/host/`: applying render scale, MetalFX spatial upscaling and the frame-rate cap from the compatibility entry.
- `src/native/host/`: the translator-option plumbing from the session configuration (the `translator` allow-list), and the thermal and memory governor's use of the memory limit, including refusing a title whose `memoryEstimateMiB` exceeds the current budget.
- `external/Paneport-wine` and `src/Paneport.Core/`: launcher-service sessions that run a launcher and the game as pseudo-processes under the helper policy (built on M8.1).
- `tests/runtime/` and `tests/win32/`: harness cases for the graphics settings, the translator options, the governor logic and the launcher-service session, plus the regression runs of the earlier Win32 and triangle tests.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S8.4.1 | MetalFX spatial upscaling and render scale | `external/Paneport-dxmt`, `src/native/host` | Harness: with `metalFx` set to `spatial` and a `renderScale` below 1, the internal render size equals `renderScale` times the output and the final image is upscaled (C-0008 section 2) |
| S8.4.2 | Frame rate cap | `external/Paneport-dxmt`, `src/native/host` | Harness or logic test: `frameRateCap` must divide the display refresh rate, and the present rate is limited to it |
| S8.4.3 | Per-title translator tuning | `src/native/host` | Config test: the `translator` options from the compatibility entry reach the session configuration, and a key outside the schema allow-list is rejected |
| S8.4.4 | Thermal governor and memory-limit use | `src/native/host` | Policy tests on macOS: the governor reacts to injected thermal and memory-pressure events, reads the limit from `pp_memory_get_budget`, and refuses a title whose `memoryEstimateMiB` exceeds the current budget |
| S8.4.5 | Launcher-service session under the helper policy | `external/Paneport-wine`, `src/Paneport.Core` | Harness: a launcher and a game run as two pseudo-processes, the helper policy is applied, and both reach the running state |

**Verification.** The automated parts run without the iPhone. The `runtime` job runs the graphics-settings cases (render scale, spatial upscaling and the frame cap), the translator-option config tests, and the launcher-service harness case; the `native` job runs the governor policy tests on macOS; and the earlier Win32 and triangle regression tests run again. This stage creates those harness and policy tests. Pass criteria: the internal resolution and present cap match the compatibility entry; an out-of-allow-list translator key is rejected; the governor refuses an over-budget title and reacts to the injected events; the launcher-service case runs both pseudo-processes under the helper policy; and the earlier regressions still pass. The frame-rate, 720p playability, launcher-service in-game result, peak footprint against the limit, and Game Mode state are device checks, measured on recelilious's device and recorded in the L8 evidence record (C-0011 section 1, plan section 7.6); they are not run in CI.

**Done when.** The deliverables are merged, the automated verification passes in CI, and the change meets the definition of done in C-0012 section 1. The device results are marked "not yet verified on device" in the pull request until the L8 evidence record exists (C-0012 section 1 item 9).

**Risks and fallbacks.** The CPU side (translation and JIT) is more likely than graphics to limit AAA frame rate on the device, so the per-title translator options and the render-scale and MetalFX settings are the main levers; the exact translator option keys are bounded by the schema allow-list and their concrete names are to be confirmed in this stage. MetalFX frame interpolation's GPU-family requirement is not established in the research; the baseline is spatial upscaling, and frame interpolation is used only if its availability is confirmed in this stage. A launcher-service title with no crash isolation can bring down the app if a helper faults; the helper policy from M8.1 refuses crash reporters and updaters, and a title that exceeds the memory budget is refused rather than started.

**Hands over to.** The L8 integration session and its phase 4 gate, and L9 (the first release).

**Session log.**

_No sessions yet._
