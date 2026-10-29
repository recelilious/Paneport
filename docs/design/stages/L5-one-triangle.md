# L5 One triangle

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L5 | One triangle | Large | Planned | | |
| M5.1 | iOS display driver | Medium | Planned | | |
| M5.2 | DXMT on the runtime | Medium | Planned | | |
| S5.0.1 | W^X region scanner and the 10-minute and background checks | Small | Planned | | |
| S5.0.2 | Diagnostics export for evidence | Small | Planned | | |
| S5.0.3 | Verified iOS build entry for the phase 1 gate | Small | Planned | | |
| S5.1.1 | Driver skeleton and registration | Small | Planned | | |
| S5.1.2 | Virtual monitor and display modes | Small | Planned | | |
| S5.1.3 | Window to layer mapping | Small | Planned | | |
| S5.1.4 | Event processing for display changes | Small | Planned | | |
| S5.1.5 | Host display API | Small | Planned | | |
| S5.1.6 | Harness layer host and readback | Small | Planned | | |
| S5.2.1 | DXMT build integration | Small | Planned | | |
| S5.2.2 | Swapchain to layer | Small | Planned | | |
| S5.2.3 | Shader library targeting and cache paths | Small | Planned | | |
| S5.2.4 | Direct3D 11 triangle program | Small | Planned | | |
| S5.2.5 | Harness pixel check | Small | Planned | | |

## L5 One triangle

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L4 |
| C-0012 gate | Phase 1, One triangle (C-0012 section 3). |
| Size | M |
| Branch | `feat/l5-one-triangle` |
| Decisions needed | None new. The gate confirms or corrects the pool sizes that recelilious set in C-0006 section 4; a correction is a C-0006 change that recelilious approves. D2 sets the entitlement values the record states. |

**Goal.** Pass the phase 1 gate. A Direct3D 11 triangle from our own test program fills the iPhone's screen through DXMT and the iOS display driver, while the device checks what C-0012 section 3 asks of the JIT pool: authorization of the default pool, no mapping both writable and executable, and translated code that keeps running for 10 minutes and across a trip to the background.

**What recelilious will see.** In the in-app console of L4, the Direct3D 11 triangle program `d3d11_triangle.exe` is in the program list. Run, it fills the screen with a rotating triangle. A status line from the device test flavor shows the time since the debugger detached, the frame count and the latest scan for writable and executable mappings. After a trip to the Home screen the triangle keeps rotating. When the run ends, the console shows the frame-rate summary and the scan summary, and the diagnostics export can be saved and copied to Windows.

**Acceptance steps.** L5 is the phase 1 gate. C-0012 section 3 says it passes when:

> A Direct3D 11 test program renders a rotating triangle full screen on the device. JIT authorization of the default pool succeeds, the debugger detaches, and the pool is then verified (C-0006). A scan of the process's memory regions during the run finds no mapping that is both writable and executable. Translated code keeps running for at least 10 minutes after the debugger detaches, including after the app goes to the background and returns. The record states the pool size, how long authorization took and the iOS build, which is then added to `compat/ios-builds.json`. The native host framework resolves as C-0009 section 4 describes.

| Criterion | Steps |
|---|---|
| G1. A Direct3D 11 test program renders a rotating triangle full screen | 5, 6, 7 |
| G2. Authorization of the default pool succeeds, the debugger detaches, and the pool is verified | 4 |
| G3. No mapping that is both writable and executable during the run | 5, 6, 7, 8 |
| G4. Translated code runs for at least 10 minutes after the detach, including a background trip | 6, 7, 8 |
| G5. The record states the pool size, the authorization time and the iOS build, and the build is added to `compat/ios-builds.json` | 2, 4, 10 |
| G6. The native host framework resolves as C-0009 section 4 describes | 2 |

