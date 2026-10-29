# L1 Paneport on the iPhone

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L1 | Paneport on the iPhone | Large | Planned | | |
| S1.0.1 | Evidence record template | Small | Planned | | |
| S1.0.2 | Recorded install route | Small | Planned | | |
| M1.1 | macOS build environment and the iOS CI job | Medium | Planned | | |
| S1.1.1 | Toolchain pins | Small | Planned | | |
| S1.1.2 | Mac bootstrap script | Small | Planned | | |
| S1.1.3 | Minimal iOS project | Small | Planned | | |
| S1.1.4 | iOS CI job | Small | Planned | | |
| S1.1.5 | Development environment design document | Small | Planned | | |
| M1.2 | Native host skeleton and interface conformance | Medium | Planned | | |
| S1.2.1 | Status, errors, pp_free and ABI version | Small | Planned | | |
| S1.2.2 | Lock-free event queue | Small | Planned | | |
| S1.2.3 | Event thread and callbacks | Small | Planned | | |
| S1.2.4 | Log pipeline | Small | Planned | | |
| S1.2.5 | Memory budget estimator | Small | Planned | | |
| S1.2.6 | Crash record format and writer | Small | Planned | | |
| S1.2.7 | Paneport.Interop bindings | Small | Planned | | |
| S1.2.8 | Layout table and conformance test | Small | Planned | | |
| S1.2.9 | Native format and analysis configuration | Small | Planned | | |
| S1.2.10 | Host framework packaging | Small | Planned | | |
| M1.3 | iOS app head | Medium | Planned | | |
| S1.3.1 | Scene and root view controller | Small | Planned | | |
| S1.3.2 | Native host embedding and host adapter | Small | Planned | | |
| S1.3.3 | Info.plist and entitlements | Small | Planned | | |
| S1.3.4 | Storage layout bootstrap | Small | Planned | | |
| S1.3.5 | Settings store on disk | Small | Planned | | |
| S1.3.6 | Keychain token store | Small | Planned | | |
| S1.3.7 | Diagnostics wiring | Small | Planned | | |
| S1.3.8 | Crash record reader | Small | Planned | | |
| S1.3.9 | Ad-hoc signing, packaging and scan | Small | Planned | | |
| S1.3.10 | Device test flavor skeleton | Small | Planned | | |

## L1 Paneport on the iPhone

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L0 |
| C-0012 gate | None. The phase 1 gate at L5 checks on a device that the host framework resolves as C-0009 section 4 describes (C-0012 section 3). L1 records which import its build uses. |
| Size | S, for the integration session's own work (plan section 2.5). |
| Branch | `feat/l1-app-on-iphone` |
| Decisions needed | D2 (free or paid Apple account) before the acceptance run, because it sets the signing route that the record shows. D1 is made before M1.1 starts and D7 during it; D3 and D12 before M1.3 starts (plan section 4.2). |

**Goal.** Install Paneport on recelilious's iPhone for the first time, and show on the device what every later device stage stands on: the iOS app head with the shell of L0, the native host behind the interface of C-0005, and the facts the device gives the app. Those facts are the host ABI version, the memory limit and its Game Mode level, the entitlement values, crash records and the storage layout of C-0004 section 4. CI builds and signs ad hoc, and a sideloading tool re-signs, so no Apple identity enters CI (C-0009 section 5).

**What recelilious will see.**

- The `ios` job of the integration pull request producing two ad-hoc-signed `.ipa` artifacts from one commit: the main app and the device test flavor (plan section 7.3).
- Paneport on the iPhone, installed with AltStore 2.2 or later from Windows or with Xcode on a Mac (C-0009 section 5), opening full screen on the shell of L0.
- Diagnostics naming the native host, not the fake host of L0, with the ABI version, the memory limit and its timeline, the Game Mode level, the entitlements, crash records, the storage folders, and build and device information.
- The test flavor replacing the main app, with a developer menu that checks the interface layout, the keychain and the crash record reader on the device.

**Acceptance steps.** L1 is not a C-0012 gate. The steps show that the stage works on the device and fill the evidence record. Never copy an Apple ID, a team name or identifier, a device name or a re-signed bundle identifier from any tool.

Before you start: D2 is decided. The iPhone is a 17 Pro, 17 Pro Max, 18 Pro or 18 Pro Max on iOS 26 or 27 with Developer Mode on (C-0001 section 2, C-0009 section 1). AltServer runs on Windows with Apple's own iTunes and iCloud, not the Microsoft Store versions, and AltStore 2.2 or later is on the iPhone (`avalonia_ui_layer.md` section 2.2); or a Mac with the pinned Xcode is at hand (`docs/design/dev-environment.md`). Both `.ipa` artifacts of the `ios` job are downloaded from the pull request's last CI run.

Main app:

1. Install the main app's `.ipa` with AltStore, or with Xcode or `devicectl` on a Mac; S1.0.2 records the exact steps. **Expected:** the tool reports success and Paneport is on the home screen. Note any entitlement warning the tool shows, without account or device details.
2. Open Paneport from the home screen. If iOS asks you to trust the developer first, do so in Settings. **Expected:** within a few seconds the shell fills the screen on an empty Library page, with navigation to Library, Settings and Diagnostics. The app does not close.
3. Open Diagnostics. **Expected:** the host section names the native host and shows the ABI version as major and minor, marked compatible (C-0005 section 1), and the import used: the framework or `__Internal` (C-0009 section 4).
4. Read the build and device sections. **Expected:** the pull request's last commit hash, the version, Release, Mono with full AOT and the interpreter off (C-0009 section 3), a device model identifier such as `iPhone18,1`, and the iOS version and build. No device name appears.
5. Read the memory section. **Expected:** a limit of about 6,144 MB, which a 12 GB iPhone gives an app with `increased-memory-limit` before Game Mode (`process_and_memory_model.md`). Available memory plus the footprint equal the limit, and no warning shows. A limit far below that, such as the 3.3 GB measured on 8 GB iPhones without the entitlement, means the install dropped the entitlement: the page warns, and you record the value and stop.
6. Keep Diagnostics in the foreground for 60 seconds. **Expected:** the timeline gains a sample every second, with the peak footprint and the thermal state. If Game Mode engages, the limit rises to about 8,192 MB a few seconds after the app came to the front, and the Game Mode level shows as reached (C-0009 section 4, C-0005 section 2). Record what happened; L1 does not require the rise.
7. Read the entitlements section. **Expected:** `get-task-allow` is true, from the sideloading tool's development profile (`avalonia_ui_layer.md` section 2.2); `increased-memory-limit` is true; `extended-virtual-addressing` is false, because the default build does not declare it (C-0009 section 4). Each value names its source. No team identifier, application-identifier prefix or bundle identifier appears.
8. Read the crash record section. **Expected:** "No crash record from a previous run."
9. Read the storage section, then open the Files app at On My iPhone. **Expected:** every folder of C-0004 section 4 exists. `prefixes/`, `library/`, `downloads/` and `runtime/` are excluded from backup, `compat/` and `settings/` are not, and `Library/Caches/Paneport/` is noted as excluded by iOS. The Files app shows a Paneport folder with an Import folder.
10. Send the app to the background for 30 seconds and back. Then close it in the app switcher and reopen it. **Expected:** the shell returns each time. After the relaunch the timeline starts again, and there is still no crash record.

Device test flavor:

11. Install the test flavor's `.ipa` the same way. **Expected:** it replaces the main app, since both share one bundle identifier (D12, plan section 7.3). There is one Paneport icon with the test flavor's display name, and no extra app slot is used.
12. Open it, then its Developer entry. **Expected:** a developer menu with Interface conformance, Token store and Crash record.
13. Open Interface conformance. **Expected:** PASS. Every struct size and member offset matches between the host and Paneport.Interop (C-0005 section 5, C-0011 section 1).
14. In Token store, tap Write test item, then Read. **Expected:** "match" and the item's length, with the attributes "accessible after first unlock, this device only" and "not synchronizable" (C-0004 section 4). Tap Delete, then Read. **Expected:** no item.
15. In Crash record, tap Write test record. Close the app in the app switcher, reopen it and open Diagnostics. **Expected:** the test record from the previous run, marked as a test, with its time and fields. Tap Dismiss and relaunch. **Expected:** no crash record.
16. Install the main app's `.ipa` again. **Expected:** it replaces the test flavor. Diagnostics shows no crash record and every storage folder.
17. Fill in the evidence record, or give your notes to the next L1 session. **Expected:** every field below is present, nothing forbidden appears, and the `checks` job passes on it.

**Evidence record.**

- File `docs/evidence/YYYY-MM-DD-l1-app-on-iphone.md`, dated in UTC (C-0012 section 4). The integration session commits it as a template (S1.0.1). recelilious fills it in, or reports the results for Claude to fill in. A run on another date renames the file.
- Fields (C-0012 section 4):
  - the feature, "L1 Paneport on the iPhone", with no gate; the build as commit hash and version, the same for both flavors;
  - the device model identifier, and the iOS version and build;
  - the signing route (D2); the sideloading tool and its version, such as AltStore and AltServer, or Xcode and the install command; the three entitlement values of step 7 with their sources, and any warning the tool showed;
  - the JIT enabler (none); whether the Game Mode level was reached, and after how long; the first and the highest memory limit read;
  - the steps with their expected and observed results; the metrics: the limit timeline, the peak footprint and the thermal state over time (C-0011 section 1), and the import used; scrubbed logs, only where a step failed;
  - the verdict, the tester as a project handle (`recelilious` or `Claude`), and the date.
- Never in the record or its attachments: a UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). Here the likely leaks are what AltStore, AltServer, Xcode and Settings show, so never attach screenshots of them, nor the download paths of the `.ipa` files. Screenshots show only Paneport's own pages, which never show those values (M1.3).

**Integration session.** It starts when M1.1, M1.2 and M1.3 are Done, from `main` with all three merged.

1. Follow the session checklist (plan section 3.3) and create `feat/l1-app-on-iphone`.
2. Run `dotnet`, `native`, `ios` and `checks` on the branch (C-0011 section 3). This is the first Release build with full AOT in which the shell of M0.3, the host of M1.2 and the head of M1.3 meet, with both flavors built, signed, checked and scanned from one commit. Fix what it finds on this branch; larger work becomes a follow-up in the session log.
3. Do S1.0.1, and draft S1.0.2 from the documents of M1.1 and M1.3.
4. Match the step texts to the final labels of Diagnostics and the developer pages.
5. With a local Mac (D1), install once through Xcode or `devicectl` as a smoke check, recorded in the pull request as a device check.
6. Run the personal-data scan, gitleaks and the format checks locally (C-0011 section 2). Push and open the pull request with the acceptance steps, marking device behavior "not yet verified on device" (C-0011 section 3). Give recelilious the summary in Chinese (plan section 3.3).
7. After the run, a continuation session fills in the record, completes S1.0.2 with the route that worked, and sets L1 to Accepted once recelilious accepts it and the record is merged (plan section 2.4). A failed step leaves L1 In progress or Blocked, and the fix goes to the stage that owns the part.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S1.0.1 | Evidence record template: every field of C-0012 section 4, one row per acceptance step, and the list of what must never appear | `docs/evidence/YYYY-MM-DD-l1-app-on-iphone.md` | The personal-data check and lychee pass on it (C-0011 section 2). Review: one entry per field and per step. |
| S1.0.2 | Recorded install route: the tool and its version, the account type, the steps for a CI artifact, trusting the developer, and the seven-day re-sign on a free account. L2 and L6 install through it as the route recorded for L1. | `docs/design/dev-environment.md` | Review: it matches the evidence record and holds no Apple ID, device name or local path. lychee passes. |

**Session log.**

_No sessions yet._

