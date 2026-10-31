# L6 A 2D game

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L6 | A 2D game | Large | Planned | | |
| S6.0.1 | L6 smoke program | Small | Planned | | |
| M6.1 | Input stack | Medium | Planned | | |
| S6.1.1 | Input queue and API | Small | Planned | | |
| S6.1.2 | GameController capture | Small | Planned | | |
| S6.1.3 | Gamepad path into Wine | Small | Planned | | |
| S6.1.4 | Keyboard and mouse in the driver | Small | Planned | | |
| S6.1.5 | Virtual gamepad overlay | Small | Planned | | |
| S6.1.6 | Touch-to-mouse mode | Small | Planned | | |
| S6.1.7 | XInput reader test program | Small | Planned | | |
| M6.2 | Audio stack | Medium | Planned | | |
| S6.2.1 | RemoteIO output | Small | Planned | | |
| S6.2.2 | Audio session and interruptions | Small | Planned | | |
| S6.2.3 | Endpoints | Small | Planned | | |
| S6.2.4 | Tone test program | Small | Planned | | |
| S6.2.5 | XAudio2 path | Small | Planned | | |
| M6.3 | Memory governor and file-backed tier | Medium | Planned | | |
| S6.3.1 | Limit sampler | Small | Planned | | |
| S6.3.2 | Reported physical memory | Small | Planned | | |
| S6.3.3 | File-backed tier | Small | Planned | | |
| S6.3.4 | Governor policy | Small | Planned | | |
| S6.3.5 | Pressure and thermal events | Small | Planned | | |
| M6.4 | Compatibility database core | Medium | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| S6.4.1 | Title schema | Small | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| S6.4.2 | Typed model and loader | Small | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| S6.4.3 | Precedence rules | Small | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| S6.4.4 | Device layer store | Small | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| S6.4.5 | Fixtures | Small | In progress | `feat/m6-4-compat-db-core` | 2026-10-31 |
| M6.5 | Game import and session flow | Medium | Planned | | |
| S6.5.1 | Import pipeline | Small | Planned | | |
| S6.5.2 | PE classifier and synthetic PE generator | Small | Planned | | |
| S6.5.3 | Local title entries | Small | Planned | | |
| S6.5.4 | Launch flow | Small | Planned | | |
| S6.5.5 | Relaunch prompt | Small | Planned | | |
| S6.5.6 | In-game menu | Small | Planned | | |
| S6.5.7 | HUD and frame-time counters | Small | Planned | | |
| S6.5.8 | Metrics export and summary tool | Small | Planned | | |
| M6.6 | Performance pass for the test game | Medium | Planned | | |
| S6.6.1 | Case-insensitive file lookup cache | Small | Planned | | |
| S6.6.2 | Per-title translator options | Small | Planned | | |
| S6.6.3 | Graphics settings from the entry | Small | Planned | | |
| S6.6.4 | Thread quality of service | Small | Planned | | |
| S6.6.5 | Test title's compatibility entry | Small | Planned | | |
| S6.6.6 | On-screen layout field | Small | Planned | | |

## L6 A 2D game

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L5 |
| C-0012 gate | Phase 2, "A 2D game" (C-0012 section 3) |
| Size | M, for the integration session's own work (plan section 2.5). Plan section 5.1 gives Large stages no size. |
| Branch | `feat/l6-2d-game` |
| Decisions needed | D6: the test title. M6.6 needs the same decision. See also the question on `ceg` for local titles in M6.5. |

**Goal.** Show on the iPhone that Paneport runs a real game that the user brought in. An imported 2D or indie Direct3D 11 game plays for 10 minutes on an iPhone 17 Pro with a median of at least 55 fps and a 1% low of at least 45 fps, and the app is not killed for memory (C-0012 section 3). This is the roadmap's phase 2 milestone, a game "imported locally rather than through Steam" (roadmap, phase 2). It closes the loop of single-process Wine, the translator, graphics, input, audio and the memory governor.

The title comes from D6. It has to fit what the runtime can do at this point:

- It is a 64-bit (x86-64) Windows program. 32-bit guests come in M7.3.
- It renders with Direct3D 11, as the gate asks. DXMT covers Direct3D 10 and 11 (C-0004 section 2), and the Direct3D 9 path is still open (plan section 8).
- It was obtained outside Steam, for example as a DRM-free copy. It is then a `local-` title (C-0008 section 1) and uses the Direct path (C-0007 section 5).
- Its folder holds no Steam marker: no `steam_api.dll` or `steam_api64.dll` anywhere in it, and no SteamStub on its main program. The app refuses a local title that shows one (C-0008 section 5).
- It runs as one Windows process with no launcher. Several pseudo-processes come in M8.1.
- It needs neither Microsoft's redistributable runtimes nor .NET through Wine Mono. Both stay in the backlog (plan sections 7.5 and 10).
- It has no anti-cheat (C-0001 section 5).
- It can be played with a controller, and ideally with touch.
- It needs far less than the memory limit. The research cites about 3,200 MB for one well-known 2D title (process_and_memory_model.md Q6).

**What recelilious will see.**

- The game folder, imported through the Files app, in Paneport's library with its name and size.
- Its details: no Steam API, no SteamStub, no `steam_appid.txt`, CEG "no", and the Direct launch path.
- A clear refusal for a test folder that carries a Steam marker.
- The game full screen with sound, after the usual JIT setup with StikDebug.
- Play with a controller, and with the on-screen gamepad and touch-to-mouse when no controller is connected.
- A HUD with the frame rate (median and 1% low), memory (footprint, limit and Game Mode) and the thermal state.
- A 10-minute measurement whose result meets the gate.
- The relaunch prompt after the game ends.
- A diagnostics export that can be saved on Windows.

**Acceptance steps.**

You need:

- the iPhone 17 Pro with Developer Mode, StikDebug with its pairing file, and LocalDevVPN (plan section 4.1);
- the install route recorded for L1, for example AltServer on Windows with AltStore 2.2 or later (C-0009 section 5);
- the D6 game folder on Windows;
- a Bluetooth controller (an Xbox or PlayStation pad, or an MFi controller), and optionally a Bluetooth keyboard and mouse;
- Low Power Mode turned off, because it caps the display at 60 Hz (avalonia_ui_layer.md section 3.1).

Steps:

1. Install the device test app from the integration session's pull request through the L1 install route. It is the full app plus developer screens, and it replaces the main app because both share one bundle identifier (plan section 7.3). Expected: Paneport opens on the iPhone.
2. Open the diagnostics page and note the values the evidence record needs, read the same way as for the L5 record: the model identifier, the iOS version and build, the build's commit hash and version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app. Expected: the model identifier is `iPhone18,1` (iPhone 17 Pro), and `get-task-allow` and `increased-memory-limit` are true. If Paneport warns that this iOS build is not on its verified list (C-0006 section 6), note that too.
3. Copy the game folder from Windows into the Import folder inside Paneport's folder under On My iPhone in the Files app (`Documents/Import/`, C-0004 section 4). A USB drive works if it is formatted exFAT, APFS, FAT32 or macOS Extended. iOS cannot read NTFS. File Sharing from the computer is another route (precedents_and_ux_layer.md KQ5). Expected: the Files app shows the folder.
4. On Windows, generate the synthetic Steam-marked test folder with `dotnet run tools/SyntheticPe.cs`, as `tools/README.md` describes. Copy it to the iPhone the same way. It holds only our own synthetic files, never a Valve file (C-0011 section 1). Expected: the Files app shows it.
5. In Paneport, choose Import and pick the game folder. Expected: a progress bar, then the title in the library with its name and install size.
6. Open the title's details. Expected: Steam API "no", SteamStub "no", `steam_appid.txt` "no", CEG "no", launch path "Direct", rating "untested".
7. Import the synthetic folder and try to start it. Expected: Steam API "yes", and the app refuses to start it and names the marker it found (C-0008 section 5). Remove the synthetic title. Expected: it leaves the library.
8. Open the in-app console and run the L6 smoke program (S6.0.1). Expected: the JIT setup with StikDebug runs as in L3 and JIT reaches `ready` (C-0006 section 5). A rotating triangle then fills the screen and a steady tone plays. With the controller connected, the left stick changes the rotation and the bottom face button changes the color.
9. While the tone plays, start Siri and dismiss it. Expected: the tone stops while Siri is active and comes back afterwards.
10. Quit the smoke program from the in-game menu. Expected: the relaunch prompt chosen in D9 appears, and nothing else can be started in this launch (C-0004 section 1). The in-app console still shows the program's output: the controller states it read and the memory status it printed.
11. Relaunch Paneport as the prompt asks. Stay on the library screen for about 10 seconds, then open the memory developer screen (M6.3). Expected: it shows the memory limit and whether Game Mode is active. Another project saw the limit rise to about 8,192 MB a few seconds after the app came to the front, on a different model (process_and_memory_model.md Q6). Record what you see. A lower limit is not a failure in itself.
12. Start the game. Expected: the JIT setup of step 8, then the game full screen with sound. No Steam sign-in is involved.
13. Play with the controller until you reach gameplay. Open the in-game menu and choose the 10-minute measurement. Expected: the HUD shows a timer, the median and 1% low fps, the footprint, the limit with the Game Mode flag, and the thermal state.
14. During the measurement, turn the controller off for at least one minute and play with the on-screen gamepad, then turn the controller on again. If the game uses the mouse, use touches outside the gamepad as the mouse. Expected: the on-screen gamepad appears when the controller disconnects and controls the game. Touches outside it move and click as the chosen touch mode says. The gamepad hides again when the controller reconnects.
15. Play until the measurement ends after 600 seconds. Expected: the app stays open and the game keeps responding the whole time. The result shows a median of at least 55 fps, a 1% low of at least 45 fps, a peak footprint below the memory limit, and the highest thermal state.
16. Quit the game from the in-game menu. Expected: the game ends and the relaunch prompt appears.
17. Export the diagnostics of the game session, from the relaunch prompt or after relaunching, as `docs/design/session-flow.md` describes, and save the file on Windows. Expected: the file holds the session's metrics and logs. A spot check finds none of the items that C-0012 section 4 forbids, such as a device name, an Apple ID or a local path.
18. On Windows, run `tools/SessionMetrics.cs` on the export. Expected: its median and 1% low agree with step 15 within 0.5 fps. Its log summary shows that the game created a Direct3D 11 device through DXMT.
19. Optional: relaunch, connect a Bluetooth keyboard and mouse, start the game as in step 12, play with them for a few minutes, and quit as in step 16. Expected: keys and the mouse work in the game, and the mouse pointer stays locked to the game while it runs.