1. **Preconditions.** The device test flavor of this stage's build is installed from a development-signed or re-signed build, so `get-task-allow` is present (C-0009 section 4). StikDebug is paired and LocalDevVPN runs. Expected: StikDebug lists Paneport in its JIT list.
2. **Diagnostics.** Launch the app and open the diagnostics page (L1). Note the device model identifier, the iOS version and build, the build's commit hash and version, the entitlement values, the memory limit, and the native host's load path and import mode. Expected: the host was loaded from `Frameworks/paneport_host.framework/paneport_host` through the `@rpath` import, not through `__Internal`, and its ABI version matches (C-0009 section 4, C-0005 section 1). The path is shown relative to the app bundle.
3. **Start the triangle.** In the in-app console, pick `d3d11_triangle.exe` (the 12-minute entry) and tap Run. Expected: JIT is not ready, so the console offers the JIT setup screen.
4. **Authorize the pool.** Tap Enable JIT. Expected: the state moves through `debugger_attached`, `requesting`, `waiting_detach` and `verifying` to `ready` (C-0006 section 5). The console shows the requested pool size, which is the default of 512 MiB (C-0006 section 4), the time spent in each state, the authorization time from `requesting` to `ready`, the moment the debugger detached, and that verification passed.
5. **Triangle on screen.** The program starts. Expected: the game view covers the whole screen, edge to edge, in the program's clear color, and a solid triangle rotates in its middle. The status line shows the time since the detach, a growing frame count, and scans that report no writable and executable mapping. A scan runs every 10 seconds and at every JIT state change and life-cycle change.
6. **Background trip.** When the status line shows at least 3 minutes since the detach, go to the Home screen, wait at least one minute, and return to Paneport. Expected: within a few seconds the triangle rotates again; the frame count continues from where it stopped; it is the same session, with the JIT state still `ready`; the next scan reports no finding.
7. **Long run.** Leave the program running until the status line shows at least 10:00 since the detach. Expected: the triangle still rotates and every scan reports no finding. The program ends by itself at 12 minutes.
8. **Results.** Expected: the console shows exit code 0; the frames rendered with the median and 1% low frame rate (C-0011 section 1); the scan summary with the number of scans and zero findings; the time from detach to the last heartbeat line, at least 10 minutes; and the peak memory footprint and thermal states over time.
9. **Export.** Export the diagnostics (S5.0.2) through the share sheet and copy the file to Windows. Expected: it opens as text and contains none of the items the evidence record must never contain.
10. **Record and list.** Fill in the evidence record. On a Pass, add the iOS build to `compat/ios-builds.json` (S5.0.3, C-0006 section 6), and update the phase 1 row of the gate table in `docs/evidence/README.md` (C-0012 section 3).

**Evidence record.** File `docs/evidence/YYYY-MM-DD-l5-one-triangle.md`, date in UTC (C-0012 section 4). The integration session writes the template; recelilious fills in the results or hands over notes and the export. It records:

- the gate (phase 1, One triangle), the build (commit hash and app version) and the submodule commits of the Wine, FEX and DXMT forks;
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `com.apple.developer.kernel.increased-memory-limit` and `com.apple.developer.kernel.extended-virtual-addressing` in the installed app (C-0009 section 4);
- the JIT enabler (StikDebug) and its version, whether Game Mode was active, and the memory limit read during the run;
- the steps with expected and observed results, and the criteria table above with a result per criterion;
- these metrics: the pool size, the authorization time and the time in each state, the time from detach to the end of the run, the length of the background trip, the number of scans and findings, the frames, the median and 1% low frame rate, the peak memory footprint and thermal state over time (C-0011 section 1), the host's load path relative to the bundle and its import mode, and the Metal language version of the shader library (M5.2);
- scrubbed logs, normally the diagnostics export;
- the verdict (Pass, Fail or Waived, C-0012 section 3), the tester as a project handle (`recelilious` or `Claude`) and the date.

The record and its attachments never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). The device model identifier is allowed. Screenshots are cropped so that no status bar or device name shows. No Steam and no Valve binary is involved.

