# L4 First Windows program on the iPhone

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L4 | First Windows program on the iPhone | Large | Planned | | |
| M4.1 | Forks, submodules and toolchains | Medium | Planned | | |
| M4.2 | ARM64EC Wine with FEX on macOS | Medium | Planned | | |
| M4.3 | Single-process Wine on macOS | Medium | Planned | | |
| M4.4 | Runtime on iOS | Medium | Planned | | |
| S4.0.1 | In-app console and the Run sample program action | Small | Planned | | |
| S4.1.1 | Fork repositories and integration branches | Small | Planned | | |
| S4.1.2 | Submodules | Small | Planned | | |
| S4.1.3 | Toolchain pins and Mac bootstrap update | Small | Planned | | |
| S4.1.4 | C-0003 register proposal and mingw-w64 runtime review | Small | Planned | | |
| S4.1.5 | Forks design document | Small | Planned | | |
| S4.2.1 | Wine build script for macOS | Small | Planned | | |
| S4.2.2 | PE-side build | Small | Planned | | |
| S4.2.3 | FEX ARM64EC build for Darwin | Small | Planned | | |
| S4.2.4 | Win32 test programs | Small | Planned | | |
| S4.2.5 | Harness, runner and expected outputs | Small | Planned | | |
| S4.2.6 | Runtime CI job, layout proposal and build caching | Small | Planned | | |
| S4.3.1 | Single-process design document | Small | Planned | | |
| S4.3.2 | wineserver thread and in-process connections | Small | Planned | | |
| S4.3.3 | In-process start of the first process | Small | Planned | | |
| S4.3.4 | Signal integration through the bridge | Small | Planned | | |
| S4.3.5 | Mach thread control in the fork | Small | Planned | | |
| S4.3.6 | Teardown and exit semantics | Small | Planned | | |
| S4.3.7 | Prefix bootstrap and drive letters | Small | Planned | | |
| S4.3.8 | Bridge header and the host session API | Small | Planned | | |
| S4.3.9 | Fork and exec trap | Small | Planned | | |
| S4.4.1 | iOS cross-build | Small | Planned | | |
| S4.4.2 | Framework packaging and loader paths | Small | Planned | | |
| S4.4.3 | FEX pool allocator hook | Small | Planned | | |
| S4.4.4 | PE code pages from the pool | Small | Planned | | |
| S4.4.5 | Address-space, register and TLS constraints | Small | Planned | | |
| S4.4.6 | Template prefix generation and install | Small | Planned | | |
| S4.4.7 | Guest console capture on iOS | Small | Planned | | |
| S4.4.8 | Bundle layout check and size report | Small | Planned | | |

## L4 First Windows program on the iPhone

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L2, L3 |
| C-0012 gate | None. The phase 1 gate at L5 (C-0012 section 3) adds graphics, the scan for writable and executable mappings and the 10-minute run. |
| Size | M |
| Branch | `feat/l4-first-windows-program` |
| Decisions needed | None new. D4 and D5 are made before M4.1. D2 (free or paid Apple account) sets the entitlement values the record states, and with them the address map the runtime gets. |

**Goal.** Run a Windows x86-64 program inside Paneport on an iPhone 17 or 18 Pro for the first time. The Wine fork runs in single-process mode, FEX translates the program into the JIT pool that L3 proved (C-0006), and the program's console output appears in the app. Graphics come with L5. It starts after the phase 0 gates of L2, so the signal dispatcher has passed its four-hour run on the device before Wine relies on it.

**What recelilious will see.** The device test flavor (M1.3) has an in-app console. It lists the bundled test programs and has a Run sample program button. Once JIT is `ready`, the button runs the console test program `console.exe` from `tests/win32/`. Its lines appear in order: a greeting, the CPU features the guest sees, a checksum over a fixed buffer that equals the expected value, the structured exception handling test passed, the threads test passed, and exit code 0. A second run in the same launch is refused with a relaunch message (C-0004 section 1).

**Acceptance steps.** Manual steps on the device. L4 is not a C-0012 gate, so the steps show that the feature works and produce the evidence.

1. **Preconditions.** The device test flavor of this stage's build is installed from a development-signed or re-signed build, so `get-task-allow` is present (C-0009 section 4). StikDebug is paired and LocalDevVPN runs, as in L3. Expected: StikDebug lists Paneport in its JIT list.
2. **First launch.** Launch the app and open the in-app console. Expected: the console reports once that the template prefix was installed into `Library/Application Support/Paneport/prefixes/` (plan section 7.4), showing the path relative to the app container only. The app stays responsive during the copy.
3. **Run before JIT.** Tap Run sample program without enabling JIT. Expected: no session starts. The console says JIT is needed (`pp_session_start` refuses with `PP_E_JIT_NOT_READY`, C-0005 section 2) and offers the JIT setup screen of L3.
4. **Enable JIT.** Tap Enable JIT there. Expected: the state reaches `ready` with the default pool of 512 MiB (C-0006 sections 4 and 5).
5. **Run the sample program.** Return to the console and tap Run sample program. Expected, in order: a session-started line; the greeting naming x86-64; a CPU feature line with a vendor string and a list that includes SSE and SSE2; the checksum line, with the computed value equal to the expected value printed beside it and a throughput figure; the structured exception handling line, passed for an access violation, an integer divide by zero and a `RaiseException` call; the threads line, passed; the result line; a process-exited line with exit code 0; a session-ended line (C-0005 section 2).
6. **The app survives the exit.** Open another screen and come back. Expected: the app responds and the console still shows the output. A Windows process exit is not an app exit (M4.3).
7. **Second run.** Tap Run sample program again. Expected: refused with the relaunch message (`PP_E_SESSION_EXISTS`, C-0005 section 2). Nothing crashes.
8. **Relaunch and run again.** Close Paneport in the app switcher, launch it, enable JIT and run the sample program. Expected: the JIT state starts at `not_debugged` (C-0006 section 1). The output matches step 5 line for line except the throughput, and the prefix is not installed again.
9. **Another program (optional).** After one more relaunch and JIT, run `file_io.exe` from the list. Expected: every file operation passes, drive `C:` is seen, and the exit code is 0.
10. **Record.** Note the session summary the console shows after each run (peak memory footprint, thermal states, memory limit), read the remaining values from the diagnostics page, and fill in the evidence record.

