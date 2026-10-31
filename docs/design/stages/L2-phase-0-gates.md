# L2 Phase 0 gates

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L2 | Phase 0 gates | Large | Planned | | |
| S2.0.1 | Evidence index and record template | Small | Planned | | |
| S2.0.2 | Gate metrics and results summary | Small | Planned | | |
| M2.1 | Overlay gate | Medium | Planned | | |
| S2.1.1 | Metal test pattern and frame pacing | Small | Planned | | |
| S2.1.2 | Transparent overlay style | Small | Planned | | |
| S2.1.3 | Pass-through hit testing | Small | Planned | | |
| S2.1.4 | Touch routing and logger | Small | Planned | | |
| S2.1.5 | Game view and overlay design document | Small | Planned | | |
| S2.1.6 | Overlay gate page | Small | Planned | | |
| S2.1.7 | Screenshot blend check | Small | Planned | | |
| M2.2 | Signal dispatcher | Medium | Planned | | |
| S2.2.1 | Install, save and chain | Small | Planned | | |
| S2.2.2 | Routing registries | Small | Planned | | |
| S2.2.3 | Guest handler registration API | Small | Planned | | |
| S2.2.4 | Mach thread control helpers | Small | Planned | | |
| S2.2.5 | Crash-chaining call and installed check | Small | Planned | | |
| S2.2.6 | Stress harness and screen | Small | Planned | | |
| S2.2.7 | Signals design document | Small | Planned | | |
| M2.3 | SteamKit2 on the device | Medium | Planned | | |
| S2.3.1 | Connection | Small | Planned | | |
| S2.3.2 | Sign-in flows | Small | Planned | | |
| S2.3.3 | Token store integration | Small | Planned | | |
| S2.3.4 | Licenses and app information | Small | Planned | | |
| S2.3.5 | Depot key, manifest request code and manifest | Small | Planned | | |
| S2.3.6 | Chunk download and verification | Small | Planned | | |
| S2.3.7 | iOS networking | Small | Planned | | |
| S2.3.8 | AOT and interpreter measurement | Small | Planned | | |
| S2.3.9 | Developer test screen | Small | Planned | | |
| S2.3.10 | QR code rendering | Small | Planned | | |
| S2.3.11 | Secret-free logging | Small | Planned | | |
| S2.3.12 | Steam service design document | Small | Planned | | |

## L2 Phase 0 gates

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L1 |
| C-0012 gate | Phase 0: Transparent overlay, Signal dispatcher, and SteamKit2 on the device (C-0012 section 3) |
| Size | S, for the integration session. Plan section 5.1 gives Large stages no size; the Medium stages carry their own. |
| Branch | `feat/l2-phase-0-gates` |
| Decisions needed | None before the session starts. If a gate fails, recelilious chooses the fallback (C-0012 section 3). |

**Goal.** Prove on the iPhone the three assumptions that could change the architecture, before any runtime work on the device (plan section 9). Each gate ends as Pass, Fail or Waived (C-0012 section 3). The criteria below are quoted from C-0012 section 3, which is authoritative.

| Gate | Passes when (C-0012 section 3) | Built by |
|---|---|---|
| Transparent overlay | A transparent Avalonia view over the game view blends correctly pixel for pixel, lets touches outside its controls fall through to the game view, and routes multi-touch correctly. | M2.1 |
| Signal dispatcher | Over at least 4 hours of continuous running on the device, managed null-reference faults are caught as exceptions, guest faults take the guest path, and there are no native crashes and no garbage-collector hangs. The record confirms the call that turns off Mono's crash chaining (C-0005 section 4). | M2.2 |
| SteamKit2 on the device | A Release device build on Mono with full ahead-of-time compilation completes sign-in with Steam Guard and with a QR code, reads the license list and app information, gets depot keys and manifest request codes, and downloads a manifest and some chunks. The record states the interpreter setting it needed and the time for sign-in and for the manifest download. | M2.3 |

**What recelilious will see.**

- On the iPhone, the device test flavor of Paneport (plan section 7.3) with a developer screen that opens three gate pages.
- The overlay page: a moving full-screen test pattern drawn with Metal, transparent Avalonia controls on top, and a live touch log that names the owner of every touch.
- The signal page: a stress run with live counters, a four-hour timer and a result screen with a verdict.
- The Steam page: sign-in by approval in the Steam mobile app, by a Steam Guard code and by a QR code, then the license count, app information, a depot key and a manifest request code (shown only as "obtained"), a manifest and a few verified chunks, with timings.
- On Windows, the blend check tool's verdict on a screenshot of the overlay page.
- Afterwards, three evidence records in `docs/evidence/` and their verdicts in the gate table of `docs/evidence/README.md`.

**Acceptance steps.**

Before you start:

- Install a Release build of the device test flavor, built from the head of the integration branch on Mono with full AOT (C-0009 section 3). Use the install route recorded for L1 (C-0009 section 5). The test flavor replaces the main app, because both share one bundle identifier (D12).
- Put the iPhone on a charger and turn Low Power Mode off. Start the app from the home screen only. No debugger may be attached, because an attached debugger intercepts exceptions (C-0006 section 1).
- For the Steam gate you need your own Steam account with the Steam mobile app on the iPhone, a second device with the Steam mobile app signed in to the same account (to scan the QR code, C-0007 section 3), and an app you own that has a Windows depot. You type the credentials yourself; they never go to Claude or into a record.
- On Windows you need a clone of the repository at the build's commit and the .NET 10 SDK, to run the blend check tool.

Build and device:

1. Open the app and its developer screen. **Expected:** the build section shows the commit hash of the integration branch head, the version, the configuration Release, the runtime Mono with full AOT, and the interpreter setting (off, or the assembly list from `eng/toolchain.json`).
2. Read the device section. **Expected:** the device model identifier, the iOS version and build, the memory limit and whether it has reached the Game Mode level (C-0005 section 2), and the entitlement values read the way the L1 evidence record did. Copy them into the record template.

Overlay gate:

3. Open the overlay gate page. **Expected:** a full-screen test pattern that moves smoothly; overlay buttons, a stick and semi-transparent probe panels on top; a frame-rate readout; an empty touch log.
4. Look closely at the screen. **Expected:** outside the controls the pattern shows with no white, grey or tinted layer over it; the probe panels show the pattern through them; control edges have no white fringe.
5. Take a screenshot with the iPhone's buttons and copy it to the Windows machine. Run `dotnet run tools/CheckOverlayBlend.cs -- <screenshot file>` from the repository root. **Expected:** every probe region is reported as PASS and the tool exits with code 0. Keep the tool's output for the record, without the file path.
6. Tap the pattern 20 times at spread-out points outside the controls, including the gaps between controls and points just outside their edges. **Expected:** each tap draws a marker on the game view under the finger; the log lists each touch with the owner "game view"; the swallowed counter stays at 0.
7. Tap each overlay control once. **Expected:** each control reacts and its counter goes up; the log shows the owner "overlay"; no marker appears on the game view.
8. Hold the stick with one finger and draw on the pattern with a second finger, then lift both. **Expected:** two touches with different identifiers; the stick follows the first finger while the game view draws a trail under the second; each touch keeps its owner from start to end; the lost counter stays at 0.
9. Repeat step 8 with the fingers in the opposite order. Then put four fingers down at once, two on controls and two on the pattern, move them and lift them in any order. **Expected:** the same as in step 8, for every finger.
10. Read the page summary. **Expected:** the median and 1% low frame rates, the peak memory footprint and the thermal state over time (C-0011 section 1). Copy the summary into the record.

Signal dispatcher gate:

11. Open the signal gate page. **Expected:** the startup check shows that the call to `mono_set_crash_chaining(FALSE)` resolved and returned during startup (C-0005 section 4), and that the dispatcher is installed for SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT.
12. Start the four-hour run and leave the app in the foreground without touching it. The page keeps the screen on. **Expected:** the counters rise: managed NullReferenceExceptions thrown and caught, guest faults raised and handled by the guest path (by thread mark and by address), faults passed to the runtime handler, and garbage collections; the elapsed time advances.
13. After at least 4 hours, read the result screen. **Expected:** the verdict PASS; the run is valid (the app never left the foreground); the duration is at least 4:00:00; exceptions thrown equal exceptions caught; guest faults raised equal guest faults handled by the guest path; unexpected faults are 0; garbage collections are above 0; the longest heartbeat gap is below the hang threshold that `docs/design/signals.md` sets; the dispatcher is still installed for all six signals.
14. Open the iPhone's analytics data in Settings, under Privacy & Security (the exact menu path is to be confirmed in the integration session). **Expected:** no crash report from Paneport dated during the run.
15. Copy the result summary, with the peak memory footprint and the thermal state over time, into the record.

SteamKit2 gate:

16. Open the Steam gate page. **Expected:** it shows Release, Mono, full AOT and the interpreter setting; the state is Signed out and "token stored" is no. If a token is stored, tap Sign out first.
17. Enter your account name and password, keep the default confirmation and tap Sign in. When the Steam mobile app asks, switch to it, approve the sign-in and return to Paneport. **Expected:** the state is Logged on; "token stored" is yes and the token is never shown; the sign-in times are shown (total time, and the time spent waiting for your approval).
18. Tap Sign out. **Expected:** "token stored" is no.
19. Sign in again and choose to enter a code instead. Enter the Steam Guard code from the Steam mobile app, or from email if your account uses email codes. **Expected:** Logged on, with sign-in times.
20. Sign out, then choose QR sign-in. Scan the code with the Steam mobile app on the second device and approve. **Expected:** Logged on, with sign-in times. If the code changes while it is shown, the page redraws it.
21. Close the app in the app switcher, open it again and open the Steam gate page. **Expected:** it logs on with the stored token and asks for no password. This is not a gate criterion; it checks the token store.
22. Tap Licenses. **Expected:** a license count above zero.
23. Enter the app ID of your test app and tap App info. **Expected:** the app's name and its Windows depots with their sizes.
24. Choose the smallest Windows depot, then tap Depot key and Manifest code. **Expected:** "depot key: obtained" and "manifest request code: obtained". Neither value is ever shown.
25. Tap Manifest. **Expected:** the manifest is downloaded and parsed; its file count, chunk count and size are shown, with the manifest download time.
26. Tap Chunks. **Expected:** at least 10 chunks are downloaded and each one is checked against the hash in the manifest (C-0007 section 4); 0 are rejected; the bytes and time are shown.
27. Read the page summary. **Expected:** the interpreter setting, the sign-in times, the manifest download time, the chunk results, the connection transport used, any system permission prompt seen, the peak memory footprint and the thermal state. Copy it into the record.
28. Tap Delete test data, then Sign out. **Expected:** the downloaded test files are gone and "token stored" is no. Keychain items survive the switch between the test flavor and the main app (plan section 7.3), so no token may be left behind.

Records:

29. Write the three evidence records from the copied summaries, or give the summaries to the next L2 session to write them. **Expected:** each record has every field of C-0012 section 4, contains nothing that section forbids, and passes the `checks` job (C-0011 section 2).
30. Set each gate's verdict in the gate table of `docs/evidence/README.md`. **Expected:** three rows for phase 0, each linking its record.