**Integration session.** The L5 session starts once M5.1 and M5.2 are Done. It wires the display path into the device test flavor. When the in-app console runs a window program, the game view of Paneport.iOS, which owns the `CAMetalLayer` (C-0004 section 2), goes full screen. Its layer is handed over with `pp_display_attach`, size changes go through `pp_display_resize`, and the layer is released with `pp_display_detach` when the program ends (C-0005 section 2). If no stage this one depends on has created the game view yet, the session adds a minimal full-screen view backed by a `CAMetalLayer` in Paneport.iOS. With the design documents of M5.1 and M5.2, it decides how presentation pauses while the app is in the background; whether iOS rejects GPU work submitted in the background is to be confirmed in this stage. It adds the 12-minute entry for `d3d11_triangle.exe` to the console's list and makes sure the JIT flow requests the default pool. It builds S5.0.1 to S5.0.3. It runs the `dotnet`, `native`, `ios`, `runtime` and `checks` jobs, including every earlier harness case and `tools/CheckBundle.cs`, and checks by SHA-256 that the bundled triangle program is the one the `runtime` job ran. It prepares the acceptance steps and the evidence record template (C-0012 section 2). After the gate run it completes the record, adds the iOS build entry and updates the gate table, creating the table if no earlier record has (plan section 7.7).

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S5.0.1 | W^X region scanner and the 10-minute and background checks. The scanner walks the task's memory regions with `mach_vm_region` and reports every mapping whose current protection is both writable and executable, with its address range; it also records mappings whose maximum protection allows both. It runs every 10 seconds and at every JIT state change and life-cycle change. Guest output lines get timestamps relative to the detach; the status line over the game view shows the time since the detach, the frame count from the program's heartbeat lines and the scan results; entering and leaving the background are logged | scanner in `src/native/host/`, compiled only into test builds and reached by the device test flavor like the conformance table of C-0005 section 5; timers and status line in `tests/device` | CTest on macOS: a clean test process reports no finding, and a planted mapping is reported with its range (how a planted mapping is made on macOS, for example a `MAP_JIT` region, is to be confirmed in this stage). A build check shows that the release configuration does not compile the scanner |
| S5.0.2 | Diagnostics export for evidence: the console output with timestamps, the JIT state timeline, the scan results, the frame summary, memory and thermal samples, the host's bundle-relative load path and import mode, and the evidence fields the app can read; the diagnostics page shows the load path and import mode if L1 did not add them. Everything C-0012 section 4 forbids is removed, and paths become relative to the app container (C-0004 section 4) | export and scrubber in `src/Paneport.Core`, share sheet in the device test flavor in `tests/device` | xUnit in `tests/Paneport.Core.Tests`: a synthetic export with a planted UDID-shaped value, a device name, an absolute container path, a team identifier and a fake token comes out without any of them, and every other line is unchanged |
| S5.0.3 | Verified iOS build entry for the phase 1 gate, added after a Pass | `compat/ios-builds.json` | The CI compatibility check validates the entry against `compat/schema/ios-builds.schema.json` (C-0011 section 1). If L3 already listed the same build and model, the entry points to the L5 record in the way the schema allows; how is to be confirmed against the schema |

**Session log.**

_No sessions yet._

## M5.1 iOS display driver

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L5 |
| Depends on | M4.4 |
| Decisions needed | None from plan section 4.2. |
| Size | L |
| Branch | `feat/m5-1-ios-display-driver` |
| Design documents | `docs/design/display-driver.md` |

**Goal.** Write `wineios.drv`, a Wine display driver for iOS. It gives Windows programs one full-screen virtual monitor and maps the top-level game window to the single `CAMetalLayer` that the host hands over, so that graphics layers can present into it. It uses only QuartzCore, Metal and the bridge, so the macOS harness can host it with a layer of its own (plan section 7.1).

**Scope.**

In:

- `wineios.drv` in the Wine fork, in C and Objective-C under LGPL-2.1-or-later, kept in separate iOS files where Wine's structure allows (C-0010 section 4). It implements the subset of Wine's user driver interface (`struct user_driver_funcs`, `WINE_GDI_DRIVER_VERSION` 110 in the research, to be confirmed against the D5 Wine version) that a minimal driver needs: window life cycle, window and client surfaces, event processing and display modes (wine_on_ios.md KQ3). Upstream `winemac.drv` may be adapted, since Wine is an upstream component (C-0001 section 6). Juice ships a driver with the same name; its code is never read or copied.
- Registration (S5.1.1): the session loads the driver as its graphics driver, the template prefix build of M4.4 names it, its Unix side is packaged like the other Wine Unix modules, and `eng/bundle-layout.json` lists it. Its Mach-O links no UIKit and no AppKit.
- One virtual monitor (S5.1.2) with the size, scale and refresh rate the host reports through the bridge (C-0005 section 2). Display modes come from the screen: the native mode plus scaled modes of the same aspect ratio, listed in the design document. A mode change by the program changes the size the graphics layer renders at, while the layer stays full screen.
- Window to layer mapping (S5.1.3). The top-level window that covers the monitor, the game window, is mapped to the layer. Its client surface is the `CAMetalLayer` that DXMT presents into (M5.2). A minimal window surface copies the game window's GDI content into the layer while no client surface presents, which makes plain windows visible and gives the harness a solid fill to read back; full GDI support is not a goal. The design document says how other top-level windows, such as a message box, are shown.
- The Vulkan entry maps `VK_KHR_win32_surface` to `VK_EXT_metal_surface` on the same layer, following `winemac.drv` (wine_on_ios.md KQ3 and KQ6). No Vulkan implementation is bundled yet, since MoltenVK is optional in C-0003 section 6, so the entry is built and reviewed but not run; a harness case comes with the first Vulkan implementation.
- Event processing (S5.1.4): the driver's event entry point turns the host's display changes (attach, resize, detach) into the window messages a program expects. Input comes with M6.1, which adds the host's input queue and the bridge members that drain it (the L6 brief); the design leaves a place for them.
- The host display API (S5.1.5): `pp_display_attach`, `pp_display_resize` and `pp_display_detach`, all [main] (C-0005 section 2), and the members of `pp_wine_bridge_t` in `src/native/include/paneport/wine_bridge.h` through which the fork reaches the layer and the display values (C-0005 section 3). Bindings in Paneport.Interop and conformance entries for the new structs (C-0005 section 5).
- The harness layer host (S5.1.6) in `tests/runtime/`: the harness creates a `CAMetalLayer` with a given size, scale and refresh rate, passes it through the display API, and reads back the last presented image at given points. The readback stays inside the harness, never in the driver; its mechanism is to be confirmed in this stage.
- `window.exe` in `tests/win32/`: it creates a window covering the primary monitor, prints its client size, the monitor size, the current mode and the mode list, fills its client area with a solid color, prints the new size after a change, and exits after a set time.
- The desktop window. Upstream Wine gives it to an `explorer.exe` process, but the session of M4.3 runs one process, and this stage cannot rely on M8.1. The design document says how the desktop window exists in one process; to be confirmed in this stage.
- `docs/design/display-driver.md`.

Out: keyboard, mouse, gamepad, touch and the cursor image (M6.1); several monitors and external displays (plan section 10); HDR; DXMT (M5.2).

**Read first.** C-0005 sections 1, 2, 3 and 5; C-0004 sections 1 and 2; C-0009 section 4; C-0010 sections 3 and 4; C-0003 sections 4 and 6; C-0001 section 6; C-0011 sections 1 and 4. Research notes wine_on_ios.md (KQ3 and KQ6) and graphics_directx_to_metal.md (KQ8, presentation through `CAMetalLayer`). `docs/design/single-process-wine.md`, `docs/design/runtime-ios.md` and `docs/design/runtime-macos-harness.md`.

**Deliverables.**