**Evidence record.** File `docs/evidence/YYYY-MM-DD-l4-first-program.md`, date in UTC (C-0012 section 4). The integration session writes the template; recelilious fills in the results or hands over notes. It records:

- the feature (L4 First Windows program on the iPhone), the build (commit hash and app version) and the submodule commits of the Wine and FEX forks;
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `com.apple.developer.kernel.increased-memory-limit` and `com.apple.developer.kernel.extended-virtual-addressing` in the installed app (C-0009 section 4);
- the JIT enabler (StikDebug) and its version, whether Game Mode was active, and the memory limit read during the run;
- the steps with expected and observed results, and these metrics: the pool size, the authorization time, the time from tapping Run to the first output line, the checksum value and throughput, and the peak memory footprint and thermal state over time (C-0011 section 1);
- the complete console output of step 5, scrubbed, so that a later rerun can be compared with it (L10 does this);
- scrubbed logs, the verdict, the tester as a project handle (`recelilious` or `Claude`) and the date.

The record and its attachments never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). The device model identifier is allowed. Screenshots are cropped to the console text. No Steam and no Valve binary is involved.

**Integration session.** The L4 session starts once M4.1 to M4.4 are Done. It builds the in-app console (S4.0.1) and wires it to the runtime: the console reads guest output and the process and session events through Paneport.Interop (C-0005 sections 1 and 2). The Run action reads `pp_jit_get_state`, offers the JIT setup screen when the state is not `ready`, then calls `pp_session_start` with the shared prefix and `pp_process_launch` for `console.exe` with output capture on. The session checks by SHA-256 that the test programs the device test flavor bundles are the files the `runtime` job ran. It runs the `dotnet`, `native`, `ios`, `runtime` and `checks` jobs, and `tools/CheckBundle.cs` confirms that the main app holds no test program. It prepares the acceptance steps and the evidence record template (C-0012 section 2), and after recelilious's run it completes the record, which recelilious merges.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.0.1 | In-app console and the Run sample program action: the program list, the button for `console.exe`, the JIT check, guest output, process and session events, the relaunch message, and a session summary (peak footprint from `pp_memory_get_budget`, thermal states from host events) | view model in `src/Paneport.Core` behind a Core session interface, view in `src/Paneport.UI`, shown only by the device test flavor in `tests/device`, which implements the interface over Paneport.Interop | Headless UI tests in `tests/Paneport.UI.Tests` with a fake session: lines keep their order, the exit code shows, `PP_E_JIT_NOT_READY` leads to the JIT setup offer, `PP_E_SESSION_EXISTS` to the relaunch message, and the summary shows the fake's highest footprint |

**Session log.**

_No sessions yet._

## M4.1 Forks, submodules and toolchains

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L4 |
| Depends on | M1.1 |
| Decisions needed | D4 (who creates the three fork repositories) and D5 (the upstream versions, which this stage proposes with reasons and recelilious approves). |
| Size | M |
| Branch | `build/m4-1-forks-toolchains` |
| Design documents | `docs/design/forks.md` |

**Goal.** Set up the fork repositories that hold all code derived from Wine, FEX-Emu and DXMT (C-0003 section 4), attach them under `external/` at pinned commits, and pin every tool their builds need, so that later stages build the runtime from a fresh clone.

**Scope.**

In:

- `Paneport-wine`, `Paneport-FEX` and `Paneport-dxmt` on GitHub, created as D4 says, public as a release requires (C-0012 section 5), keeping the upstream license files and notices (C-0003 section 4).
- In each fork, an integration branch `paneport/integration` that starts at the D5 release. Stage work goes on `paneport/<stage ID>` branches, and this repository's stage branch moves the pointer (plan section 3.3).
- The D5 proposal with reasons. Inputs from the research: Wine 11.0 is the latest stable release with ARM64EC, the new WoW64 and 4K-page simulation on 16 KB hosts; native arm64 Darwin support is in upstream merge request 11638, whose merge status is unknown; the FEX wiki recommends a Wine branch with extra ARM64EC fixes (wine_on_ios.md KQ2). FEX publishes monthly tags, and FEX-darwin (MIT, registered in C-0003 section 6) is based on FEX-2609.1 (x86_to_arm_translation.md KQ5). DXMT 0.80 is the last MIT release; later commits are LGPL-2.1-or-later, as the register records (graphics_directx_to_metal.md KQ3).
- Submodules under `external/`, each pinned to a commit on its integration branch (C-0004 section 3).
- Each fork's own `.clang-format` and `.editorconfig`, matching upstream; the root files apply only to `src/`, `tests/` and `tools/`, and the checks exclude `external/` (C-0010 section 4).
- Pins in `eng/toolchain.json` for llvm-mingw, CMake, Meson, Ninja and the other tools the fork builds need (C-0009 section 2), checked by `tools/CheckToolchain.cs` (M0.2). The Mac bootstrap script of M1.1 installs the same pins.
- A C-0003 change on a `contract/<summary>` branch (plan section 8): Tool rows for the build tools the forks need, such as Python, bison, flex and autoconf, and llvm-mingw's license. It carries the mingw-w64 runtime review that C-0003 section 6 asks for before the first Windows-side build: the linker map of a scratch link per PE target, never committed or shipped, is matched to the notices in `COPYING.MinGW-w64-runtime.txt`, and the entry leaves Under review.
- `docs/design/forks.md`: versions and reasons, the branch and rebase policy, how pointers move, the upstream rule of C-0003 section 10 (FEX changes are never proposed upstream), and the CI caching plan, keyed by submodule commit, toolchain pins and build script hash, for the `ios` job now and the `runtime` job later.