The phase 2 gate in C-0012 section 3 passes when "An imported 2D or indie DX11 game plays for 10 minutes on an iPhone 17 Pro with a median of at least 55 fps and a 1% low of at least 45 fps, and the app is not killed for memory."

How the steps check the gate:

| Gate criterion (C-0012 section 3) | Checked by |
|---|---|
| An imported game | Steps 3 and 5 |
| A 2D or indie Direct3D 11 game | The D6 choice, and step 18 for Direct3D 11 |
| Plays for 10 minutes | Steps 13 to 15 |
| On an iPhone 17 Pro | Step 2 |
| A median of at least 55 fps | Steps 15 and 18 |
| A 1% low of at least 45 fps | Steps 15 and 18 |
| The app is not killed for memory | Step 15: the app stays open, and the peak footprint stays below the limit |

The gate passes when the steps in the table meet their expected results. The other steps check L6 features: the classification and refusal rules (C-0007 section 5, C-0008 section 5), the relaunch prompt that C-0004 section 1 asks a device test to confirm, audio interruptions, the touch overlay and the export. A failure there is recorded and fixed, but does not fail the gate by itself. recelilious decides whether L6 is accepted.

- If the test device is an iPhone 17 Pro Max, 18 Pro or 18 Pro Max, the record states its model identifier, and the run does not meet the "on an iPhone 17 Pro" criterion. The gate table is authoritative, so the gate can then pass only through a waiver. Only recelilious can waive it, with a new evidence record that states the reason (C-0012 section 3).
- The record states whether the shader cache was warm. A first session compiles shaders and can show a lower 1% low (graphics_directx_to_metal.md KQ7). If a first session misses the gate only for that reason, a second run with a warm cache may follow. The record then reports both runs and says which one the verdict rests on, and recelilious gives the verdict.

**Evidence record.**

- File: `docs/evidence/YYYY-MM-DD-l6-2d-game.md`, dated in UTC on the day of the gate run (C-0012 section 4). The integration session writes the template. recelilious fills in the results, or hands over notes and the export.
- Fields that C-0012 section 4 requires:
  - the gate (phase 2, "A 2D game") and the build: commit hash and version, and the note that it is the device test app;
  - the device model identifier, and the iOS version and build;
  - the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app;
  - the JIT enabler and its version, whether Game Mode was active, and the memory limit read during the run (at launch, lowest and highest);
  - the steps with their expected and observed results, the metrics, and scrubbed log excerpts;
  - the verdict (Pass, Fail or Waived), the tester as `recelilious` or `Claude`, and the date.
- Metrics (C-0011 section 1): the measurement window; the number of frames; the median and 1% low fps with the definition used; the average of the slowest 1% of frames; the peak footprint; the footprint just before the game's first process started, which gives `memoryEstimateMiB` (C-0008 section 2); the JIT pool size; the thermal state over time and the highest state; whether the shader cache was warm; the input used (controller type, minutes on the on-screen gamepad).
- The title: `local-<slug>`, its name, its `gameBuild` (the SHA-256 of the program file, C-0008 section 4), the graphics API seen in the log, and the compatibility settings in effect.
- The before and after table from M6.6.
- Never in the record or its attachments: UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). Also no game files and no screenshots of the game, so no game artwork enters the repository. Claude runs the personal-data check of C-0011 section 2 on the record before pushing it.

**Integration session.**

1. Check that M6.1 to M6.6 are Done and that D6 is recorded. Update `main` and create `feat/l6-2d-game`.
2. Complete and check the wiring of the Medium stages in the app (plan section 3.1): the input sink and the in-game menu, the audio session, the memory launch check and the HUD data, and the title's reviewed entry with M6.6's settings in the bundled snapshot.
3. Build S6.0.1 and ship it in the device test app next to the test programs that the in-app console of L4 runs. Run it on the harness together with every harness case of M6.1 to M6.6.
4. Run the `dotnet`, `native`, `ios`, `runtime` and `checks` jobs on the branch. The `runtime` job is the one M4.2 adds (plan section 8).
5. Write the evidence record template with every field above. Update the acceptance steps in this brief with the final names of screens and menu items.
6. Open the pull request with the device test app build for recelilious, from the `ios` job or from a Mac (D1), and mark device behavior "not yet verified on device" (C-0012 section 1 item 9). recelilious installs it through the L1 install route.
7. After recelilious's run, fill in the record from recelilious's notes and the export, and recompute the numbers with `tools/SessionMetrics.cs`. Add the test report to `compat/titles/local-<slug>.json` with the fields C-0008 section 4 requires (`date`, `device`, `iosVersion`, `paneportVersion`, `gameBuild`, the `rating` this test observed and the `evidence` path) and with `fpsMedian`, `fpsLow1`, `peakMemoryMiB` and `thermalState`. Its `paneportVersion` is a commit hash, so the title's own `rating` stays `untested` (C-0008 section 3). Update the phase 2 row in `docs/evidence/README.md` (C-0012 section 3), and create that file with its gate table first if no earlier record has (plan section 7.7).
8. Set L6 to Accepted once recelilious accepts it and the record is merged. If the gate fails, record Fail, and recelilious decides the next step. Only recelilious can waive a gate.
9. Remind recelilious that plan section 8 expects the Direct3D 9 and Direct3D 12 decision (D11) after L6.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.0.1 | L6 smoke program: one Windows program that renders the rotating Direct3D 11 triangle, plays a 440 Hz tone through WASAPI, reads XInput (the left stick changes the rotation, the bottom face button changes the color) and prints its memory status. It exercises graphics, audio, input and the memory report in one session. | `tests/win32/` (program), `tests/runtime/` (harness case) | Builds with llvm-mingw for x86-64. On the harness, with scripted input through `pp_input_submit` and the audio capture back end of M6.2: the printed input transcript matches the script, the pixel at the triangle's center has the new color after the scripted button press, the captured tone is 440 Hz within 1%, the physical memory it prints equals the value the host derived from the harness's fake limit (M6.3), and the program exits with code 0. |

**Session log.**

_No sessions yet._

## M6.1 Input stack

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M5.1, M2.1 |
| Decisions needed | None from plan section 4.2. The stage chooses the gamepad path into Wine (S6.1.3) and records the choice in `docs/design/input.md`. |
| Size | L |
| Branch | `feat/m6-1-input-stack` |
| Design documents | Writes `docs/design/input.md`. Reads the design documents of M2.1 (overlay) and M5.1 (display driver). |

**Goal.** Bring controller, keyboard, mouse and touch input from iOS to Windows programs. Input enters the native host through `pp_input_submit` (C-0005 section 2) and crosses to the Wine fork through the bridge (C-0005 section 3). Games receive it as XInput controller state and as keyboard and mouse input. Players without a controller get an on-screen gamepad and a touch-to-mouse mode.

**Scope.**

In:

- The host's input queue. It is lock-free and owned by the native host (C-0004 section 1). It takes batches of fixed-size events for gamepad state, keys, mouse movement and buttons, and touches (C-0005 section 2). Each event struct starts with `struct_size` (C-0005 section 1). New functions and members raise the minor ABI version, and the conformance table covers the new structs (C-0005 sections 1 and 5).
- Bridge members through which the fork drains the queue, and a way to wake the driver's event loop when input arrives. The research describes the usual Wine way, which wakes the GUI thread through its queue file descriptor (wine_on_ios.md KQ3). The design document picks the mechanism.
- Capture in Paneport.iOS, which owns GameController input (C-0004 section 2):
  - `GCController` for Xbox and PlayStation pads and MFi controllers. Controllers that attach to the phone, such as a Backbone, are expected to appear as MFi controllers; to be confirmed in this stage.
  - `GCKeyboard`. All keyboards arrive merged through `GCKeyboard.coalesced` (precedents_and_ux_layer.md KQ4).
  - `GCMouse`, which delivers only relative deltas (precedents_and_ux_layer.md KQ4).
- Pointer lock. While a game runs and a mouse is connected, the game's view controller asks for it with `prefersPointerLocked`. iOS honors it only when the scene is full screen and in the foreground, so the stage observes the lock state and asks again when it changes (precedents_and_ux_layer.md KQ4). The driver keeps the Windows cursor position from the deltas.
- The gamepad path into Wine (S6.1.3), chosen between two options:
  - Option A, preferred: a `winebus.sys` backend in the Wine fork, fed by the host's gamepad state. If Wine's XInput, DirectInput and RawInput paths all read HID devices from `winebus.sys`, as the stage expects to confirm, Option A serves all of them at once. Upstream Wine hosts such drivers in `winedevice.exe` processes, and an iOS precedent runs no `winedevice.exe` and replaces the driver host with in-process code (process_and_memory_model.md Q1). Option A is taken only if the single-process session of M4.3 can host the driver stack without several pseudo-processes.
  - Option B, fallback: the fork's XInput modules read the gamepad state from the bridge directly. The research infers from the iOS precedents' "via XInput" statements that they use a provider of this kind, and found no evidence of a HID backend there (precedents_and_ux_layer.md KQ4). DirectInput and RawInput for gamepads then wait for a follow-up after M8.1.