| Gate | Criterion | Checked in steps |
|---|---|---|
| Transparent overlay | Blends correctly pixel for pixel | 4, 5 |
| Transparent overlay | Touches outside its controls fall through to the game view | 6, 7 |
| Transparent overlay | Routes multi-touch correctly | 8, 9 |
| Signal dispatcher | At least 4 hours of continuous running on the device | 12, 13 |
| Signal dispatcher | Managed null-reference faults are caught as exceptions | 12, 13 |
| Signal dispatcher | Guest faults take the guest path | 12, 13 |
| Signal dispatcher | No native crashes | 13, 14 |
| Signal dispatcher | No garbage-collector hangs | 13 |
| Signal dispatcher | The record confirms the call that turns off Mono's crash chaining | 11 |
| SteamKit2 on the device | A Release device build on Mono with full AOT | 1, 16 |
| SteamKit2 on the device | Sign-in with Steam Guard | 17, 19 |
| SteamKit2 on the device | Sign-in with a QR code | 20 |
| SteamKit2 on the device | Reads the license list and app information | 22, 23 |
| SteamKit2 on the device | Gets depot keys and manifest request codes | 24 |
| SteamKit2 on the device | Downloads a manifest and some chunks | 25, 26 |
| SteamKit2 on the device | The record states the interpreter setting it needed | 1, 27 |
| SteamKit2 on the device | The record states the time for sign-in and for the manifest download | 17, 19, 20, 25, 27 |

If a gate fails, its record says Fail and what failed, and L2 becomes Blocked. recelilious decides what to adopt; the fallbacks the research proposes are in the table of C-0012 section 3: drawing the on-screen gamepad in the game's own Metal pass (C-0004), moving the shell to Swift (C-0001 section 4, C-0003 section 3, C-0004, C-0007, C-0010), or writing our own Steam protocol client (C-0003, C-0004, C-0007). The decision is recorded through contract changes first, and then new stages in the plan. Only recelilious can waive a gate, with a new evidence record that states the reason (C-0012 section 3).

**Evidence record.**

- One record per gate, because each gate has its own verdict row (C-0012 section 3): `docs/evidence/YYYY-MM-DD-l2-overlay.md`, `docs/evidence/YYYY-MM-DD-l2-signal-dispatcher.md` and `docs/evidence/YYYY-MM-DD-l2-steamkit2.md`, with the date in UTC. A repeated run gets a new record.
- Each record holds the fields of C-0012 section 4:
  - the gate, and the build (commit hash and version);
  - the device model identifier, and the iOS version and build;
  - the signing route (free or paid team, D2), the sideloading tool and its version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app (C-0009 section 4);
  - the JIT enabler and its version (none: phase 0 does not use JIT), whether Game Mode was active, and the memory limit read during the run;
  - the steps, the expected and observed results, the metrics and scrubbed logs;
  - the verdict, the tester as a project handle (`recelilious` or `Claude`), and the date.
- Gate metrics: for the overlay, the median and 1% low frame rates, the blend check output and the touch counters; for the signal dispatcher, the duration, every counter, the longest heartbeat gap, the crash-chaining confirmation and the installed check; for SteamKit2, the interpreter setting, the sign-in and manifest download times, the chunk results and the transport. Every record adds the peak memory footprint and the thermal state over time (C-0011 section 1).
- A record and its attachments never contain a UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). Attach the blend check output, not the screenshot. Name the Steam test app only if recelilious agrees.

**Integration session.** It starts when M2.1, M2.2 and M2.3 are Done, from `main` with all three merged.

1. Do S2.0.1 and S2.0.2.
2. Make sure the developer screen opens all three gate pages and shows the build and device information of steps 1 and 2.
3. Run the checks that need everything together: the `dotnet`, `native`, `ios` and `checks` jobs on the branch, and one Release build of the test flavor from one commit that contains all three pages.
4. Ask recelilious for a short smoke run before the full acceptance: each page opens, and the signal run passes 10 minutes. Record it in the pull request as a device check.
5. Confirm the menu path of step 14 and adjust the acceptance steps to the final pages.
6. Commit, push and open the pull request. Give recelilious a summary in Chinese with the acceptance steps (plan section 3.3).
7. After the runs, a continuation session writes the three records from recelilious's summaries, updates the gate table, and sets L2 to Accepted when all three gates have passed or been waived. Otherwise L2 stays Blocked with the reason until recelilious decides the fallback.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S2.0.1 | Evidence index and record template: the gate table with one row per gate of C-0012 section 3 (phases 0 to 4 and the runtime migration), each with its verdict (Pass, Fail, Waived or Not run) and a link to its latest record; a record template inside the README that lists every field of C-0012 section 4 and what must never appear. Plan section 7.7 creates the table here. | `docs/evidence/README.md` | Review checklist: one row per gate, every field present; the `checks` job passes, including links and the personal-data check (C-0011 section 2). |
| S2.0.2 | Gate metrics and results summary: samples the memory budget (`pp_memory_get_budget`, C-0005 section 2) and the thermal state for every gate run, and produces a text summary with build, device and gate results for copying into a record, with the personal data of C-0012 section 4 removed (C-0004 section 4). | `tests/device` for the pages; the sampling logic and the scrubber in `src/Paneport.Core` | Unit tests in `tests/Paneport.Core.Tests`: the scrubber removes every kind of item C-0012 section 4 lists from sample text, and the peak and timeline logic works on synthetic samples. The sample values are generated at run time, so the personal-data check finds nothing in the source. |

**Notes from earlier stages.**