Out: building the forks for real (M4.2, M4.4, M5.2); changes inside a fork beyond its configuration files; the `runtime` job (M4.2).

**Read first.** C-0003 sections 4, 5, 6 and 10; C-0004 sections 2 and 3; C-0009 sections 2 and 3; C-0010 sections 1, 4 and 6; C-0011 sections 2 and 4; C-0002 section 6; C-0012 sections 1 and 2; C-0001 section 6. Research notes wine_on_ios.md (KQ2), x86_to_arm_translation.md (KQ1 and KQ5) and graphics_directx_to_metal.md (KQ3). Plan sections 3.3, 4.2 and 8, and `docs/design/repository-checks.md` (M0.2).

**Deliverables.**

- The three fork repositories with their integration branches (outside this repository).
- `.gitmodules`, `external/Paneport-wine`, `external/Paneport-FEX` and `external/Paneport-dxmt`.
- The `eng/toolchain.json` additions and the updated Mac bootstrap script; `tools/CheckToolchain.cs` only if a new kind of pin needs it.
- `docs/design/forks.md`, which also records the outcome of D5.
- The C-0003 change, committed locally and pushed only after recelilious's review (C-0012 section 2).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.1.1 | Fork repositories and integration branches | the three fork repositories | Review checklist: each integration branch starts at the D5 tag or commit, keeps upstream's license files and notices, and carries its own `.clang-format` and `.editorconfig` |
| S4.1.2 | Submodules | `.gitmodules`, `external/` | A fresh `git clone --recurse-submodules` checks out each submodule at its recorded commit, as `git submodule status` shows |
| S4.1.3 | Toolchain pins and Mac bootstrap update | `eng/toolchain.json`, the Mac bootstrap script of M1.1 | `tools/CheckToolchain.cs` passes on a macOS runner, and fails naming the tool when one is missing or at another version |
| S4.1.4 | C-0003 register proposal and mingw-w64 runtime review | a `contract/<summary>` branch | `tools/CheckLicenses.cs` names exactly the unregistered entries before the change and passes once it is accepted; review checklist: every linked runtime object matches a notice |
| S4.1.5 | Forks design document | `docs/design/forks.md` | Review against C-0003 sections 4 and 10, C-0004 section 3 and C-0010 section 4; lychee passes |

**Verification.** All without recelilious's iPhone. This stage creates no new script; the clone check runs as workflow steps.

- Fresh clone, in CI and on the Windows workstation. Pass: `git submodule status` lists the three forks at their recorded commits, none missing or modified.
- `tools/CheckToolchain.cs` on the macOS runner. Pass: every new pin matches.
- `checks` job: `tools/CheckLicenses.cs` passes once the C-0003 change is accepted, and REUSE, gitleaks, the personal-data check and lychee pass.
- First fork targets, after the mingw-w64 review, on the local Mac (D1) or in the `ios` job, which builds the forks (C-0011 section 4): Wine's configure for the macOS host with the PE architectures the FEX wiki shows (`--enable-archs=arm64ec,aarch64,i386`, wine_on_ios.md KQ2) and Wine's host build tools; FEX's CMake configuration for `arm64ec-w64-mingw32`; DXMT's Meson setup. Pass: each exits with 0 and logs the pinned versions.

**Done when.** The forks, the submodules and the pins are in place, the C-0003 change is accepted and the license check passes, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch.

**Risks and fallbacks.**

- Merge request 11638 may never be merged, or may change. The fork carries it, and moving to a later upstream release follows the rebase policy.
- FEX-Emu forbids AI-generated contributions (C-0003 section 10). Our FEX changes stay in the fork, and AI attribution is never removed.
- If D5 picks DXMT 0.80 or earlier, which is MIT, the C-0003 change records that license for the pinned release.
- The forks are large. The caching plan keeps jobs that do not build them from fetching them; how CI fetches only the needed history is to be confirmed in this stage.
- Without a local Mac (D1), the first fork targets run only in CI, and each iteration is slow.

**Hands over to.**

- M4.2: the forks, the integration branches, the pins and the caching plan.
- M4.4 and M5.2: the same forks and pins for the iOS builds and DXMT.
- M7.3: the FEX fork, where it builds the WoW64 module.

**Notes from earlier stages.**

- M0.2 ([L0 brief](L0-foundation.md), session log of 2026-10-29): every submodule needs an entry in `eng/license-map.json`, which `tools/CheckLicenses.cs` reads together with `.gitmodules`.

**Session log.**

_No sessions yet._

## M4.2 ARM64EC Wine with FEX on macOS

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L4 |
| Depends on | M4.1 |
| Decisions needed | D1 (a local Apple silicon Mac) is strongly recommended from here on (plan section 4.1); without one, every build and harness run goes through CI. |
| Size | XL |
| Branch | `feat/m4-2-arm64ec-wine-macos` |
| Design documents | `docs/design/runtime-macos-harness.md` |

**Goal.** Build the Wine fork as ARM64EC Wine for arm64 macOS with FEX as its x86-64 emulator, and run our own Win32 test programs on it through a harness that checks their output. This is the baseline: upstream-style multi-process Wine on the same XNU kernel, Mach and 16 KB pages as iOS, with a debugger and automated runs available (plan section 7.1).

**Scope.**

In:

- Wine's Unix side for arm64 macOS, built by `eng/mac/build-wine.sh` (Bash 3.2, C-0010 section 1). If the D5 base lacks merge request 11638, the fork carries it: a position-independent loader, operating-system-chosen addresses for the shared user data and the TEB, PE protections applied in two passes so that no page is writable and executable, and a TEB lookup that does not trust x18 (wine_on_ios.md KQ1 and KQ2).
- Wine's PE side, built with the pinned llvm-mingw for `arm64ec`, `aarch64` and `i386` (C-0009 section 3). Only the 64-bit guest path runs before M7.3.
- FEX's ARM64EC module for `arm64ec-w64-mingw32`, built by `eng/mac/build-fex.sh` and loaded by Wine as the x86-64 emulator named under `HKLM\Software\Microsoft\Wow64\amd64` (wine_on_ios.md KQ2). FEX-darwin's Darwin changes are merged on the FEX fork's stage branch, and the hooks it says Wine must provide (shared data, JIT write protection, native JIT allocation) go into the Wine fork (x86_to_arm_translation.md KQ3).
- The Win32 test programs in `tests/win32/`, our own C17 code under MIT, built with the pinned llvm-mingw and `-Werror` by `eng/mac/build-win32-tests.sh`, which takes the target as a parameter. This stage builds x86-64; M7.3 adds the i686 builds of plan section 7.2. Each prints a fixed line format and exits with 0 only when every check passed:
  - `console.exe`, the console test program and L4's sample program: a greeting naming its architecture, the CPU feature, checksum, structured exception handling and threads checks, and a result line; an argument sets its exit code.
  - `cpu_features.exe` (vendor and feature flags from CPUID) and `checksum.exe` (a fixed 64 MiB buffer from a fixed formula, its checksum beside the expected value, and the throughput).
  - `seh.exe`, the structured exception handling program: an access violation, an integer divide by zero and a `RaiseException` call, each caught by its own handler, with compiler `__try` blocks if the pinned llvm-mingw supports them here and a vectored handler otherwise (to be confirmed in this stage).
  - `threads.exe` (interlocked updates, a critical section, events, and one worker suspended, its context changed, then resumed) and `file_io.exe` (file operations on `C:`, and `GetLogicalDrives` reporting `C:`).
- The harness in `tests/runtime/`, our own C17 code under MIT, in `tests/runtime/harness/`. Each case is a JSON file in `tests/runtime/cases/` naming the program, arguments, timeout, exit code and expected output in `tests/runtime/expected/`. Here it starts Wine's loader as a child process with a fresh temporary prefix, captures the output and compares. Lines match exactly, except lines marked as patterns, such as the CPU feature and throughput lines. Expected files hold no host path.
- `eng/mac/run-harness.sh`: builds what is missing, runs the selected cases, each in a fresh harness process, writes a summary, and exits non-zero on any failure.
- Console programs need no display driver; how the harness tells Wine to run without one is to be confirmed in this stage.
- Two contract changes on `contract/<summary>` branches (plan section 8): C-0011 gains a `runtime` job, and C-0004 adds `tests/runtime/` and `tests/win32/`. The job runs on an Apple silicon macOS runner, restores the cached fork builds, builds what is missing, runs `eng/mac/run-harness.sh` and uploads the logs. recelilious decides in the review whether it runs on every pull request and joins the jobs C-0011 section 3 requires.
- `docs/design/runtime-macos-harness.md`, and a `README.md` in `tests/runtime/` and `tests/win32/` (C-0004 section 3).

Out: single-process mode, the native host and the bridge (M4.3); iOS builds (M4.4); graphics (M5.1, M5.2); the WoW64 module and i686 builds, which wait for D11 (M7.3); multi-process programs (M8.1).

**Read first.** C-0001 sections 6 and 7; C-0003 sections 4, 6 and 10; C-0004 sections 1 to 3; C-0009 sections 2 and 3; C-0010 sections 1, 3, 4 and 6; C-0011 sections 1, 2 and 4; C-0012 sections 1 and 2. Research notes wine_on_ios.md (KQ1 and KQ2), x86_to_arm_translation.md (KQ2, KQ3 and KQ5) and process_and_memory_model.md (Q1). `docs/design/forks.md`; plan sections 7.1, 7.2, 7.6 and 8.

**Deliverables.**

- `eng/mac/build-wine.sh`, `eng/mac/build-fex.sh`, `eng/mac/build-win32-tests.sh` and `eng/mac/run-harness.sh`.
- Changes on `paneport/M4.2` in `external/Paneport-wine` and `external/Paneport-FEX`, and the moved pointers.
- `tests/win32/` with the six programs, and `tests/runtime/` with the harness, cases and expected outputs, each with its `README.md`.
- The `runtime` job in `.github/workflows/`, once the C-0011 change is accepted.
- `docs/design/runtime-macos-harness.md` and the two contract branches.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.2.1 | Wine build script for macOS, carrying merge request 11638 if needed | `eng/mac/build-wine.sh`, `external/Paneport-wine` | It builds from a clean tree with the pinned tools, and the built loader prints its version |
| S4.2.2 | PE-side build for `arm64ec`, `aarch64` and `i386` | `eng/mac/build-wine.sh` | Each architecture's module list is complete, and a sample of modules shows the machine type of its folder, read with the pinned llvm-mingw's object tools |
| S4.2.3 | FEX ARM64EC build for Darwin, with FEX-darwin's changes and the Wine-side hooks | `eng/mac/build-fex.sh`, `external/Paneport-FEX`, `external/Paneport-wine` | With the module named as the x86-64 emulator, `console.exe` prints its greeting |
| S4.2.4 | Win32 test programs | `tests/win32/`, `eng/mac/build-win32-tests.sh` | They build for x86-64 with `-Werror`; on Windows 11 they print their expected lines natively and exit with 0 (optional local check) |
| S4.2.5 | Harness, runner and expected outputs | `tests/runtime/`, `eng/mac/run-harness.sh` | A planted case with one wrong expected line and one with a wrong exit code each make the runner exit non-zero and name the case; the clean set passes |
| S4.2.6 | Runtime CI job, layout proposal and build caching | two `contract/<summary>` branches, `.github/workflows/` | Once accepted, the job passes on the branch, and a second run at the same submodule commits restores the fork builds from the cache, as its log shows |

**Verification.** All without recelilious's iPhone. This stage creates the build scripts, the harness with its cases and expected outputs, and `eng/mac/run-harness.sh`.