- Keyboard and mouse in the iOS display driver from M5.1 (named `wineios.drv` in the roadmap):
  - HID usage codes from `GCKeyboard` mapped to Windows scan codes and virtual keys, for a US layout first. Which keyboard entry points of `user_driver_funcs` version 110 (wine_on_ios.md KQ3) the driver must implement, and which defaults are enough, is to be confirmed in this stage.
  - Mouse buttons, the wheel, relative motion and raw mouse input for games that capture the mouse, and absolute positions otherwise.
  - The Windows cursor: the driver receives the cursor image and position. How the cursor is drawn above the game image on iOS is decided in the design document.
- The on-screen gamepad:
  - A layout model in Paneport.Core with fractional screen coordinates and element kinds for buttons, sticks and the d-pad, each with its own hit area and dead zone.
  - Controls in Paneport.UI, which holds the in-game overlay controls (C-0004 section 2), drawn in the transparent overlay of M2.1. They produce gamepad state.
  - The overlay acts as one controller. It hides when a physical controller connects and shows again when the last one disconnects. The in-game menu of M6.5 can also show or hide it.
  - The design of the per-title controller or touch layout field, which C-0008 section 2 adds together with the design document for on-screen controls: its name, its values and its default, which selects the one default layout this stage ships. M6.6 adds the field to the schema (S6.6.6).
- A touch-to-mouse mode. Touches that start outside the overlay's controls fall through to the game view (M2.1), which turns them into mouse events. In direct mode a touch moves the pointer to that position and clicks. In trackpad mode a finger moves the pointer by its motion. A setting chooses direct, trackpad or off. Positions are normalized to the game view, and the driver maps them to the virtual monitor.
- An input sink interface in Paneport.Core. Paneport.iOS implements it through Paneport.Interop, and the fake host of Paneport.Desktop implements it too, so the overlay can be tried on Windows.
- The XInput reader in `tests/win32/` and an input case in `tests/runtime/`.
- An input developer screen in `tests/device/` that shows the events the host receives.

Out:

- Controller haptics and rumble, and an editor for on-screen controls (plan section 10).
- Changing the title schema. This stage's design document defines the layout field, but M6.4, which owns the schema, is not among this stage's dependencies. M6.6 adds the field as an optional schema field under C-0008 section 5.
- Text input methods and keyboard layouts other than US.
- Windows touch and pointer messages. Touches reach games only as the mouse.
- Steering the app's own screens with a controller.
- DirectInput and RawInput for gamepads, if Option B is chosen.

**Read first.**