## M1.1 macOS build environment and the iOS CI job

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L1 |
| Depends on | L0 |
| Decisions needed | D1 (is a local Apple silicon Mac available?) before the session starts; without one, every Mac step runs in CI. D7 (the `ios` job on every pull request or only before merging): the session measures the job's run time and asks recelilious before merging (C-0011 section 4). |
| Size | M |
| Branch | `build/m1-1-ios-ci` |
| Design documents | `docs/design/dev-environment.md` (new). Updates `docs/design/solution-and-build.md` (M0.1) with Paneport.iOS and the workload pin, and `docs/design/repository-checks.md` (M0.2) with the `ios` job and the Xcode check. |

**Goal.** Make the iOS side of the solution buildable on a macOS host: pin Xcode and the runner image next to the SDK, document how to work with a local Mac or through CI alone, create a minimal Paneport.iOS, and add the `ios` CI job that builds it without signing (C-0009 sections 1 to 3, C-0011 section 4).

**Scope.**

In:

- The iOS workload set in `global.json` for the pinned .NET 10 SDK (C-0009 section 2). The exact property is to be confirmed in this stage, and the SDK pin may move in a reviewed change.
- In `eng/toolchain.json`, the Xcode version that the workload set needs and the macOS runner image (C-0009 section 2), a pair that must exist on one hosted image. On 2026-10-27 the `macos-26` image had Xcode 26.0.1 to 26.6, Xcode 27 needed the preview label `xcode-27`, and the newest .NET 10 iOS workload, 27.1.10018, needed Xcode 27.1 (`avalonia_ui_layer.md` section 2.2). The pins are to be confirmed in this stage.
- `tools/CheckToolchain.cs` (M0.2) gains checks of `xcodebuild -version` and of the installed workload set, with new self-test cases.
- `eng/mac/bootstrap.sh`, in Bash 3.2 (C-0010 section 1), for CI and a local Mac alike. It requires macOS on Apple silicon, runs `CheckToolchain` so that one tool owns the comparisons, installs the pinned workload set, explains anything missing, and exits non-zero on a mismatch. It never touches certificates, profiles or pairing files. M1.2 extends it.
- A minimal `src/Paneport.iOS/`: `net10.0-ios` on Mono (C-0010 section 1), with an app delegate, a scene delegate and an empty view controller that shows the app name. It uses scenes from the start, because apps built with Xcode 27 must (`avalonia_ui_layer.md` section 2.2). The minimum iOS version is 26.0 (C-0001 section 2). The bundle identifier is D3's value, or a marked placeholder that M1.3 replaces. The project joins `Paneport.slnx` but not `eng/ci.slnf` (C-0011 section 4).
- The `ios` job: the pinned image and Xcode, the bootstrap, a locked restore, and a Release build for `ios-arm64` without signing (C-0009 section 3, C-0011 section 4). The properties for an unsigned device build, such as `EnableCodeSigning=false`, are to be confirmed in this stage (experiment E13 in `avalonia_ui_layer.md`). The job uploads the `.app` and prints the SDK, workload set and Xcode versions. CI holds no Apple certificate or profile (C-0009 section 5). Any new GitHub Action is registered first (C-0003 section 5).
- `docs/design/dev-environment.md`:
  - Windows: VS 2026 or Rider 2026.1 or later, since VS 2022 17.14 cannot target `net10.0`, and Pair to Mac from VS 2026 or from the command line given in `avalonia_ui_layer.md` section 2.2.
  - The Mac with the pinned Xcode and the bootstrap, and CI-only work when D1 says there is no Mac.
  - The device: Developer Mode, AltStore 2.2 or later with AltServer, the free account's three apps and seven-day re-sign against a paid account, and why SideStore and Sideloadly are not supported routes yet (C-0009 sections 1 and 5). StikDebug comes with L3 (C-0006).
  - Certificates, profiles and pairing files are never committed (C-0002 section 6), and Hot Restart is not used (C-0009 section 1).
- D7, recorded in plan section 4.2 and applied to the job's triggers.

Out: the native host, the CMake and LLVM pins and the `native` job (M1.2); Avalonia on iOS, entitlements, ad-hoc signing and the test flavor (M1.3); the forks (M4.1); the `release` job (M9.1).

**Read first.**

- C-0009 sections 1, 2, 3 and 5; C-0010 section 1; C-0011 sections 2 and 4; C-0004 section 3; C-0003 sections 5 and 6; C-0002 section 6; C-0012 section 1.
- `avalonia_ui_layer.md` sections 2.2 and 3.2 (versions, runner images, building and signing from Windows); `ios_platform_constraints.md` section 4.
- `docs/design/solution-and-build.md` and `docs/design/repository-checks.md`.

**Deliverables.**

- `global.json`, `eng/toolchain.json`, `tools/CheckToolchain.cs`, and `eng/mac/bootstrap.sh`, described in `eng/README.md`.
- `src/Paneport.iOS/` with its `README.md` and lock file, and `Paneport.slnx`.
- `.github/workflows/ci.yml` and `.github/workflows/README.md`.
- `docs/design/dev-environment.md` with its row in `docs/design/README.md`, the two document updates, and the D7 row of plan section 4.2.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S1.1.1 | Toolchain pins | `global.json`, `eng/toolchain.json`, `tools/CheckToolchain.cs` | Self-test on synthetic outputs: the pinned pair passes; another Xcode or workload set fails and names the expected and the found value. |
| S1.1.2 | Mac bootstrap script | `eng/mac/bootstrap.sh` | `bash -n` passes. On the runner it exits 0 with the pinned Xcode, and non-zero with a message when `DEVELOPER_DIR` points at another Xcode. If the image has only one Xcode, a test-only variable sets a wrong pin instead (to be confirmed). |
| S1.1.3 | Minimal iOS project | `src/Paneport.iOS/` | The `ios` job builds it. Its Info.plist names the scene delegate and iOS 26.0, and `lipo -info` reports arm64 only. |
| S1.1.4 | iOS CI job | `.github/workflows/ci.yml` | Green on the pull request's last commit, with the `.app` artifact and the three versions in its log. Its run time is reported for D7. |
| S1.1.5 | Development environment design document | `docs/design/dev-environment.md` | Review: every section exists, no example holds an Apple ID, device name or local path, and the public sources are listed (C-0001 section 6). lychee passes. |

