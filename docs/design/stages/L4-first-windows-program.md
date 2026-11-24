# L4 First Windows program on the iPhone

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L4 | First Windows program on the iPhone | Large | Planned | | |
| M4.1 | Forks, submodules and toolchains | Medium | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| M4.2 | ARM64EC Wine with FEX on macOS | Medium | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| M4.3 | Single-process Wine on macOS | Medium | Planned | | |
| M4.4 | Runtime on iOS | Medium | Planned | | |
| M4.5 | Shared user data for x86-64 guests | Medium | Ready | | |
| S4.0.1 | In-app console and the Run sample program action | Small | Planned | | |
| S4.1.1 | Fork repositories and integration branches | Small | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| S4.1.2 | Submodules | Small | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| S4.1.3 | Toolchain pins and Mac bootstrap update | Small | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| S4.1.4 | C-0003 register proposal and mingw-w64 runtime review | Small | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| S4.1.5 | Forks design document | Small | Done | [#17](https://github.com/recelilious/Paneport/pull/17) | 2026-11-11 |
| S4.2.1 | Wine build script for macOS | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| S4.2.2 | PE-side build | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| S4.2.3 | FEX ARM64EC build for Darwin | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| S4.2.4 | Win32 test programs | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| S4.2.5 | Harness, runner and expected outputs | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
| S4.2.6 | Runtime CI job, layout proposal and build caching | Small | Done | [#22](https://github.com/recelilious/Paneport/pull/22) | 2026-11-22 |
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
| S4.5.1 | FEX's redirection of guest reads of the shared user data | Small | Planned | | |
| S4.5.2 | x86-64 check of the shared user data and ntdll's syscall exports | Small | Planned | | |
| S4.5.3 | Record in the design document, and notes for M4.4 and M7.3 | Small | Planned | | |

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

**Integration session.** The L4 session starts once M4.1 to M4.4 are Done; L4 does not wait for M4.5. It builds the in-app console (S4.0.1) and wires it to the runtime: the console reads guest output and the process and session events through Paneport.Interop (C-0005 sections 1 and 2). The Run action reads `pp_jit_get_state`, offers the JIT setup screen when the state is not `ready`, then calls `pp_session_start` with the shared prefix and `pp_process_launch` for `console.exe` with output capture on. The session checks by SHA-256 that the test programs the device test flavor bundles are the files the `runtime` job ran. It runs the `dotnet`, `native`, `ios`, `runtime` and `checks` jobs, and `tools/CheckBundle.cs` confirms that the main app holds no test program. It prepares the acceptance steps and the evidence record template (C-0012 section 2), and after recelilious's run it completes the record, which recelilious merges.

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
- M1.1 ([L1 brief](L1-app-on-iphone.md), session log of 2026-10-31): each tool pinned in `eng/toolchain.json` needs a check in `tools/CheckToolchain.cs` under an option that runs where the tool exists, because a pin without one fails every run, and a register row mapped in `eng/license-map.json` (`docs/design/repository-checks.md` section 5). `eng/mac/bootstrap.sh` installs the Mac's tools and leaves every comparison to that tool.

**Session log.**

- **2026-11-10 (UTC), first session.** Pull request: [#17](https://github.com/recelilious/Paneport/pull/17), a draft until the contract proposal below is accepted.
  - **Decisions.** recelilious confirmed D4 and approved D5 on 2026-11-10: the forks `recelilious/Paneport-wine`, `recelilious/Paneport-FEX` and `recelilious/Paneport-dxmt`, and Wine 11.20, FEX-2610 with two fixes of FEX-2611 to `CheckCall`, DXMT `main` at `6e69d0f94290` and llvm-mingw 20261104. This session had proposed Wine 11.21 with FEX-2611; an independent check of the proposal's facts led to the older pair, the newest one reported working on Apple silicon (`docs/design/forks.md` section 2). recelilious also decided that the provenance of AI-written fork code goes into C-0003 and into the review before a public release. Plan section 4.2 records D4 and D5.
  - **Done.** S4.1.1: the three forks, public, each with `paneport/integration` as its default branch at the approved base plus its configuration commits: Wine `0bec332`, FEX `0192e62` (with the two cherry-picks), DXMT `03d8683`. S4.1.2: `.gitmodules` with the three forks over https, shallow, mapped in `eng/license-map.json`. S4.1.3: `eng/toolchain.json` pins llvm-mingw 20261104, CMake 4.4.5, Ninja 1.13.2, Meson 1.12.1, Python 3.14.10, GNU Bison 3.8.2 and flex 2.6.4, each SHA-256 from two sources; `tools/CheckToolchain.cs` gains `--mac-tools`, and its self-test grows from 79 to 89 cases; `eng/mac/bootstrap.sh` installs the tools in `$PANEPORT_TOOLS` and checks them. S4.1.4: the mingw-w64 runtime review (`forks.md` section 9) and the C-0003 1.4 proposal. S4.1.5: `docs/design/forks.md`. The `ios` job runs the forks' first targets and a planted mismatch of `--mac-tools`. Updates of `repository-checks.md`, `dev-environment.md`, the plan, the READMEs of `eng/`, `tools/` and the workflows, the design index and `eng/lychee.toml`, which now leaves `external/` alone.
  - **Verification.** CI on `1853085` ([run](https://github.com/recelilious/Paneport/actions/runs/42792375947)): `ios` passed in 7 min 34 s. The bootstrap installed the seven tools, about 100 s, and every pin matched; the planted mismatch named the missing llvm-mingw and Xcode's Python 3.9.6; the fetch of the forks showed `git submodule status` with all three at their recorded commits; Wine's `configure` with `--enable-archs=arm64ec,aarch64,i386` and `make __tooldeps__`, Wine's i386 libraries, FEX's CMake configuration for `arm64ec-w64-mingw32` and DXMT's 32-bit Meson setup all exited with 0 and logged clang 23.1.5, Bison 3.8.2, flex 2.6.4, CMake 4.4.5, Ninja 1.13.2, Python 3.14.10 and Meson 1.12.1. `dotnet` passed on both runners. `checks` fails only on the license step, for Python, GNU Bison and flex, until C-0003 1.4 is accepted. On the workstation: a fresh `git clone --recurse-submodules` of this branch into a folder of 15 characters passed in 113 s, with the three forks at their recorded commits and the four `update = none` submodules not fetched; one into a folder of 115 characters failed on Windows' path limit, which `forks.md` section 5.2 now explains; the toolchain self-test passes 89 checks, the personal-data scan finds nothing, the license check names only those three tools, and the tools' formatting passes. The scratch links of the runtime review and a dry run of the first targets against the upstream commits ran on a throwaway branch, since deleted.
  - **Contract proposal.** `contract/m4-1-fork-build-tools`, committed locally (`cb49b7d`) and not pushed until review (C-0012 section 2): C-0003 1.4. The preamble makes the provenance of AI-written fork code a required item of the review before a public release; section 4 requires a row for third-party code that a fork carries, such as FEX's submodules, before a build ships it, and records that no rule marks changed LGPL files yet (LGPL-2.1 section 2(b)); section 5 allows ZPL-2.1, gdtoa's and Sun's notices and public-domain code, and says how vendor code that an upstream embeds and a vendor SDK submodule of a fork are treated; section 6 moves the mingw-w64 runtime to Allowed after the review, records llvm-mingw's license, corrects FEX-darwin's row for its MPL-2.0 patch and adds rows for Python, GNU Bison, flex, GNU Autoconf and the mingw-w64 DirectX headers; section 10 lists Wine's and DXMT's policies, keeps all fork changes in the forks and adds the provenance rule.
  - **Left.** recelilious reviews the contract proposal. Once it is accepted, the acceptance commit goes on the contract branch, which is pushed and merged first; then `checks` passes on #17, M4.1 and S4.1.1 to S4.1.5 are set to Done in the branch's last commit, and recelilious merges #17. The forks need nothing more: their `paneport/integration` branches already hold the recorded commits.
  - **For later sessions.** Notes left in the briefs of M4.2, M5.2 and M9.1. M1.2 ([#18](https://github.com/recelilious/Paneport/pull/18)) edits `eng/toolchain.json`, `tools/CheckToolchain.cs`, `eng/mac/bootstrap.sh` and the `ios` job too. Its `cmake` pin is the same as this branch's, but it checks it with `--cmake` and `--llvm` instead of the shared `--mac-tools` this session proposed by message, and its bootstrap installs parts (`--ios`, `--native`) with `install_download` into `$PANEPORT_TOOLS_DIR`. #18 merges first, so this branch then rebases onto `main`, adds the fork tools to M1.2's design as a part of their own, renames `--mac-tools` for the tools that are left, and runs CI again. `forks.md` section 11 lists the open items: DXMT's ARM64EC setup with LLVM 15 in M5.2, the Autoconf pin, the register rows of the forks' third-party code, the rule for marking changed LGPL files, FEX-darwin's MPL-2.0 patch, and FEX's version string.
- **2026-11-10 (UTC), contract acceptance.** recelilious accepted the C-0003 1.4 proposal. The acceptance commit `c4f0c32` on `contract/m4-1-fork-build-tools` applies it as proposed, drops the proposal section and updates the contracts index; the branch is pushed with its pull request, [#19](https://github.com/recelilious/Paneport/pull/19), which merges before #17. With that register, the license check of this branch passes, as a local run against it showed. `forks.md` and the M9.1 note now speak of the accepted version.
  - **Left.** recelilious merges #19 and #18 (M1.2). Then this branch rebases onto `main` and adds the build tools of the forks to M1.2's bootstrap and checks, as the first session's log says, CI runs again, M4.1 and S4.1.1 to S4.1.5 are set to Done in the branch's last commit, and recelilious merges #17.
- **2026-11-11 (UTC), rebuild on M1.2.** recelilious merged #19 on 2026-11-10 as `e5c97ea`, and #18 (M1.2) on 2026-11-11 as `d1f659b`; the contract branch and its worktree are deleted. #18 had rewritten the files that this branch changes, so the branch was rebuilt on `main` at `d1f659b` instead of being rebased commit by commit; the commits of this repository that the entries above name, such as `1853085`, are from before the rebuild. Pull request: [#17](https://github.com/recelilious/Paneport/pull/17).
  - **Done.** The build tools of the forks follow M1.2's design.
    - `eng/mac/bootstrap.sh` gains `--forks`, a part of its own next to `--ios` and `--native`, and with no option prepares all three. M1.2's `install_download` installs the tools into `$PANEPORT_TOOLS_DIR`; it now also takes an archive that holds the program alone, as Ninja's does, and `.` for the install folder itself. Meson runs from its source folder through a script beside it that starts it with the pinned Python, and Bison is built once, in its install folder. llvm-mingw stays off the PATH: the bootstrap hands its folder on as `LLVM_MINGW`, through `GITHUB_ENV` in CI or a printed `export` line on a Mac, and puts it last on the PATH of its own check. The first session's `$PANEPORT_TOOLS` and `env.sh` are gone, and the messages that CI reads are unchanged.
    - `tools/CheckToolchain.cs` checks the six tools with `--fork-tools`, which replaces `--mac-tools`. CMake keeps M1.2's pin and `--cmake`, so this branch's identical `cmake` pin is dropped. The llvm-mingw check also reads `llvm-mingw-<release>`, the folder name that `install_download` gives. The self-test grows from M1.2's 89 cases to 99.
    - The `ios` job runs `eng/mac/bootstrap.sh --ios --forks`, and its second run must also install no tool again. The planted mismatch runs `--fork-tools`, and the forks' first targets follow M1.2's framework checks.
    - `forks.md` section 6, `dev-environment.md`, `repository-checks.md`, the READMEs of `eng/`, `tools/` and the workflows, and the notes for M4.2 use the new names.
  - **Verification.** CI on `d98dc1e`, the code of this branch ([run](https://github.com/recelilious/Paneport/actions/runs/42988046811)): every job passed, `ios` in 5 min 19 s. There, the bootstrap with `--ios --forks` installed the workload, CMake and the six tools of the forks, Bison's build included, and checked them in 109 s, and its second run installed nothing; the planted mismatch of `--fork-tools` named the missing llvm-mingw and Xcode's Python 3.9.6, and the Bison 2.3 of macOS too; `git submodule status` showed the three forks at their recorded commits; and fetching the forks (26 s), Wine's `configure` and build tools (65 s), Wine's i386 libraries, FEX's CMake configuration and DXMT's 32-bit Meson setup all exited with 0 and logged clang 23.1.5, Bison 3.8.2, flex 2.6.4, CMake 4.4.5, Ninja 1.13.2, Python 3.14.10 and Meson 1.12.1. M1.2's framework steps and the app of M1.1 passed as before. `native`, `checks` and both `dotnet` jobs passed, and the license check accepted the three submodules and the six tools. On the workstation, the self-tests of the toolchain (99 cases), personal-data (244) and license (53) checks pass, and so do the license, toolchain and personal-data checks, the tools' formatting and `bash -n` on the scripts in `eng/mac/`. The fresh clone on the workstation was not repeated: `.gitmodules` and the three submodule commits are unchanged since the first session, and the `ios` job's fresh checkout fetched the forks at those commits.
  - **Left.** recelilious reviews and merges #17. The verification passed and only the merge is left, so M4.1 and S4.1.1 to S4.1.5 are set to Done in the branch's last commit, as plan section 3.3 asks. The forks need nothing more: their `paneport/integration` branches hold the recorded commits.
- **2026-11-11 (UTC), review follow-up.** An independent review of #17 at `c1f9749`, by two reviewers and two verifiers who tried to refute each finding, found one high and nine low points. This session fixed all of them on the same branch, [#17](https://github.com/recelilious/Paneport/pull/17).
  - **High.** FK-1: FEX's `External/drm-headers` holds nine headers under GPL-2.0 WITH Linux-syscall-note alone, which C-0003 section 5 keeps out of the forks. The FEX fork marks it `update = none` in `0d4b14e`, a commit made through GitHub's contents API on `paneport/M4.1`, and `external/Paneport-FEX` points at it. Only `Source/Tools/LinuxEmulation` includes the headers, and FEX builds it only when it does not build for MinGW, so the Windows-side build needs none of them and nothing is left for recelilious to decide.
  - **Low.**
    - TC-1: before it names the install folder after the pin, `install_download` checks that llvm-mingw's archive holds the folder of the pinned release, because the version check reads the release from that name.
    - TC-2: `repository-checks.md` no longer takes a second SHA-256 from a download on the workstation.
    - TC-3: the READMEs of `eng/` and the workflows name the build tools of the forks among the pins.
    - TC-4: `PANEPORT_TOOLS_DIR` must be an absolute path.
    - FK-2: `forks.md` says why M4.1 put its configuration commits on `paneport/integration`, and that later fork changes follow its section 4.2.
    - FK-3: the deepest path of a recursive clone is 186 characters, in range-v3's `doc/gh-pages`, so a folder on Windows may have about 70.
    - FK-4: `forks.md` section 10 adds tiny-json, musl's and Arm Limited's code and Wine's headers in FEX, and DXMT's MD5 code from dxbc-spirv, and the note for M4.2 names them.
    - FK-5: the message of `5c00616` says that each fork carries one configuration commit of its own. FEX carries two, after the two cherry-picks, and now `0d4b14e` too, and DXMT two; `forks.md` section 3 is the exact list.
    - FK-6: the briefs of M4.4 and M7.3 note where the fork rules and FEX's `CLAUDE.md` are explained.
  - **Verification.** CI on `9e101d2`, the fixes ([run](https://github.com/recelilious/Paneport/actions/runs/42998423260)): every job passed, `ios` in 5 min 7 s. Its bootstrap installed llvm-mingw past the new check of the archive's folder, and `git submodule status` showed `external/Paneport-FEX` at `0d4b14e`, which GitHub served by its hash although it is not on the fork's default branch. Wine's `configure` and tools, its i386 libraries, FEX's CMake configuration, DXMT's 32-bit Meson setup and both planted mismatches passed again, and so did `native`, `checks` and both `dotnet` jobs. On the workstation, the self-tests of the toolchain (99 cases), personal-data (244) and license (53) checks pass, and so do the license, toolchain and personal-data checks, the tools' formatting and `bash -n`; the two new checks of the bootstrap were run on sample values, where the folder of another release, a folder without its variant and relative tools folders all fail. Nothing was downloaded: the facts of the review were read, and the fork commit was made, through the GitHub API. The recursive clone on the workstation was not repeated at `0d4b14e`, because it needs a download that recelilious must approve; CI's fresh checkout recorded the new commit, and `update = none` only leaves submodules out of a clone.
  - **Left.** recelilious reviews and merges #17. Then the FEX fork's `paneport/integration` is fast-forwarded to `0d4b14e` and `paneport/M4.1` is deleted, by recelilious or by Claude when recelilious asks (forks.md section 4.2). The verification passed again and only the merge is left, so M4.1 and S4.1.1 to S4.1.5 are set to Done in the branch's last commit, as plan section 3.3 asks.
  - **For later sessions.** C-0003 section 4 still gives "the thirteen submodules of FEX-Emu that its build may fetch" as its example; with `drm-headers` no longer fetched, the build fetches twelve, which a later revision of the contract can correct.
- **2026-11-11 (UTC), second review follow-up.** A check of the fixes at `3cbdd44` confirmed TC-1 to TC-4, FK-2, FK-3, FK-5 and the drm-headers fix of FK-1. It also found upstream files under licenses that C-0003 section 5 forbids where no build of Paneport compiles them, more code of other projects that the Windows-side builds compile, two more stages that change the forks, and a check of the bootstrap that ran too late. Pull request: [#17](https://github.com/recelilious/Paneport/pull/17).
  - **Decision.** recelilious decided on 2026-11-11 to change C-0003 so that such upstream files may stay where no build ships them.
  - **Contract proposal.** `contract/m4-1-unshipped-upstream-files`, committed locally (`1a2ac1e`) and not pushed until review (C-0012 section 2): C-0003 1.5. Section 4 lets a fork, and a submodule of a fork that a build needs, keep upstream files under a license that section 5 forbids, as long as nothing that ships compiles, links or copies them, their notices stay and no shipped binary contains them, and lets the source archives of a release hold them as part of a fork's source. A submodule of a fork that no build needs and that holds such content stays `update = none`, and section 4 no longer counts FEX-Emu's submodules. Section 5's Forbidden row points to that rule and still keeps code with no license out of the forks.
  - **Done.**
    - The FEX fork's `paneport/M4.1` is rebuilt as one commit on `0192e62`, `dd288e6`, which marks `External/drm-headers`, `External/Catch2` and `External/jemalloc_glibc` `update = none` and replaces `0d4b14e`. It was made through GitHub's git data API, with the local git identity as its author and committer (C-0002 section 4); `0d4b14e` had come out under the account's display name. `external/Paneport-FEX` points at it. FEX uses Catch2 only with `BUILD_TESTING`, which the MinGW build turns off, and builds jemalloc_glibc only with its glibc allocator, which FEX turns off for MinGW, so the MinGW build needs neither. FEX now fetches 10 submodules.
    - `forks.md` section 5.3 says that xxHash's GPL-2.0 command-line tool, tests and Makefiles are fetched and never built, names Wine's own `config.guess` and `config.sub`, both of which C-0003 1.5 would allow, records the further notices in range-v3's `LICENSE.txt`, and says that no build ships Tracy or its CC BY-SA 3.0 icon. Section 10 and the note for M4.2 add mingw-w64's `LoadConfig.S`, VIXL's `VixlUtils.inl` and Sebastiano Vigna's SplitMix64 in FEX, and dxbc-spirv's small vector and Alexander Bessonov's GUID parser in DXMT.
    - The briefs of M6.6 and M10.1 get the note on the fork rules and the upstreams' `CLAUDE.md` and `AGENTS.md`.
    - `eng/mac/bootstrap.sh` sets and checks `PANEPORT_TOOLS_DIR` right after the check of the SDK and Xcode, so that a relative value stops it before `dotnet workload install` changes the SDK.
    - The entry above now says that the recursive clone on the workstation was not repeated.
  - **Verification.** On the workstation, the self-tests of the toolchain (99 cases), personal-data (244) and license (53) checks pass, and so do the license, toolchain and personal-data checks, the tools' formatting and `bash -n`. Everything about the forks was read, and the fork commit was made, through the GitHub API, with nothing downloaded. The recursive clone on the workstation was not repeated at `dd288e6` either, for the same reason; CI's fresh checkout records the new commit, and `update = none` only leaves submodules out of a clone. CI runs on this round's head, and the pull request records its result.
  - **Left.** recelilious reviews the C-0003 1.5 proposal. Once it is accepted, the acceptance commit goes on the contract branch, which is pushed and merged first; then this branch says so in `forks.md`, M4.1 and S4.1.1 to S4.1.5 are set to Done in its last commit with CI green on it, and recelilious merges #17. After that merge, the FEX fork's `paneport/integration` is fast-forwarded to `dd288e6` and its `paneport/M4.1` is deleted (forks.md section 4.2).
- **2026-11-11 (UTC), C-0003 1.5 acceptance.** recelilious accepted the C-0003 1.5 proposal, narrowed to the GPL: only upstream files under GPL-2.0 or GPL-3.0, with or without an exception such as the Autoconf exception, may stay in the forks where no build ships them, and AGPL-3.0, SSPL, non-commercial-only code and code with no license never enter a fork or its submodules. The acceptance commit `9e088ed` on `contract/m4-1-unshipped-upstream-files` applies it with that narrowing, drops the proposal section and updates the contracts index; the branch is pushed with its pull request, [#20](https://github.com/recelilious/Paneport/pull/20), which merges before #17. `forks.md` section 5.3 now speaks of the accepted version. Section 4 of C-0003 no longer counts FEX-Emu's submodules, which settles the note of the first follow-up.
  - **Verification.** CI on `f02186b`, the second follow-up ([run](https://github.com/recelilious/Paneport/actions/runs/43024112472)): every job passed, `ios` in 7 min 38 s. `git submodule status` showed `external/Paneport-FEX` at `dd288e6`, and the bootstrap, both planted mismatches and every first target passed again. The license check does not read the text of C-0003, so it passes with and without #20.
  - **Left.** recelilious merges #20, then #17. Then the FEX fork's `paneport/integration` is fast-forwarded to `dd288e6` and its `paneport/M4.1` is deleted, by recelilious or by Claude when recelilious asks (forks.md section 4.2). The verification passed and only the merges are left, so M4.1 and S4.1.1 to S4.1.5 are set to Done in the branch's last commit, as plan section 3.3 asks.

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
  - **Deviation for macOS, accepted by recelilious on 2026-11-21 (decision C of the review of #22).** The fork trusts x18 instead. macOS writes a value of its own into `TPIDR_EL0` at every context switch, and the request's global TEB pointer is overwritten by every thread, so the request's lookup cannot work on macOS, while PE code reads the TEB from x18 itself. macOS keeps x18 for a main executable that names the macOS 12 SDK or an older one, so the loader is linked that way (`0e1e42c`), and the dispatchers take the TEB from x18 again (`dcade67`, `908c58a`) and find the syscall frame through it, as Wine 11.20 does (`51a6336`; runtime-macos-harness.md section 2.1). iOS keeps no such compatibility, so M4.3 and M4.4 settle x18 there first (the notes of M4.3 and the risks of M4.4 below).
- Wine's PE side, built with the pinned llvm-mingw for `arm64ec`, `aarch64` and `i386` (C-0009 section 3). Only the 64-bit guest path runs before M7.3.
- FEX's ARM64EC module for `arm64ec-w64-mingw32`, built by `eng/mac/build-fex.sh` and loaded by Wine as the x86-64 emulator named under `HKLM\Software\Microsoft\Wow64\amd64` (wine_on_ios.md KQ2). The FEX fork's own Darwin changes go on its stage branch, and the hooks that Wine must provide for them, such as JIT write protection and native JIT allocation (x86_to_arm_translation.md KQ3), go into the Wine fork. No code comes from FEX-darwin, Hadron or Arknights-MacOS-Runtime, which are read as evidence only (recelilious's decisions of 2026-11-15 and 2026-11-17, C-0003 1.6).
- The Win32 test programs in `tests/win32/`, our own C17 code under MIT, built with the pinned llvm-mingw and `-Werror` by `eng/mac/build-win32-tests.sh`, which takes the target as a parameter. This stage builds x86-64; M7.3 adds the i686 builds of plan section 7.2. Each prints a fixed line format and exits with 0 only when every check passed:
  - `console.exe`, the console test program and L4's sample program: a greeting naming its architecture, the CPU feature, checksum, structured exception handling and threads checks, and a result line; an argument sets its exit code.
  - `cpu_features.exe` (vendor and feature flags from CPUID) and `checksum.exe` (a fixed 64 MiB buffer from a fixed formula, its checksum beside the expected value, and the throughput).
  - `seh.exe`, the structured exception handling program: an access violation, an integer divide by zero and a `RaiseException` call, each caught by its own handler, with compiler `__try` blocks if the pinned llvm-mingw supports them here and a vectored handler otherwise (to be confirmed in this stage).
  - `threads.exe` (interlocked updates, a critical section, events, and one worker suspended, its context changed, then resumed) and `file_io.exe` (file operations on `C:`, and `GetLogicalDrives` reporting `C:`).
- The harness in `tests/runtime/`, our own C17 code under MIT, in `tests/runtime/harness/`. Each case is a JSON file in `tests/runtime/cases/` naming the program, arguments, timeout, exit code and expected output in `tests/runtime/expected/`. Here it starts Wine's loader as a child process with a fresh temporary prefix, captures the output and compares. Lines match exactly, except lines marked as patterns, such as the CPU feature and throughput lines. Expected files hold no host path.
- `eng/mac/run-harness.sh`: builds what is missing, runs the selected cases, each in a fresh harness process, writes a summary, and exits non-zero on any failure.
- Console programs need no display driver; how the harness tells Wine to run without one is to be confirmed in this stage.
- Four contract changes on the branch `contract/m4-2-runtime-harness` (plan section 8): C-0011 1.3 adds the runtime tests and a `runtime` job; C-0004 1.1 adds `tests/runtime/` and `tests/win32/`; C-0003 1.6 registers actions/cache, says which changed fork files carry a notice and moves FEX-darwin to the reference-only list; and C-0010 1.1 says how such a file is marked. The job runs on an Apple silicon macOS runner, restores the cached fork builds, builds what is missing, runs `eng/mac/run-harness.sh` and uploads the logs. recelilious decided on 2026-11-17 that it runs for every pull request and every push to `main`, and that a merge waits for it (plan decision D14).
- `docs/design/runtime-macos-harness.md`, and a `README.md` in `tests/runtime/` and `tests/win32/` (C-0004 section 3).

Out: single-process mode, the native host and the bridge (M4.3); iOS builds (M4.4); graphics (M5.1, M5.2); the WoW64 module and i686 builds, which wait for D11 (M7.3); multi-process programs (M8.1).

**Read first.** C-0001 sections 6 and 7; C-0003 sections 4, 6 and 10; C-0004 sections 1 to 3; C-0009 sections 2 and 3; C-0010 sections 1, 3, 4 and 6; C-0011 sections 1, 2 and 4; C-0012 sections 1 and 2. Research notes wine_on_ios.md (KQ1 and KQ2), x86_to_arm_translation.md (KQ2, KQ3 and KQ5) and process_and_memory_model.md (Q1). `docs/design/forks.md`; plan sections 7.1, 7.2, 7.6 and 8.

**Deliverables.**

- `eng/mac/build-wine.sh`, `eng/mac/build-fex.sh`, `eng/mac/build-win32-tests.sh` and `eng/mac/run-harness.sh`.
- Changes on `paneport/M4.2` in `external/Paneport-wine` and `external/Paneport-FEX`, and the moved pointers.
- `tests/win32/` with the six programs, and `tests/runtime/` with the harness, cases and expected outputs, each with its `README.md`.
- The `runtime` job in `.github/workflows/`, once the C-0011 change is accepted.
- `docs/design/runtime-macos-harness.md` and the contract branch.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.2.1 | Wine build script for macOS, carrying merge request 11638 if needed | `eng/mac/build-wine.sh`, `external/Paneport-wine` | It builds from a clean tree with the pinned tools, and the built loader prints its version |
| S4.2.2 | PE-side build for `arm64ec`, `aarch64` and `i386` | `eng/mac/build-wine.sh` | Each architecture's module list is complete, and a sample of modules shows the machine type of its folder, read with the pinned llvm-mingw's object tools |
| S4.2.3 | FEX ARM64EC build for Darwin, with the fork's Darwin changes and the Wine-side hooks | `eng/mac/build-fex.sh`, `external/Paneport-FEX`, `external/Paneport-wine` | With the module named as the x86-64 emulator, `console.exe` prints its greeting |
| S4.2.4 | Win32 test programs | `tests/win32/`, `eng/mac/build-win32-tests.sh` | They build for x86-64 with `-Werror`; on Windows 11 they print their expected lines natively and exit with 0 (optional local check) |
| S4.2.5 | Harness, runner and expected outputs | `tests/runtime/`, `eng/mac/run-harness.sh` | A planted case with one wrong expected line and one with a wrong exit code each make the runner exit non-zero and name the case; the clean set passes |
| S4.2.6 | Runtime CI job, layout proposal and build caching | `contract/m4-2-runtime-harness`, `.github/workflows/` | Once accepted, the job passes on the branch, and a second run at the same submodule commits restores the fork builds from the cache, as its log shows |

**Verification.** All without recelilious's iPhone. This stage creates the build scripts, the harness with its cases and expected outputs, and `eng/mac/run-harness.sh`.

- `eng/mac/run-harness.sh --mode multi`, on the local Mac and in the `runtime` job once it exists. Pass: each of the six programs prints exactly its expected lines (patterns where marked) and exits with 0 within its timeout; Wine reports no unhandled exception; the planted cases fail as S4.2.5 states.
- `native` job: clang-format covers `tests/runtime/` and `tests/win32/` and clang-tidy covers `tests/runtime/` (C-0011 section 2), and CTest of the harness's output comparison passes under `asan-ubsan`. clang-tidy covers `tests/win32/` in the `runtime` job, with llvm-mingw's own clang-tidy, which finds llvm-mingw's headers (runtime-macos-harness.md section 8).
- `checks` job: REUSE, gitleaks, the personal-data check over `tests/`, lychee and `tools/CheckLicenses.cs` pass.
- Until the C-0011 change is accepted, the harness runs on the local Mac, and the pull request carries its output and lists the CI check not yet run (C-0011 section 3).

**Done when.** The deliverables exist, the four contract changes are accepted (C-0012 section 1 item 3), the `runtime` job passes on the last commit, the design document is written, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch. While a contract change waits for review, the stage is Blocked with that reason (plan section 2.4).

**Risks and fallbacks.**

- Merge request 11638 may not apply to the D5 base; the fallback is a base it applies to, or a port.
- Wine 11's 4K-page simulation shares protections within a 16 KB host page, which collides with W^X where a section boundary mixes code and data (wine_on_ios.md KQ1). The two-pass protections address this, and our programs can use 16 KB section alignment (`-Wl,-section-alignment=16384`, x86_to_arm_translation.md KQ2).
- Settled on 2026-11-15 and confirmed on 2026-11-17: no code comes from FEX-darwin, so the FEX fork writes its own Darwin changes from FEX's own code and Apple's documentation, and stays MIT.
- Whether the harness needs the `com.apple.security.cs.allow-jit` entitlement for the forks' `MAP_JIT` allocations, or runs without the hardened runtime, is to be confirmed in this stage, as for M3.1's tests. Confirmed on 2026-11-16: without the hardened runtime, `MAP_JIT` memory runs code with no entitlement (runtime-macos-harness.md section 9).
- If cold fork builds do not fit the job's time limit, the job splits into a cache-warming part and a test part; the runner's limits are to be confirmed in this stage.
- Expected outputs must not depend on helpers such as `wineboot` and `services.exe` (process_and_memory_model.md Q1), because M4.3 starts one process only. No Microsoft, Valve or game binary is used (plan section 7.6).

**Hands over to.**

- M4.3: the builds, the harness, the cases and expected outputs, which must pass unchanged in single-process mode.
- M4.4: the build scripts, extended for iOS.
- M5.1, M5.2, M6.1, M6.2, M6.3, M6.5, M6.6, M7.3 and M8.1: the harness, `eng/mac/run-harness.sh`, the case format, `tests/win32/` and the `runtime` job.
- L4: `console.exe`.

**Notes from earlier stages.**

- M4.1 (session log of 2026-11-10): the forks, their integration branches and the rules for fork branches, pointers and rebases are in `docs/design/forks.md` sections 1 to 5. This stage's fork changes go on `paneport/M4.2` in each fork, and `paneport/integration` is fast-forwarded only after recelilious merges (section 4.2).
- M4.1 (session log of 2026-11-10): merge request 11638 changes `configure.ac`, so the fork's rebased copy regenerates Wine's `configure` with GNU Autoconf 2.73, the version that generated Wine 11.20's. Pin it in `eng/toolchain.json` with a `--fork-tools` check in `tools/CheckToolchain.cs`, and install it with the bootstrap's `--forks`; C-0003 registers Autoconf from version 1.4 on (forks.md section 2).
- M4.1 (session log of 2026-11-10): the forks start at Wine 11.20, at FEX-2610 with two fixes of FEX-2611 to `CheckCall`, and at DXMT `6e69d0f94290` (forks.md section 2). FEX-darwin's patches are written against FEX-2610, so they apply as written; its patch imported from the Arknights runtime is MPL-2.0, a Conditional component, and stays out until the register covers it (forks.md section 11). Superseded by recelilious's decisions of 2026-11-15 and 2026-11-17: no code comes from FEX-darwin, Hadron or Arknights-MacOS-Runtime (C-0003 1.6).
- M4.1 (session log of 2026-11-10): `eng/mac/bootstrap.sh --forks` installs the pinned build tools into `$PANEPORT_TOOLS_DIR` and hands `LLVM_MINGW` to the later CI steps, or prints its `export` line on a Mac; a build script puts `$LLVM_MINGW/bin` first on its own PATH. The first fork targets configured Wine with `--without-freetype`; the real build decides on FreeType (forks.md sections 6 and 7).
- M4.1 (session log of 2026-11-10): on the `xcode-27` image, fetching the three forks at depth 1 took about 30 s (Wine's checkout is about 500 MB), Wine's `configure` 58 s and its build tools 10 s; the bootstrap's tools take about 100 s without a cache, most of it for building Bison (forks.md sections 7 and 8).
- M4.1 (session log of 2026-11-10): caching needs `actions/cache`, which C-0003 does not register yet; register it before S4.2.6 uses it, with the keys of forks.md section 8.
- M4.1 (session log of 2026-11-10): check the linker maps of the first real FEX build against the mingw-w64 runtime objects that forks.md section 9 reviewed. An object from another runtime file adds that file's notice; one from the Cephes files is not shipped, and the stage asks recelilious.
- M4.1 (session log of 2026-11-10): the third-party code inside the forks (forks.md sections 5.3 and 10) ships once it is built; list what the builds link and propose the register rows it needs before that build, beginning with FEX's 10 fetched submodules, which have no rows, and the code that the Windows-side builds compile from the forks' own trees: tiny-json, musl's and Arm Limited's code, Wine's headers, mingw-w64's `LoadConfig.S`, VIXL's `VixlUtils.inl` and Vigna's SplitMix64 in FEX, and in DXMT the MD5 code and the small vector from dxbc-spirv and Alexander Bessonov's GUID parser (forks.md section 10, a first survey that this stage completes).
- M4.1 (session log of 2026-11-11): the FEX fork marks `External/drm-headers`, `External/Catch2` and `External/jemalloc_glibc` as `update = none`, from `dd288e6` on, because each holds GPL code and the MinGW build needs none of them: nine of the DRM headers are GPL-2.0 WITH Linux-syscall-note alone, and only `Source/Tools/LinuxEmulation`, which the MinGW build leaves out, includes them; Catch2 carries a GPL-3.0 script and is used only with `BUILD_TESTING`, which the build script keeps off; jemalloc_glibc carries GPL-3.0 `config.guess` and `config.sub` and is built only with `ENABLE_JEMALLOC_GLIBC_ALLOC`, which FEX turns off for MinGW. A build that needs one of them first needs a change to C-0003, which only recelilious can make (forks.md section 5.3).
- M4.1 (session log of 2026-11-10): LGPL-2.1 section 2(b) asks that a changed file carry a notice that it was changed, with the date, and no contract says yet how Paneport marks a changed Wine file; propose the rule before the first change to a Wine file (forks.md section 11).
- M4.1 (session log of 2026-11-10): a checkout of FEX at depth 1 holds no tag, so FEX's CMake reports an empty version; the build script sets one, such as `-DOVERRIDE_VERSION`.

**Session log.**

- **2026-11-15 (UTC), first session.** Branch `feat/m4-2-arm64ec-wine-macos`, draft pull request [#22](https://github.com/recelilious/Paneport/pull/22). M4.1, the only dependency, is Done, and D1 is recorded: every Mac step runs in CI.
  - **Done.**
    - S4.2.4: the six programs of `tests/win32/`, our own C17 under MIT, and `eng/mac/build-win32-tests.sh`, which builds them for x86-64 with the pinned llvm-mingw, `-Werror`, 16 KiB sections, linker maps and a compile database. The brief's question on SEH is answered: compiler `__try` blocks, with `-fms-extensions`. clang emits `__C_specific_handler` scope tables for MinGW but has no asynchronous exceptions there, so each fault happens in a called function. The checksum's expected value, `0x5e75a40f80343ce2`, was computed independently in C, with both ways of summing the lanes, and in JavaScript with BigInt arithmetic. The programs carry the i686 code paths, which no build has run yet.
    - S4.2.5: the harness in `tests/runtime/` (case reader, comparison with exact and pattern lines, child process in its own process group, Wine's unhandled-exception reports, logs), the seven cases and six expected outputs, the two planted cases, and `eng/mac/run-harness.sh` with its template prefix. The harness joins the native CMake project: the preset `macos-harness` builds it, and `asan-ubsan` and `tsan` run its 28 CTest cases, among them a check of every committed case and expected output.
    - S4.2.1 to S4.2.3: `eng/mac/build-wine.sh` and `eng/mac/build-fex.sh`, with the checks of S4.2.1 and S4.2.2 and the linker-map listing for forks.md section 9. They have not run yet.
    - `docs/design/runtime-macos-harness.md`; READMEs of `tests/runtime/` and `tests/win32/`; `REUSE.toml` for the expected outputs; clang-tidy's scope and `eng/mac/check-native-style.sh` extended to both folders; the `ios` job builds the Win32 programs and runs llvm-mingw's clang-tidy on them, since the `native` job has no llvm-mingw.
  - **Found.** Merge request 11638 is still open; its author keeps it on GitHub as one commit, `458eb1a48180` on `trcrsired/wine` branch `apple-silicon-mac-woa`, on Wine's master of 2026-08-13; gitlab.winehq.org refuses automated clients. The request adds no `MAP_JIT`, which FEX's code buffers need on Apple silicon (design section 2.1). Wine reads the emulator's file name from `HKLM\Software\Microsoft\Wow64\amd64` and loads it from `C:\windows\system32`, falling back to its builtin folder. FEX-2610's Unix library is optional. FEX-darwin moved its pin to a commit after FEX-2611 on 2026-11-11, and its macOS support is now five patches imported from Hadron, a Mac app that plays Windows games from a Steam library; the FEX-2610 queue is its commit `ca7997932ee7`. The pinned clang-tidy flags `&&`, `||` and `!` results and `bool` conditions of `?:` that become `bool` values in C, which the code here now avoids, as the native host does.
  - **Verification.** On the workstation, with WSL's clang 22 as a Linux stand-in: the 28 CTest cases of the harness pass under AddressSanitizer and UndefinedBehaviorSanitizer; an end-to-end run with a stand-in loader passes the clean cases and fails the planted wrong line and wrong exit code with their names, and a reported unhandled exception; clang-format is clean, and clang-tidy is clean on `tests/runtime` except Linux's own declaration of `environ`, which macOS lacks. The personal-data and license checks pass. CI on the pull request runs the `dotnet`, `checks`, `native` and `ios` jobs; the pull request records their results. Not run yet: the Wine and FEX builds and the harness under Wine.
  - **Contract proposals.** On the local branch `contract/m4-2-runtime-harness`, four commits on `main`, not pushed until review (C-0012 section 2): C-0011 1.1 (`e2bc461`), the `runtime` job and its test level, running for every pull request and joining the jobs a merge needs, the two points that the brief leaves to recelilious; C-0004 1.1 (`7f07d6c`), `tests/runtime/` and `tests/win32/` in the layout; C-0003 1.6 (`c1fea91`), `actions/cache` and a pointer to C-0010 for changed files; C-0010 1.1 (`2e1ffc3`), the notice line in a changed LGPL file of a fork.
  - **Waiting on recelilious.** Three decisions, asked on 2026-11-15: downloading checkouts of the Wine and FEX forks and of merge request 11638's commit for the fork work; whether Hadron's patches may enter the FEX fork under C-0001 section 6 (recommended: no, and write FEX's macOS changes in the fork); and a scratch workflow for the Wine and FEX builds and the harness until the `runtime` job exists. The contract review.
  - **Left.** The fork changes on `paneport/M4.2`: merge request 11638 rebased onto Wine 11.20, with `configure` regenerated by GNU Autoconf 2.73, which is pinned then; `MAP_JIT` for executable allocations and the write scopes; FEX's write scopes and 16 KB guard regions; the notice lines of C-0010 1.1 once accepted. The first runs: Wine's and FEX's builds, the module list, the linker map, the template prefix and the cases, which answer the open questions of the design's section 9. The `runtime` job with `actions/cache` once C-0011 1.1 and C-0003 1.6 are accepted. The register rows for Wine's bundled PE libraries and FEX's compiled submodules and in-tree code, read from their license files, before M4.4 ships them.
  - **For later sessions.** Notes left in the briefs of M4.3 and M7.3, in the L7 brief.
- **2026-11-16 (UTC), second session.** The same branch and pull request, after recelilious's decisions. The harness passes all seven cases under Wine and FEX on the runner, and fails the planted cases with their names.
  - **Decisions.** recelilious answered the three questions on 2026-11-15, through the session that coordinates the stages. Downloads: only a checkout of the Wine fork at depth 1, of the FEX fork without submodules, and of merge request 11638's commit `458eb1a`, as source that nothing runs, deleted when the stage no longer needs them. Hadron: no code from Hadron or from FEX-darwin's patches that build on it; the FEX fork's macOS changes are written from FEX's own code and Apple's public documentation, without opening those patches. Scratch CI: a throwaway workflow on `scratch/m4-2-*` branches, with registered actions pinned by commit, no `actions/cache`, no secrets, read permissions, no artifacts, and each branch deleted after its run. On 2026-11-16 recelilious set a standing mode in which a session waits only on contract changes, open decisions, downloads, design choices and irreversible actions.
  - **Done.**
    - Wine fork, `paneport/M4.2`: `8fe7cfe` carries merge request 11638 by trcrsired, rebased onto Wine 11.20, with the request's own `configure` hunks; `42f009e` adds the notice line of C-0010 1.1 to the 18 files it changes; `53cf7fa` maps the code that FEX allocates as ARM64EC code with `MAP_JIT` and exports `__wine_jit_write_protect`; after the probes, `0e1e42c` links the loader with 16 KB segments and the SDK version 12, `dcade67` takes the TEB from x18 again in the syscall dispatcher, and `908c58a` drops the request's other uses of `TPIDR_EL0` and its reload of x18 (design section 2.1).
    - FEX fork, `paneport/M4.2`, branched from `dd288e6`: `13c0a6a` brackets every write to FEX's code buffers with a write scope, `4d07acd` makes its guard regions 16 KB on Windows builds, `026d646` does the same for the call-return stack, and `5c3cbd8` raises the x86 divide error for a zero divisor on Windows builds (design section 3.1).
    - This branch: the pointers move to those commits (`18cc96c`, `f294105`, `cc7a647`, `7b9f820`, `b4f7bd0`); the Wine build's module check accepts ARM64X modules (`3af2d16`) and checks the segments and SDK version of the loader (`cc7a647`, `b2f1053`); the FEX build reads lld's map (`3513951`); the design document, `forks.md`, and notes for M4.4 and M7.3 (`ff58c84`, `ea8b57c`, `4581329`).
    - The contract branch `contract/m4-2-runtime-harness` gains C-0003 1.6 items 4 and 5: FEX-darwin's row moves to the reference-only list, with a row for Hadron and the decision of 2026-11-15.
    - A survey of the third-party code that the builds compile (design section 10).
    - Rebase: #21 (M1.3) and #23 (M1.3's C-0011 1.1) merged into `main` on 2026-11-16, so this branch was rebased onto `32386e5`. The conflicts were tables and steps that both sides extended, and every side was kept: `eng/README.md`, `docs/design/README.md`, the `ios` job in `ci.yml` and in `.github/workflows/README.md`, the steps of `docs/design/repository-checks.md`, renumbered, and the notes for M4.4. `src/native/CMakeLists.txt` merged by itself, keeping `device.c` and the framework identifier of D3. The contract branch was rebuilt on `main` with the same five commits; its proposal for C-0011 is now version 1.2, since 1.1 is M1.3's.
  - **Found.**
    - The rebased request builds: Wine configures and builds for `arm64ec`, `aarch64` and `i386` with Xcode 27.1's clang 23.1.5 and llvm-mingw 20261104, and the loader prints `wine-11.20`. llvm-readobj reports the 64-bit modules as ARM64X files, and Wine's programs exist in the 64-bit folder only, which the first module check did not expect.
    - macOS 27 on the runner kills an arm64 executable with 4 KB segments at exec time, which is how the request linked its loader, and keeps x18 only for a main executable that names the macOS 12 SDK or an older one; it puts its own value back into `TPIDR_EL0`, on which the request relied. `bin/wine` is Wine's launcher from `tools/wine`, which execs the loader in `lib/wine/aarch64-unix`. Executing code from file mappings, from anonymous memory made executable, and from `MAP_JIT` memory all work without an entitlement, and `MAP_JIT` refuses `MAP_FIXED`, as the hooks assume.
    - FEX lowers x86 `div` and `idiv` to ARM64's division, which returns zero for a zero divisor, so no guest saw a divide error until `5c3cbd8`; the quotient overflow that also raises it is not checked yet.
    - lld's map names an archive member without its archive, so the first FEX build's count of library members was zero; mingw-w64 names its members after their library, which the check now uses. FEX's module links no object of mingw-w64's runtime, as `forks.md` section 9 asked the first real build to check.
    - The survey: Wine compiles FFmpeg's `libavutil/aarch64/crc.S`, which states GPL-2.0-or-later, and FEX's Cephes has no license text; twelve licenses and three notices are not on C-0003 section 5's list; Wine's `NOTICES.md` misses several notices. M4.4 registers this code before it ships it, and the plan of `forks.md` section 10 moves there with it.
    - The request still writes two global variables for the TEB and the syscall frame, defines its TEB Unix calls where a build for x86-64 Linux skips them, and has no WoW64 entries for its Unix calls; a 32-bit process cannot reserve the low 4 GB, which macOS keeps as page zero (design section 11, notes for M7.3).
    - lychee's failures on the pull request on 2026-11-15 were cmake.org's expired certificate and a passing 404 of lists.gnu.org, not links of this branch; both cleared on 2026-11-16.
  - **Verification.** Scratch runs of the Wine and FEX builds and the harness, each on a branch deleted after its run; their commits hold this branch as it was before the rebase:
    - [43695160263](https://github.com/recelilious/Paneport/actions/runs/43695160263), `scratch/m4-2-first-build`, Wine `42f009e` and FEX `dd288e6`: Wine configured in 44 s, built in 1572 s with 3 jobs and installed in 32 s; its module check failed on its own expectations, fixed in `3af2d16`. FEX built in 94 s, the test programs and the harness in 11 s.
    - [43880164998](https://github.com/recelilious/Paneport/actions/runs/43880164998), `scratch/m4-2-jit`, Wine `53cf7fa` and FEX `026d646`: both forks build with the hooks, and the module check passes. `wineboot --init` was killed with signal 9 about 3 s after it started, before Wine printed anything.
    - [43888072005](https://github.com/recelilious/Paneport/actions/runs/43888072005), `scratch/m4-2-diag`: the loader, `wineserver` and `ntdll.so` carry valid ad-hoc signatures, and the system logged only AMFI's notes on them, which every program built on the runner gets.
    - Probes, small programs of our own compiled and run on the runner: [43895941446](https://github.com/recelilious/Paneport/actions/runs/43895941446) (`scratch/m4-2-probe`, whose full build was cancelled once outdated), [43896386188](https://github.com/recelilious/Paneport/actions/runs/43896386188), [43896798225](https://github.com/recelilious/Paneport/actions/runs/43896798225) and [43897063226](https://github.com/recelilious/Paneport/actions/runs/43897063226) (`scratch/m4-2-probe2` to `probe4`). They found what design section 9 records about executable memory, 4 KB segments, x18 and `TPIDR_EL0`.
    - [43898193842](https://github.com/recelilious/Paneport/actions/runs/43898193842), `scratch/m4-2-loader`, Wine `dcade67`: the new loader check failed on the launcher, fixed in `b2f1053`.
    - [43905595489](https://github.com/recelilious/Paneport/actions/runs/43905595489), `scratch/m4-2-loader2`: Wine starts, creates the prefix, connects to its wineserver and loads `ntdll.dll`, then both of `wineboot`'s processes stop with a stack overflow in `ntdll.dll`.
    - [43913447326](https://github.com/recelilious/Paneport/actions/runs/43913447326) (`scratch/m4-2-seh`) and [43920675307](https://github.com/recelilious/Paneport/actions/runs/43920675307) (`scratch/m4-2-lldb2`; [43920563956](https://github.com/recelilious/Paneport/actions/runs/43920563956) was cancelled for a wrong address): the first PE code faults in `loader_init` with x18 zero, and every later use of the TEB faults again until the stack runs out. Two more probes, [43927270434](https://github.com/recelilious/Paneport/actions/runs/43927270434) and [43927636186](https://github.com/recelilious/Paneport/actions/runs/43927636186) (`scratch/m4-2-probe5`, `probe6`), showed that macOS keeps x18 in that setting, so Wine itself zeroed it; fixed in `908c58a`.
    - [43928068884](https://github.com/recelilious/Paneport/actions/runs/43928068884), `scratch/m4-2-x18`: the first harness run. The template prefix took 58 s; `checksum`, `cpu_features`, `file_io` and `threads` passed, and the other three cases failed on the divide check alone.
    - [43936699061](https://github.com/recelilious/Paneport/actions/runs/43936699061), `scratch/m4-2-de`, Wine `908c58a` and FEX `5c3cbd8`: the workflow passed. Wine configured in 64 s, built in 1799 s and installed in 31 s; FEX configured in 14 s and built in 71 s, and its map lists 235 input files and no object of mingw-w64's runtime. `wineboot` made the template prefix in 63 s, and all seven cases passed in 10 to 13 s each: `checksum`, `console_exit_code`, `console`, `cpu_features`, `file_io`, `seh` and `threads`. The planted cases failed and were named: `wrong_exit_code` exited with 3, not 4, and `wrong_line` differs in line 1. This answers S4.2.1 to S4.2.5 by script.
    - After the rebase, on the workstation: the personal-data check and the license check pass; with WSL's clang as a Linux stand-in, the harness's 28 unit cases pass under AddressSanitizer and UndefinedBehaviorSanitizer, and the end-to-end run with a stand-in loader behaves as before. CI on the pull request at `4581329`, the rebased head, passes: `checks`, `dotnet` on both runners, `native`, and `ios` in 16 min 19 s ([run](https://github.com/recelilious/Paneport/actions/runs/43940249792)).
  - **Downloads.** The three checkouts that recelilious allowed, in the session's scratchpad since 2026-11-15, read and changed as source; nothing in them was run. They stay while the review of this branch may still ask for changes to the forks, and are deleted when the stage's fork work ends.
  - **Waiting on recelilious.** The review of the four contract proposals on `contract/m4-2-runtime-harness` (C-0011 1.2, C-0004 1.1, C-0003 1.6, C-0010 1.1) and of the move of the register rows to M4.4 (design section 10); the independent review of the pull request.
  - **Left.** S4.2.6: once C-0011 1.2, C-0004 1.1 and C-0003 1.6 are accepted, the `runtime` job in `.github/workflows/ci.yml` with `actions/cache`, its first run on the branch, and a second run at the same submodule commits that restores the fork builds from the cache. Deleting the three downloads when the stage's fork work ends.
  - **For later sessions.** Notes left in the briefs of M4.4 (register rows, GPL file, Cephes, the JIT pool on iOS) and M7.3 (WoW64 gaps).
- **2026-11-17 (UTC), revision of the contract proposals.** The same branch and pull request. recelilious answered the two points that the brief leaves open, and the session that coordinates the stages checked the four proposals against `main` and this branch.
  - **Decisions.** On 2026-11-17 recelilious decided that the `runtime` job runs for every pull request and every push to `main` and that a merge waits for it, with each fork build saved to the cache as soon as it is built, a 90-minute timeout and NuGet caches saved only on `main` (plan decision D14). recelilious also confirmed that FEX-darwin as a whole, its Wine patch and scripts included, and Arknights-MacOS-Runtime are reference only, as Hadron is; M5.2 does not open Arknights-MacOS-Runtime's patches either.
  - **Done.**
    - The contract branch, rebuilt on `main` at `4d883e6` with one commit for each contract, not pushed (C-0012 section 2): C-0011 1.3 (`15ae253`), which is 1.3 because M1.3's proposal for the scan scope takes 1.2 and merges first; C-0004 1.1 (`1780260`); C-0003 1.6 (`fc7f9a1`); C-0010 1.1 (`ee947d3`). Against the earlier proposals, C-0011 states the job's trigger on its own, names the keys of the tools cache and the fork caches, saves each build as soon as it is built, moves the build and analysis of the Win32 test programs into the `runtime` job, and says that the harness's unit tests run in the `native` job. C-0004 lists every folder of `tests/` and leaves the bundling of test programs to M4.4. C-0003 extends the dating sentence of section 6, covers a file under the LGPL in any fork, and lists FEX-darwin, Hadron and Arknights-MacOS-Runtime in section 7 with their licenses. C-0010 allows several dates on one line, keys the notice on the license of the file as well as the fork's, Apache-2.0 included, narrows the exemption of generated files, and says where the line goes, when it may follow carried work and when it goes.
    - Wine fork: `f95f7a1` adds the notice lines to `configure` and `include/config.h.in`, which carry the request's hunks placed by hand, so the revised rule gives them lines of their own; this branch moves the pointer to it.
    - This branch: decision D14 and two rows of plan section 8; the briefs of M4.2, M5.2 and M7.3, `forks.md` and the design document follow the decisions.
  - **Found.** actions/cache v6.1.0, its newest release, is commit `55cc8345863c7cc4c66a329aec7e433d2d1c52a9`. Hadron's own code is under BSD-3-Clause, and each folder of its `patches/` under the license of the project it changes, as its license files say.
  - **Waiting on recelilious.** The final acceptance of the four proposals.
  - **Left.** S4.2.6, once the proposals are accepted: the `runtime` job, with the build and analysis of the Win32 test programs moved there from the `ios` job, actions/cache mapped in `eng/license-map.json` and pinned at v6.1.0, and NuGet caches saved only on `main`; its first run, and a second run that restores the fork builds. Rebasing this branch and the contract branch onto `main` once M1.3's C-0011 1.2 has merged. Deleting the three downloads when the stage's fork work ends.
- **2026-11-18 (UTC), fourth session.** The same branch and pull request, after recelilious accepted the four contract changes on 2026-11-17.
  - **Done.**
    - Contract pull request [#29](https://github.com/recelilious/Paneport/pull/29): the branch `contract/m4-2-runtime-harness`, rebased onto `main` after M1.3 accepted C-0011 1.2, with one acceptance commit for each of C-0011 1.3, C-0004 1.1, C-0003 1.6 and C-0010 1.1. It merges right before this branch.
    - This branch, rebased onto `main` at `ff70eb8`. The commit that built the Win32 test programs in the `ios` job was dropped, so the branch leaves that job as `main` has it.
    - S4.2.6: the `runtime` job (`d016f9b`), as C-0011 1.3 describes it; the `dotnet` job saves NuGet caches only on `main` (`fd32493`); the workflows README, `repository-checks.md`, the design document and `forks.md` follow (`b54318d`).
  - **Verification.** The cold run, [44421264200](https://github.com/recelilious/Paneport/actions/runs/44421264200) on `fd32493`, passed in 44 min 16 s, 2120 s of them for Wine. The warm run at the same fork commits, [44421739191](https://github.com/recelilious/Paneport/actions/runs/44421739191) on `b54318d`, restored the tools and both forks from the cache and passed in 4 min 23 s. In both, all seven cases passed and the planted cases failed with their names. `checks` fails only on the license check until #29 brings the register row of `actions/cache`.
  - **Downloads.** The three checkouts of 2026-11-15, the Wine fork, the FEX fork and merge request 11638's commit, were deleted from the session's scratchpad on 2026-11-19, once the job had passed. Nothing in them was run.
  - **Left.** The independent review of this pull request and its fixes; then the rebase onto `main` once #29 has merged, and a green run of every job.
- **2026-11-22 (UTC), fix round 1 of the review of #22.** The same branch and pull request. The independent review of the branch at `fef2aa1` confirmed 8 of its claims and 14 in part and found new defects, three of them of high severity; the session that coordinates the stages decided the fixes, and recelilious decided A to D on 2026-11-21.
  - **Decisions.** recelilious approved the four recommendations on 2026-11-21: Cephes' e-mailed BSD permission counts as its license, with its register row in M4.4 (A); #29's acceptance commit of C-0004 1.1 is corrected to the approved tree, which the coordinating session did on 2026-11-22, moving #29's head from `b6806b4` to `1c4283a` (B); the fork's trust in x18 is a deviation from this stage's scope, recorded in the scope with its basis and the notes for M4.3 and M4.4 (C); and GNU Make gets a Tool row in a later change of the register, without blocking this pull request (D).
  - **Done.**
    - The branch, rebased onto `main` at `09fb3d8`, with the notices generator of #30 and the thread-control fix of #31.
    - Wine fork, `paneport/M4.2`: `6c0601b` adds the flag for the dynamic shared user data for arm64ec, in `configure.ac` and by hand in `configure`, and `fb8609e` makes kernelbase read the data through ntdll's export (F1); `0a3519e` takes a thread's syscall frame from the thread's own data instead of a global variable (F10). The pointer moves to `0a3519e` (`5ab668d`).
    - FEX fork, `paneport/M4.2`: `9614dd8` opens a write scope in the handler for misaligned accesses before its spin lock (F2); `e124d2d` reads the divisor of DIV and IDIV once (F3); `379b052`, `bddc603` and `aa8f7a6` check the handler's write scope by a depth that each thread counts, and report a missing scope through FEX's log. The pointer moves to `aa8f7a6` (`aca91bc`).
    - The build scripts (F5 to F7), the harness and its tests (F9), the test programs' checks of F1 to F3 and F8, CI (F11 and F12) and the documents (F4, F13, A, C and D), each as the review's finding proposed.
  - **Found.**
    - F10, the intermittent abnormal exit of the threads check in run 44429601493: merge request 11638's `__wine_get_syscall_frame_c` stored the thread's syscall frame in a global variable and returned it from there, so a thread could take the frame of another that entered a dispatcher at the same time, run its syscall on that thread's kernel stack, and leave its own x18 and stack pointer for that thread to return with (runtime-macos-harness.md section 2.1). Probes in `ntdll.so` at each syscall entry and in each signal handler showed a thread's frame holding a neighbour's x18 and stack pointer after `NtAlertThreadByThreadId`, though the thread's own x18 had been right when it entered. Ruled out on the way: FEX, TSO and self-modifying code, the reuse of a dead thread's thread data, TEB or stack, an early return from `pthread_join`, a signal in the dispatchers' user-stack windows, and the kernel: a program without Wine that names SDK 12 kept every thread's x18 through 94 billion checks while 712,428 threads came and went (runtime-macos-harness.md section 9).
    - The runner keeps `MAP_JIT` memory writable and executable for every thread, so the `runtime` job cannot catch a missing write scope through the hardware (the notes for M4.3 and M4.4); and FEX's Windows C runtime has no `stderr`, so a check inside FEX's module reports through FEX's log.
    - FEX's divisor read twice showed only with `FEX_MULTIBLOCK=0` on the runner, so the `seh` case, which runs FEX's defaults, catches it only now and then (runtime-macos-harness.md section 4).
  - **Verification.**
    - F10: run [45029460328](https://github.com/recelilious/Paneport/actions/runs/45029460328), cold, on the fixed forks: with `FEX_MULTIBLOCK=0`, a native ARM64 program of the threads check's first three phases and threads.exe under FEX each passed 20 of 20 runs with this branch's `ntdll.so`, and failed 15 and 6 of 20 runs with one built from `f95f7a1`, the fork's commit before the fix. The cold job took 35 min 33 s, 1321 s of them for building Wine.
    - F2: run [44992054732](https://github.com/recelilious/Paneport/actions/runs/44992054732): a second thread's misaligned access passes with the handler's write scope; without it, the depth check reports "Write scope depth check" and the program fails.
    - F3: runs [45026809747](https://github.com/recelilious/Paneport/actions/runs/45026809747) and [45027825766](https://github.com/recelilious/Paneport/actions/runs/45027825766): with `FEX_MULTIBLOCK=0`, the divisor race gave wrong quotients in 6 of 8 runs at `5c3cbd8` and in none of 8 at `e124d2d`.
    - F1: before the fix, a program that reads the three clocks crashed in `GetTickCount` in 2 of 2 runs of a diagnosis.
    - The final runs are listed in the pull request.
  - **Downloads.** None: the CI logs and the forks' files were read through the GitHub API, in memory.
  - **Waiting on recelilious.** The Tool row for GNU Make in a later change of the register (D).
  - **Left.** The merge of #29, then the rebase onto `main` and a green run of every job.
  - **Correction.** The log of 2026-11-16 names the wrong compiler for the Unix side: Xcode 27.1's Apple clang 21.0.0 builds it, and llvm-mingw 20261104, with LLVM 23.1.5, the PE side, as the Build Wine step of run [45029460328](https://github.com/recelilious/Paneport/actions/runs/45029460328) prints.
  - **For later sessions.** Notes left in the briefs of M4.3 (x18 in single mode, `MAP_JIT` on the runner), M4.4 (`KUSER_SHARED_DATA`, `MAP_JIT`) and M7.3 in the [L7 brief](L7-steam-library.md) (`KUSER_SHARED_DATA`).
- **2026-11-22 (UTC), fix round 2 of the review of #22.** The same branch and pull request. The check of fix round 1 at `96f389a` found 43 of its 49 fixes holding and six holding in part, and 25 new defects, three of them of medium severity and none of high; the session that coordinates the stages decided the fixes, none of which needed recelilious.
  - **Done.**
    - Wine fork, `paneport/M4.2`: `51a6336` restores Wine 11.20's prologues of `__wine_syscall_dispatcher` and `__wine_unix_call_dispatcher`, which find the syscall frame through x18, and drops the request's `__wine_get_syscall_frame_c` (K1). The pointer moves to `51a6336` (`2b4ad0a`). The FEX fork stays at `aa8f7a6`.
    - The test programs: threads.exe suspends a thread that loops on a syscall 1000 times and reads its context each time (K1's check); seh.exe's filter also takes `EXCEPTION_INT_OVERFLOW` as a raise, since Windows reads the divisor again to choose the exception code (H1); and the divisor race counts its 20,000 divisions from the first divide error, within at most 10 s.
    - The harness: a case may set environment variables and run several times, and the new cases `seh_multiblock_off` and `threads_multiblock_off` run seh.exe 3 times and threads.exe 5 times with `FEX_MULTIBLOCK=0` (F3's and F10's checks); a unit case pins the truncation flag of a cut error stream (HV2).
    - The build scripts: `build-win32-tests.sh` searches a copy of each image without zero bytes and with forward slashes, so that UTF-16 and backslash forms of a local path show too, and handles an unset or empty `HOME` (H2); `build-wine.sh` fails when otool lists no segment of the loader (K3), or when a PE architecture folder that `--enable-archs` names is missing (KV1).
    - The documents, as decided: K1, K2, K4 to K7, KV2, H3, C2 to C6, CV1, CV4, R22CD8, R22CD13, R22CD14 and the nits.
  - **Found.**
    - Setting the context that it had read, the check of the syscall loop met a separate race on the fixed build: in 1 of the 10 runs of `threads_multiblock_off` in run [45049881895](https://github.com/recelilious/Paneport/actions/runs/45049881895), the thread resumed at its x86-64 RIP as its native PC and crashed. The check now reads the context only, and M4.3's thread control settles the race (runtime-macos-harness.md section 11, the notes of M4.3).
    - Windows 11 reads the divisor again after a divide error to choose the exception code, so a division whose divisor the other thread has just changed from 0 to 1 raises `EXCEPTION_INT_OVERFLOW`, which ended seh.exe natively there (H1).
  - **Verification.**
    - K1, with `FEX_MULTIBLOCK=0`: in run [45049881895](https://github.com/recelilious/Paneport/actions/runs/45049881895), whose check also set the context back, threads.exe hung in 20 of 20 runs with an `ntdll.so` built from `0a3519e` and passed 20 of 20 with `51a6336`; in run [45061785316](https://github.com/recelilious/Paneport/actions/runs/45061785316), reading the context only, it hung in 20 of 20 runs with `0a3519e` and passed 60 of 60 with `51a6336`.
    - The new cases, with `FEX_MULTIBLOCK=0`: seh.exe with FEX's module from `5c3cbd8` failed 17 of 20 runs (run 45049881895), so 3 runs catch a return of the double read in 99.7% of jobs; threads.exe with an `ntdll.so` built from `f95f7a1` failed before its syscall loop, F10's kind of failure, in 12 and 11 of 20 runs (runs 45049881895 and 45061785316), and in 6 of 20 in fix round 1's run 45029460328, so 5 runs catch a return of the shared syscall frame in about 96% of jobs at those 29 of 60. The two cases took 9.7 s and 17.7 s in run 45061785316, `threads_multiblock_off` still with 10 runs.
    - H1, natively on the workstation's Windows 11: the old seh.exe died with `0xC0000095` in 15 of 15 runs, and the fixed one passed 15 of 15; the new threads.exe passed 5 of 5.
    - H2: of four probe images, with a wide-string path and three backslash `__FILE__` paths, the old search found none and the new one all four; clean images pass both, and an empty `HOME` no longer flags every image.
    - K3 and KV1: with stand-ins for an otool that fails, a missing i386 folder and no PE folder at all, the old script printed "ready" and exited with 0, and the new one exits with 1; the clean stand-in passes both.
    - HV2: without the truncation flag that `child.c` sets, the new unit case fails.
    - The final runs are listed in the pull request.
  - **Downloads.** None: the CI logs and the forks' files were read through the GitHub API, in memory.
  - **Waiting on recelilious.** The Tool row for GNU Make in a later change of the register (D), and who builds FEX's redirection of reads of the shared user data, which the coordinating session asks (K2).
  - **Left.** The merge of #29, then the rebase onto `main` and a green run of every job. The message of FEX's `aa8f7a6` does not say why iOS needs the change: its depth check guards the write scope that iOS enforces. That reason goes into the FEX fork's next commit, or into the message when the branch is next rebased, without rewriting the branch before then (forks.md section 4.4, K6).
  - **Correction.** The log of 2026-11-16 says that twelve licenses and three notices are not on C-0003 section 5's list, and that FEX's Cephes has no license text. The notices are four, range-v3's three and the University of Michigan's notice that OpenLDAP carries, and Cephes has a license text, `External/cephes/LICENSE`, which rests on an e-mail in which its author allowed BSD terms (runtime-macos-harness.md section 10).
  - **For later sessions.** Notes left in the briefs of M4.3 (the dispatchers' prologue state for its context code, and the race in the ARM64EC suspension), M4.4 (Wine's x64 syscall thunks and the shared user data) and M7.3 in the [L7 brief](L7-steam-library.md) (the same thunks, and the shared user data of the i386 build).
- **2026-11-22 (UTC), rebase onto `main` after #29.** The same branch and pull request. The coordinating session's check of fix round 2 found no defect, and #29 merged as `ea8ee29`, which brings C-0011 1.3, C-0004 1.1, C-0003 1.6 and C-0010 1.1 to `main`.
  - **Done.** The branch, rebased onto `ea8ee29` without conflicts: its tree differs from `ccf0625` only by #29's changes under `docs/contracts/`. With `actions/cache` in the register on `main`, the license check of the `checks` job passes too. M4.2 and S4.2.1 to S4.2.6 are set to Done, since only the merge is left of the Done-when list (plan section 3.3).
  - **Verification.** CI on the rebased head, which the pull request lists.
  - **Left.** recelilious merges #22. Then each fork's `paneport/integration` is fast-forwarded to the recorded commit, Wine's `51a6336` and FEX's `aa8f7a6`, by recelilious or by Claude when recelilious asks, and the forks' `paneport/M4.2` branches are deleted (forks.md section 4.2). The items that wait on recelilious stay as fix round 2 lists them.

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

**Notes from earlier stages.**

- M2.2 ([L2 brief](L2-phase-0-gates.md), session log of 2026-11-17): the bridge offers `pp_signals_set_guest_handler`, `pp_signals_mark_thread`, `pp_signals_unmark_thread`, `pp_signals_add_reservation` and `pp_signals_remove_reservation` of `src/native/host/signals/signals.h` unchanged; they follow C-0005 section 1. Wine's handler runs in signal context, must return, never calls the registration functions, and leaves the context unchanged when it declines; a declined fault ends the app with a record of the guest or translator role, and so does a fault inside the handler on a marked thread. Wine's own alternate stacks are kept. The Mach helpers are in `signals/thread_control.h`, and test builds run `pp_signals_check_installed` after `pp_session_start` ([signals.md](../signals.md) sections 6 to 8 and 12).
- M2.2, the Mach helpers of `signals/thread_control.h`: a state that `pp_thread_control_set_state` writes applies as written only to a thread stopped in user code. A thread suspended in a system call or a Mach trap, the usual state of a waiting guest thread, first finishes or restarts the call, and the kernel then rewrites x0 and x1, the carry flag, or pc, which a restart moves back one instruction. The wineserver therefore aborts the wait first (`thread_abort_safely`), reads the state again, and only then writes it. The helpers' error paths call `pp_error_set`, whose `_Thread_local` buffer Darwin allocates on a thread's first use and which formats with `vsnprintf`: a thread that holds another one suspended inside `malloc` can deadlock there, so M4.3 keeps such calls out of that window ([signals.md](../signals.md) section 8).
- M2.2 ([L2 brief](L2-phase-0-gates.md), session log of 2026-11-17, fix check): never send SIGSEGV, SIGBUS or SIGILL to a guest thread, not even to interrupt it. Darwin on arm64 gives a sent one a fault code and the address of the thread's last fault, so the dispatcher routes it as a fault at that address, and Wine's handler would act on a fault that did not happen ([signals.md](../signals.md) section 3).
- M4.2 (session log of 2026-11-15): `paneport_harness` is part of the native CMake project, built by the preset `macos-harness`, so single mode can link the native host there; it runs one case per process and has only multi mode, and refuses `--mode single` with a message that names this stage (runtime-macos-harness.md section 5).
- M4.2 (session log of 2026-11-15): the harness copies each program into the prefix and starts it as `C:\paneport-tests\<program>`, so no case needs a drive that maps the host's root (S4.3.7). In multi mode, `eng/mac/run-harness.sh` makes a template prefix with `wineboot`, then starts a persistent wineserver for each case outside the case's process group and ends it afterwards; single mode replaces both (runtime-macos-harness.md section 6).
- M4.2 (fix round of 2026-11-22): in single mode the harness is the process's main executable, so it must be linked with `-platform_version macos 12.0 12.0`, as Wine's loader is, for macOS to keep x18, which holds the TEB; otherwise the TEB has to be found without x18, which M4.2 found no way to do for PE code (runtime-macos-harness.md sections 2.1 and 9).
- M4.2 (fix round of 2026-11-22): the runner, a virtual M1, keeps `MAP_JIT` memory writable and executable for every thread, so the `runtime` job cannot catch a missing write scope in FEX through the hardware. Only FEX's handler for misaligned accesses has a check that stands in for it: a depth of its write scope, which the `threads` case fails on when the scope is missing (FEX `379b052`, `bddc603` and `aa8f7a6`). This stage verifies on the same runner, so a missing scope anywhere else still passes here; the first run that enforces the protection is the device check of L4's acceptance, or a run on a Mac with Apple silicon (runtime-macos-harness.md section 3.1).
- M4.2 (fix round 2 of 2026-11-22): from this stage on, thread control uses Mach calls and the in-process wineserver sends no SIGUSR1 (C-0005 section 4), so `usr1_handler`'s rebuilding of the syscall frame belongs to M4.2's multi-process mode. The Mach-based context code meets threads inside Wine's syscall dispatchers too, before they switch to the kernel stack, and must read and set their state there. Wine 11.20's prologues, which the fork restored in `51a6336`, keep it readable at every instruction: sp stays the user stack pointer, x8 and x9 hold the syscall number and the return address and x30 the stub's return, and nothing is called. A change to the prologues must keep that, and the check of threads.exe that suspends a thread in a syscall loop shows a break on macOS (runtime-macos-harness.md sections 2.1 and 4).
- M4.2 (fix round 2 of 2026-11-22): a race in Wine's ARM64EC suspension is open. In run [45049881895](https://github.com/recelilious/Paneport/actions/runs/45049881895), with Wine `51a6336`, FEX `aa8f7a6` and `FEX_MULTIBLOCK=0`, the check of a thread suspended in a syscall loop, which then set the context that it had read back with `SetThreadContext`, crashed in 1 of the 10 runs of `threads_multiblock_off`: the thread resumed at its x86-64 RIP, `0x140007D21` in threads.exe, as its native PC ("Unhandled page fault on execute access to 0000000140007D21"), after the same check had passed 20 of 20 runs in the job's diagnosis step. `context_x64_to_arm` keeps Lr, Sp and Pc, so a round trip of the thread's own context cannot do that; M4.2's reading is a race on the doorbell path of the suspension, while the thread enters or leaves simulation, in which the x86-64 context is applied as the native one. M4.2's check reads the context without setting it, and passed 70 of 70 runs that way in run [45061785316](https://github.com/recelilious/Paneport/actions/runs/45061785316). This stage's thread control by Mach calls reads and sets the contexts of ARM64EC threads, so it settles the race, and the check can then set the context again (runtime-macos-harness.md sections 4 and 11).
- M3.1 (session log of 2026-11-23): `paneport/jit.h` adds the first status code of a later group, `PP_E_JIT_STATE` (-10), and the first event kind, `PP_EVENT_JIT_STATE_CHANGED` (1), and raises `PP_ABI_VERSION_MINOR` and `HostAbi.Minor` to 3. The session group's `PP_E_JIT_NOT_READY` and `PP_E_SESSION_EXISTS` and its event kinds go beside them; whichever of M3.1 and M4.3 merges second renumbers its own. `pp_jit_is_ready` in `src/native/host/jit/jit_provider.h` is the precondition of `pp_session_start` ([jit.md](../jit.md) section 8).

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
- The pool hand-off (S4.4.3). The Wine fork obtains M3.1's pool registry through a bridge member and passes it to FEX (C-0005 section 3). FEX allocates executable memory only there, writes through the writable alias, runs from the execute-only view and refuses any other executable allocation (C-0006 section 3, steps 4 and 7). How FEX's code emitter handles a write address that differs from the execute address is to be confirmed in this stage. On the harness, M3.1's macOS backend with a simulated broker replaces M4.3's hook, so the hand-off runs in CI.
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
- If x18 or implicit TLS breaks an assumption in Wine or FEX: M4.2 found that no Wine-side lookup of the TEB keeps PE code working once x18 is zeroed. PE code reads the TEB from x18 itself, macOS rewrites `TPIDR_EL0` at every context switch, and the request's global pointer races between threads, so the fork removed the request's `TPIDR_EL0` lookup in `908c58a` (runtime-macos-harness.md sections 2.1 and 9). x18 on iOS is therefore settled first, in S4.4.5, before anything relies on it. No patch from a GPL fork is used (x86_to_arm_translation.md KQ7).
- A free-account install may get too small an address map; D2 is the remedy.
- Loader path mistakes surface only on the device. The bundle check catches misplaced files; whether a non-gating iOS Simulator run is worth adding is to be confirmed in this stage.

**Hands over to.**

- L4: the runtime on the device, the template prefix and the console capture.
- M5.1: the iOS build, the packaging and the bridge for the display driver.
- M6.2 and M6.3: the runtime on iOS (the L6 brief).
- M7.3: the bundle check `tools/CheckBundle.cs` and the template prefix build, both extended by S7.3.6.
- L5: the pool hand-off and the PE code placement that the phase 1 gate checks.

**Notes from earlier stages.**

- M4.1 (session log of 2026-11-11): fork branches, pointer moves and the fast-forward of `paneport/integration` after a merge are in `docs/design/forks.md` section 4.2. FEX's `CLAUDE.md` and `AGENTS.md` are upstream's contribution policy, which this stage's work in the fork leaves untouched (forks.md section 4.3).
- S9.1.1 ([L9 brief](L9-first-release.md), M9.1 session log of 2026-11-19): every file that this stage adds to the bundle needs a rule in `eng/release/bundle-components.json`, or the `ios` job warns, and a rule for a new component needs its texts there, such as the fork's license files as `{ "repository": "external/..." }`, or the job fails; a new NuGet package of the app needs a row of `eng/license-map.json` and texts too. `licenses/` at the root of the bundle belongs to the notices generator (`docs/design/release-engineering.md` sections 1 and 5).
- M1.3 ([L1 brief](L1-app-on-iphone.md), session log of 2026-11-15): the head embeds frameworks with a `NativeReference` of kind `Framework` in `src/Paneport.iOS/AppHead.props`, and `eng/mac/sign-adhoc.sh` signs every framework and library in the app's `Frameworks/` before the app. `eng/mac/check-app.sh` expects exactly `increased-memory-limit` as the app's entitlements and checks the host framework by name; new bundle locations need their checks there (`docs/design/ios-app-head.md` section 11).
- M4.2 (session log of 2026-11-16): the register rows for the code of other projects that Wine's PE modules and FEX's module compile come to this stage, the first that ships them; `docs/design/runtime-macos-harness.md` section 10 lists that code with its licenses. Before the modules ship, show from link maps that no module holds FFmpeg's GPL file `libavutil/aarch64/crc.S`, or leave it out of the build; register FEX's Cephes, whose author's e-mailed permission of BSD terms recelilious accepted as its license on 2026-11-21 (decision A of the review of #22), before a module that carries it ships (C-0003 section 4); and propose the licenses and notices that C-0003 section 5 does not list yet.
- M4.2 (fix round of 2026-11-22): Wine places the shared user data at an address that macOS chooses and gives the PE side its address (runtime-macos-harness.md section 2.1), but x86-64 code that reads `KUSER_SHARED_DATA` at its fixed address, `0x7ffe0000`, as Go's Windows runtime does for its clocks, reads inside the loader's page zero, and FEX passes the fault to the guest, which usually ends the program. Wine's own x64 syscall thunks read it too: the ARM64X `ntdll.dll` exports each syscall to x86-64 callers through an `EXP+#Nt...` thunk of `include/wine/asm.h` (lines 271 to 282), which tests the byte at `0x7ffe0308` before its `syscall` instruction, so every x86-64 program that calls an ntdll syscall export directly, through its imports or `GetProcAddress`, faults; the thunk keeps its Windows form, which Chromium and other programs that inspect the stubs depend on. The plan is a redirection in the emulator of guest reads of that page to the real one, written from FEX's and Wine's own code, which fixes both cases (runtime-macos-harness.md section 11). The first stage that runs x86-64 programs other than those of `tests/win32` needs it, not only a game that reads the page itself, and an x86-64 check that calls an ntdll syscall export lands with it. M4.5 builds it, a stage of its own that recelilious decided on 2026-11-23. iOS apps keep a page zero too, so the redirection is needed on the device as well, and this stage's iOS build carries it once M4.5 is done.
- M4.2 (session log of 2026-11-16): the Wine fork's `map_jit_view` and `__wine_jit_write_protect` (`dlls/ntdll/unix/virtual.c`, `53cf7fa`) are compiled for every Apple aarch64 target, iOS included, and map FEX's code with `MAP_JIT` and switch it with `pthread_jit_write_protect_np`. On iOS that memory comes from the JIT pool of M3.1 instead, so this stage replaces the `mmap` there and checks how the pool switches between writing and running; FEX's write scopes call the same export (runtime-macos-harness.md sections 2.1 and 3.1).
- M4.2 (fix round of 2026-11-22): the runner keeps `MAP_JIT` memory writable and executable for every thread, so CI cannot catch a missing write scope in FEX through the hardware; only the handler for misaligned accesses checks a depth of its scope instead (FEX `379b052`, `bddc603` and `aa8f7a6`). This stage verifies on the same runner, so the first run that enforces the protection is the device check of L4's acceptance, or a run on a Mac with Apple silicon: read a fault on a write to FEX's code buffer there as a missing scope first, and keep the depth check whatever way the pool switches (runtime-macos-harness.md section 3.1).
- M3.1 (session logs of 2026-11-23 and 2026-11-24): the pool registry is `pp_jit_pool_copy` in `src/native/host/jit/jit_provider.h`: for each region, which is 128 MiB but for the last one, which takes the rest of the pool, `exec_address`, readable and executable, `write_address`, readable and writable, both with maxima lowered where the system allows it (best effort, logged otherwise; jit.md section 6), `size`, and the signal dispatcher's handles of both views, which stay registered. The bridge member declares a struct of its own, because `paneport/wine_bridge.h` includes no other Paneport header. On the harness, the macOS backend's simulated broker, whose regions are anonymous readable and executable memory and not `MAP_JIT` memory ([jit.md](../jit.md) section 2), reaches `ready` after `pp_jit_set_enabler_opened(1)` and `pp_jit_request_pool`, with the memory stub set ([jit.md](../jit.md) sections 2, 6 and 11). M3.1 stays In progress until the pull request of S3.1.6, which follows #40, so this stage waits for it.

**Session log.**

_No sessions yet._

## M4.5 Shared user data for x86-64 guests

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L4 |
| Depends on | M4.2 |
| Decisions needed | None from plan section 4.2. recelilious decided on 2026-11-23 that the redirection is a stage of its own (item K2 of the review of #22). This stage chooses how FEX finds the guest's reads of the page: when it translates them, when they fault, or both. It records the choice in the design document, and the pull request reviews it. |
| Size | M |
| Branch | `feat/m4-5-shared-user-data` |
| Design documents | Updates `docs/design/runtime-macos-harness.md` |

**Goal.** Let x86-64 guest code read the shared user data at its fixed address, `0x7ffe0000`, as it can on Windows.
- **Why the reads fault now.** The loader's page zero covers the low 4 GB on Apple silicon. So Wine places the data where macOS chooses and gives the PE side its address (M4.2, `runtime-macos-harness.md` section 2.1). Code that reads the page at its fixed address faults:
  - Wine's own x64 syscall thunks of the ARM64X `ntdll.dll`, the `EXP+#Nt...` thunks of `include/wine/asm.h`, test the byte at `0x7ffe0308` before their `syscall` instruction. So every x86-64 program that calls an ntdll syscall export directly faults.
  - Guest code that reads the page for its clocks faults too, as Go's Windows runtime does.
- **The fix.** FEX redirects those reads to the data where Wine placed it.
- **Who waits for it.** L4's acceptance does not need it. M6.6, which plays the first imported game, waits for it.

**Scope.**

In:

- **The redirection in the FEX fork (S4.5.1), on `paneport/M4.5`.**
  - A read of the page from `0x7ffe0000` to `0x7ffe0fff` by any x86-64 instruction that reads memory gets the shared user data at the address that Wine gives the PE side. That includes the thunks' `testb $1, 0x7ffe0308`.
  - A write to the page raises an access violation in the guest, as on Windows, where the page is read-only for user mode.
  - Ordinary loads and stores get no extra check.
  - The code is written from FEX's and Wine's own code and Apple's documentation (C-0001 section 6). It takes no code from FEX-darwin, Hadron or Arknights-MacOS-Runtime (C-0003 section 7).
- **The thunks keep their Windows form,** because Chromium and other programs that inspect the stubs depend on it (`runtime-macos-harness.md` section 11).
- **The check (S4.5.2).** An x86-64 test program in `tests/win32/`, as a program of its own or a check in an existing one, comes with its expected output and a case in `tests/runtime/`, and also passes natively on Windows 11. It:
  - calls ntdll syscall exports directly, through its imports and through `GetProcAddress`, for example `NtQueryPerformanceCounter`, `NtQuerySystemTime` and `NtDelayExecution`;
  - reads fields of the page at `0x7ffe0000` itself, such as `InterruptTime`, `SystemTime`, `TickCount` and `NtMajorVersion`, and compares them with what the matching APIs return;
  - checks that a write to the page raises an access violation.
- **The record (S4.5.3).** `runtime-macos-harness.md` says how FEX finds the reads and where it takes the address from, and closes the item of section 11. This brief's M4.4 notes and the L7 brief's M7.3 notes record what follows for iOS and for i386 guests.

Out:
- i386 guests, whose PE build reads the fixed address (M7.3);
- iOS builds (M4.4);
- any change to the bytes of the thunks.

**Read first.**
- C-0001 section 6; C-0003 sections 4 and 7; C-0010 section 4; C-0011 sections 1 and 4; C-0012 section 1.
- `docs/design/runtime-macos-harness.md` sections 2.1, 3.1, 4 and 11, and `docs/design/forks.md` sections 4 and 9.
- The M4.2 section of this brief, with its session logs.

**Deliverables.**

- `external/Paneport-FEX` on `paneport/M4.5`, with C-0010 section 4's notice wherever a changed file needs one. The pointer moves. `external/Paneport-wine` changes only if Wine must hand FEX the address in a new way.
- `tests/win32/`: the check and its program. `tests/runtime/`: the case and its expected output.
- `docs/design/runtime-macos-harness.md`: the record.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S4.5.1 | FEX's redirection of guest reads of the shared user data | `external/Paneport-FEX` | Harness: the S4.5.2 case passes with the new FEX, and fails with FEX `aa8f7a6` in a temporary diagnosis step |
| S4.5.2 | x86-64 check of the shared user data and ntdll's syscall exports | `tests/win32/`, `tests/runtime/` | Natively on Windows 11, the program prints its expected lines and exits with 0 |
| S4.5.3 | Record in the design document, and notes for M4.4 and M7.3 | `docs/design/runtime-macos-harness.md`, this brief, the L7 brief | Review: section 11's item is closed, and the method and its sources are named |

**Verification.** All without recelilious's iPhone.

- **`runtime` job.** Every case passes, the new one included, and the planted cases fail with their names. A temporary diagnosis step shows the new case failing with FEX `aa8f7a6`, and is dropped before the final push.
- **Natively on Windows 11.** The new program passes (S4.5.2).
- **The other jobs.** `native`, `ios`, both `dotnet` jobs and `checks` pass.

**Done when.** The deliverables exist, the verification passes in CI on the last commit, the definition of done in C-0012 section 1 holds, and recelilious has merged the branch.

**Notes from earlier stages.**

- M4.2 (fix round 2 of 2026-11-22): `runtime-macos-harness.md` section 11, "Guest reads of the shared user data at its fixed address", gives what faults, the fix, when it is needed and its check.
- M4.2 (fix round 2 of 2026-11-22): FEX's misaligned-access handler already emulates a guest access when it faults, and its scope rules are in `runtime-macos-harness.md` section 3.1.

**Session log.**

_No sessions yet._