- `external/Paneport-wine` on `paneport/M5.1`: the driver; the pointer moves.
- `src/native/host/`: the display group. `src/native/include/paneport/`: its declarations and the new `wine_bridge.h` members. `src/Paneport.Interop/` and `tests/Paneport.Interop.Tests/`: bindings and conformance entries.
- `tests/runtime/`: the layer host, the readback and the display cases. `tests/win32/`: `window.exe`.
- The template prefix build and `eng/bundle-layout.json`, updated for the driver.
- `docs/design/display-driver.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S5.1.1 | Driver skeleton and registration | `external/Paneport-wine`, the template prefix build, `eng/bundle-layout.json` | The driver builds for macOS and iOS, and Xcode's object tools show no UIKit or AppKit among its linked libraries; on the harness a session loads it as the graphics driver, as the host log shows |
| S5.1.2 | Virtual monitor and display modes | `external/Paneport-wine` | Harness: for two different attach sizes, `window.exe` prints the monitor size, current mode and mode list that match the attached values |
| S5.1.3 | Window to layer mapping, window surface and client surface | `external/Paneport-wine` | Harness: the solid fill of `window.exe` reads back as its color at five fixed points of the layer |
| S5.1.4 | Event processing for display changes | `external/Paneport-wine` | Harness: after `pp_display_resize`, `window.exe` prints the new client size; after `pp_display_detach` it keeps running and exits with 0 |
| S5.1.5 | Host display API and bridge members | `src/native/host/`, `src/native/include/paneport/`, `src/Paneport.Interop/` | CTest: attach, resize and detach reach a fake bridge consumer in order with the given values, and calls from a thread other than the main thread are refused; conformance passes |
| S5.1.6 | Harness layer host and readback | `tests/runtime/` | A harness self-test presents a known image into its own layer without Wine and reads back the expected colors at the fixed points |

**Verification.** All of this runs without recelilious's iPhone; the device check is L5's acceptance. This stage creates the layer host and readback, the display cases and `window.exe`.

- `runtime` job: `eng/mac/run-harness.sh` runs the display cases in pool mode (M4.4). Pass: the printed sizes and modes equal the attached values; the readback at the five points equals the fill color within 2 of 255 per channel; the resize and detach cases pass as S5.1.4 states; `window.exe` exits with 0; every earlier case still passes, the trap never fires and the dispatcher check passes.
- `ios` job: the driver builds for iOS, and `tools/CheckBundle.cs` passes with the driver in place.
- `native` job: CTest of the display API under `asan-ubsan` and `tsan`, the header test and interface conformance (C-0005 section 5).
- `checks` job: REUSE, gitleaks, the personal-data check and lychee pass.

**Done when.** The deliverables exist, the verification passes in CI on the last commit, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch. The pull request marks the device path "not yet verified on device" (C-0012 section 1 item 9).

**Risks and fallbacks.**

- `user_driver_funcs` changes between Wine versions. The driver follows the D5 version, and each rebase reviews the interface again.
- Whether a `CAMetalLayer` on a GitHub runner vends drawables and presents is to be confirmed in this stage. The fallback runs the display cases on the local Mac (D1), with the output in the pull request (C-0011 section 3).
- If the readback cannot see the presented image, the fallback is the guest's own back-buffer readback in M5.2's triangle program, and later stages are told that only that check exists.
- If the desktop window needs a second process, the fallback is in-process code that creates it, never a second pseudo-process before M8.1.
- Coordinates in points and pixels can drift apart with the screen scale. The design document fixes which one the monitor reports and how touches will map in M6.1.

**Hands over to.**

- M5.2: the client surface and layer that DXMT presents into, and the layer host with its readback.
- M6.1: the driver for keyboard and mouse, the event entry point and the virtual monitor that touches map to (the L6 brief).
- M6.5 and M6.6: the display path and the readback at fixed points that the render-scale checks use (the L6 brief).
- L5: the display path on the device.

**Session log.**

_No sessions yet._

## M5.2 DXMT on the runtime

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L5 |
| Depends on | M5.1 |
| Decisions needed | None from plan section 4.2. The DXMT version was chosen in D5 (M4.1). |
| Size | L |
| Branch | `feat/m5-2-dxmt-runtime` |
| Design documents | `docs/design/graphics-dxmt.md` |

**Goal.** Bring Direct3D 11 to Metal through the DXMT fork, built for the macOS harness and for iOS, presenting into the layer the display driver maps. Our own triangle program proves it on the harness with a pixel check.

**Scope.**

In:

- The DXMT fork (LGPL-2.1-or-later, C-0003 section 6), built by `eng/mac/build-dxmt.sh`: the PE side as ARM64EC for x86-64 guests, since DXMT has an ARM64EC build file (wine_on_ios.md KQ6), and the Mach-O side for macOS and arm64 iOS. On iOS the Mach-O side is a dynamic framework and the PE modules go to `wine/arm64ec-windows/` (C-0009 section 4). The research lists its build needs: Meson 1.3 or later, LLVM 15 with headers and static libraries, Xcode with the Metal toolchain, and Wine headers (graphics_directx_to_metal.md KQ3). They are pinned in `eng/toolchain.json` (C-0009 section 2), reusing M4.1's pins where they fit.
- How DXMT's modules take the place of Wine's own `d3d11`, `dxgi` and `d3d10core`, by their location in the bundle's PE folders or by DLL overrides in the template prefix, is decided in the design document.
- Swapchain to layer (S5.2.2): DXMT's swapchain presents into the `CAMetalLayer` of the driver's client surface (M5.1). DXMT gets the layer from the Wine display driver, never from the host, because C-0004 section 2 gives the DXMT fork no path to the host. How upstream DXMT obtains the layer from `winemac.drv` and what the fork changes for `wineios.drv` is to be confirmed in this stage.
- Shader library targeting (S5.2.3): DXMT's own Metal shader library and the shaders it compiles at run time target a Metal language version that both iOS 26 and iOS 27 support (C-0001 section 2). The research reports an iOS port whose library targeted Metal 4.1, which only iOS 27 supports, and which was retargeted to Metal 3.1 for iOS 26 (graphics_directx_to_metal.md KQ7 and KQ11). The version is chosen and recorded in the design document; that it works on both systems is to be confirmed in this stage.
- Shader and pipeline caches (S5.2.3) under `Library/Caches/Paneport/` (C-0004 section 4). The host sets them through the cache variables the research names, `DXMT_SHADER_CACHE` and `DXMT_SHADER_CACHE_PATH` (graphics_directx_to_metal.md KQ3), never through a title's environment; their values are to be confirmed in this stage. On the harness they point to a temporary folder.
- `d3d11_triangle.exe` in `tests/win32/` (S5.2.4), our own C17 code under MIT, built with llvm-mingw against mingw-w64's headers, never the Windows SDK headers (C-0003 section 5). It creates a window covering the primary monitor and a swapchain, clears to a known color, and draws a solid triangle centered on the screen that rotates with time. The triangle contains the center at every angle and leaves the corners free. It prints a heartbeat with the frame count every 10 seconds and a summary at exit: frames, median and 1% low frame rate. Arguments set a duration in seconds or a frame count, a clear-only mode, and a self-check that reads back the center pixel of its back buffer through a staging texture. Its shaders are compiled without any Microsoft compiler or DLL (C-0001 section 7): either by Wine's own `d3dcompiler_47` at run time, or at build time by a compiler registered as a build tool under C-0003. The choice is to be confirmed in this stage.
- The harness pixel check (S5.2.5): with M5.1's layer host, after the last frame the center point reads as the triangle color and the four corner points as the clear color.
- `docs/design/graphics-dxmt.md`, with its sources (C-0001 section 6).

Out: Direct3D 9, 10 and 12 (D11, plan section 10); the i686 triangle (L7); graphics settings such as render scale and frame cap (M6.6); counting presents (M6.5); MetalFX; performance work (M6.6, M8.4).

**Read first.** C-0003 sections 3, 5 and 6; C-0004 sections 2 and 4; C-0009 sections 2, 3 and 4; C-0010 sections 3 and 4; C-0011 sections 1 and 4; C-0001 sections 2, 6 and 7. Research notes graphics_directx_to_metal.md (KQ3, DXMT; KQ7, shaders and caches; KQ8, presentation; KQ11, the iOS precedent's Metal version fix) and wine_on_ios.md (KQ6). `docs/design/display-driver.md`, `docs/design/runtime-ios.md` and `docs/design/forks.md`.

**Deliverables.**

- `eng/mac/build-dxmt.sh`, and any new pins in `eng/toolchain.json`.
- `external/Paneport-dxmt` on `paneport/M5.2`, and `external/Paneport-wine` if the driver side needs changes; the pointers move.
- `src/native/host/`: the cache variables for the session.
- `tests/win32/`: `d3d11_triangle.exe`. `tests/runtime/`: the triangle cases and the pixel check.
- `eng/bundle-layout.json` and `tools/CheckBundle.cs`, updated for DXMT's framework and PE modules.
- `docs/design/graphics-dxmt.md`, and a C-0003 change on a contract branch if a new shipped component needs it.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S5.2.1 | DXMT build integration for macOS and iOS | `eng/mac/build-dxmt.sh`, `external/Paneport-dxmt` | The build produces the PE modules and the Mach-O side for both platforms from a clean tree, and `tools/CheckBundle.cs` finds them in their places |
| S5.2.2 | Swapchain to layer | `external/Paneport-dxmt`, `external/Paneport-wine` | Harness: `d3d11_triangle.exe` in clear-only mode reads back as the clear color at the five fixed points |
| S5.2.3 | Shader library targeting and cache paths | `external/Paneport-dxmt`, `src/native/host/` | A build check that the shader library has the recorded Metal language version (how the version is read from the library is to be confirmed in this stage); harness: the cache folder holds entries after a first run, and a second run finds them |
| S5.2.4 | Direct3D 11 triangle program | `tests/win32/` | Builds for x86-64 with `-Werror`; on Windows 11 it shows the rotating triangle and its self-check passes (an optional local check) |
| S5.2.5 | Harness pixel check | `tests/runtime/` | Planted wrong expected colors make the case fail and name the point; the correct set passes |

**Verification.** All of this runs without recelilious's iPhone; the device check is L5's acceptance. This stage creates `eng/mac/build-dxmt.sh`, the triangle cases, the pixel check and the shader version check.

- `runtime` job: `eng/mac/run-harness.sh` runs `d3d11_triangle.exe` for 1,000 frames in pool mode. Pass: exit code 0; the summary reports 1,000 frames; the center point reads as the triangle color and the four corners as the clear color, within 2 of 255 per channel; the self-check passes; the logs hold no DXMT or Metal error, with Metal's API validation on where the harness can turn it on (to be confirmed in this stage); every earlier case still passes, the trap never fires and the dispatcher check passes.
- `ios` job: DXMT builds for iOS, and `tools/CheckBundle.cs` passes with its framework and PE modules.
- `checks` job: `tools/CheckLicenses.cs` covers every new pin and component, and REUSE, gitleaks, the personal-data check and lychee pass.

**Done when.** The deliverables exist, the verification passes in CI on the last commit, any contract change it needs is accepted, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch. The pull request marks the device path "not yet verified on device" (C-0012 section 1 item 9).

**Risks and fallbacks.**

- If DXMT's Mach-O side links LLVM libraries, they ship in the app and must be registered under C-0003 first. The stage proposes that change on a contract branch and waits (C-0012 section 2).
- DXMT needs exactly LLVM 15 (graphics_directx_to_metal.md KQ3), which may differ from the LLVM in llvm-mingw. The two are pinned and installed side by side.
- DXMT targets macOS. Metal or QuartzCore calls it makes that iOS lacks are to be confirmed in this stage. An existing iOS port shows the work is possible, but its changes are GPL and are never read or used (C-0001 section 6).
- If the chosen Metal version lacks a feature DXMT needs, the design document picks the lowest version that works on iOS 26.
- Whether GitHub's macOS runners give the harness a Metal device that can render is to be confirmed in this stage. The fallback runs the triangle cases on the local Mac (D1), with the output in the pull request.
- If the layer readback of M5.1 is not available, the pixel check uses the program's own self-check, and the pull request says so.

**Hands over to.**

- L5: Direct3D 11 on the device path.
- M6.5: `d3d11_triangle.exe` with its frame count, and the present path (the L6 brief).
- M6.6: DXMT for the render-scale and frame-cap settings, the triangle program and the pixel check (the L6 brief).
- M8.4: `docs/design/graphics-dxmt.md` (the L8 brief).
- L7: the pixel check, which S7.0.4 reuses for an i686 triangle if D6 needs one (the L7 brief).

**Session log.**

_No sessions yet._