**Verification.** All of it runs in CI, without recelilious's iPhone.

| Check | Passes when |
|---|---|
| `ios` job | It is green. Its log shows the SDK, the workload set and the Xcode version, each equal to its pin, and `CheckToolchain` exits 0. |
| The planted mismatch step in the `ios` job | The bootstrap, run against another Xcode, exits non-zero and names both versions. The step passes only in that case. |
| The `.app` artifact | It has the scene manifest and the minimum iOS version, its executable is arm64 only, and `codesign -dv` reports that it is not signed. |
| `dotnet` and `checks` jobs | Green. `eng/ci.slnf` is unchanged, the new self-test cases pass, the license checker accepts any new action, and lychee passes on the new document. |

Scripts this stage creates: `eng/mac/bootstrap.sh` and the new `CheckToolchain` cases.

**Done when.** The verification passes, `dev-environment.md` is merged with the stage (C-0012 section 1 item 8), the job's triggers follow D7, recelilious has merged the branch, the status is Done and the session log is updated.

**Risks and fallbacks.**

- No workload set may have its Xcode on a stable hosted image. The options are an older workload set, the preview label, or building on recelilious's Mac (D1); recelilious decides.
- Moving the SDK pin regenerates the lock files of L0, and the `dotnet` job must stay green.
- An unsigned device build may need more properties than E13 lists. Ad-hoc signing comes with M1.3.
- A locked restore for `ios-arm64` may add runtime-specific lock entries. If `tools/CheckLicenses.cs` cannot read them, this branch fixes the reader.
- macOS runner minutes are free only while the repository is public (C-0011 section 4).

**Hands over to.**

- M1.2: the bootstrap and the `ios` job, extended with the CMake and LLVM pins, the native build and the `native` job.
- M1.3: Paneport.iOS with scenes, the `ios` job and the development environment document.
- M4.1: the macOS build environment and `eng/toolchain.json` for the forks' tools.
- M9.1: the runner image pin and the `ios` job steps that the `release` job reuses.
- L1: the install setup that S1.0.2 completes.

**Session log.**

_No sessions yet._

## M1.2 Native host skeleton and interface conformance

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L1 |
| Depends on | M1.1 |
| Decisions needed | None. D1 helps: with a local Mac, CTest and the sanitizer builds run before each push; without one, only the `native` job runs them. |
| Size | L |
| Branch | `feat/m1-2-native-host` |
| Design documents | `docs/design/native-host.md` (new: the native host and its headers) and `docs/design/interop.md` (new: Paneport.Interop, whose full design L0 assigned to this stage). Updates `docs/design/repository-checks.md` (M0.2) with the `native` job. |

**Goal.** Create the native host as a CMake project with the life cycle, diagnostics and memory groups of C-0005 section 2, the host event thread, its lock-free queue, the log pipeline and the crash record writer. Give Paneport.Interop its first source-generated bindings, prove that C and C# agree on every struct layout, add the native checks and the `native` CI job, and package the host as `paneport_host.framework` for iOS.

**Scope.**

In:

- The CMake project at `src/native/`, with `CMakeLists.txt` and `CMakePresets.json`, and shared presets and toolchain files in `eng/cmake/` (C-0004 section 3). Presets: `ios-device`, the framework the app ships; `ios-device-test`, the same with the test-build exports, for the device test flavor; `macos-test`, the host test library; and `asan-ubsan` and `tsan`, the names C-0011 section 4 fixes.
- C17, with Objective-C and ARC only where Apple APIs need it, `-Wall -Wextra -Wpedantic -Werror` with SDK headers as system headers, no variable-length arrays, and no mutable global state except documented singletons (C-0010 sections 1 and 3).
- Headers in `src/native/include/paneport/` that follow C-0005 section 1: `extern "C"` guards, the naming rules, `pp_status_t`, the two ABI version macros, `struct_size` first in every struct, and a comment on every function with its purpose, thread rule and memory ownership (C-0010 section 3). Only the implemented groups are declared; later groups come with their stages and raise the minor version.
- In `src/native/host/`:
  - `pp_abi_version`, `pp_last_error` (per thread, valid until that thread's next call) and `pp_free`, all [any].
  - `pp_init` [main], taking a configuration struct with the crash record folder, which the host copies (C-0005 section 1), and starting the host event thread; `pp_shutdown` [main], after which no callback runs. A second `pp_init` fails. Test runners do not run tests on the process's main thread, so in test builds the first caller of `pp_init` stands in for it; `native-host.md` records the rule.
  - `pp_set_event_callback` and `pp_set_log_callback` [main]. Callbacks run only on the event thread, fed by a bounded lock-free queue with many producers: fixed-size entries, no allocation on push, and overflow counted and logged once.
  - The log pipeline, the only path for native logs (C-0010 section 3), with no secret in any line (C-0010 section 2) and a small buffer for lines logged before a callback is set.
  - `pp_memory_get_budget` [any] (C-0005 section 2). On iOS the limit is `os_proc_available_memory()` plus `phys_footprint` from `TASK_VM_INFO` (`process_and_memory_model.md`), and the Game Mode flag is set at about 8,192 MB, with the exact threshold in `native-host.md`. No private call is used. On macOS a stub reports values that tests set. The sampling cadence and pressure events stay with M6.3.
  - The crash record of C-0005 section 2: a fixed binary layout documented in `native-host.md` (magic, version, size, time, signal and code, fault address, program counter, thread role, a test flag and a short message), and a writer that uses only async-signal-safe calls on a file that `pp_init` prepares. M2.2 calls it.
  - Test-build exports named `pp_test_...`, only in `ios-device-test` and `macos-test`: the layout table of C-0005 section 5, and a call that writes a test crash record.
- Paneport.Interop with `AllowUnsafeBlocks` and `DisableRuntimeMarshalling`, `LibraryImport` declarations, blittable structs, and `[UnmanagedCallersOnly]` callbacks that catch every exception (C-0005 section 1, C-0010 section 2). It refuses to start on a major version mismatch. On iOS it imports `@rpath/paneport_host.framework/paneport_host`, with `__Internal` as the fallback (C-0009 section 4). How the library name differs per target is to be confirmed in this stage and recorded in `interop.md`. It still depends on nothing else in the solution (C-0004 section 2).
- `tests/native/`: CTest with a small assertion header (C-0011 section 1), and a test that compiles every public header as C17 and as C++20 (C-0005 section 5).
- `tests/Paneport.Interop.Tests/` on macOS, outside `eng/ci.slnf` (C-0011 section 4): the layout comparison, with a planted wrong definition, and binding tests against the test library. How C# measures member offsets under `DisableRuntimeMarshalling` is to be confirmed in this stage. The comparison class also runs in the device test flavor (M1.3).
- Root `.clang-format` (LLVM style, four-space indentation, 100 columns) and `.clang-tidy`, for `src/`, `tests/` and `tools/` only (C-0010 sections 3 and 4).
- The CMake pin and the LLVM release of clang-format and clang-tidy in `eng/toolchain.json`, each with the SHA-256 of its download (C-0009 section 2), checked by `CheckToolchain` and installed by the bootstrap outside the repository.
- The `native` job (C-0011 section 4): format and analysis checks, CTest with `asan-ubsan` and with `tsan`, the `macos-test` library, and `tests/Paneport.Interop.Tests` against it.
- `paneport_host.framework` for `arm64-apple-ios`, with the install name `@rpath/paneport_host.framework/paneport_host` and its own Info.plist (C-0009 section 4), built in the `ios` job with both iOS presets. M1.3 signs it.
- A `README.md` in each new folder that C-0004 section 3 shows.

Out: the dispatcher (M2.2); the JIT group (M3.1); the session, process, display, input, audio and Steam groups (M4.3 to M8.2); the Wine bridge header, which comes with the Wine session work; the sampler and the governor (M6.3); embedding the framework in the app (M1.3).

**Read first.**

- C-0005, all sections; C-0004 sections 1 to 3; C-0010 sections 1, 2, 3, 4 and 6; C-0011 sections 1, 2 and 4; C-0009 sections 2 to 4; C-0003 section 6; C-0012 section 1.
- `process_and_memory_model.md` (the limit estimate and the Game Mode level); `avalonia_ui_layer.md` section 2.2 (frameworks, P/Invoke and callbacks) and section 3.3 (threads and queues).
- `docs/design/solution-and-build.md`, `docs/design/repository-checks.md` and `docs/design/dev-environment.md`.

**Deliverables.**

- `src/native/`, with `include/paneport/`, `host/`, `CMakeLists.txt` and `CMakePresets.json`; `eng/cmake/`.
- `src/Paneport.Interop/`, `tests/native/` and `tests/Paneport.Interop.Tests/`.
- `.clang-format` and `.clang-tidy`.
- `eng/toolchain.json`, `tools/CheckToolchain.cs`, `eng/mac/bootstrap.sh`, and `eng/mac/check-framework.sh`, which checks the install name and the exported symbols.
- `.github/workflows/ci.yml`: the `native` job, and the framework build in the `ios` job.
- `docs/design/native-host.md` and `docs/design/interop.md`, with their rows in `docs/design/README.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S1.2.1 | Status, errors, `pp_free` and ABI version | `src/native/include/paneport/`, `src/native/host/` | CTest: the version decodes as major and minor; an error message set on one thread is not visible on another and stays valid until that thread's next call; host memory freed with `pp_free` leaves no AddressSanitizer report. |
| S1.2.2 | Lock-free event queue | `src/native/host/` | CTest under `tsan`: eight producers and one consumer move 1,000,000 entries, each arriving once, intact and in order per producer; a full queue refuses the push, and the overflow count equals the refused entries. |
| S1.2.3 | Event thread and callbacks | `src/native/host/` | CTest: events posted from many threads reach the callback only on the event thread; none arrives after `pp_shutdown` returns; a second `pp_init` fails; a call from the wrong thread fails with its error. |
| S1.2.4 | Log pipeline | `src/native/host/` | CTest: lines from many threads arrive whole and in order per thread; lines logged before the callback is set arrive once it is; an overlong line is cut, never overflowed. |
| S1.2.5 | Memory budget estimator | `src/native/host/` | CTest with the macOS stub: the limit equals available memory plus the footprint; the Game Mode flag is off at 6,144 MB and on at 8,192 MB; a smaller `struct_size` gets only the members it knows. The iOS path builds in the `ios` job, and L1 shows its readings. |
| S1.2.6 | Crash record format and writer | `src/native/host/` | CTest: the writer, called inside a signal handler on a test thread, leaves a file whose bytes match the documented layout, and a test record carries its flag. |
| S1.2.7 | Paneport.Interop bindings | `src/Paneport.Interop/` | The `dotnet` job builds it with the AOT and trimming analyzers and no warning. On macOS every binding is called against the test library, and an exception thrown in a callback is caught and logged, never crossing into C. |
| S1.2.8 | Layout table and conformance test | `src/native/host/` (test builds), `tests/Paneport.Interop.Tests/` | Every struct's size and member offsets match between C and C#, and the planted wrong definition is reported with its struct and member. |
| S1.2.9 | Native format and analysis configuration | `.clang-format`, `.clang-tidy` | clang-format in check mode and clang-tidy report nothing on the new code. A planted format error in an uncommitted scratch copy fails the check. |
| S1.2.10 | Host framework packaging | `src/native/`, `eng/mac/check-framework.sh` | In the `ios` job, `otool -D` prints `@rpath/paneport_host.framework/paneport_host`; `nm` shows only `pp_` exports in the release build and only added `pp_test_` exports in the test build; both are arm64. |

**Verification.** All of it runs in CI, without recelilious's iPhone.

| Check | Passes when |
|---|---|
| `native` job | clang-format and clang-tidy report nothing. CTest passes with `asan-ubsan` and with `tsan`, with no sanitizer report. The header compile test passes as C17 and C++20. `tests/Paneport.Interop.Tests` passes against the `macos-test` library, including the conformance test and its planted mismatch. |
| `ios` job | Both frameworks build, `eng/mac/check-framework.sh` passes on each, and the minimal app of M1.1 still builds. |
| `dotnet` and `checks` jobs | Green. Paneport.Interop builds in `eng/ci.slnf`, the trimmed publish of `tests/Paneport.TrimTest` shows no new warning, REUSE finds the SPDX lines in every new C, header and CMake file (C-0010 section 6), and the license checker accepts the new pins. |

Scripts this stage creates: the CTest suite and its assertion header, `eng/mac/check-framework.sh`, the presets and toolchain files, and the extensions of the bootstrap and `CheckToolchain`.

**Done when.** The verification passes, both design documents are merged with the stage, every header function carries the comment that C-0010 section 3 asks for, recelilious has merged the branch, the status is Done and the session log is updated. The pull request marks the iOS readings "not yet verified on device" (C-0012 section 1 item 9).

**Risks and fallbacks.**

- `os_proc_available_memory` exists only on iOS. The macOS stub keeps the interface testable, and L1 shows the real readings.
- Whether `tsan` and `asan-ubsan` work with the pinned Xcode's clang on the hosted image is to be confirmed in this stage. If one does not, the session asks recelilious, because C-0011 section 1 requires both builds.
- clang-tidy from the pinned LLVM release may disagree with Apple's clang about flags; `.clang-tidy` documents each exclusion and its reason.
- The host event thread becomes known to the runtime when it first runs a managed callback. That is expected for this thread and for no other host thread (C-0004 section 1).
- If the framework import does not resolve under Mono on the device, `__Internal` is the fallback (C-0009 section 4). M1.3 chooses, and L1 shows the result.

**Hands over to.**

- M1.3: the framework from both iOS presets, the bindings, the crash record layout and the test-build exports.
- M2.2: the event thread, the log pipeline, the crash record writer, the pattern for test-build exports, CTest and the sanitizer presets.
- M3.1, M4.3, M4.4, M6.1 and M6.3: the header conventions, the queue, the memory estimator and the presets, each extended with its own functions.
- M10.1 and M10.2: Paneport.Interop and its tests, which move to CoreCLR.

**Session log.**

_No sessions yet._

## M1.3 iOS app head

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L1 |
| Depends on | M1.1, M1.2, M0.3 |
| Decisions needed | D3 (the bundle identifier) and D12 (whether the test flavor shares it; recommended) before the session starts. This brief assumes D12 is yes; otherwise the test flavor gets its own identifier, and L1 steps 11 and 16 change. |
| Size | L |
| Branch | `feat/m1-3-ios-app-head` |
| Design documents | `docs/design/ios-app-head.md` (new: the scene and view tree, startup, the host adapter, Info.plist and entitlements, storage, settings, the token store, Diagnostics, crash records, packaging, ad-hoc signing, the `.ipa` scan and the test flavor). Updates `docs/design/app-shell.md` (M0.3) with the iOS composition root, and `docs/design/dev-environment.md` (M1.1) with installing the CI artifacts. |

**Goal.** Turn the minimal Paneport.iOS into the app head of C-0004 section 2: the shell of M0.3 on a UIKit root view controller over an idle game view, the host of M1.2 behind the runtime host interface, the storage layout and keychain of C-0004 section 4, a Diagnostics page with real readings, and CI that produces ad-hoc-signed `.ipa` files for the main app and the device test flavor.

**Scope.**

In:

- Paneport.iOS on `net10.0-ios` with Mono, full AOT in Release and the interpreter off (C-0010 section 1, C-0009 section 3), referencing Core, Steam, UI and Interop (C-0004 section 2). Avalonia's iOS packages without a register row are registered first (C-0003 section 5).
- An app delegate and a `UIWindowSceneDelegate` that create the window and a C# root view controller. It owns a full-screen GameView, a `UIView` with an opaque, idle `CAMetalLayer`, under one full-screen AvaloniaView that shows the main view of M0.3 (`avalonia_ui_layer.md` sections 2.1 and 3.1). Avalonia 12 has no API for an app-supplied root view controller, and its maintainer advises hosting AvaloniaView in your own (section 2.1). How Avalonia's platform starts without its own scene delegate is to be confirmed in this stage, without copying Avalonia's app delegate (C-0001 section 6).
- The composition root: a host adapter over Paneport.Interop, the keychain token store, the settings store, a log sink in `Library/Caches/Paneport/`, an empty library service and the Steam stub of M0.3. Startup runs on the main thread: the ABI major check, where a mismatch shows a message instead of the shell, then `pp_init` with the crash record folder, then the shell. Host events reach the view models on the UI thread.
- The framework embedded with a `NativeReference` of kind `Framework` and imported as `@rpath/paneport_host.framework/paneport_host`, with `__Internal` as the fallback (C-0009 section 4). The main app embeds the `ios-device` build, the test flavor the `ios-device-test` build.
- `Entitlements.plist` and Info.plist as C-0009 section 4 says: `increased-memory-limit` declared explicitly; no `get-task-allow`, which the profile provides; `extended-virtual-addressing` only when `PaneportExtendedVirtualAddressing` is `true`; the six keys from `LSApplicationCategoryType` to `LSSupportsOpeningDocumentsInPlace`; the scene manifest; and the bundle identifier from D3. `BGTaskSchedulerPermittedIdentifiers` waits for M7.1. `ios-app-head.md` sets the version scheme of development builds; release versions come from tags (C-0009 section 6, M9.1).
- A build stamp: the commit hash, the version, the configuration, the runtime and AOT mode, the interpreter setting and the host import.
- The storage bootstrap: at launch, every folder of C-0004 section 4 is created, the folders marked "No" are excluded from backup, with a helper that marks them again after files are replaced, and `Documents/Import/` shows in the Files app.
- The settings store of M0.3 on disk in `settings/`, written with `System.Text.Json` source generation (C-0010 section 2) and replaced atomically.
- The keychain token store behind M0.3's interface: one generic-password item with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly`, not synchronizable (C-0004 section 4), through `SecKeyChain` and `SecRecord` (`avalonia_ui_layer.md` section 2.3). It holds the refresh token in M0.3's secret value type and the account name, which signing in with a refresh token also needs (section 2.3); M7.1 proposes the same layout. The logic is in Paneport.Core over a small secure-item interface, so a fake tests it on Windows, and Paneport.iOS implements the interface on the keychain. If M0.3's interface lacks the account name, this stage extends it.
- Diagnostics wired to the host. It shows the implementation, the ABI version and its compatibility, and the import; the memory budget every second with a timeline (the first and current values, the highest limit, and when the Game Mode level was first reached), the peak footprint and the thermal state over time (C-0011 section 1); the entitlements with their source; the previous run's crash record; the storage folders with their backup marks; the build stamp; and the device model identifier with the iOS version and build.
  - A warning appears when the limit is far below 6,144 MB and names a dropped `increased-memory-limit` as the likely cause (`ios_platform_constraints.md` section 4). `ios-app-head.md` sets the threshold.
  - How the app reads its entitlements, from its code signature, its embedded profile or both, is to be confirmed in this stage. Other profile data is discarded and never logged.
  - The page never shows a device name, team identifier, application-identifier prefix or bundle identifier.
- The crash record reader (C-0005 section 2): a parser in Paneport.Core for the layout of M1.2, run at launch. The record shows until the user dismisses it, and is then deleted. A malformed record is reported and never stops the app.
- Packaging in the `ios` job. Both flavors are built in Release with full AOT and without signing. `eng/mac/sign-adhoc.sh` signs each embedded framework with `codesign --force --sign -`, then the app with `codesign --force --sign - --entitlements Entitlements.plist`. `eng/mac/package-ipa.sh` makes the `.ipa`, and `eng/mac/check-app.sh` checks the entitlements, the Info.plist values, the framework, and that every signature is ad hoc with no team identifier. `tools/ScanPackage.cs` scans each package, and both are uploaded, named by flavor and short commit hash. No Apple identity is used (C-0009 section 5). C-0011 section 4 says the job runs "without signing"; ad-hoc signing holds no identity, and plan section 5.2 asks for it. If recelilious reads the contract otherwise, the step moves through a C-0011 change.
- `tools/ScanPackage.cs` fails on a provisioning profile, or on a property list or entitlement that names a team identifier or an application-identifier prefix, and it lists the entitlements it found. M9.1 extends it and keeps its name.
- The device test flavor in `tests/device/`: its own `net10.0-ios` app, the full app plus developer screens (plan section 7.3), with the main app's bundle identifier (D12), its own display name and the `ios-device-test` host. It compiles the head's sources from `src/Paneport.iOS/` instead of referencing that project, on which nothing may depend (C-0004 section 2); the mechanism is to be confirmed in this stage. A Developer entry opens the developer menu, which later stages extend. Its first pages:
  - Interface conformance runs the comparison of M1.2 on the device (C-0005 section 5).
  - Token store writes a test item under a test key that is never the token's, reads it back with its attributes, and deletes it.
  - Crash record writes a marked test record through the test-build export.
- A `README.md` for `tests/device/` and each other new folder that C-0004 section 3 shows.

Out: drawing in the GameView, transparency and pass-through (M2.1); the dispatcher and the crash-chaining call (M2.2); Steam (M2.3); GameController input (M6.1); audio (M6.2); background tasks (M7.1); copying `compat/` into the bundle (M6.5); the JIT screen and its URL opener (L3); the licenses screen (M9.1); exporting logs, which needs the scrubbing of C-0004 section 4; and signing with an Apple identity in CI, which never happens (C-0009 section 5).

**Read first.**

- C-0004 sections 1, 2 and 4; C-0005 sections 1 and 2; C-0007 sections 2 and 3; C-0009 sections 3 to 5; C-0010 sections 1 and 2; C-0011 sections 1, 2 and 4; C-0012 sections 1 and 4; C-0001 section 6; C-0002 section 6; C-0003 section 5.
- `avalonia_ui_layer.md` sections 2.1, 2.2, 2.3, 3.1 and 3.2 (life cycle and hosting, signing and entitlements, frameworks, the keychain, the view tree, the pipeline); `ios_platform_constraints.md` section 4; `process_and_memory_model.md`.
- `docs/design/app-shell.md`, `docs/design/native-host.md`, `docs/design/interop.md` and `docs/design/dev-environment.md`.

**Deliverables.**

- `src/Paneport.iOS/`, `src/Paneport.Core/` and `src/Paneport.UI/`: the modules above.
- `tests/Paneport.Core.Tests/`, `tests/Paneport.UI.Tests/` and `tests/device/`.
- `eng/mac/sign-adhoc.sh`, `eng/mac/package-ipa.sh`, `eng/mac/check-app.sh` and `tools/ScanPackage.cs`.
- `.github/workflows/ci.yml`, the app steps of the `ios` job; `Paneport.slnx`.
- `docs/design/ios-app-head.md` with its row in `docs/design/README.md`, and the two document updates.
- A C-0011 clarification on a `contract/<summary>` branch (plan section 8): C-0011 section 4 says the `ios` job builds without signing, and the ad-hoc signature, although it carries no Apple identity, is still a signature. The signing step merges only after recelilious accepts the change (C-0012 section 1).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S1.3.1 | Scene and root view controller | `src/Paneport.iOS/` | The `ios` job builds it; review against the view tree in `ios-app-head.md`. L1 step 2 is the device check. |
| S1.3.2 | Native host embedding and host adapter | `src/Paneport.iOS/`, `src/Paneport.Core/` | xUnit with a fake host: a major mismatch stops startup with its message, and events reach the view models through the UI dispatcher. `check-app.sh` finds the framework in `Frameworks/`, linked by the executable. |
| S1.3.3 | Info.plist and entitlements | `src/Paneport.iOS/` | `check-app.sh`: every C-0009 section 4 key has its value; both flavors carry one bundle identifier and different display names; `codesign -d --entitlements` lists exactly `increased-memory-limit`, set to true. |
| S1.3.4 | Storage layout bootstrap | `src/Paneport.Core/`, `src/Paneport.iOS/` | xUnit: the layout table lists every path of C-0004 section 4 with its backup mark, and the bootstrap over a fake file system creates each folder and marks exactly the folders marked "No". |
| S1.3.5 | Settings store on disk | `src/Paneport.Core/`, `src/Paneport.iOS/` | xUnit over a fake file system: settings round-trip, an interrupted write leaves the previous file, and an unreadable file falls back to the defaults with a log line. |
| S1.3.6 | Keychain token store | `src/Paneport.Core/`, `src/Paneport.iOS/` | xUnit with a fake secure-item store: write, read, overwrite, delete and a missing item behave as the interface says; a corrupt item is reported and removed; no token or account name reaches a captured log or a `ToString` result. L1 step 14 checks the attributes. |
| S1.3.7 | Diagnostics wiring | `src/Paneport.Core/`, `src/Paneport.UI/`, `src/Paneport.iOS/` | xUnit with a fake host and clock: the timeline, the peak, the Game Mode transition and the warning threshold. A headless UI test shows every section, with no forbidden item in the text for sample inputs. |
| S1.3.8 | Crash record reader | `src/Paneport.Core/`, `src/Paneport.iOS/` | xUnit on synthetic records built from the documented layout: a valid record, a test record, a truncated one, a wrong version and an empty file each give the expected result. |
| S1.3.9 | Ad-hoc signing, packaging and scan | `eng/mac/`, `tools/ScanPackage.cs` | The `ios` job signs, packages, checks and scans both flavors. The tool's self-test on generated packages: a clean one passes, and a planted provisioning profile, team identifier or application-identifier prefix each fails, naming the file. |
| S1.3.10 | Device test flavor skeleton | `tests/device/` | The `ios` job builds it and `check-app.sh` passes on it. Its conformance page runs the class that `tests/Paneport.Interop.Tests` runs on macOS. |

**Verification.** All of it runs in CI, without recelilious's iPhone.

| Check | Passes when |
|---|---|
| `ios` job | Both flavors build in Release with full AOT and the interpreter off; `check-app.sh` and `tools/ScanPackage.cs` pass on both packages; `codesign --verify` passes on each app and framework; both `.ipa` artifacts are uploaded. |
| `dotnet` job | Paneport.Core.Tests and Paneport.UI.Tests pass with the tests of S1.3.2 and S1.3.4 to S1.3.8, and the trimmed publish shows no new warning. |
| `native` and `checks` jobs | Green. The personal-data check passes over `tests/`, the license checker accepts Avalonia's iOS packages, and the `ScanPackage` self-test passes with the other tools' self-tests. |

The device behavior, such as the readings, the import, the entitlements after re-signing and the keychain attributes, is L1's acceptance. The pull request marks it "not yet verified on device" (C-0012 section 1 item 9).

Scripts this stage creates: `eng/mac/sign-adhoc.sh`, `eng/mac/package-ipa.sh`, `eng/mac/check-app.sh` and `tools/ScanPackage.cs`.

**Done when.** The verification passes, `ios-app-head.md` is merged with the stage, recelilious has merged the branch, the status is Done and the session log is updated.

**Risks and fallbacks.**

- Hosting AvaloniaView in our own view controller is supported but little tested (`avalonia_ui_layer.md` section 2.1). If Avalonia's platform cannot start without its own scene delegate, the session stops and asks recelilious before working around it.
- An ad-hoc-signed `.ipa` re-signed by AltStore is untested (experiment E13), and iOS honors `increased-memory-limit` only when the profile grants it (`ios_platform_constraints.md` section 4). L1 shows whether the route keeps it. The fallback is a build on recelilious's Mac signed with a development profile (C-0009 section 5), never in CI.
- Reading entitlements on the device may need a parser of our own for the signature format. If neither source is readable, the page shows "unknown", the memory limit stands in for `increased-memory-limit`, and the record says so.
- Avalonia's iOS behavior under AOT and trimming is unverified (`avalonia_ui_layer.md` section 2.2). Problems go into the pull request; C-0010 section 2 allows suppressions only in Paneport.Steam.
- If review finds that building the test flavor from the head's sources breaks C-0004 section 2, the alternative goes through a C-0004 change first.
- recelilious may read C-0011 section 4 as already allowing the ad-hoc signature, because it uses no Apple identity. Then the clarification is dropped and the session log records the decision. If recelilious rejects ad-hoc signing in CI, the `.ipa` comes from a local Mac (D1), and L1 waits for one.
- Two full-AOT builds lengthen the `ios` job; D7 settles when it runs.

**Hands over to.**

- L1: both `.ipa` artifacts, Diagnostics and the developer menu.
- M2.1: the root view controller, the GameView, the AvaloniaView and the developer menu.
- M2.2: the startup sequence that the crash-chaining call joins, the crash record reader, and the test flavor with the `ios-device-test` host.
- M2.3: the keychain token store, the settings store and the developer menu.
- M3.1 and L3: Paneport.iOS for the URL opener, and the developer menu for the JIT self-test.
- M9.1: the `ios` job steps, the signing scripts in `eng/mac/`, the packaging location and `tools/ScanPackage.cs`.
- M10.2: the Mono build settings and the import name, which move to CoreCLR.

**Session log.**

_No sessions yet._