- `eng/mac/run-harness.sh --mode multi`, on the local Mac and in the `runtime` job once it exists. Pass: each of the six programs prints exactly its expected lines (patterns where marked) and exits with 0 within its timeout; Wine reports no unhandled exception; the planted cases fail as S4.2.5 states.
- `native` job: clang-format and clang-tidy cover `tests/runtime/` and `tests/win32/` (C-0011 section 2), and CTest of the harness's output comparison passes under `asan-ubsan`.
- `checks` job: REUSE, gitleaks, the personal-data check over `tests/`, lychee and `tools/CheckLicenses.cs` pass.
- Until the C-0011 change is accepted, the harness runs on the local Mac, and the pull request carries its output and lists the CI check not yet run (C-0011 section 3).

**Done when.** The deliverables exist, both contract changes are accepted (C-0012 section 1 item 3), the `runtime` job passes on the last commit, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch. While a contract change waits for review, the stage is Blocked with that reason (plan section 2.4).

**Risks and fallbacks.**

- Merge request 11638 may not apply to the D5 base; the fallback is a base it applies to, or a port.
- Wine 11's 4K-page simulation shares protections within a 16 KB host page, which collides with W^X where a section boundary mixes code and data (wine_on_ios.md KQ1). The two-pass protections address this, and our programs can use 16 KB section alignment (`-Wl,-section-alignment=16384`, x86_to_arm_translation.md KQ2).
- If FEX-darwin's changes do not fit the D5 FEX version, the fork ports what it needs; they are MIT, so the fork stays MIT.
- Whether the harness needs the `com.apple.security.cs.allow-jit` entitlement for FEX-darwin's `MAP_JIT` allocations, or runs without the hardened runtime, is to be confirmed in this stage, as for M3.1's tests.
- If cold fork builds do not fit the job's time limit, the job splits into a cache-warming part and a test part; the runner's limits are to be confirmed in this stage.
- Expected outputs must not depend on helpers such as `wineboot` and `services.exe` (process_and_memory_model.md Q1), because M4.3 starts one process only. No Microsoft, Valve or game binary is used (plan section 7.6).

**Hands over to.**

- M4.3: the builds, the harness, the cases and expected outputs, which must pass unchanged in single-process mode.
- M4.4: the build scripts, extended for iOS.
- M5.1, M5.2, M6.1, M6.2, M6.3, M6.5, M6.6, M7.3 and M8.1: the harness, `eng/mac/run-harness.sh`, the case format, `tests/win32/` and the `runtime` job.
- L4: `console.exe`.

**Session log.**

_No sessions yet._

## M4.3 Single-process Wine on macOS

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L4 |
| Depends on | M4.2, M2.2 |
| Decisions needed | None from plan section 4.2. The choice between in-process `wineboot` and the template prefix (plan section 7.4) is made in this stage's design document and reviewed in the pull request. |
| Size | XL |
| Branch | `feat/m4-3-single-process-wine` |
| Design documents | `docs/design/single-process-wine.md` |

**Goal.** Run Wine as iOS needs it: the wineserver as a thread and the first Windows process as a group of threads in one process that never forks or execs (C-0004 section 1), started through the host's session API and the Wine bridge (C-0005 sections 2 and 3), while the dispatcher (M2.2) keeps the fault signals (C-0005 section 4). Every M4.2 harness test passes unchanged.

**Scope.**

In:

- `docs/design/single-process-wine.md` (S4.3.1), covering the items below with its sources (C-0001 section 6). It names the Wine helpers that are not started (`wineboot`, `services.exe`, `explorer.exe`, `winedevice.exe`, plug and play; process_and_memory_model.md Q1). Its address-space plan, which M7.3 reads, places the guest's images, the relocated shared user data and the TEBs (as merge request 11638 does, wine_on_ios.md KQ1), lists the guest reservations registered through the bridge (C-0005 section 3), and keeps room for 32-bit guest windows.
- The wineserver's event loop on a thread the host creates (C-0004 section 1), reached by the first process without a server socket in the file system (S4.3.2). Whether Wine's request channels and file descriptor passing work unchanged inside one process is to be confirmed in this stage.
- The first process started on a new pthread instead of Wine's double fork and exec of the loader (wine_on_ios.md KQ1), with no `fork`, `exec` or `posix_spawn` (S4.3.3).
- Signals (S4.3.4). The fork never installs handlers for SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP or SIGABRT; it registers its guest fault handler through the bridge and marks every pseudo-process thread as a guest thread (C-0005 sections 3 and 4). The design document maps each signal upstream Wine handles (wine_on_ios.md KQ1) to its replacement; for SIGINT and SIGUSR2, which C-0005 section 4 does not name, that is to be confirmed in this stage against the dispatcher's design. Handlers never assume that x18 or `TPIDR_EL0` survive signal delivery.
- The dispatcher check (S4.3.4): test builds check after `pp_session_start` that the dispatcher still holds all six fault signals, and fail the session otherwise (C-0005 section 4).
- Thread control (S4.3.5): suspend, context read and write, wake-ups for queued system calls and termination through Mach thread calls and in-process wake-ups; the in-process wineserver never sends SIGUSR1 or SIGQUIT (C-0005 section 4).
- Teardown and exit (S4.3.6). `ExitProcess` and `TerminateProcess` end the pseudo-process's threads and report a process-exited event with the exit code, and the last exit reports session-ended (C-0005 section 2). No path calls `exit` or `_exit`. `pp_session_stop` ends the wineserver thread; a second `pp_session_start` fails with `PP_E_SESSION_EXISTS`.
- Prefix bootstrap and drive letters (S4.3.7). The design document chooses between wineboot's steps run in-process and the template prefix (plan section 7.4). With no `winedevice.exe` or `mountmgr.sys`, the fork publishes drive letters itself, since otherwise `GetLogicalDrives` returns 0 (process_and_memory_model.md Q1): `C:` for the prefix and, per launch, one letter for the program's folder. No drive maps the host's root.
- The bridge header `src/native/include/paneport/wine_bridge.h` (S4.3.8): `pp_wine_bridge_t` with the members this stage needs (logging, guest thread marking, guest reservations, fault handler registration, process and session events). It is MIT, includes no other Paneport header, and the fork's build reaches it through `src/native/include` (C-0005 section 3). The fork's entry header holds declarations only, under the fork's license.
- The host session API (S4.3.8): `pp_session_start`, `pp_session_stop`, `pp_process_launch`, `pp_process_get_state` and `pp_process_terminate` (C-0005 section 2), for the first process. The session configuration gets the members this stage uses (prefix path, architecture, environment); later stages append theirs (C-0005 section 1). `pp_process_launch` can capture standard output and error through a pipe the host gives the process as its standard handles; each line reaches the log callback marked as guest output with the process handle. Paneport.Interop bindings and conformance entries come with the new structs (C-0005 section 5).
- The JIT precondition stays (C-0005 section 2). M3.1 is not a dependency here, so a test-build hook on the harness reports `ready` and FEX allocates code memory as in M4.2, until M4.4 replaces the hook.
- The fork and exec trap (S4.3.9): test builds of the harness intercept `fork`, `vfork`, the `exec` family, `posix_spawn`, `system`, `popen` and any sending of SIGUSR1 or SIGQUIT; a call fails the case and names the caller (plan section 7.1). How the trap intercepts calls on macOS is to be confirmed in this stage.
- Single-process mode becomes the harness default; `--mode multi` stays for comparisons.