- L0 ([L0 brief](L0-foundation.md), session log of 2026-10-30): S0.0.1 created `docs/evidence/README.md` with the gate table, every gate at "No record yet" where S2.0.1 names "Not run", and with the fields and forbidden items of C-0012 section 4 as a list. S2.0.1 builds its record template on that file; the verdicts still come from step 30 and integration step 7.

**Session log.**

_No sessions yet._

## M2.1 Overlay gate

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L2 |
| Depends on | L1 |
| Decisions needed | None before the session starts. If the gate fails in L2, recelilious decides on the fallback of C-0012 section 3. |
| Size | M |
| Branch | `feat/m2-1-overlay-gate` |
| Design documents | `docs/design/game-view-and-overlay.md` (new). Reads `docs/design/ios-app-head.md` (M1.3) and `docs/design/app-shell.md` (M0.3). |

**Goal.** Build what the transparent overlay gate needs (C-0012 section 3): a game view that draws with Metal at up to 120 Hz, a transparent AvaloniaView over it that lets touches outside its controls reach the game view, correct multi-touch routing, the overlay gate page in the device test flavor, and a tool that checks the blend on a screenshot.

**Scope.**

In:

- A render loop for the GameView of M1.3: a `CADisplayLink` asking for up to 120 Hz, which `CADisableMinimumFrameDurationOnPhone` allows (C-0009 section 4). It pauses when hidden or in the background, and stops when its renderer detaches, so that the display driver can own the layer later (M5.1). Frame statistics give the median and 1% low (C-0011 section 1); which timestamps they use is to be confirmed in this stage.
- A test pattern for the test flavor: moving gradients, a checkerboard and fine lines that show blending errors, reference patches under the probe panels, and corner markers for the blend tool. How its shaders are built is to be confirmed in this stage.
- Transparency: a style for `EmbeddableControlRoot` that sets `Background` and `TransparencyBackgroundFallback` to `Transparent`, loaded before the AvaloniaView is constructed, because later changes are ignored (`avalonia_ui_layer.md` sections 2.1 and 3.1). The one AvaloniaView switches between the opaque shell and transparent overlay content.
- Pass-through: an AvaloniaView subclass overrides `HitTest` and `PointInside` and asks `InputHitTest` on its top level. For an empty hit or the transparent root it returns no view, so UIKit gives the touch to the GameView (section 3.1). The decision is a pure function in Paneport.UI.
- Multi-touch: UIKit fixes a touch's owner when it begins, as the research infers and the gate confirms. A touch ledger in Paneport.Core records each owner from began to ended or cancelled, and counts swallowed touches (the transparent root got one) and lost ones (no end, or a changed owner). The GameView draws a marker and a trail under each of its touches.
- The overlay gate page in `tests/device/`, in the developer menu of M1.3: the pattern, buttons with counters, a virtual stick, semi-transparent probe panels that take no touches, a frame-rate readout, the touch log with identifier, owner and phase, the counters, and the frame statistics. S2.0.2 adds the memory and thermal samples and the copyable summary.
- `tools/CheckOverlayBlend.cs`: finds the corner markers in a PNG screenshot, compares each probe region with the blend expected from its reference patch and the panel's color and alpha, prints PASS or FAIL per region, and exits 0 only if all pass. It reads PNG with the .NET libraries alone or with a library registered first (C-0003 section 5). The screenshot's color space and the tolerance are to be confirmed in this stage.
- `docs/design/game-view-and-overlay.md`.

Out: the on-screen gamepad, touch-to-mouse and GameController (M6.1); the menu and HUD (M6.5); the display driver's use of the layer (M5.1); touch drags faster than Avalonia 12.1.4 delivers, which this stage only measures.

**Read first.**

- C-0012 section 3; C-0004 sections 1 and 2; C-0009 section 4; C-0010 section 2; C-0011 section 1; C-0001 section 6.
- `avalonia_ui_layer.md` sections 2.1, 3.1 and 4 (the transparency correction, the display link, the view tree, experiments E1 to E3).
- `docs/design/ios-app-head.md` and `docs/design/app-shell.md`.

**Deliverables.**

- `src/Paneport.iOS/`, `src/Paneport.UI/` and `src/Paneport.Core/`: the modules above.
- `tests/device/`, `tests/Paneport.Core.Tests/` and `tests/Paneport.UI.Tests/`.
- `tools/CheckOverlayBlend.cs`, and `docs/design/game-view-and-overlay.md` with its row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S2.1.1 | Metal test pattern and frame pacing | loop in `src/Paneport.iOS/`, statistics in `src/Paneport.Core/`, pattern in `tests/device/` | xUnit on synthetic frame times: the median and 1% low equal hand-computed values. The `ios` job builds the loop and the pattern. |
| S2.1.2 | Transparent overlay style | `src/Paneport.UI/`, startup order in `src/Paneport.iOS/` | Headless UI test: with the style loaded first, the root's background and fallback are transparent, and the shell pages stay opaque. |
| S2.1.3 | Pass-through hit testing | decision in `src/Paneport.UI/`, subclass in `src/Paneport.iOS/` | Headless UI tests: points on controls are kept; points in gaps, one pixel outside edges and on probe panels pass through; an opaque shell page keeps every point. |
| S2.1.4 | Touch routing and logger | ledger in `src/Paneport.Core/`, GameView touches in `src/Paneport.iOS/`, log in `tests/device/` | xUnit on synthetic sequences: up to five touches with interleaved phases and any lift order keep their owners; a touch that reaches the transparent root counts as swallowed, one without an end as lost. |
| S2.1.5 | Game view and overlay design document | `docs/design/game-view-and-overlay.md` | Review: the view tree, startup order, hit-test rule, touch ownership, pacing, metrics, fallback and public sources (C-0001 section 6). lychee passes. |
| S2.1.6 | Overlay gate page | `tests/device/` | The `ios` job builds the test flavor with it; its device behavior is the gate. |
| S2.1.7 | Screenshot blend check | `tools/CheckOverlayBlend.cs` | Self-test on synthetic PNG files made at run time: a correct blend passes; a grey veil, a tint, a white fringe and a shifted region each fail, naming the region. |