- C-0004 sections 1 and 2; C-0005 sections 1, 2, 3 and 5; C-0008 sections 2 and 5 (the layout field); C-0010 sections 2, 3 and 4; C-0011 sections 1 and 4; C-0001 section 6 (other products' documentation may be read only to learn what is feasible).
- precedents_and_ux_layer.md KQ4; avalonia_ui_layer.md section 3.1 (rules 4, 5 and 8) and section 4 (E9); wine_on_ios.md KQ3; process_and_memory_model.md Q1; roadmap-2026-10.md, its paragraph on audio and the UI shell.
- The design documents of M2.1 (overlay) and M5.1 (display driver).

**Deliverables.**

- `src/native/include/paneport/`: the input event structs, `pp_input_submit`, and the new members of `paneport/wine_bridge.h`.
- `src/native/host/`: the queue and the input bridging. `tests/native/`: the queue tests.
- `src/Paneport.Interop/` and `tests/Paneport.Interop.Tests/`: the binding and the conformance entries.
- `src/Paneport.Core/` and `tests/Paneport.Core.Tests/`: the layout model, the hit mapping, the touch-to-mouse mapping and the input sink.
- `src/Paneport.UI/` and `tests/Paneport.UI.Tests/`: the overlay controls.
- `src/Paneport.iOS/`: GameController capture, pointer lock and the game view's touch handling.
- `src/Paneport.Desktop/`: the fake host's input sink.
- `external/Paneport-wine`: the gamepad path and keyboard and mouse input, on the fork's `paneport/<stage ID>` branch (plan section 3.3). This repository moves the submodule pointer.
- `tests/win32/`: the XInput reader. `tests/runtime/`: the input case. `tests/device/`: the input developer screen.
- `docs/design/input.md`, with a row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.1.1 | Input queue and API: `pp_input_submit` [any], the multi-producer queue, the bridge drain and the wake-up. | `src/native/host/`, `src/native/include/paneport/`, `src/Paneport.Interop/` | CTest under the `asan-ubsan` and `tsan` presets: several producer threads and one consumer; every event arrives once, intact and in order per producer; overflow is counted and reported; no sanitizer report. The conformance test matches the C# structs. |
| S6.1.2 | GameController capture: controllers, keyboards and mice, connection changes, player slots and pointer lock. | `src/Paneport.iOS/` (framework calls), `src/Paneport.Core/` (mapping from element values to event fields) | xUnit tests of the mapping with fake readings: every button, trigger and stick value lands in the right field with the documented scaling. The iOS code builds in the `ios` job. |
| S6.1.3 | Gamepad path into Wine, Option A or Option B. | `external/Paneport-wine` | Harness: scripted gamepad states submitted through `pp_input_submit` are printed by the XInput reader with exact button masks, and trigger and stick values within 1 unit of the documented conversion. |
| S6.1.4 | Keyboard and mouse in the iOS display driver, including the cursor image. | `external/Paneport-wine` | Harness: scripted key presses and mouse events produce the expected key, character and mouse messages and raw mouse input, as printed by the reader. |
| S6.1.5 | Virtual gamepad overlay: layout model, hit mapping and overlay controls. | `src/Paneport.Core/`, `src/Paneport.UI/` | xUnit tests of the hit mapping: a point maps to the right element; stick deflection and the dead zone; two fingers on two elements at once; a touch that starts outside every element passes through. Headless UI tests: taps and drags on the rendered overlay produce the expected gamepad states through a fake sink. |
| S6.1.6 | Touch-to-mouse mode: the direct and trackpad mappings. | `src/Paneport.Core/` (mapping), `src/Paneport.iOS/` (game view touches) | xUnit tests: normalized positions, motion scaling, clamping at the edges, a tap as a click, and the documented gesture for the right button. |
| S6.1.7 | XInput reader test program: a Windows window program that prints one line per change of controller state, key, character, mouse message and raw input, in a fixed format that the harness parses. | `tests/win32/` | Builds with llvm-mingw for x86-64. On Windows with a real controller it prints the documented lines (an optional manual check). |

**Verification.**

- `dotnet` job: `dotnet test` of `eng/ci.slnf` passes, including the new Core and UI tests, and `dotnet format --verify-no-changes` passes.
- `native` job: CTest passes under `asan-ubsan` and `tsan` with no sanitizer report. `tests/Paneport.Interop.Tests` passes against the host's test build with the new structs.
- `ios` job: Paneport.iOS builds with the capture code.
- `runtime` job (added by M4.2, plan section 8): the input case starts a session, runs the XInput reader, submits a scripted sequence of at least 50 gamepad states and 30 key and mouse events, and compares the reader's output with the expected transcript. Pass: every scripted change appears once and in order, with no extra lines, within 60 seconds. The case also records the delay from submit to print for the design document. That delay is not a pass criterion.
- `checks` job passes.
- Scripts this stage creates: the input harness case and the XInput reader.
- Device check (recelilious, optional before L6): on the input developer screen, every button, trigger and stick of a connected controller shows up, and so do keys and mouse motion. The XInput reader, run from the in-app console, lists the same changes.

**Done when.** Every automated check above passes on the last commit of the branch. `docs/design/input.md` records the gamepad path with the harness evidence, the wake-up mechanism, how the cursor is drawn, the default layout and the layout field. The pull request marks device behavior as "not yet verified on device" unless the device check ran (C-0012 section 1 item 9).

**Risks and fallbacks.**

- Option A may need `winedevice.exe` as a separate pseudo-process. Then Option B ships, and Option A becomes a proposed follow-up after M8.1.
- The on-screen gamepad and the touch pass-through build on the transparent overlay of M2.1. If the overlay gate failed and recelilious adopted its fallback, drawing the gamepad inside the game's Metal pass (C-0012 section 3), the overlay controls move there and the design document records it.
- Through Avalonia 12.1.4, touch drags arrive at about 60 Hz on ProMotion (avalonia_ui_layer.md section 3.1 rule 5). If the stick feels late, the game view handles the stick's touches natively with the same layout model, and the overlay only draws. The last fallback is `GCVirtualController`, which the same rule names.
- Hardware keyboard events may not reach `GCKeyboard` while an Avalonia text box has focus (avalonia_ui_layer.md section 4, E9). The overlay keeps focus away from text boxes while a game runs.
- One report describes mouse deltas that lag at times while the pointer is locked, and the research found no source on pointer lock with a mouse on iPhone, as opposed to iPad (precedents_and_ux_layer.md KQ4). Mouse play is optional in L6 (step 19), and the device check watches for both problems.
- If the US layout misses keys a game needs, the missing keys are listed for a follow-up.

**Hands over to.** M6.6 (input settings for the test title, the layout field it adds to the schema, and the input sink that M6.5's in-game menu holds back while it is open), L6 (play with a controller and the on-screen gamepad, and S6.0.1), L7 and L8 (games started from Steam use the same input). Haptics and the layout editor stay in the backlog.

**Session log.**

_No sessions yet._

## M6.2 Audio stack

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M4.4 |
| Decisions needed | None. |
| Size | M |
| Branch | `feat/m6-2-audio-stack` |
| Design documents | Writes `docs/design/audio.md`. Reads the design documents of M4.3 and M4.4 (single-process Wine and the runtime on iOS). |

**Goal.** Windows programs play sound on the iPhone. Wine's audio driver outputs through the iOS RemoteIO unit under an `AVAudioSession`. Games see ordinary mmdevapi (WASAPI) endpoints, and XAudio2 games work through Wine's FAudio-based modules. iOS interruptions pause and resume the sound through `pp_audio_set_active` (C-0005 section 2).

**Scope.**

In:

- `winecoreaudio.drv` in the Wine fork, ported as the research describes (graphics_directx_to_metal.md KQ10):
  - HAL device enumeration replaced by one virtual default output device;
  - `kAudioUnitSubType_HALOutput`, which is macOS-only, replaced by `kAudioUnitSubType_RemoteIO`, which macOS does not have;
  - the AudioUnit render callback and the AudioConverter resampling kept.
  iOS-specific code goes into separate files such as `_ios.c` where upstream's structure allows it (C-0010 section 4).
- An output layer inside the driver with three back ends: RemoteIO on iOS; upstream's HAL path on macOS, for local runs on a Mac; and a capture back end, built only into test builds and selected only by the harness, that writes the rendered samples to a file. Whether GitHub's macOS runners have an audio output device is to be confirmed in this stage. The capture back end makes the harness independent of the answer.
- The render callback runs on the system's audio thread. It only copies samples from a lock-free buffer. It runs no guest code and never calls into .NET (C-0004 section 1).
- The audio session in Paneport.iOS: the playback category, activated when a session starts. Interruption notifications are mapped to `pp_audio_set_active` on the main thread, since C-0005 section 2 marks that function [main]. The host passes the state to the driver through a bridge member or through the fork's entry header (C-0005 section 3). The design document picks one.
- mmdevapi endpoints: one default render endpoint with a documented mix format, and no capture endpoint.
- XAudio2 through Wine's built-in FAudio-based modules. The research says FAudio depends on SDL 3.2 or later (graphics_directx_to_metal.md KQ10). Whether Wine's built-in copy needs SDL on iOS is to be confirmed in this stage. FAudio and SDL are both registered as Allowed (C-0003 section 6).
- Buffer sizes. The research expects larger buffers than Wine's defaults while translation loads the CPU (graphics_directx_to_metal.md KQ10). The design document sets them and says how to change them.
- The tone program in `tests/win32/` and the audio cases in `tests/runtime/`.

Out:

- Audio capture and the microphone.
- Video and media decoding. FFmpeg is not registered under C-0003, and GStreamer's GPL plugins are excluded (C-0003 section 6).
- MIDI, layouts beyond stereo, and spatial audio.
- Audio while the app is in the background.

**Read first.**

- C-0004 sections 1 and 2 (the Wine fork owns "RemoteIO audio"); C-0005 sections 1, 2 and 3; C-0003 sections 5 and 6; C-0010 section 4; C-0011 sections 1 and 4.
- graphics_directx_to_metal.md KQ10; wine_on_ios.md KQ7 and KQ9; roadmap-2026-10.md, its paragraph on audio and the UI shell.
- The design documents of M4.3 and M4.4.

**Deliverables.**

- `external/Paneport-wine`: the driver port, the output layer and the build of the XAudio2 modules, on the fork's `paneport/<stage ID>` branch. This repository moves the submodule pointer.
- `src/native/host/` and `src/native/include/paneport/`: `pp_audio_set_active` and its path to the driver. `tests/native/`: its tests.
- `src/Paneport.iOS/`: the audio session and the interruption observer.
- `tests/win32/`: the tone program. `tests/runtime/`: the audio cases.
- `docs/design/audio.md`, with a row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.2.1 | RemoteIO output: the output layer with its RemoteIO, HAL and capture back ends. | `external/Paneport-wine` | The `ios` job builds the RemoteIO back end. On the harness, a known buffer that needs no conversion reaches the capture back end unchanged. |
| S6.2.2 | Audio session and interruptions: category, activation, interruption observer and `pp_audio_set_active`. | `src/Paneport.iOS/`, `src/native/host/` | CTest with a fake output: inactive stops the output, active restarts it, and repeated calls change nothing. The iOS side builds in the `ios` job. |
| S6.2.3 | Endpoints: one default render endpoint and its mix format. | `external/Paneport-wine` | Harness: the tone program's endpoint listing shows exactly one default render endpoint, with the documented format. |
| S6.2.4 | Tone test program: lists the endpoints, then plays a 440 Hz sine tone for 2 seconds through WASAPI shared mode, or through XAudio2 when started with a switch. | `tests/win32/` | Builds with llvm-mingw for x86-64. On Windows it plays an audible tone (an optional manual check). |
| S6.2.5 | XAudio2 path: Wine's FAudio-based modules, built and loaded in the session. | `external/Paneport-wine` | Harness: the tone program in XAudio2 mode passes the same capture check as in WASAPI mode. |

**Verification.**

- `runtime` job: the audio cases run the tone program in WASAPI mode and in XAudio2 mode with the capture back end. Pass for each mode: exit code 0; captured length within 50 ms of 2 seconds; dominant frequency within 1% of 440 Hz, computed by the harness; no silence longer than 50 ms between the first and the last sample.
- `native` job: CTest passes under `asan-ubsan` and `tsan`. The conformance test passes if any struct changed.
- `ios` job: the fork and Paneport.iOS build for iOS.
- `dotnet` and `checks` jobs pass.
- Scripts this stage creates: the tone program and the audio harness cases.
- Device check (recelilious, optional before L6 and part of L6 steps 8 and 9): the tone program, run from the in-app console, is audible and steady, and an interruption pauses and resumes it.

**Done when.** The automated checks pass on the last commit. `docs/design/audio.md` records the back ends, the buffer sizes, the session category, the interruption path, route changes such as headphones, and the answer to the SDL question. Device behavior is marked "not yet verified on device" until the device check runs (C-0012 section 1 item 9).

**Risks and fallbacks.**

- If Wine's built-in FAudio needs SDL on iOS, SDL's iOS audio back end is built and bundled. SDL is already registered (C-0003 section 6).
- If the sound crackles under load, the buffers grow, as the research expects.
- If a route change, such as plugging in headphones, stops the output, the driver restarts the unit.
- If the RemoteIO unit cannot restart after an interruption, the session goes on without sound and logs the reason, rather than ending the game.

**Hands over to.** M6.6 (buffer tuning for the test title), L6 (game audio, the interruption check and S6.0.1), L7 and L8 (games started from Steam use the same audio).

**Session log.**

_No sessions yet._

## M6.3 Memory governor and file-backed tier

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M4.4 |
| Decisions needed | None from plan section 4.2. The tier's backing file is a kind of file that C-0004 section 4 does not list. If its place needs a new row there, recelilious approves that contract change first (C-0012 section 2). |
| Size | L |
| Branch | `feat/m6-3-memory-governor` |
| Design documents | Writes `docs/design/memory.md`. Reads `docs/design/signals.md` (M2.2), `docs/design/jit.md` (M3.1) and the design document of M4.4. |

**Goal.** Keep a session inside the iOS memory limit. The host measures the limit and the footprint all the time, reports a physical memory size to Windows that fits the limit, refuses titles that cannot fit, and raises pressure and thermal events. It also offers a file-backed tier for large guest allocations, whose pages the research says do not count against the limit.

**Scope.**

In:

- The limit sampler. The memory limit is `os_proc_available_memory()` plus the current `phys_footprint`, sampled again over time, and Game Mode is reported when the limit reaches about 8,192 MB. Private calls such as `memorystatus_control` are not used (C-0005 section 2).
  - It samples every few seconds normally, and every 250 ms near the limit. Another project switches to 250 ms within about 1.2 GB of the limit (process_and_memory_model.md Q7).
  - It keeps the peak footprint and a timeline for the HUD and the evidence.
  - It backs `pp_memory_get_budget` (C-0005 section 2), and replaces any single reading that an earlier stage put behind that function.
  - On the macOS harness the reading comes from a fake source set by the harness. Apple documents that `os_proc_available_memory()` returns 0 for a process that is not an app (process_and_memory_model.md Q5), and the harness needs repeatable values anyway.
- The JIT pool counts against the limit once it is authorized (C-0006 section 4). The sampler sees it in the footprint, and the readout shows its size separately.
- The physical memory size reported to Windows. The host derives it from the memory limit when the session starts (C-0005 section 2), and the Wine fork reads it through the bridge (C-0005 section 3). The fork reports it through every Windows call that reports physical memory. The research names `GlobalMemoryStatusEx`, `MmNumberOfPhysicalPages` and `NtQuerySystemInformation`, and warns never to report the device's installed RAM (`hw.memsize`), because engines size their pools from this value (process_and_memory_model.md Q7). The design document gives the formula. It also decides whether the host waits a few seconds for Game Mode before it fixes the value.
- The file-backed tier:
  - Large writable, non-executable guest commits are backed by a shared mapping of a file. The research reports that iOS writes dirty pages of a `MAP_SHARED` file mapping back to the file and does not charge them to `phys_footprint` (process_and_memory_model.md Q6). This is to be confirmed on the device in this stage.
  - The host owns the backing file and its accounting. The Wine fork's virtual memory code calls the tier through the bridge for eligible commits, aligned to the 16 KB host page.
  - Releasing memory frees the file's blocks. The research names `F_PUNCHHOLE` for this; to be confirmed against Apple's documentation in this stage.
  - Tier mappings that hold guest memory are registered as guest reservations, so the dispatcher routes their faults to the guest path (C-0005 sections 3 and 4).
  - The tier stays off by default until the device check confirms the accounting. The design document says when the governor turns it on.
- The backing file's place in the container. The design document proposes it, for example an unlinked file in `Library/Caches/Paneport/`. If that needs a change to C-0004 section 4, the change goes through the contract process before this code merges.
- Governor policy:
  - The launch check in Paneport.Core. A title is refused when its `memoryEstimateMiB` (C-0008 section 2), plus the footprint the session will have just before the game's first process starts, plus a safety margin, exceeds the memory limit. The check runs before JIT is set up (M6.5), so it adds the JIT pool that the launch will request (C-0006 section 4) and the session's own start-up cost, which the design document measures, to the current footprint. The message gives the numbers. The research suggests a margin of 300 to 500 MB (process_and_memory_model.md Q7). If the estimate fits only at the Game Mode level, the check waits briefly for Game Mode, as the design document says. Titles with no estimate start, and the governor watches them.
  - Pressure levels derived from the headroom between the footprint and the limit, delivered as memory pressure events with a level (C-0005 section 2). Whether iOS's own memory warnings are used as well is to be confirmed in this stage.
- Thermal state. The host reads `thermalState` of `ProcessInfo` (ios_platform_constraints.md section 8) and delivers thermal state change events (C-0005 section 2). The host is the memory and thermal governor (C-0004 section 2).
- The shell's reaction. Paneport.Core receives pressure events through Paneport.Interop and releases the caches it owns. The research's target for the shell while a game runs is 150 MB or less (roadmap, risk 4). M6.6 measures it.
- A memory developer screen in `tests/device/` with the limit, footprint, peak, Game Mode flag, thermal state and the tier canary.
- New functions or appended struct members raise the minor ABI version (C-0005 section 1).

Out:

- RAM donors in app-extension processes. Any extension process needs a change to C-0004 section 1, and the research lists donors as a research item.
- Private calls such as `memorystatus_control` (C-0005 section 2).
- Moving PE image sections into the tier.
- 32-bit guest windows (M7.3).
- A per-title tier switch in the compatibility schema, unless the device check shows that one is needed. It would be an optional field under C-0008 section 5.

**Read first.**

- C-0005 sections 1 to 5; C-0006 section 4; C-0008 section 2; C-0004 sections 1, 2 and 4; C-0009 section 4 (the memory entitlement and the Game Mode keys); C-0011 section 1 (device metrics); C-0012 section 3 (the phase 4 gate needs the peak footprint, the limit and the Game Mode state).
- process_and_memory_model.md Q5, Q6 and Q7; ios_platform_constraints.md sections 4 and 8; roadmap-2026-10.md, its section on the three physical ceilings and its risk 4.
- `docs/design/signals.md` (guest reservations and fault routing), `docs/design/jit.md` (the pool) and the design document of M4.4.

**Deliverables.**

- `src/native/host/` and `src/native/include/paneport/`: the sampler, the derivation, the tier service, the policy and the events. `tests/native/`: their tests.
- `external/Paneport-wine`: the physical memory report and the tier hook, on the fork's `paneport/<stage ID>` branch. This repository moves the submodule pointer.
- `src/Paneport.Interop/` and `tests/Paneport.Interop.Tests/`: bindings and conformance entries.
- `src/Paneport.Core/` and `tests/Paneport.Core.Tests/`: the launch check and the event handling.
- `tests/win32/`: a memory report program and a tier allocation program. `tests/runtime/`: their cases.
- `tests/device/`: the memory developer screen with the tier canary.
- `docs/design/memory.md`, with a row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.3.1 | Limit sampler: limit, footprint, peak, Game Mode flag, cadence and timeline. | `src/native/host/` | CTest with a fake clock and fake readings: the cadence switches to 250 ms when the headroom falls below the threshold, and back when it recovers; Game Mode is reported only at the documented level; the peak is the largest value seen. |
| S6.3.2 | Reported physical memory: the derivation in the host and the report in the fork. | `src/native/host/`, `external/Paneport-wine` | CTest of the formula. Harness: with a fake limit set by the harness, the memory report program prints exactly the value the host derived, from every call it uses. |
| S6.3.3 | File-backed tier: backing file, mapping, release, accounting and the fork's hook. | `src/native/host/`, `external/Paneport-wine` | Harness: the tier allocation program commits, fills, reads back, decommits and frees 256 MiB in blocks. Every byte reads back as written, and the tier's counters return to zero after the frees. |
| S6.3.4 | Governor policy: pressure levels in the host, the launch check in Paneport.Core. | `src/native/host/`, `src/Paneport.Core/` | CTest with synthetic footprint series: each level is entered and left at the documented thresholds, with hysteresis. xUnit tests of the launch check with fake budgets and estimates, including titles with no estimate. |
| S6.3.5 | Pressure and thermal events, and the data the HUD shows. | `src/native/host/`, `src/Paneport.Interop/`, `src/Paneport.Core/` | CTest under `tsan`: events raised on any thread reach the callback only on the host event thread (C-0005 section 1). xUnit tests: events become the HUD's memory and thermal readouts. |

**Verification.**

- `native` job: CTest passes under `asan-ubsan` and `tsan` with no sanitizer report, and the conformance test passes.
- `dotnet` job: the launch check and event tests pass.
- `runtime` job: the memory report case (exact match) and the tier case (data intact, counters back to zero, no fault) pass.
- `ios` and `checks` jobs pass.
- Scripts this stage creates: the memory report program, the tier allocation program and their harness cases.
- Device check (recelilious; required before the tier is turned on by default): on the memory developer screen, the canary writes 512 MiB through the tier. Pass: the footprint grows by less than 64 MiB. Another project saw 2 MB for the same amount (process_and_memory_model.md Q6). The same screen shows whether Game Mode raises the limit a few seconds after the app comes to the front.

**Done when.** The automated checks pass. `docs/design/memory.md` records the derivation, the thresholds, the pressure levels, the rule for which commits use the tier, the backing file's place (with any contract change already accepted), and the canary result or "not yet verified on device".

**Risks and fallbacks.**

- The accounting that the tier relies on is undocumented, and Apple could change it (process_and_memory_model.md Q7). If the canary fails, the tier stays off. L6 does not depend on it, because the 2D test title should fit without it.
- The tier trades memory for flash writes and can stutter when pages come back in (process_and_memory_model.md Q7). The governor uses it only for eligible large commits.
- If Game Mode does not engage, the limit stays near 6,144 MB (C-0009 section 4). The launch check and the reported memory follow the lower value.
- A reported size that is too high makes games allocate too much. One that is too low makes some games refuse to run or lower their settings. M6.6 tunes the formula with the test title.

**Hands over to.** M6.6 (connecting the launch check and the HUD data to M6.5's flow, `memoryEstimateMiB` for the test title, and tier tuning), L6 (the memory metrics and S6.0.1), L7 and L8 (larger games, and the phase 4 gate needs the peak footprint, the limit and the Game Mode state).

**Session log.**

_No sessions yet._

## M6.4 Compatibility database core

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M0.3 |
| Decisions needed | None. The CI validator waits for D8 in M8.3. |
| Size | M |
| Branch | `feat/m6-4-compat-db-core` |
| Design documents | Writes `docs/design/compat-db.md`. Reads `docs/design/app-shell.md` (M0.3). |

**Goal.** Define the title schema. Give the app typed models, a loader that merges the schema defaults, the bundled snapshot and the device layer under the precedence rules of C-0008 section 1, and a writer for the device layer.

**Scope.**

In:

- `compat/schema/title.schema.json`, in JSON Schema draft 2020-12. It implements C-0008 section 2 and never contradicts it (C-0008 section 1):
  - every field, with sizes in MiB and `schemaVersion` 1;
  - the slug pattern; `jitPoolMiB` from 256 to 896; `renderScale` greater than 0 and at most 1; `metalFx` `off` or `spatial`; the fixed list of runtime identifiers. That `frameRateCap` divides the display's refresh rate is checked at run time, since the schema cannot know the display;
  - `targetDevices`, with the model identifiers of the devices in C-0001 section 2 (C-0008 section 3). The research gives `iPhone18,1` for the iPhone 17 Pro and `iPhone18,2` for the 17 Pro Max. The identifiers of the iPhone 18 Pro and 18 Pro Max are to be confirmed in this stage.
- Allow-lists for `translator` and `environment` keys. They start empty or nearly so, and later stages add entries under C-0008 section 5. The environment allow-list never contains `SteamAppId`, `SteamGameId`, `SteamAppPath` or any other variable that `steam_api` or a DRM wrapper reads (C-0008 section 5).
- `drm` has no default that would allow a launch. Absent means not yet classified (C-0008 section 2).
- Typed models in Paneport.Core, read and written with `System.Text.Json` source generators (C-0010 section 2).
- The loader for the bundled snapshot (C-0009 section 4) and for the device layer in `Library/Application Support/Paneport/compat/` (C-0004 section 4). The app checks in code the rules it relies on: types, ranges, allow-lists and the launch rule. It needs no schema library. Full schema validation in CI comes in M8.3, after D8 (C-0011 section 5).
- Precedence. The effective settings are the schema defaults, then the bundled entry, then the device layer. A `drm` value set in the bundled entry wins over a computed one, and the device layer can never turn `launch` into `direct` when `drm` requires the Dock path (C-0008 section 1). Each `drm` member counts as a value of its own, so a reviewed entry can set `ceg` by hand, as C-0007 section 5 says, while the classifier computes the other three. `launch` is derived from `drm` as C-0008 section 5 says. A title with no entry runs with the defaults and shows as `untested`.
- The device layer store: one JSON file per title, named like the repository files (C-0008 section 1), written atomically. It holds the computed classification and the user's overrides. The folder is backed up (C-0004 section 4).
- Synthetic fixtures, valid and invalid, in the test project. They hold no game data and no personal data (C-0008 section 5), and they stay out of `compat/titles/`.
- A test that loads every file in `compat/titles/` through the loader's checks. Entries added before M8.3 are then checked in the `dotnet` job.
- READMEs for `compat/`, `compat/schema/` and `compat/titles/`, where they do not exist yet (C-0004 section 3).

Out:

- The CI compatibility check, with its file-name and rating checks (M8.3, after D8).
- Computing `rating`. The app shows the stored value, and `untested` for a title with no entry.
- Classification itself: M6.5 for local titles, M7.2 for Steam titles.
- The iOS side: the build step that copies `compat/` into the app bundle (C-0009 section 4) and the container paths of the snapshot and the device layer. Paneport.iOS comes with M1.1 and M1.3, which are not among this stage's dependencies, so M6.5 adds them.
- `compat/ios-builds.json` and its schema (C-0006 section 6), which M3.1 creates.
- Any data from other projects' compatibility lists (C-0008 section 5).

**Read first.**

- C-0008 sections 1 to 6; C-0007 section 5; C-0004 sections 2, 3 and 4; C-0009 section 4; C-0010 sections 2 and 7; C-0011 sections 1, 2 and 5; C-0001 section 2.
- roadmap-2026-10.md, its phase 4 paragraph, which places the database in the larger plan.
- `docs/design/app-shell.md` (M0.3), whose domain model the compatibility models join.

**Deliverables.**

- `compat/README.md`, `compat/schema/title.schema.json`, `compat/schema/README.md` and `compat/titles/README.md`.
- `src/Paneport.Core/`: the models, the loader, the precedence rules and the store.
- `src/Paneport.Desktop/`: a local folder for the fake host.
- `tests/Paneport.Core.Tests/`: the tests and the fixtures.
- `docs/design/compat-db.md`, with a row in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.4.1 | Title schema. | `compat/schema/` | The schema parses as JSON and declares draft 2020-12. An xUnit test reads it as plain JSON and checks that no environment allow-list key starts with `Steam` and that `targetDevices` matches the design document. A checklist in the design document maps every row of C-0008 section 2 to a schema clause. |
| S6.4.2 | Typed model and loader. | `src/Paneport.Core/` | xUnit: every valid fixture loads; every invalid fixture is rejected with the expected reason; loading, writing and loading again keeps every value. |
| S6.4.3 | Precedence rules. | `src/Paneport.Core/` | xUnit: one test for each rule of C-0008 section 1, including both exceptions; a reviewed entry that sets only `ceg` while computed values fill the other `drm` members; and tests of the launch rule of C-0008 section 5 for local and Steam titles. |
| S6.4.4 | Device layer store. | `src/Paneport.Core/` | xUnit with a temporary folder: an atomic replace (a failed write leaves the old file), one file per title, and file names that match the ids. |
| S6.4.5 | Fixtures. | `tests/Paneport.Core.Tests/` | The test code lists each invalid fixture with the rule it breaks. Review finds only fictional values in them, and the personal-data check of C-0011 section 2 passes on them once M0.2 has added it. |

**Verification.**

- `dotnet` job: `dotnet test` of `eng/ci.slnf` passes on Windows and Ubuntu, and the format check passes. Once M0.2 has added the trimmed publish of `tests/Paneport.TrimTest`, it shows no new warning.
- `checks` job: REUSE passes for the JSON files through `REUSE.toml` (C-0008 section 6). Once M0.2 has added the personal-data check, it passes on the fixtures.
- Pass: no failing test, and every rule of C-0008 sections 1 and 5 that the loader enforces has at least one test, as the design document lists.
- Scripts this stage creates: none besides the tests.

**Done when.** The checks pass. `docs/design/compat-db.md` describes how objects and arrays merge, how unknown fields and a newer `schemaVersion` are handled, and which rules the code enforces.

**Risks and fallbacks.**

- The schema can drift from C-0008. The checklist and the M8.3 check catch drift, and the contract wins.
- Merging nested objects can surprise. The design document fixes the semantics and the tests pin them.
- If an identifier of a target device cannot be confirmed, the schema lists the confirmed ones, and the design document records the gap for a later schema change.
- This stage depends only on M0.3. The trimmed test app and the personal-data check come from M0.2, which the plan runs in the same wave as M0.3 (plan section 6.3). If they are not merged when this stage runs, the pull request says which checks have not run. The personal-data check then covers `compat/` and `tests/` on every later run of the `checks` job (C-0011 section 2).

**Hands over to.** M6.5 (local title entries, the session configuration, and the iOS paths and bundling of the snapshot), M6.6 (the test title's entry and the layout field), M7.1 and M7.2 (Steam titles), M8.3 (the CI check reuses the models and the schema).

**Notes from earlier stages.**

- M0.3 ([L0 brief](L0-foundation.md), session log of 2026-10-30): `docs/design/app-shell.md` sections 2 and 10: the title schema must use the slug pattern of `TitleId`, `^[a-z0-9]+(-[a-z0-9]+)*$`, unless recelilious reads C-0008 section 1 differently, in which case the two are aligned through C-0008.

**Session log.**

_No sessions yet._

## M6.5 Game import and session flow

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M6.4, M5.2, M2.1 |
| Decisions needed | D9: how the app asks for the relaunch. recelilious decides before the stage starts. Options: (a) a screen that asks the user to close Paneport in the app switcher and open it again; (b) a button that ends the app, after which the user opens it again; (c) option (b) plus a local notification that reopens the app with one tap, which needs notification permission and is to be confirmed in this stage. There is also a question on `ceg` for local titles, under Risks and fallbacks. |
| Size | L |
| Branch | `feat/m6-5-import-and-session-flow` |
| Design documents | Writes `docs/design/session-flow.md` (with the D9 decision) and `docs/design/pe-classifier.md`. |

**Goal.** Let the user import a Windows game through the Files app, classify it by reading its files, and refuse it when it shows a Steam marker. Run it in a session built from its compatibility entry, with an in-game menu, a performance HUD, and the relaunch prompt after the session.

**Scope.**

In:

- Import:
  - A folder picked with `UIDocumentPickerViewController`, or placed in `Documents/Import/` through the Files app (C-0004 section 4, with the Info.plist keys of C-0009 section 4), goes into `Library/Application Support/Paneport/library/<slug>/`. The app creates `Documents/Import/` at start, so that the folder shows in the Files app. A folder that is already in the container is moved instead of copied where the file system allows it; to be confirmed in this stage.
  - Free space is checked first. The import shows progress and can be cancelled, and a cancelled import leaves nothing behind.
  - The library folder is excluded from backup, and the mark is set again after files are replaced (C-0004 section 4). The research notes that the flag can reset (precedents_and_ux_layer.md KQ5).
  - Removing a title deletes its files and its device-layer entry.
- The main program. It is matched by `exeSha256` against bundled `local-` entries, or the user chooses it among the `.exe` files. The choice is stored in the device layer.
- The PE classifier in Paneport.Core, where C-0007 section 2 puts classification:
  - Steam API: `steam_api.dll` or `steam_api64.dll` present anywhere in the install folder, or imported by any program file in it through its normal or its delay-load imports. File names and imported names match in any letter case, because the iOS file system is case-sensitive and Windows names are not (precedents_and_ux_layer.md KQ5);
  - SteamStub: a wrapper on the main program. The research found only one community-reported signal, a section name, and calls it unverified (steam_launch_single_process.md Key Question 6). The classifier implements the signals the design document lists, and treats an unusual or malformed main program as uncertain, never as clean;
  - `steam_appid.txt`: recorded, never created;
  - CEG: cannot be detected by reading files. The computed classification leaves it `unknown`, and the value comes from the reviewed entry (C-0007 section 5).
  The classifier only reads. It is written from public PE format documentation with our own structure definitions (C-0003 section 5), and nobody who writes it reads the source code of DRM-removal tools (C-0007 section 5). The result goes into the device layer, and a `drm` value from the bundled entry wins (C-0008 section 1).
- Synthetic PE files. A writer in the test project builds them for the unit tests, and a file-based tool, `tools/SyntheticPe.cs`, writes test folders for manual and device checks (C-0011 section 1). Its output is never committed.
- Local title entries: `local-<slug>`, with a slug of lowercase letters, digits and single hyphens that is unique in the folder (C-0008 section 1), and the `exeSha256` of the main program.
- Refusal. A local title is launched only when `steamApi` and `steamStub` are false and `ceg` is `no`. Otherwise the app refuses and shows which value blocks the launch (C-0008 section 5, C-0007 section 5).
- The launch flow:
  - choose the title, then run the memory launch check of M6.3 when it exists;
  - set up JIT when the state is not `ready`, with the sequence of C-0006 section 3 and the pool size of C-0006 section 4, including `jitPoolMiB`;
  - start the session with the configuration of C-0005 section 2: architecture, translator options, environment, blocked helper programs, graphics settings, the prefix from `prefix` and the title id, and the locale from the settings. An `isolated` prefix is created from the template prefix when it is missing (plan section 7.4);
  - launch `executable.path` with its working directory and arguments through `pp_process_launch`. The install folder must be reachable from Windows. How the single-process Wine fork publishes drive letters is to be confirmed against M4.3 and M4.4;
  - show clear messages for `PP_E_JIT_NOT_READY` and `PP_E_SESSION_EXISTS`.
- The relaunch prompt (D9). It appears when the session ends, whether the game quit, exited or crashed (C-0004 section 1). After a relaunch, if the previous launch left a crash record (C-0005 section 2), the app says so once.
- The in-game menu. A small overlay button opens it, and so does a controller button combination chosen in the design document. It offers Resume, Quit game, the HUD on or off, the on-screen gamepad on or off, and the 10-minute measurement. While it is open, no input reaches the game. Whether it also freezes the game's threads is decided in the design document. That would need a new host function under C-0005 section 1. Otherwise the menu only holds back input.
- The HUD:
  - It shows the median and 1% low fps over the last few seconds and over a measurement, the footprint, the limit, the Game Mode flag and the thermal state. It redraws at most twice a second, because an overlay that animates competes with the game for GPU time and wake-ups (avalonia_ui_layer.md section 3.1 rules 6 and 7, and section 4, E2).
  - Frame times come from host counters. The present path reports each present through the Wine bridge (C-0005 section 3) into a ring that the host keeps, and a new [any] function lets .NET read it (C-0005 section 1). Which part of the present path reports presents is to be confirmed against what M5.1 and M5.2 built. C-0004 section 2 lists the allowed dependency directions and gives the DXMT fork no direct path to the host.
  - The design document defines the 1% low. The proposal is the frame rate at the 99th percentile of frame times, with the average of the slowest 1% of frames reported as well.
- Metrics recording and export. The export holds the frame times, the memory samples, the thermal timeline, the Game Mode flag, the evidence fields that the app can know, and the session log. The recording is kept on disk, so the last session can still be exported after the relaunch. Before export the app removes the personal data listed in C-0012 section 4 (C-0004 section 4). `tools/SessionMetrics.cs` computes the median, the 1% low, the peak footprint and the thermal states over time from an export. It prints the metrics table of the evidence record, a `reports` entry for the title file (C-0008 section 4), and a summary of the session log that names the Direct3D device the game created, from log lines that the design document lists.
- The iOS side of M6.4's loader: the container paths of the bundled snapshot and the device layer, and the build step that copies `compat/` into the app bundle (C-0009 section 4), unless a stage this one depends on already added it.
- Connections to M6.1, M6.2 and M6.3. Where one of them is already Done, the launch flow, the menu and the HUD use it. Whatever is not yet available is connected by M6.6 or by the integration session of L6.

Out:

- Steam titles: their classification and direct launch (M7.2), and Dock launches (M8.2).
- Setup-task sessions such as installers (C-0004 section 1), and Microsoft runtimes (plan sections 7.5 and 10).
- Exporting saves (D10, L7).
- Games that start other Windows processes (M8.1).

**Read first.**

- C-0007 sections 1, 2 and 5; C-0008 sections 1, 2, 4 and 5; C-0004 sections 1, 2 and 4; C-0005 sections 1, 2 and 3; C-0006 sections 3, 4 and 5; C-0009 section 4; C-0011 sections 1 and 2; C-0012 section 4; C-0003 section 5; C-0001 section 7.
- steam_launch_single_process.md Key Question 6; precedents_and_ux_layer.md KQ5; avalonia_ui_layer.md sections 3.1 and 4; graphics_directx_to_metal.md KQ7 and KQ8.
- `docs/design/compat-db.md` (M6.4), `docs/design/app-shell.md` (M0.3), `docs/design/jit.md` (M3.1, the JIT flow), the design document of M5.2 (DXMT), and the overlay design document of M2.1.

**Deliverables.**

- `src/Paneport.Core/` and `tests/Paneport.Core.Tests/`: the import service, the classifier, local entries, the launch state machine, the statistics and the recorder, and the synthetic PE writer.
- `src/Paneport.UI/` and `tests/Paneport.UI.Tests/`: the import screens, title details, relaunch prompt, in-game menu and HUD.
- `src/Paneport.iOS/`: the document picker, the container paths (including those of M6.4's loader), the bundling step for `compat/`, the backup flag, and the share sheet for the export.
- `src/Paneport.Interop/`, `tests/Paneport.Interop.Tests/`, `src/native/host/`, `src/native/include/paneport/` and `tests/native/`: the frame-time counters and the session configuration.
- `external/Paneport-wine` or `external/Paneport-dxmt`, as found: present reporting, on the fork's `paneport/<stage ID>` branch. This repository moves the submodule pointer.
- `tools/SyntheticPe.cs` and `tools/SessionMetrics.cs`, with `tools/README.md` updated.
- `tests/win32/`: the console test program (plan section 7.2), extended to print its arguments, working directory and environment. `tests/runtime/`: the session configuration and present-count cases.
- `docs/design/session-flow.md` and `docs/design/pe-classifier.md`, with rows in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.5.1 | Import pipeline: picker, copy or move, progress, cancel, free space, backup flag and removal. | `src/Paneport.Core/`, `src/Paneport.iOS/`, `src/Paneport.UI/` | xUnit with a temporary folder and a fake backup flag: progress reaches 100%; a cancel leaves nothing; too little space refuses before copying; every replaced file is marked again. UI tests of the import screen. |
| S6.5.2 | PE classifier and synthetic PE generator. | `src/Paneport.Core/`, `tests/Paneport.Core.Tests/`, `tools/SyntheticPe.cs` | xUnit on generated files: each marker is found in each place that C-0007 section 5 names, including names in other letter cases and delay-load imports; a clean program in a clean folder classifies as clean; malformed files classify as uncertain; the folder's files are unchanged afterwards (hashes and times). A folder written by the tool is flagged by the classifier. |
| S6.5.3 | Local title entries: slug, `exeSha256`, the match with bundled entries, and the refusal rule. | `src/Paneport.Core/` | xUnit: slugs follow C-0008 section 1 and stay unique; the hash covers the main program; a matching bundled entry is found; every combination of `drm` values that C-0008 section 5 does not allow for a local title is refused. |
| S6.5.4 | Launch flow: state machine, session configuration and process launch. | `src/Paneport.Core/`, `src/Paneport.iOS/`, `src/Paneport.Interop/`, `src/native/host/` | xUnit against a fake host: a JIT state other than `ready` leads to the JIT setup; both error codes produce their messages; a refusal by the launch check stops the launch. Harness: a session started with a configuration taken from a fixture entry runs the console program, which prints its arguments, working directory and allow-listed environment exactly as configured. |
| S6.5.5 | Relaunch prompt and the crash-record notice. | `src/Paneport.UI/`, `src/Paneport.Core/` | UI tests: after a session-ended event from the fake host, the prompt appears as D9 decided and no launch is offered; with a crash record present at start, the notice appears once. |
| S6.5.6 | In-game menu. | `src/Paneport.UI/`, `src/Paneport.Core/` | UI tests: the overlay button and the controller combination open it; while it is open, a fake input sink that stands in for M6.1's passes nothing to the game; Quit calls `pp_process_terminate` and then `pp_session_stop` on the fake host, in that order. |
| S6.5.7 | HUD and frame-time counters. | `src/native/host/`, the present path in a fork, `src/Paneport.Core/`, `src/Paneport.UI/` | CTest under `tsan`: the counter ring with concurrent writes and reads. xUnit: the median and 1% low of synthetic frame-time series equal hand-computed values. Harness: the presents counted for M5.2's Direct3D 11 triangle program match the number of frames the program reports, within 1. If the program does not print a frame count yet, this stage adds it. |
| S6.5.8 | Metrics export and summary tool. | `src/Paneport.Core/`, `src/Paneport.iOS/`, `tools/SessionMetrics.cs` | xUnit: an export built with planted personal values (a device name, a local path, an e-mail address) contains none of them, and an export written before a simulated relaunch can still be read after it. The tool's numbers and log summary on a synthetic export equal the expected ones. |

**Verification.**

- `dotnet` job: all tests pass, including the UI tests, and the format check passes.
- `native` job: CTest passes under `asan-ubsan` and `tsan`, and the conformance test passes with the new structs.
- `runtime` job: the session configuration case and the present-count case pass as S6.5.4 and S6.5.7 state.
- `ios` and `checks` jobs pass. The personal-data check passes on the fixtures and the sample exports.
- Scripts this stage creates: `tools/SyntheticPe.cs`, `tools/SessionMetrics.cs` and the two harness cases.
- Device check: the whole flow is accepted in L6 (steps 5 to 18). Until then the pull request marks it "not yet verified on device".

**Done when.** The checks pass. `docs/design/session-flow.md` records D9, the launch states, the menu, the HUD definitions and the export format. `docs/design/pe-classifier.md` lists every signal with its public source, and records that real samples are checked only on the device, with results only in evidence records (C-0007 section 5).

**Risks and fallbacks.**

- `ceg` for local titles. C-0008 section 5 launches a local title only when `ceg` is `no`, and C-0007 section 5 says that CEG cannot be detected and is set by hand in the title's entry. This brief therefore takes `ceg` from the reviewed entry, which M6.6 writes for the test title, and any other local title is refused until it has one. Whether the user may set `ceg` in the device layer is a question for recelilious. If the answer needs a change to C-0007 or C-0008, it goes through the contract process (C-0012 section 2).
- The SteamStub signal is unverified (steam_launch_single_process.md Key Question 6). The classifier errs toward "uncertain", which refuses a local title. Real samples are checked only on the device: the D6 title in L6, and Steam titles in L7 and L8.
- Frame times taken at different points of the present path give slightly different numbers. The design document says where they are taken, and the evidence names it.
- If the drive setup of M4.3 and M4.4 cannot reach the library folder, the session adds a mapping for it, and the design document says how.
- The in-game menu, its button and the HUD draw in the transparent overlay of M2.1. If the overlay fallback of C-0012 section 3 was adopted, the menu and the HUD follow it, as in M6.1's risks.

**Hands over to.** M6.6 (the full flow and the measurement tools), L6 (the acceptance flow), M7.2 (the classifier and the direct launch for Steam titles), M8.2 (the launch flow gains the Dock path), M8.4 (the measurement tools).

**Notes from earlier stages.**

- M0.3 ([L0 brief](L0-foundation.md), session log of 2026-10-30): `docs/design/app-shell.md` section 10 leaves to this stage whether Wine needs the locale setting in another form, such as `de_DE.UTF-8`, which the session flow then converts.

**Session log.**

_No sessions yet._

## M6.6 Performance pass for the test game

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L6 |
| Depends on | M6.1, M6.2, M6.3, M6.5 |
| Decisions needed | D6: the L6 test title. The L6 brief above lists what the title must fit. |
| Size | L |
| Branch | `perf/m6-6-test-game-performance` |
| Design documents | Writes `docs/design/performance-l6.md` (the findings note) and `docs/design/file-lookup-cache.md`. Updates `docs/design/input.md` and `docs/design/compat-db.md` for the layout field. |

**Goal.** Bring the D6 title to the phase 2 gate on the iPhone 17 Pro: a median of at least 55 fps and a 1% low of at least 45 fps over 10 minutes, without a memory kill (C-0012 section 3). Measure first, change what the measurements point to, and record each change with its before and after numbers.

**Scope.**

In:

- Glue first. Check that the launch flow of M6.5 uses the input of M6.1, the audio session of M6.2, and the launch check and HUD data of M6.3. Connect whatever is still missing, so the title can be played and measured.
- The controller or touch layout field, which C-0008 section 2 adds together with the design document for on-screen controls. M6.1's `docs/design/input.md` defines it. This stage adds it to the schema as an optional field (C-0008 section 5), reads it in the loader of M6.4 and hands it to the overlay of M6.1 (S6.6.6).
- Measurement. Claude writes the measurement steps, and recelilious runs them on the iPhone and shares the export (C-0012 section 2). Each run lasts 10 minutes and is summarized with the HUD and `tools/SessionMetrics.cs`. With a Mac (D1), Instruments' Time Profiler and Metal System Trace (avalonia_ui_layer.md section 4) show where the time goes.
- Translator options within the allow-list (C-0008 section 2). The research points to the memory-ordering (TSO) settings as the main lever. For hosts with LRCPC and LRCPC2, FEX 2404 recommends TSO on, with its vector and memcpy TSO options off (x86_to_arm_translation.md KQ2). The research only infers from the M-series that the A19 Pro has LRCPC2, so the stage reads the device's CPU features, for example from the `hw.optional.arm` values that M1 dumps show, and records them in the findings note; whether iOS exposes them the same way is to be confirmed in this stage. The exact option names, and how the session passes them to FEX, are to be confirmed in this stage. Each new allow-list entry is a schema change under C-0008 section 5.
- Translation stalls. The research reports that FEX added an on-disk code cache (x86_to_arm_translation.md KQ6). Whether it can work within the JIT pool rules of C-0006 is evaluated, not promised. Its files would go to `Library/Caches/Paneport/`. If C-0004 section 4 does not cover them, the change goes through the contract process.
- Graphics settings from the entry (C-0008 section 2), the levers a hot device needs:
  - `renderScale`, through the virtual monitor size that the display driver reports;
  - `frameRateCap`, through the present pacing.
  Where each one is implemented is to be confirmed in this stage.
- The shader cache, if M5.2 has not set it up already. DXMT's cache goes to `Library/Caches/Paneport/` (C-0004 section 4). The host sets it through the cache variables the research names (`DXMT_SHADER_CACHE` and `DXMT_SHADER_CACHE_PATH`, graphics_directx_to_metal.md KQ3), not through the title's environment allow-list. The values these variables take are to be confirmed in this stage.
- A case-insensitive file lookup cache in the Wine fork. iOS uses case-sensitive APFS, and Wine scans a whole directory when an exact name is not found (precedents_and_ux_layer.md KQ5). The cache maps case-folded names to real names per directory, and is invalidated when the directory changes.
- Thread quality of service for guest, translator, wineserver and render threads, so that the game's busiest threads can use the performance cores. The A19 Pro has two performance and four efficiency cores (roadmap). The calls are to be confirmed in this stage.
- Thermal behavior. The thermal state is recorded over the 10 minutes. The research reports the serious state within about 10 minutes of heavy CPU work on a 17 Pro (ios_platform_constraints.md section 8). If heat limits the run, the entry sets a frame-rate cap or a lower render scale. A dynamic thermal response is designed only if static settings are not enough.
- The shell's cost during play: the overlay static or animated (avalonia_ui_layer.md section 4, E2), and the shell's share of the footprint against the research's target of 150 MB or less (roadmap, risk 4).
- The title's entry, `compat/titles/local-<slug>.json`, with:
  - `schemaVersion` 1, the `id` with the slug and the `exeSha256` of the main program, `name`, `executable` and `architecture` `x86_64`;
  - the settings found;
  - `ceg` set by hand to `no`, with the reason in the findings note (see the `ceg` question in M6.5). The other `drm` members come from the classification on the device, as M6.4's precedence allows;
  - `memoryEstimateMiB`, measured as C-0008 section 2 defines it;
  - `jitPoolMiB` if the default does not suit;
  - `graphicsApi` `d3d11` and `rating` `untested`.
  The test report is added in L6 and names the L6 evidence record. Every value comes from our own tests (C-0008 section 5).

Out:

- Changes that only help other titles.
- MetalFX upscaling, spatial or temporal, and frame interpolation. The roadmap places them in phase 4, and M8.4 adds spatial upscaling. Until then the test title's entry leaves `metalFx` at `off`.
- Contributions upstream. FEX changes stay in our fork (C-0003 section 10).

**Read first.**

- C-0008 sections 2, 4 and 5; C-0005 section 2; C-0006 sections 3 and 4; C-0004 section 4; C-0010 section 4; C-0003 section 10; C-0011 section 1; C-0012 sections 2, 3 and 4.
- x86_to_arm_translation.md KQ2 and KQ6; graphics_directx_to_metal.md KQ3, KQ7, KQ8 and KQ9; precedents_and_ux_layer.md KQ5; ios_platform_constraints.md section 8; avalonia_ui_layer.md sections 3.1 and 4; roadmap-2026-10.md, its section on performance and game range.
- `docs/design/input.md`, `docs/design/audio.md`, `docs/design/memory.md`, `docs/design/compat-db.md` and `docs/design/session-flow.md`.

**Deliverables.**

- `external/Paneport-wine`, `external/Paneport-FEX` and `external/Paneport-dxmt`: the changes, on each fork's `paneport/<stage ID>` branch. This repository moves the submodule pointers.
- `src/native/host/`, `src/Paneport.Core/`, `src/Paneport.UI/` and `src/Paneport.iOS/`: the glue, the settings and the layout field. `tests/Paneport.Core.Tests/` and `tests/Paneport.UI.Tests/`: the layout field tests.
- `compat/schema/title.schema.json` (allow-list entries and the layout field) and `compat/titles/local-<slug>.json`.
- `tests/runtime/`: the lookup cache cases and benchmark, the translator option runs, and the graphics settings cases.
- `docs/design/performance-l6.md` and `docs/design/file-lookup-cache.md`, with rows in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S6.6.1 | Case-insensitive file lookup cache. | `external/Paneport-wine` | Harness on a case-sensitive volume, since the default macOS volume is case-insensitive (precedents_and_ux_layer.md KQ5); how the harness creates one is to be confirmed in this stage. Lookups with and without the cache give the same result for existing, missing, created, renamed and deleted names. 10,000 lookups of names whose case differs from the files', in a folder of 10,000 files, run at least 10 times faster with a warm cache than without it. |
| S6.6.2 | Per-title translator options. | `compat/schema/`, `src/Paneport.Core/`, the FEX or Wine fork as found | xUnit: keys outside the allow-list are rejected. Harness: the checksum benchmark (plan section 7.2) prints the right checksum under every allowed option set, and its run times are recorded. |
| S6.6.3 | Graphics settings from the entry: render scale and frame-rate cap. | `external/Paneport-dxmt`, `external/Paneport-wine` | Harness: the triangle program at `renderScale` 0.5 renders into a back buffer half the output size, and the presented image still fills the layer (pixel checks at fixed points). The frame-rate cap is checked on the harness if the harness's layer paces presents there, to be confirmed in this stage; otherwise it is a device check. |
| S6.6.4 | Thread quality of service. | `src/native/host/`, `external/Paneport-wine` | CTest of the table from thread role to class. Harness: the host's log lists the class given to each thread role during a test program run, and the harness checks the list. |
| S6.6.5 | Test title's compatibility entry. | `compat/titles/` | The M6.4 test loads it with every rule check, and the personal-data check passes. Review checks that every value has a measurement or a reason in the findings note. |
| S6.6.6 | On-screen layout field: the optional field that `docs/design/input.md` defines, in the schema, the typed model and the overlay. | `compat/schema/`, `src/Paneport.Core/`, `src/Paneport.UI/` | xUnit: an entry without the field gets the default layout, an entry with a valid value loads it, and an invalid value is rejected with the expected reason. A headless UI test shows the overlay with the layout that a fixture entry names. |

**Verification.**

- `runtime` job: S6.6.1 to S6.6.4 pass as their checks state.
- `dotnet` job: the tests of S6.6.2 and S6.6.6 pass, together with the rest of `eng/ci.slnf`.
- `native`, `ios` and `checks` jobs pass.
- Scripts this stage creates: the lookup cache cases and benchmark, the translator option runs, the graphics settings cases and the layout field tests.
- Device check (recelilious, required): a baseline run before the changes and a final run after them, 10 minutes each, summarized with `tools/SessionMetrics.cs`. Pass for the final run: a median of at least 55 fps, a 1% low of at least 45 fps, a peak footprint below the limit, and the app alive at the end. The before and after table goes into the pull request and is carried into the L6 evidence record.

**Done when.** The automated checks pass, and the final device run meets the gate's numbers. `docs/design/performance-l6.md` lists each change with its before and after numbers, and the entry is reviewed. If the levers above do not reach the numbers, the pull request says so with the measurements, and recelilious decides: another D6 title, a new stage for more work, or L6 to record a Fail.

**Risks and fallbacks.**

- The CPU side, translation and JIT, is more likely to limit performance than graphics (roadmap; graphics_directx_to_metal.md KQ9).
- The research reports that TSO stores to write-combined GPU memory are very slow on some hosts, with unified-memory systems faring better (x86_to_arm_translation.md KQ2). The profiling watches for it.
- The phone may throttle within 10 minutes (ios_platform_constraints.md section 8). A 60 fps cap and a lower render scale reduce heat.
- Shader compilation in a first session lowers the 1% low. The findings note and the evidence record whether the cache was warm.
- If a setting helps only on a cool device, or only right after a fresh launch, the findings note says so.

**Hands over to.** L6 (the settings for the acceptance run, and the before and after numbers), L7 (direct launches of Steam titles use the same settings path), L8 (for M8.4: the method, the tools, the lookup cache, the render scale and frame-rate cap, and the translator options), and the Direct3D 9 and Direct3D 12 decision expected after L6 (plan section 8, D11).

**Session log.**

_No sessions yet._