Out: a second Windows process, `CreateProcess` from a guest and helper programs (M8.1); iOS builds and the pool hand-off (M4.4); display, input and audio (M5.1, M6.1, M6.2); 32-bit guests (M7.3).

**Read first.** C-0004 sections 1 and 2; C-0005 sections 1 to 5; C-0006 section 3; C-0010 sections 3 and 4; C-0011 sections 1 and 4; C-0012 section 1; C-0001 section 6. Research notes wine_on_ios.md (KQ1 and KQ4), process_and_memory_model.md (Q1 and Q3), x86_to_arm_translation.md (KQ4) and ios_platform_constraints.md (section 3). `docs/design/runtime-macos-harness.md`, `docs/design/forks.md` and the design document of M2.2.

**Deliverables.**

- `external/Paneport-wine` on `paneport/M4.3`: the server thread, in-process start, signal and thread-control changes, exit path, prefix bootstrap, drive letters and the entry header; `external/Paneport-FEX` only if FEX needs a change. The pointers move.
- `src/native/include/paneport/wine_bridge.h` and the session and process declarations.
- `src/native/host/`: session control, the bridge table, output capture, the JIT test hook and the dispatcher check.
- `src/Paneport.Interop/` and `tests/Paneport.Interop.Tests/`: bindings and conformance entries.
- `tests/runtime/`: single-process mode, the trap and the session cases; `tests/native/`: the CTest cases.
- `docs/design/single-process-wine.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.3.1 | Single-process design document, with the address-space plan | `docs/design/single-process-wine.md` | Review against C-0004 section 1 and C-0005 sections 3 and 4; every Wine signal and helper has a stated replacement |
| S4.3.2 | wineserver thread and in-process connections | `external/Paneport-wine` | Harness: a session starts and stops 20 times, each in a fresh harness process, and the server thread ends cleanly each time |
| S4.3.3 | In-process start of the first process | `external/Paneport-wine` | Harness: `console.exe` prints its expected lines with the trap armed |
| S4.3.4 | Signal integration through the bridge, and the dispatcher check | `external/Paneport-wine`, `src/native/host/` | Harness: `seh.exe` passes, so its faults took the guest path; a test-only path that installs a SIGSEGV handler after session start makes the check fail the session |
| S4.3.5 | Mach thread control in the fork | `external/Paneport-wine` | Harness: `threads.exe` passes its suspend and context case, and the trap counts no SIGUSR1 or SIGQUIT |
| S4.3.6 | Teardown and exit semantics | `external/Paneport-wine`, `src/native/host/` | Harness: the process-exited event carries 0, or 3 when `console.exe` is asked for 3; after the session ends, the harness keeps running, checks itself and exits with 0 |
| S4.3.7 | Prefix bootstrap and drive letters | `external/Paneport-wine`, `tests/runtime/` | Harness: a new prefix is created without a `wineboot` process, and `file_io.exe` passes on it |
| S4.3.8 | Bridge header and the host session API, with output capture | `src/native/include/paneport/`, `src/native/host/`, `src/Paneport.Interop/` | CTest: the headers compile as C17 and C++20, a fake fork receives the table, events arrive on the host event thread (C-0005 section 1), and captured output gives the same lines however it is chunked; conformance passes |
| S4.3.9 | Fork and exec trap | `tests/runtime/` | CTest: `posix_spawn` with the trap armed is reported with its caller; disarmed, nothing is reported |

**Verification.** All without recelilious's iPhone. This stage creates single-process mode in the harness, the trap, the session cases and the CTest cases.

- `runtime` job: `eng/mac/run-harness.sh --mode single`, now the default. Pass: every M4.2 case prints the same expected lines and exit codes as in multi-process mode; the trap never fires; the dispatcher check passes after every `pp_session_start`; `console.exe` passes 20 times in a row, each in a fresh harness process.
- Session cases. Pass: a second `pp_session_start` returns `PP_E_SESSION_EXISTS`; with the hook off, `pp_session_start` returns `PP_E_JIT_NOT_READY`; the exit-code and teardown cases pass as S4.3.6 states.
- `native` job: CTest under `asan-ubsan` and `tsan` for the session state machine, event delivery and output capture; the header test and interface conformance pass (C-0005 section 5).
- `checks` job: REUSE, gitleaks, the personal-data check and lychee pass.

**Done when.** The deliverables exist, the verification passes in CI on the last commit, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch.

**Risks and fallbacks.**

- Single-process Wine is the largest piece of work (plan section 9). Two iOS projects show it works, but no source documents how (wine_on_ios.md KQ4). The design comes from Wine's own source; of other products only public documentation is read, and none of their code is copied, translated or rewritten (C-0001 section 6).
- Process-wide state in ntdll and win32u (wine_on_ios.md KQ4) is fine with one process; the design document lists it for M8.1.
- If a console program still needs a helper, the fallback is in-process replacement code, never a second process.
- An attached debugger can deadlock Wine's exception handling (ios_platform_constraints.md section 3); whether lldb on the harness needs signals passed through is to be confirmed in this stage.
- M3.1 runs in parallel and may add the pool's regions to the bridge header; the stage that merges second rebases, and the header stays one file.
- If M1.2's log line format cannot mark guest output, a member is appended (C-0005 section 1). If the trap cannot intercept calls on the runner, the fork's calls go through wrappers that a test build replaces.

**Hands over to.**

- M4.4: the single-process runtime, the bridge, the session API, the prefix bootstrap, the trap and the dispatcher check.
- M8.1: one process grows to many; the trap and the inventory of process-wide state.
- M6.5: the session configuration, the launch and the drive letters (the L6 brief).
- M7.3: the address-space plan.
- M10.1: the dispatcher check, which it extends if it exists by then (the L10 brief).

**Session log.**

_No sessions yet._

## M4.4 Runtime on iOS

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L4 |
| Depends on | M4.3, M3.1, M1.3 |
| Decisions needed | None from plan section 4.2. The packaging of Wine's Unix libraries is decided in the design document. The bundle location of the template prefix and of the test flavor's test programs needs a C-0009 change, which this stage proposes. D2 decides whether the app has extended virtual addressing, which sets the runtime's address space. |
| Size | XL |
| Branch | `feat/m4-4-runtime-ios` |
| Design documents | `docs/design/runtime-ios.md` |

**Goal.** Cross-build the single-process runtime for arm64 iOS, package it as C-0009 section 4 lays out, hand it the JIT pool that M3.1 authorizes, and ship a prefix it can start from, so that the device test flavor (M1.3) can run `console.exe`. L4 shows it to recelilious.

**Scope.**

In:

- Cross-builds for arm64 iOS (S4.4.1): Wine's Unix side, FEX and the native host, by M4.2's build scripts extended for iOS, in the `ios` job (C-0009 section 3, C-0011 section 4). Whether the PE modules built for macOS can be reused unchanged is to be confirmed in this stage.
- Packaging (S4.4.2). Wine's Unix libraries become dynamic frameworks in `Frameworks/`, signed with the app's identity (C-0009 section 4), one per module or one merged, as the design document decides; iOS loads only code bundled in the app, wrapped in frameworks (wine_on_ios.md KQ1 and KQ7). The loader finds them in `Frameworks/` and the PE modules in `wine/aarch64-windows/` and `wine/arm64ec-windows/`. Modules that need macOS-only frameworks, such as `winemac.drv` with AppKit (wine_on_ios.md KQ3), are left out and listed.
- The pool hand-off (S4.4.3). The Wine fork obtains M3.1's pool registry through a bridge member and passes it to FEX (C-0005 section 3). FEX allocates executable memory only there, writes through the writable alias, runs from the execute-only view and refuses any other executable allocation (C-0006 section 3, steps 4 and 7). How FEX's code emitter handles a write address that differs from the execute address is to be confirmed in this stage. On the harness, M3.1's macOS `MAP_JIT` provider replaces M4.3's hook, so the hand-off runs in CI.
- PE code pages from the pool (S4.4.4). Code the app maps itself does not execute outside the regions the debugger prepared (C-0006 section 1), and the research infers that this holds for the PE modules Wine's loader maps, FEX's included (wine_on_ios.md KQ1 and KQ7). The loader therefore places the executable sections of PE images in the pool through the alias, to be confirmed in this stage, and their share of the pool is measured. If C-0005 section 3 or C-0006 section 3 must say so, the change is proposed on a contract branch first.
- Address-space, register and TLS constraints (S4.4.5). The research reports that iOS zeroes x18 on thread switches and that PE modules must not use implicit TLS (ios_platform_constraints.md section 3), both to be confirmed in this stage. Shared data and PE images follow M4.3's address-space plan with 16 KB alignment, and the reservations fit the smaller address map of installs without extended virtual addressing (x86_to_arm_translation.md KQ2).
- The template prefix (S4.4.6), built in CI on the macOS harness with M4.3's bootstrap for the same Wine revision (plan section 7.4), with a fixed generic user name and no host name or absolute host path. The app installs it on first launch into `Library/Application Support/Paneport/prefixes/`, excluded from backup (C-0004 section 4), through an installer in Paneport.Core whose file-system and backup-flag parts Paneport.iOS implements. A version marker detects a newer template; the design document says what is then replaced. C-0009 section 4 has no bundle location for the template or the test flavor's programs, so this stage proposes one on a `contract/<summary>` branch and waits for recelilious (C-0012 section 2).
- Guest console capture on iOS (S4.4.7): M4.3's capture reaches .NET over Paneport.Interop with bounded buffering and a line length limit, never through the process's own standard output, which .NET shares. The device test flavor bundles the x86-64 programs of `tests/win32/`.
- The bundle check and size report (S4.4.8): `tools/CheckBundle.cs`, a C# file-based tool with a `--self-test` mode, compares a built app with `eng/bundle-layout.json`: architectures and folders of frameworks and PE modules, nothing missing or misplaced, nothing that C-0009 section 4 excludes, test programs only in the device test flavor, and a clean template. It writes a size report per component to the job summary.
- `docs/design/runtime-ios.md`.

Out: display, input and audio (M5.1, M6.1, M6.2); DXMT (M5.2); 32-bit pieces (M7.3); the in-app console itself (S4.0.1).

**Read first.** C-0009 sections 3 and 4; C-0006 sections 1, 3, 4 and 7; C-0005 sections 1 to 4; C-0004 sections 1, 2 and 4; C-0003 sections 3, 6 and 8; C-0010 section 4; C-0011 sections 1 and 4; C-0012 sections 1 and 2. Research notes wine_on_ios.md (KQ1, KQ7 and KQ9), ios_platform_constraints.md (sections 1, 3, 4 and 5), x86_to_arm_translation.md (KQ2, KQ4 and KQ7) and process_and_memory_model.md (Q6). `docs/design/jit.md` (M3.1), `docs/design/single-process-wine.md`, `docs/design/runtime-macos-harness.md` and the design documents of M1.3.

**Deliverables.**

- The build scripts in `eng/mac/` extended for iOS, and the `ios` job building the runtime.
- Changes on `paneport/M4.4` in `external/Paneport-wine` and `external/Paneport-FEX`, and the moved pointers.
- `src/native/host/` and `src/native/include/paneport/wine_bridge.h`: the pool member and output delivery.
- `src/Paneport.Core/`: the prefix installer; `src/Paneport.iOS/`: its platform parts and the framework embedding; `tests/device/`: the bundled programs.
- `tools/CheckBundle.cs` and `eng/bundle-layout.json`.
- `tests/runtime/`: pool mode and template cases; `tests/native/` and `tests/Paneport.Core.Tests/`: the tests below.
- `docs/design/runtime-ios.md`, the C-0009 change, and a C-0005 or C-0006 change if S4.4.4 needs one.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.4.1 | iOS cross-build of Wine's Unix side, FEX and the host | `eng/mac/`, `external/Paneport-wine`, `external/Paneport-FEX` | The `ios` job builds them from a cold cache, and every Mach-O it produces is arm64 for iOS |
| S4.4.2 | Framework packaging and loader paths | `external/Paneport-wine`, `src/Paneport.iOS/` | `tools/CheckBundle.cs` passes on the built app; a harness case with folders laid out like the bundle and the iOS search order starts a session |
| S4.4.3 | FEX pool allocator hook and the bridge member | `external/Paneport-FEX`, `external/Paneport-wine`, `src/native/host/` | Harness pool mode: every case passes, and a request for executable memory outside the pool is refused |
| S4.4.4 | PE code pages from the pool | `external/Paneport-wine` | Harness pool mode: a test-build counter shows no executable page mapped outside the pool, and the pool share used by PE images is reported |
| S4.4.5 | Address-space, register and TLS constraints | `external/Paneport-wine`, `external/Paneport-FEX` | Review checklist of x18, implicit TLS and the reservations against the research; a harness case passes with the address map limited to the smaller size (how the harness limits it is to be confirmed in this stage) |
| S4.4.6 | Template prefix generation and install | `eng/mac/`, `src/Paneport.Core/`, `src/Paneport.iOS/` | xUnit: a first install, an existing install, an interrupted copy and a newer template each end in a complete prefix; harness: a session starts from a copy of the generated template |
| S4.4.7 | Guest console capture on iOS | `src/native/host/`, `src/Paneport.Interop/`, `tests/device/` | CTest: lines over the limit are split and marked, and a burst beyond the buffer reports how many lines were dropped |
| S4.4.8 | Bundle layout check and size report | `tools/CheckBundle.cs`, `eng/bundle-layout.json` | `--self-test` over generated fixtures: a clean bundle passes; a missing framework, a PE module in the wrong folder, a macOS Mach-O, a test program in the main app and a planted absolute path in the template each fail and name the file |

**Verification.** All without recelilious's iPhone; the device check is L4's acceptance. This stage creates `tools/CheckBundle.cs` and its self-test, pool mode in the harness, the template generation step, and the installer and capture tests.

- `ios` job: builds the runtime, the app and the device test flavor, and runs `dotnet run tools/CheckBundle.cs` on both. Pass: every expected framework and PE module is in place with the right architecture; the main app holds no test program; neither app holds anything C-0009 section 4 excludes; the template is present and clean; the size report is in the job summary.
- `dotnet` job: `tools/CheckBundle.cs --self-test` gives every result the S4.4.8 row states, and the installer tests pass.
- `runtime` job: `eng/mac/run-harness.sh --mode pool`. Pass: every case passes with M3.1's macOS provider supplying the pool, FEX allocating only from it and the loader placing PE code in it; the refusal case passes; the template step produces the template, and a session started from a copy of it runs `console.exe`.
- `native` job: CTest shows that the fork receives exactly the registry's regions, and the output limits hold. `checks` job: the personal-data check passes over `tests/` and the template fixtures.

**Done when.** The deliverables exist, the verification passes in CI on the last commit, the contract changes it needs are accepted, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch. The pull request marks the device path "not yet verified on device" (C-0012 section 1 item 9); L4 verifies it.

**Risks and fallbacks.**

- If PE images and FEX's code cache do not fit the default pool together, the measured shares go to recelilious; the pool size changes only through C-0006 section 4.
- Many frameworks may slow the launch; one merged framework is the fallback, still dynamic and replaceable (C-0003 section 3).
- If x18 or implicit TLS breaks an assumption in Wine or FEX, the fix follows merge request 11638, which finds the TEB from thread data (wine_on_ios.md KQ1). No patch from a GPL fork is used (x86_to_arm_translation.md KQ7).
- A free-account install may get too small an address map; D2 is the remedy.
- Loader path mistakes surface only on the device. The bundle check catches misplaced files; whether a non-gating iOS Simulator run is worth adding is to be confirmed in this stage.

**Hands over to.**

- L4: the runtime on the device, the template prefix and the console capture.
- M5.1: the iOS build, the packaging and the bridge for the display driver.
- M6.2 and M6.3: the runtime on iOS (the L6 brief).
- M7.3: the bundle check `tools/CheckBundle.cs` and the template prefix build, both extended by S7.3.6.
- L5: the pool hand-off and the PE code placement that the phase 1 gate checks.

**Session log.**

_No sessions yet._