**Verification.** All of it runs without recelilious's iPhone.

| Check | Passes when |
|---|---|
| `dotnet` job | The tests of S2.1.1 to S2.1.4 pass on Windows and Ubuntu. |
| The blend tool's self-test, in `checks` with the other tools' self-tests (M0.2) | It exits 0 and reports every planted failure. |
| `ios` job | Both flavors build with the style, the subclass and the page; `check-app.sh` and `tools/ScanPackage.cs` pass. |

Scripts this stage creates: `tools/CheckOverlayBlend.cs`. The device gate is L2 steps 3 to 10; the pull request marks the overlay's device behavior "not yet verified on device" (C-0012 section 1 item 9).

**Done when.** The verification passes, `game-view-and-overlay.md` is merged with the stage, recelilious has merged the branch, the status is Done and the session log is updated.

**Risks and fallbacks.**

- The style override is checked against the source, not at run time (experiment E1). If the blend is still wrong on the device, the gate fails, and recelilious decides on the fallback of drawing the on-screen gamepad in the game's own Metal pass, which changes C-0004 first (C-0012 section 3).
- Avalonia 12.1.4 delivers touch drags at about 60 Hz on ProMotion, and its display link never idles (`avalonia_ui_layer.md` section 3.1). Neither is a gate criterion; the page records both, and M6.1 chooses the stick's path.
- Above 60 Hz needs Low Power Mode off. Whether Avalonia itself exceeds 60 Hz is unknown (E3); the gate measures the game view.
- A screenshot may lose fidelity on its way to Windows, so the tool accepts only PNG and reports an unexpected color space.

**Hands over to.**

- L2: the gate page and the blend tool (steps 3 to 10).
- M6.1: the overlay for on-screen controls, pass-through for touch-to-mouse, and the GameView's touches.
- M6.5: the overlay for the menu and HUD, and the frame statistics.
- M5.1: the GameView's layer, and the render loop that stops when the display driver attaches.
- M10.2: the style and the hit-test overrides with their tests, which must pass after the Avalonia upgrade.

**Notes from earlier stages.**

- M0.3 ([L0 brief](L0-foundation.md), session log of 2026-10-30): `docs/design/app-shell.md` section 10 leaves the shell's safe areas, the on-screen keyboard and the overlay on the phone to device checks from M1.3 and this stage.

**Session log.**

_No sessions yet._

## M2.2 Signal dispatcher

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L2 |
| Depends on | M1.2, M1.3 |
| Decisions needed | None before the session starts. If the gate fails in L2, recelilious decides on the fallback of C-0012 section 3. |
| Size | L |
| Branch | `feat/m2-2-signal-dispatcher` |
| Design documents | `docs/design/signals.md` (new). Reads `docs/design/native-host.md` and `docs/design/interop.md` (M1.2) and `docs/design/ios-app-head.md` (M1.3). |

**Goal.** Implement the single signal dispatcher of C-0005 section 4 in the native host. It owns the six fault signals, sends guest faults to the guest path and every other fault to the runtime's saved handler, offers the registrations that later stages need, and is checked in test builds. Turn off Mono's crash chaining, and give the device test flavor a stress harness for the four-hour gate.

**Scope.**

In:

- Installation in `pp_init`, after the runtime has started and before any Wine thread exists, for SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT (C-0005 section 4), with `SA_SIGINFO` and an alternate signal stack, saving the runtime's previous action for each (`avalonia_ui_layer.md` section 3.3). Apple delivers SIGBUS for a null dereference (section 2.2), so SIGSEGV and SIGBUS route alike.
- Chaining: a fault outside the guest path calls the saved action with the original arguments, honoring a `SA_SIGINFO` handler, a plain handler, the default action and ignore.
- Routing (C-0005 section 4): the guest path for a thread with the guest or translator mark, or for an address in a registered JIT pool range or guest reservation. While the probe mode is on, a fault inside the pool goes to the JIT probe route (C-0006 section 3). Everything else goes to the runtime. The handler reads the marks, ranges, reservations and probe route without locks or allocation; writers serialize outside signal context and publish atomically, with fixed, documented capacities.
- The guest handler registration API: one handler that returns handled or declined, where a declined guest fault ends the app with a crash record. It is internal to the host and shaped so that the Wine bridge can expose it unchanged (C-0005 section 3); `paneport/wine_bridge.h` comes with M4.3.
- Crash records through M1.2's writer for faults that end the app. How the dispatcher tells a fault the runtime resolves, such as a managed null reference, from one that ends the app is to be confirmed in this stage.
- Mach helpers for later guest work: `thread_suspend`, `thread_get_state`, `thread_set_state` and `thread_resume` on ARM64 (C-0005 section 4). `SIGUSR1` and `SIGQUIT` are never used for thread control.
- The `mono_set_crash_chaining(FALSE)` binding in Paneport.Interop, called by Paneport.iOS at startup after the runtime starts and before `pp_init` (C-0005 section 4). Whether it resolved and returned is kept for the signal page and Diagnostics. Which library the import names is to be confirmed in this stage. The desktop head and the macOS tests never call it.
- The test-build installed check that each of the six signals still has the dispatcher's handler, run at the end of `pp_init` in test builds and on request; M4.3 runs it after `pp_session_start` (C-0005 section 4).
- The stress harness: test-build exports in `src/native/host/` with bindings in `src/Paneport.Interop/`, verdict logic in `src/Paneport.Core/`, and the signal gate page in `tests/device/`. It runs a managed null-reference loop; native threads marked as guest that fault on guard and read-only pages; unmarked native threads that fault inside a registered reservation; a test guest handler that counts each fault and skips the faulting instruction; a garbage-collection pressure thread; a heartbeat that records the longest gap; a four-hour timer that keeps the screen on; the foreground check; and the result screen with the verdict of L2 step 13.
- `docs/design/signals.md`, which also sets the hang threshold for the heartbeat gap that L2 and L10 use.

Out: the Wine fork's handler and the bridge table (M4.3); the JIT provider (M3.1); CoreCLR (M10.1).

**Read first.**

- C-0005 sections 1 to 4; C-0004 section 1; C-0006 sections 3 and 5; C-0010 sections 1 and 3; C-0011 sections 1 and 4; C-0012 sections 3 and 4; C-0001 section 6.
- `avalonia_ui_layer.md` sections 2.2, 3.3 and 4 (Mono's handlers and crash chaining, the dispatcher design, experiments E5 and E16).
- `docs/design/native-host.md`, `docs/design/interop.md` and `docs/design/ios-app-head.md`.

**Deliverables.**

- `src/native/host/signals/`, and the harness exports in `src/native/host/`, built only with `ios-device-test` and `macos-test`.
- `src/Paneport.Interop/`, `src/Paneport.iOS/`, `src/Paneport.Core/` and `tests/device/`: the modules above.
- `tests/native/` and `tests/Paneport.Core.Tests/`.
- `docs/design/signals.md` with its row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S2.2.1 | Install, save and chain | `src/native/host/signals/` | CTest: with a stand-in runtime handler installed before `pp_init`, a fault on an unmarked thread reaches it exactly once with its original `siginfo`, for each of the six signals. |
| S2.2.2 | Routing registries | `src/native/host/signals/` | CTest under `tsan`: writers change ranges while threads fault; every fault takes the path its mark or address calls for; a pool fault goes to the probe only while the probe mode is on. |
| S2.2.3 | Guest handler registration API | `src/native/host/signals/` | CTest: the test handler sees only guest-path faults. A case expected to end by the signal declines a fault, and the next case finds its crash record. |
| S2.2.4 | Mach thread control helpers | `src/native/host/signals/` | CTest: a suspended test thread has one register changed and, once resumed, sees the new value; a finished thread gives an error. |
| S2.2.5 | Crash-chaining call and installed check | `src/Paneport.Interop/`, `src/Paneport.iOS/`, `src/native/host/signals/` | CTest: the check passes after `pp_init`, and fails naming the signal when a test replaces a handler. The binding builds in the `dotnet` job; the call is checked on the device. |
| S2.2.6 | Stress harness and screen | `src/native/host/` (test builds), `src/Paneport.Interop/`, `src/Paneport.Core/`, `tests/device/` | xUnit: on synthetic counters the verdict is PASS only when every criterion of L2 step 13 holds. CTest runs the native part for 60 seconds with the stand-in: every planted fault is handled and none is unexpected. |
| S2.2.7 | Signals design document | `docs/design/signals.md` | Review: routing, registries and limits, reservations, the probe route, declined faults and crash records, Mono, alternate stacks, the hang threshold, sanitizer settings, notes for M10.1 and sources. lychee passes. |

**Verification.** All of it runs in CI unless marked as a device check.

| Check | Passes when |
|---|---|
| `native` job: CTest with `asan-ubsan` and `tsan` | The cases of S2.2.1 to S2.2.6 pass with no sanitizer report. The sanitizer options the tests need, such as leaving the fault signals to the dispatcher, are set by the presets and recorded in `signals.md`. |
| `dotnet` job | The verdict tests pass, and Paneport.Interop builds with the new bindings. |
| `ios` job | Both flavors build; `check-framework.sh` shows the harness exports in the test build only. |
| Device check, optional, by recelilious | A 10-minute run on the signal page ends with PASS. It does not gate the merge; the pull request marks the device behavior "not yet verified on device". |

Scripts this stage creates: the CTest cases and the native harness runner in `tests/native/`, and the signal gate page.

**Done when.** The verification passes, `signals.md` is merged with the stage, recelilious has merged the branch, the status is Done and the session log is updated.

**Risks and fallbacks.**

- The interplay with Mono can only be checked on the device. Whether Mono's handler returns, resets the action or aborts on a fault it does not own decides how crash records are written; the CTest stand-in models the documented behavior.
- Darwin may allocate thread-local storage on first use, which is unsafe inside a handler on a thread that never set its mark; `signals.md` records a safe choice.
- Without an alternate signal stack, a stack overflow ends the app with no record.
- The sanitizers handle signals themselves. If a case cannot run under one of them, the session asks recelilious, because C-0011 section 1 requires both builds.
- An attached debugger intercepts faults, so device runs start from the home screen (L2).
- If the crash-chaining import does not resolve on the device, the gate's record criterion fails. If the gate fails, the proposed fallback is a Swift shell (C-0012 section 3); recelilious decides, with contract changes first.

**Hands over to.**

- L2: the signal gate page and the startup check of step 11.
- M3.1 and L3: the JIT pool range registration, the probe route and its mode.
- M4.3: guest handler registration, thread marks, reservations, the Mach helpers and the installed check after `pp_session_start`.
- M6.3: guest reservations for the file-backed tier.
- M10.1: the dispatcher, the routing tests that must keep passing, the harness and `signals.md`.

**Session log.**

_No sessions yet._

## M2.3 SteamKit2 on the device

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L2 |
| Depends on | M1.3, M0.3 |
| Decisions needed | A C-0003 change that registers SteamKit2's dependencies without a register row, such as ZstdSharp.Port (`avalonia_ui_layer.md` section 2.3), approved before they are added. D13, the QR code route for S2.3.10: a library registered through a C-0003 change or a small encoder of our own, decided by recelilious before S2.3.10 starts. recelilious uses an account and a test app of their own for the desktop run; neither is named in the repository. |
| Size | L |
| Branch | `feat/m2-3-steamkit2-device` |
| Design documents | `docs/design/steam-service.md` (new). Reads `docs/design/app-shell.md` (M0.3) and `docs/design/ios-app-head.md` (M1.3). |

**Goal.** Build the Steam service of C-0007 sections 2 to 4 in Paneport.Steam on SteamKit2, with every step the SteamKit2 gate needs: the connection; sign-in by approval, by code and by QR code; the token in the keychain; licenses; app information; depot keys; manifest request codes; a manifest; and verified chunks. Prove it on Windows with fakes and a desktop run, and give the device test flavor the page that L2 runs in a Release build on Mono with full AOT.

**Scope.**

In:

- SteamKit2 through `Directory.Packages.props` with lock files and protobuf-net pinned explicitly (`avalonia_ui_layer.md` section 3.4), behind small internal interfaces for the connection, authentication, apps and content, so that the state machines run against fakes (C-0011 section 1).
- The connection: SteamKit2's client and callback loop on a managed worker thread (C-0004 section 1); connect, reconnect with backoff, retry on another server when asked, and sign off on request. On return to the foreground it logs on again with the stored token, because iOS may close a suspended app's sockets (`avalonia_ui_layer.md` section 2.3).
- Sign-in (C-0007 section 3): the account name and password confirmed in the Steam mobile app by default, a Steam Guard code from the authenticator or email as the fallback, and QR sign-in that redraws when its challenge changes, with cancellation and clear errors. The password is cleared after use and never stored or logged. The device friendly name is fixed text, never the phone's name.
- A refresh token for the platform type that Valve's client accepts (C-0007 section 3). SteamKit2's default is SteamClient (`avalonia_ui_layer.md` section 2.3); that Valve's client accepts it is to be confirmed in this stage, and M8.2 confirms it in use. The token and the account name go into M1.3's keychain token store on iOS and stay in memory on the desktop head. The token is dropped on logon results that mean it is invalid and deleted at sign-out; no other sign-in value is kept.
- Licenses, app information with the Windows depots and their sizes, depot keys, manifest request codes, content servers, the manifest, and chunks, each checked against its manifest entry before use (C-0007 section 4) with SteamKit2's public chunk processing (`avalonia_ui_layer.md` section 2.3). Test downloads go to a test folder under `Library/Application Support/Paneport/downloads/` (C-0004 section 4), which the page deletes.
- iOS networking as the research recommends: a `SocketsHttpHandler` client for the WebSocket connection, or TCP only, because the default iOS handler cannot upgrade to WebSocket, and a separate client for content (`avalonia_ui_layer.md` sections 2.3 and 3.4, experiment E11). The page reports the transport and any permission prompt.
- AOT: Release on Mono with full AOT and the interpreter off first (C-0009 section 3). If a step fails, the smallest assembly list that makes it pass, such as `-all,protobuf-net`, goes into `eng/toolchain.json`. SteamKit2, protobuf-net and protobuf-net.Core are rooted for trimming if the device needs it (experiment E10). Suppressions stay in Paneport.Steam (C-0010 section 2).
- The Steam page: its view model in Paneport.Core and its view in Paneport.UI, opened only from the test flavor's developer menu and from a developer entry in Paneport.Desktop, so that the desktop head can sign in on Windows. It shows what L2 steps 16 to 28 name, and never a token, key, code or Steam ID.
- Logs never hold a password, token, depot key, manifest request code, Steam ID or account name (C-0004 section 4, C-0010 section 2). SteamKit2's own debug output stays off unless it passes the same filter.
- DepotDownloader's source is never opened; the research summary serves only for observable behavior (C-0007 section 2).
- `docs/design/steam-service.md`: the flows, the platform type, the token handling, networking, threading, the interpreter setting, and what M7.1 extends.

Out: the library manager, downloads, resume and background work (M7.1); classification and launch (M7.2); the token hand-off to the Dock host (M8.2); the sign-in pages for users (M7.1).

**Read first.**

- C-0007 sections 1 to 4; C-0003 sections 3, 5 and 6; C-0004 sections 1, 2 and 4; C-0009 section 3; C-0010 section 2; C-0011 sections 1 and 2; C-0012 sections 3 and 4; C-0001 section 6.
- `avalonia_ui_layer.md` sections 2.3, 3.4 and 4 (SteamKit2, authentication, PICS, content, iOS networking, experiments E10, E11 and E14); `steam_integration.md` (sign-in confirmation).
- `docs/design/app-shell.md` and `docs/design/ios-app-head.md`.

**Deliverables.**

- `src/Paneport.Steam/`, `src/Paneport.iOS/`, `src/Paneport.Core/`, `src/Paneport.UI/`, `src/Paneport.Desktop/` and `tests/device/`: the modules above.
- `tests/Paneport.Steam.Tests/` and `tests/Paneport.UI.Tests/`.
- `Directory.Packages.props`, the lock files, `eng/toolchain.json` and `eng/license-map.json`.
- `docs/design/steam-service.md` with its row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S2.3.1 | Connection, with SteamKit2 and its registered dependencies | `src/Paneport.Steam/` | xUnit with a fake connection: it reconnects after a drop, honors a sign-off request, logs on with the token after a foreground return, and stops retrying at the documented limit. |
| S2.3.2 | Sign-in flows | `src/Paneport.Steam/` | xUnit with a fake authentication service: each flow reaches Logged on; a wrong code asks again; a declined approval falls back to a code; a QR challenge change updates the code; the password is cleared after use. |
| S2.3.3 | Token store integration | `src/Paneport.Steam/`, `src/Paneport.Desktop/` | xUnit with a fake store: the token is stored, reused, dropped on each invalid-token result and deleted at sign-out, and nothing else is kept. |
| S2.3.4 | Licenses and app information | `src/Paneport.Steam/` | xUnit on synthetic data: the Windows depots and their sizes are found, and missing sections are reported, not thrown. |
| S2.3.5 | Depot key, manifest request code and manifest | `src/Paneport.Steam/` | xUnit: a missing key or a code of 0 gives "not granted"; a synthetic manifest gives the right file count, chunk count and size. |
| S2.3.6 | Chunk download and verification | `src/Paneport.Steam/` | xUnit: a chunk that does not match its manifest entry is rejected and never written; a matching synthetic chunk passes. How the test builds one is to be confirmed in this stage. |
| S2.3.7 | iOS networking | `src/Paneport.Steam/`, `src/Paneport.iOS/` | The `ios` job builds it. As a device check, the page reports the transport and any prompt. |
| S2.3.8 | AOT and interpreter measurement | `eng/toolchain.json`, `src/Paneport.iOS/` | Device check by recelilious on a Release build: every step passes with the interpreter off, or the smallest list that makes them pass is recorded. |
| S2.3.9 | Developer test screen | view model in `src/Paneport.Core/`, view in `src/Paneport.UI/`, entries in `tests/device/` and `src/Paneport.Desktop/` | Headless UI test with a fake service: every state of L2 steps 16 to 28 shows, and no token, key or code appears. |
| S2.3.10 | QR code rendering | `src/Paneport.UI/`, plus `src/Paneport.Core/` for an encoder of our own | Headless UI test: the code is drawn and redrawn on a challenge change. An encoder of our own also matches a worked example of the QR code standard; which one is to be confirmed in this stage. |
| S2.3.11 | Secret-free logging | `src/Paneport.Steam/` | xUnit: a full fake run through a capturing logger shows no password, token, depot key, manifest request code, Steam ID or account name. |
| S2.3.12 | Steam service design document | `docs/design/steam-service.md` | Review: the flows, platform type and device name, tokens, networking, threading, AOT and trimming, tests and sources, and that DepotDownloader's source was not read (C-0007 section 2). lychee passes. |

**Verification.**

| Check | Passes when |
|---|---|
| `dotnet` job | The tests of S2.3.1 to S2.3.6 and S2.3.9 to S2.3.11 pass on Windows and Ubuntu, with no connection to Steam and only synthetic inputs (C-0011 section 1). The trimmed publish shows no warning from our code and only allowed suppressions, each justified (C-0010 section 2). |
| `checks` job | After the C-0003 change, the license checker accepts SteamKit2 and its dependencies; the personal-data check finds nothing in `tests/`. |
| `ios` job | Both flavors build in Release with full AOT and the interpreter setting from `eng/toolchain.json`. |
| Desktop run, manual, by recelilious on Windows | With `dotnet run --project src/Paneport.Desktop`, the developer Steam page signs in by approval, reads the licenses and app information, gets a depot key and a manifest request code, and downloads a manifest and at least 10 chunks, none rejected. It reports the token as held in memory only (C-0007 section 3). |
| Device check, by recelilious (S2.3.8) | A Release build of the test flavor runs every step on the iPhone; the interpreter setting it needed goes into `eng/toolchain.json` and `steam-service.md`. |

This stage creates no script beyond its tests. The full gate run is L2 steps 16 to 28.

**Done when.** The verification passes, `steam-service.md` is merged with the stage, the C-0003 change was accepted before the packages were added, recelilious has merged the branch, the status is Done and the session log is updated.

**Risks and fallbacks.**

- No public report shows SteamKit2 on an iOS device (`avalonia_ui_layer.md` section 2.3). protobuf-net's reflection may fail under full AOT, and trimming can break its deserialization without an error (section 3.4). The interpreter list and the trimming roots are the planned answers, checked on the device.
- The WebSocket path may fail or bring up the Local Network prompt (E11); TCP only is the alternative.
- Repeated sign-ins may bring Steam Guard checks or new-device notices to the account (E14), so tests use fakes and only recelilious signs in, as rarely as possible. Token renewal reportedly works for one platform type only (E14).
- SteamKit2 is LGPL-2.1-only and compiled statically by AOT; the public source of this repository satisfies relinking (C-0003 section 3).
- If the gate fails, the proposed fallback is a Steam protocol client of our own (C-0012 section 3, C-0007 section 2); recelilious decides.

**Hands over to.**

- L2: the Steam page for steps 16 to 28.
- M7.1: the sign-in service and its flows, the token handling, the platform type, the interpreter setting, the QR rendering, the networking setup and chunk verification.
- M8.2: a refresh token that Valve's client can use, and the sign-off and reconnect calls around a Dock session (C-0007 section 4).
- M9.1: SteamKit2 and its dependencies for the notices.
- M10.2: the WebSocket setup, the interpreter list and the suppressions, which change on .NET 11.

**Notes from earlier stages.**

- M0.3 ([L0 brief](L0-foundation.md), session log of 2026-10-30): `docs/design/app-shell.md` section 10 asks this stage whether the managed side keeps the refresh token as bytes that it can clear.

**Session log.**

_No sessions yet._
