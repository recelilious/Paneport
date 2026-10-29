# L0 Foundation

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L0 | Foundation | Large | Planned | | |
| S0.0.1 | Evidence folder and the L0 record template | Small | Planned | | |
| S0.0.2 | Root README and contributing guide | Small | Planned | | |
| M0.1 | Solution scaffold and repository hygiene | Medium | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.1 | Build properties and analyzers | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.2 | Central package management and lock files | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.3 | REUSE, LICENSES and SPDX headers | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.4 | Folder READMEs | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.5 | Test projects and dependency direction tests | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| S0.1.6 | First CI workflow | Small | In progress | `build/m0-1-solution-scaffold` | 2026-10-29 |
| M0.2 | Repository checks and CI | Medium | Planned | | |
| S0.2.1 | Personal-data and forbidden-file scanner | Small | Planned | | |
| S0.2.2 | Dependency license checker | Small | Planned | | |
| S0.2.3 | Toolchain pin checker | Small | Planned | | |
| S0.2.4 | Complete CI jobs | Small | Planned | | |
| S0.2.5 | Trimmed test app | Small | Planned | | |
| S0.2.6 | Repository checks design document | Small | Planned | | |
| M0.3 | Shell skeleton with a fake host | Medium | Planned | | |
| S0.3.1 | Domain model | Small | Planned | | |
| S0.3.2 | Service interfaces | Small | Planned | | |
| S0.3.3 | Navigation and view-model base | Small | Planned | | |
| S0.3.4 | Library and Title details pages | Small | Planned | | |
| S0.3.5 | Settings and Diagnostics pages | Small | Planned | | |
| S0.3.6 | Fake host and fake services | Small | Planned | | |
| S0.3.7 | App shell design document | Small | Planned | | |

## L0 Foundation

| Field | Value |
|---|---|
| Level | Large |
| Depends on | None |
| C-0012 gate | None |
| Size | S, for the integration session. The Medium stages have their own sizes. |
| Branch | `feat/l0-foundation` (integration session) |
| Decisions needed | None. Plan section 4.2 lists no decision for L0. |

**Goal.** Give every later stage a checked base. L0 delivers a .NET 10 solution that follows the accepted contracts, CI that enforces the repository checks of C-0011, and an Avalonia desktop head that shows the Paneport shell with sample data and a fake native host.

**What recelilious will see.**

- On Windows, `dotnet run --project src/Paneport.Desktop` opens a window titled Paneport. It has a Library page with fictional sample titles, a Title details page, a Settings page and a Diagnostics page. Diagnostics says that the host is a fake and shows a host ABI version and a memory limit.
- On GitHub, the L0 pull request shows the `dotnet` job passing on Windows and on Ubuntu, and the `checks` job passing.
- Optionally, a scratch pull request with a planted e-mail address makes `checks` fail.
- Nothing runs on the iPhone yet. Paneport.iOS needs the iOS workload and a macOS build host, so M1.1 creates it.

**Acceptance steps.** recelilious runs steps 1 to 10 on the Windows 11 workstation (C-0009 section 1), in a terminal at the root of a clone with the L0 integration branch checked out. The workstation needs the SDK version that `global.json` names. Steps 11 and 12 run on GitHub.

1. Run `dotnet --version`. Expected: it prints exactly the SDK version written in `global.json`. If that SDK is missing, `dotnet` reports it instead; install it and repeat.
2. Run `dotnet build eng/ci.slnf -c Release`. Expected: the build succeeds with 0 warnings and 0 errors.
3. Run `dotnet test eng/ci.slnf -c Release`, or the test command that the quick start in `README.md` gives if M0.1 chose another form. Expected: every test passes, and the summary shows no failed test.
4. Open any three `.cs` files under `src/` and `tests/`. Expected: each starts with the two SPDX lines of C-0010 section 6.
5. Run `dotnet run --project src/Paneport.Desktop`. Expected: within a few seconds a window titled Paneport opens on the Library page, with navigation to Library, Settings and Diagnostics.
6. Look at the Library page. Expected: it lists the sample titles from the table in `docs/design/app-shell.md`, each with its install state and launch path. The sample with the very long name stays readable.
7. Select a sample title. Expected: Title details shows the same name, identity, install state and launch path. Going back returns to the Library page.
8. Open Settings, change one setting, open another page and come back. Expected: the changed value is still shown.
9. Open Diagnostics. Expected: it says that the host is a fake. It shows the host ABI version and the memory budget values that `docs/design/app-shell.md` lists for the fake host.
10. Close the window. Expected: the app closes without an error dialog, and the terminal shows no exception. Nothing in the window suggested an affiliation with Microsoft, Valve or Apple (C-0001 section 7 item 4).
11. Open the L0 pull request on GitHub. Expected: on its last commit, the `dotnet` job passed on Windows and on Ubuntu, and the `checks` job passed.
12. Optional. In GitHub's web editor, add one line with an e-mail address at the `example.com` domain to `tests/README.md`, and propose the change as a new pull request. Expected: `checks` fails, and its personal-data step names the file and the line. `dotnet` passes. Close that pull request without merging and delete its branch.

**Evidence record.**

- File: `docs/evidence/YYYY-MM-DD-l0-foundation.md`, with the UTC date of the acceptance run (C-0012 section 4). The integration session commits it as a template (C-0012 section 2 item 3). recelilious fills in the results, or reports them so that Claude fills them in. If the run happens on another date, the file is renamed to that date.
- Fields, as C-0012 section 4 requires them, for a stage that runs only on Windows:
  - Gate or feature: "L0 Foundation", with no C-0012 gate. Build: the commit hash that was tested. Version: none yet, because versions start at 0.1.0 (C-0009 section 6).
  - Device model identifier, iOS version and build: not applicable. Record the Windows version and build, and the SDK version that step 1 printed, instead.
  - Signing route, sideloading tool and its version, entitlement values: not applicable.
  - JIT enabler and its version, Game Mode, memory limit: not applicable. Record that the Diagnostics values came from the fake host.
  - Steps: one row per acceptance step, with the expected and the observed result. Metrics: none. Logs: only the summary lines of the build and test output, scrubbed, and links to the CI runs of steps 11 and 12.
  - Verdict (Pass or Fail), the tester as a project handle (`recelilious`), and the date.
- Never in the record or its attachments (C-0012 section 4): UDIDs, ECIDs, serial numbers, device names, Apple IDs, team identifiers, application-identifier prefixes, re-signed bundle identifiers, Steam account names, Steam IDs, IP addresses, access or refresh tokens, depot keys, manifest request codes, real names and local paths. On Windows the likely leaks are local paths and the user or machine name in `dotnet` output and in screenshots. A screenshot, if there is one, shows only the Paneport window. The personal-data scanner checks `docs/evidence/` in CI (C-0011 section 2).

**Integration session.** Read first: C-0004 section 3, C-0011 sections 2 to 4, C-0012 sections 1 to 4, and the briefs and session logs of M0.1, M0.2 and M0.3 below. The session starts when M0.1, M0.2 and M0.3 are Done.

1. Follow the session checklist (plan section 3.3) and create `feat/l0-foundation` from the updated `main`.
2. Run everything together on the workstation: the toolchain check, the locked restore, build, tests and format check of `eng/ci.slnf`, the trimmed publish of `tests/Paneport.TrimTest`, and the three tools with their self-tests. M0.2 and M0.3 ran in parallel, so this is the first time the trimmed test app and the license checker see M0.3's view models and pages. Fix what this finds on the integration branch. Anything larger becomes a proposed follow-up in the session log.
3. Do S0.0.1 and S0.0.2.
4. Check the definition of done (C-0012 section 1) for L0 as a whole:
   - every folder of the C-0004 section 3 tree that now exists has a `README.md`;
   - every component created in L0 has its design document, or, for the placeholders Paneport.Steam and Paneport.Interop that hold no code yet, a recorded stage that writes it with their first code (plan section 7.7);
   - every package, tool and action is covered by an accepted row of the C-0003 section 6 register.
5. Run the personal-data scan, gitleaks (once its pinned release is installed) and the format check locally (C-0011 section 2). Push and open the pull request. Its description holds the acceptance steps above. It states that the `native` and `ios` jobs do not exist yet and come with M1.2 and M1.1, as C-0011 section 3 asks for checks that need macOS.
6. Suggest to recelilious that `main` requires the `dotnet` and `checks` jobs before merging. Repository settings are recelilious's to change; Claude does not change them.
7. Leave L0 In progress with the pull request link, append to the session log, and give recelilious the Chinese summary with the manual checks (plan section 3.3). L0 becomes Accepted once recelilious accepts it and the filled-in evidence record is merged (plan section 2.4).

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S0.0.1 | Evidence folder and the L0 record template | `docs/evidence/README.md`, `docs/evidence/YYYY-MM-DD-l0-foundation.md` | The personal-data scan and lychee pass on `docs/evidence/`. The template has an entry for every field of C-0012 section 4. The README states the naming rule of C-0012 section 4 and holds the gate table of C-0012 section 3, one row per gate, each with "No record yet". S2.0.1 records the first verdict. Plan section 7.7 places the gate table with S2.0.1; an empty table here keeps the README in line with C-0012 section 3 from the start. |
| S0.0.2 | Root README and contributing guide | `README.md`, `CONTRIBUTING.md` | lychee passes. The README's status line and its list of contracts match the repository. Its quick start (prerequisites, build, test, run the desktop head) works when followed on a fresh clone. `CONTRIBUTING.md` says how to run the local checks before a push (C-0011 section 2). |

**Session log.**

_No sessions yet._

## M0.1 Solution scaffold and repository hygiene

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L0 |
| Depends on | None |
| Size | M |
| Branch | `build/m0-1-solution-scaffold` |
| Decisions needed | None from plan section 4.2. recelilious approves any C-0003 register change that this stage needs before the stage merges. |
| Design documents | Writes `docs/design/solution-and-build.md` and adds it to `docs/design/README.md`. |

**Goal.** Create the .NET solution and the repository rules that every later stage builds on: a pinned SDK, strict build properties, central package versions with lock files, license headers, folder READMEs, and a first CI workflow that builds, tests and checks formatting and REUSE.

**Scope.**

In:

- `Paneport.slnx` with the projects Paneport.Core, Paneport.Steam (empty), Paneport.UI, Paneport.Interop (empty) and Paneport.Desktop, and the test projects Paneport.Core.Tests, Paneport.Steam.Tests and Paneport.UI.Tests. Paneport.UI holds the Avalonia application class. Paneport.Desktop opens an empty window titled Paneport.
- Project references only in the directions C-0004 section 2 allows. Paneport.Interop references nothing in the solution.
- The Avalonia projects are written by hand from Avalonia's documentation. Avalonia's template package is not installed, so it needs no register row.
- `global.json`, pinning the exact .NET 10 SDK and not rolling forward to another one (C-0009 section 2).
- `Directory.Build.props` with the rules of C-0010 sections 1 and 2: C# 14, nullable reference types, warnings as errors, analysis level `latest-recommended`, and `IsAotCompatible` on the four libraries. The libraries also generate their XML documentation file, so a public API without a comment fails the build (C-0010 section 5). CI builds set `ContinuousIntegrationBuild` with `Deterministic` (C-0009 section 6).
- `Directory.Packages.props` with central package management, and a committed `packages.lock.json` for every project (C-0009 section 2). Packages: Avalonia 12 for Paneport.UI and Paneport.Desktop (C-0010 section 1), and xunit.v3 and Avalonia.Headless.XUnit for the tests (C-0011 sections 1 and 5).
- `eng/ci.slnf`, listing every project of `Paneport.slnx` except Paneport.iOS, `tests/device` and `tests/Paneport.Interop.Tests` (C-0011 section 4). None of the three exists yet.
- C# formatting, naming and code-style rules in `.editorconfig`, in `[*.cs]` sections (C-0010 section 2). The forks hold no C#, and the format checks leave out `external/` (C-0010 section 4).
- `REUSE.toml`, `LICENSES/MIT.txt`, and the two SPDX lines in every original file that has a comment syntax, including the root files that exist today (C-0010 section 6, C-0003 section 4). Markdown files outside `docs/` carry the lines in an HTML comment. JSON files, images and everything under `docs/` are covered by `REUSE.toml`.
- A `README.md` in every folder of the C-0004 section 3 tree that exists after this stage, including `docs/`, which has none today.
- A first `.github/workflows/ci.yml`. Its `dotnet` job runs on Windows and Ubuntu: locked restore, build, tests and format check of `eng/ci.slnf`. Its `checks` job runs on Ubuntu and so far runs only `reuse lint`, with the REUSE tool pinned in `eng/toolchain.json` (C-0011 section 4, C-0009 section 2).
- Registering every new package and every GitHub Action under C-0003 before adding it (C-0003 section 5), through a contract change where the register has no row.

Out:

- Paneport.iOS, `tests/device`, `tests/Paneport.Interop.Tests` and the iOS workload pin (M1.1, M1.2).
- Domain code, view models and pages (M0.3). CommunityToolkit.Mvvm arrives with M0.3.
- The tools in `tools/`, the other steps of `checks`, the toolchain check and the trimmed test app (M0.2).
- The `native`, `ios` and `release` jobs (M1.2, M1.1, M9.1), `.clang-format` and `.clang-tidy` (M1.2), `.gitmodules` and `external/` (M4.1), and `compat/` (M6.4).

**Read first.**

- C-0002 sections 5, 6 and 7; C-0003 sections 4, 5 and 6; C-0004 sections 2 and 3; C-0009 sections 2 and 6; C-0010 sections 1, 2, 4, 5, 6 and 7; C-0011 sections 1 to 5; C-0012 sections 1 and 2.
- Plan sections 3.4, 4.1 and 7.
- `avalonia_ui_layer.md`:
  - section 2.1, versions: 12.1.4 was the stable release on 2026-10-09, and Avalonia.iOS 12.1.4 depends on Avalonia.HarfBuzz and Avalonia.Skia;
  - section 2.2 part 2, what `IsAotCompatible` turns on;
  - section 3.2, VS 2026 or Rider 2026.1 or later (VS 2022 17.14 cannot target `net10.0`), and the SDK pinned in `global.json`;
  - section 3.4, compiled XAML and no runtime XAML loading.

**Deliverables.**

- Glue work: `Paneport.slnx`, the project files, `eng/ci.slnf`.
- `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `REUSE.toml`, `LICENSES/MIT.txt`.
- `.editorconfig` with the C# rules, and `.gitignore` entries for any new build output, such as test results.
- `eng/toolchain.json` (the REUSE tool only) and `eng/README.md`.
- `src/README.md`, and the five project folders under `src/`, each with its project file and `README.md`.
- `tests/README.md`, and the three test project folders, each with its project file, tests and `README.md`.
- A `packages.lock.json` next to every project file.
- `.github/workflows/ci.yml` and `.github/workflows/README.md`.
- `docs/README.md`.
- `docs/design/solution-and-build.md`. It is the first design record of each project: its role (C-0004 section 2), its dependency rule and its planned content, with the stage that writes its full design (M0.3 for Paneport.Core, Paneport.UI and Paneport.Desktop; M1.2 for Paneport.Interop; M2.3 for Paneport.Steam). It also covers the build properties; how to add a project (to `Paneport.slnx`, and to `eng/ci.slnf` unless C-0011 section 4 leaves it out); how to add or update a package (register it first, then `Directory.Packages.props`, then restore to update the lock files); the REUSE rules; the exact test command; and the public sources consulted (C-0001 section 6).
- If needed, a C-0003 change on `contract/register-l0-dependencies`, committed locally and pushed only after recelilious has reviewed it (C-0012 section 2).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S0.1.1 | Build properties and analyzers | `global.json`, `Directory.Build.props`, `.editorconfig` | `dotnet --version` at the root prints the pinned SDK. A scratch file with a nullable warning makes the build fail; without it the build passes. |
| S0.1.2 | Central package management and lock files | `Directory.Packages.props`, the `packages.lock.json` files | Locked restore of `eng/ci.slnf` passes. A version written on a package reference in a project file makes restore fail. Every package ID in the lock files is matched to a C-0003 section 6 row in the pull request. |
| S0.1.3 | REUSE, LICENSES and SPDX headers | `REUSE.toml`, `LICENSES/MIT.txt`, the header lines in every original file | `reuse lint` passes on the whole repository in the `checks` job. |
| S0.1.4 | Folder READMEs | A `README.md` in each folder of the C-0004 section 3 tree that exists | Every such folder has a `README.md` that says what it holds, checked against a folder listing in the pull request. |
| S0.1.5 | Test projects and dependency direction tests | `tests/Paneport.Core.Tests`, `tests/Paneport.Steam.Tests`, `tests/Paneport.UI.Tests` | Each test project has a test that fails when its project references a Paneport project that C-0004 section 2 does not allow. Paneport.UI.Tests also has one headless test that starts the Paneport.UI application on the headless platform and shows a window. |
| S0.1.6 | First CI workflow | `.github/workflows/ci.yml`, `.github/workflows/README.md`, `eng/toolchain.json` | On the pull request's last commit, the `dotnet` job passes on Windows and on Ubuntu, and `reuse lint` passes in `checks`. |

**Verification.** On the Windows workstation, from the repository root:

| Command | Passes when |
|---|---|
| `dotnet --version` | It prints exactly the version in `global.json`. |
| `dotnet restore eng/ci.slnf --locked-mode` | It exits with 0 and changes no lock file. |
| `dotnet build eng/ci.slnf -c Release --no-restore` | It reports 0 warnings and 0 errors. |
| `dotnet test eng/ci.slnf -c Release --no-build` | Every test passes, each test project runs at least one test, and no test is skipped. |
| `dotnet format eng/ci.slnf --verify-no-changes` | It exits with 0. |

- The exact form of the test command depends on how xunit.v3 runs under `dotnet test`, through VSTest or through Microsoft.Testing.Platform (to be confirmed in this stage). The pass criterion does not change. The command goes into `solution-and-build.md`.
- Planted failures, checked once and never merged: a nullable warning fails the build; a misformatted line fails the format check; a version on a package reference fails restore. These run locally. A `.cs` file without the SPDX lines fails `reuse lint`; this one runs on a draft pull request from a scratch branch, because REUSE runs only in CI or on a Mac.
- In CI, on the pull request's last commit: the `dotnet` job passes on both runners, and `reuse lint` passes.
- The pull request lists every package ID from the lock files and every action in `ci.yml`, each with the C-0003 section 6 row that covers it.
- M0.2's tools do not exist yet. The session reviews its diff by hand for what the personal-data check of C-0011 section 2 looks for, runs the format check before each push, and says both in the pull request.
- This stage creates the first version of `.github/workflows/ci.yml`. It creates no tool or script.

**Done when.**

- Every deliverable exists and the verification above passes.
- Every package and action that the stage adds is covered by an accepted row of the C-0003 section 6 register.
- `docs/design/solution-and-build.md` is merged with the stage.
- recelilious has merged the branch, the status is Done, and the session log is updated.

**Risks and fallbacks.**

- **No SDK on the workstation yet.** A check on 2026-10-28 found .NET runtimes but no .NET SDK on the Windows workstation, and no Python installation. Before writing code, the session asks recelilious to install the .NET 10 SDK version it proposes to pin, or for approval to install it (plan section 4.1).
- **Register gaps.** The register names components, not package IDs. The lock files will hold transitive packages, such as Avalonia's platform and text-shaping packages and the test platform's packages; which ones the desktop and test packages bring is to be confirmed in this stage. The workflow uses GitHub's checkout and .NET setup actions. They are CI tools under C-0003 section 5 and have no row yet; their licenses are to be confirmed in this stage. Fallback: one C-0003 change that adds the missing rows, on a `contract/` branch, reviewed by recelilious before this stage merges. Plan section 8 does not list this change. A dependency never merges without its row.
- **Solution filter over `.slnx`.** That `dotnet build`, `dotnet test` and `dotnet format` accept `eng/ci.slnf` when its solution is `Paneport.slnx` is to be confirmed in this stage. Fallback for the format check: run it on each project that the filter lists. If build or test cannot use the filter, stop and ask recelilious, because C-0004 section 3 and C-0011 section 4 fix both names.
- **Headless tests on xunit.v3.** That Avalonia.Headless.XUnit at the pinned Avalonia version runs on xunit.v3 is to be confirmed in this stage. Fallback: drive Avalonia.Headless directly from xunit.v3 tests, which still matches C-0011 section 1, and tell recelilious. No other test or assertion library is added without a register row. FluentAssertions 8 and later are not allowed (C-0011 section 5), and mocking libraries are not used (C-0011 section 1).
- **Where library properties are set.** C-0004 section 3 has no root `Directory.Build.targets`, and adding one needs a contract change. Set `IsAotCompatible` and the documentation file either by project name in `Directory.Build.props` or in each library's project file.
- **SDK band.** The SDK pinned now must also suit M1.1, which needs an iOS workload set and its Xcode version for the same SDK (`avalonia_ui_layer.md` section 3.2). M1.1 may move the pin in a reviewed change (C-0009 section 2).
- **Vulnerability warnings.** With warnings as errors, a new NuGet advisory for a package may fail a build that did not change (to be confirmed in this stage how NuGet's audit is set up). Treat it as a real finding and update the package in a reviewed change.
- **Header comments in rewritten files.** Tools that rewrite solution or project files may drop the header comment (to be confirmed in this stage). REUSE in CI catches it; add the lines back.
- **REUSE locally.** The REUSE tool is a Python program. It runs in CI, or on a Mac if one is available (D1).
- **A README in `.github/`.** GitHub may show a README in `.github/` in place of the root README (to be confirmed in this stage). The README therefore lives in `.github/workflows/`.
- **Analyzer rules in tests.** Some recommended rules fight test naming. Relax a rule only for `tests/`, in `.editorconfig`, naming the rule and the reason.

**Hands over to.**

- M0.2: the solution, `eng/ci.slnf`, the first `ci.yml`, `eng/toolchain.json` and the register rows.
- M0.3: the projects, the Avalonia and test packages, the headless test setup and the build properties.
- M1.1: adds Paneport.iOS to `Paneport.slnx` but not to `eng/ci.slnf`, and the iOS workload set to `global.json` (C-0009 section 2).
- M1.2: Paneport.Interop gets `AllowUnsafeBlocks` and `DisableRuntimeMarshalling` with its first bindings (C-0010 section 2, C-0005 section 1), and its design document. `tests/Paneport.Interop.Tests` stays out of `eng/ci.slnf`.
- M2.3: Paneport.Steam and its tests get their first code and their design document.
- M4.1: the root `.editorconfig` has a `[*]` section that also reaches files in `external/`, unless each fork has its own root `.editorconfig` (C-0010 section 4).

**Session log.**

_No sessions yet._

## M0.2 Repository checks and CI

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L0 |
| Depends on | M0.1 |
| Size | M |
| Branch | `ci/m0-2-repository-checks` |
| Decisions needed | None from plan section 4.2. recelilious agrees a link-check policy and a trimming-warning policy if the first runs need one (see Risks and fallbacks). |
| Design documents | Writes `docs/design/repository-checks.md` and adds it to `docs/design/README.md`. Reads `docs/design/solution-and-build.md`. |

**Goal.** Turn the rest of the repository rules into automated checks. C# file-based tools find personal data, forbidden files, unregistered dependencies and toolchain drift, and the complete `dotnet` and `checks` jobs fail when a rule is broken.

**Scope.**

In:

- `tools/ScanPersonalData.cs` (S0.2.1). It scans `docs/evidence/`, `compat/` and `tests/` for UDIDs, ECIDs, serial numbers, team identifiers and application-identifier prefixes, e-mail addresses, Steam IDs, IP addresses, tokens, depot keys, manifest request codes and local paths. It fails on any tracked file with an extension that C-0002 section 6 forbids. Device model identifiers are allowed (C-0011 section 2). Folders that do not exist yet are skipped.
- `tools/CheckLicenses.cs` (S0.2.2). It reads every `packages.lock.json` (direct and transitive packages), the package directives of the file-based tools, `.gitmodules` and `eng/toolchain.json` (C-0011 section 2). In addition, it reads the `uses:` lines of the workflows, because actions are CI tools under C-0003 section 5. It parses the register table of C-0003 section 6 from the Markdown file and fails on anything the register does not cover. A row in the Forbidden or Under review category covers nothing. A Tool row covers tools and actions, never a package in a lock file.
- `tools/CheckToolchain.cs` (S0.2.3). It compares the installed SDK with `global.json`, and the installed CI tools with `eng/toolchain.json` (C-0009 section 2).
- `eng/toolchain.json` gains the gitleaks and lychee pins, each with the SHA-256 of its release download.
- A self-test mode in each tool, over synthetic fixtures only: addresses at the `example.com` domain, IP addresses from the RFC 5737 documentation ranges, and made-up identifiers (C-0011 section 1). Never real personal data.
- The complete `dotnet` job (S0.2.4): toolchain check, locked restore, build, tests and format check of `eng/ci.slnf`, then the trimmed publish of `tests/Paneport.TrimTest` (C-0011 section 4, C-0010 section 2).
- The complete `checks` job (S0.2.4): install the pinned CI tools and check them against `eng/toolchain.json`; run the tools' self-tests; then REUSE, gitleaks from the pinned release (not the gitleaks GitHub Action, C-0011 section 2), the personal-data and forbidden-file check, lychee and the dependency licenses (C-0011 section 4).
- `tests/Paneport.TrimTest/` (S0.2.5), a minimal program that references Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop and roots them with `TrimmerRootAssembly`. It is published trimmed for the runner's runtime identifier, with `TrimmerSingleWarn` set to false so that every warning shows. It is added to `Paneport.slnx` and `eng/ci.slnf`.
- `tools/README.md` and `docs/design/repository-checks.md` (S0.2.6).

Out:

- The compatibility data step of `checks` (M8.3) and the `.ipa` scan of C-0009 section 6 (M9.1).
- The `native`, `ios` and `release` jobs (M1.2, M1.1, M9.1).
- Repository settings, such as required checks on `main`. They are recelilious's.
- Changing the lists of checks in C-0011. A gap found here goes through the contract process.

**Read first.**

- C-0001 section 7; C-0002 sections 5 and 6; C-0003 sections 5 and 6; C-0004 sections 2 and 3; C-0009 sections 2 and 6; C-0010 sections 1, 2, 6 and 7; C-0011 sections 1 to 5; C-0012 sections 1, 2 and 4.
- Plan sections 3.3 and 7.6.
- `docs/design/solution-and-build.md` (M0.1).
- `avalonia_ui_layer.md` section 2.2 part 2 and section 3.4: with `TrimmerSingleWarn` set to false the trimmer shows every warning instead of one per package, and `TrimmerRootAssembly` roots whole assemblies.

**Deliverables.**

- `tools/ScanPersonalData.cs`, `tools/CheckLicenses.cs`, `tools/CheckToolchain.cs` and `tools/README.md`.
- `eng/toolchain.json` with gitleaks and lychee added, and `eng/license-map.json`, the reviewed mapping from package IDs, tools and actions to register rows.
- The gitleaks and lychee configuration files under `eng/`. A configuration file at the root would be a new root file, which needs a C-0004 change (C-0004 section 3).
- `.github/workflows/ci.yml` with both jobs complete.
- `tests/Paneport.TrimTest/` with its project file, its program, its lock file and `README.md`.
- `docs/design/repository-checks.md`. It records every scanner rule, what it matches and its known false positives; the mapping policy of the license checker; the pins and how to change one; the jobs and their steps; how to run each check locally; the planted-failure procedure; and the public sources consulted (C-0001 section 6).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S0.2.1 | Personal-data and forbidden-file scanner | `tools/ScanPersonalData.cs` | Its self-test passes: every rule flags its positive fixtures and ignores its negative ones. A scan of the repository passes. |
| S0.2.2 | Dependency license checker | `tools/CheckLicenses.cs`, `eng/license-map.json` | Its self-test passes on a synthetic register table, synthetic lock files and synthetic directives. A check of the repository passes. |
| S0.2.3 | Toolchain pin checker | `tools/CheckToolchain.cs`, `eng/toolchain.json` | Its self-test passes on synthetic version outputs. A check of the workstation's SDK passes. |
| S0.2.4 | Complete CI jobs | `.github/workflows/ci.yml`, the configuration files under `eng/` | Both jobs pass on the pull request, and each planted violation fails its own step. |
| S0.2.5 | Trimmed test app | `tests/Paneport.TrimTest/` | Its trimmed publish succeeds, shows every warning, and has no warning outside a list that recelilious approved. |
| S0.2.6 | Repository checks design document | `docs/design/repository-checks.md` | It covers every rule and step listed above and its public sources, and lychee passes on it. |

**Verification.** On the Windows workstation, from the repository root:

| Command | Passes when |
|---|---|
| `dotnet run tools/ScanPersonalData.cs -- --self-test` | It exits with 0 after reporting every rule's fixtures as expected. |
| `dotnet run tools/CheckLicenses.cs -- --self-test` | It exits with 0 after reporting every fixture as expected. |
| `dotnet run tools/CheckToolchain.cs -- --self-test` | It exits with 0 after reporting every fixture as expected. |
| `dotnet run tools/ScanPersonalData.cs` | It exits with 0 on the repository. |
| `dotnet run tools/CheckLicenses.cs` | It exits with 0 on the repository. |
| `dotnet run tools/CheckToolchain.cs` | It exits with 0. On the workstation it checks the SDK; CI also asks it to check the CI tools. |
| The M0.1 commands | They pass as they do in M0.1. |

`dotnet run <file>.cs` is the form that C-0010 section 1 fixes. `--self-test` is an option of our own tools, designed in this stage.

In CI, on the pull request's last commit:

- `dotnet` passes on Windows and on Ubuntu, and its log shows the trimmed publish with every warning listed.
- `checks` passes, and its log shows each tool's self-test before that tool's scan.

Planted failures run on a draft pull request from a scratch branch, which is closed without merging. The planted values are synthetic, since the pull request stays visible on GitHub after it is closed.

| Planted change | Step that must fail |
|---|---|
| A line with an e-mail address at the `example.com` domain in `tests/README.md` | Personal-data check |
| An address from an RFC 5737 range in a test source file | Personal-data check |
| An entry for an unregistered tool in `eng/toolchain.json` | Dependency licenses |
| A `.cs` file without the SPDX lines | REUSE |
| A broken relative link in a README | lychee |
| A made-up value, clearly not a live credential, in a format that gitleaks' default rules detect | gitleaks |
| A misformatted line in a `.cs` file | Format check in `dotnet` |
| A reflection call in Paneport.Core that the trimming analyzers reject | Build in `dotnet` |

- Pass criteria: each planted change fails the step named, and that step's output names the file. Later steps keep running after a step fails, so one run shows every failure. The pull request links the run.
- The forbidden-file rule is checked by its self-test only, so no file with an extension that C-0002 section 6 forbids is committed, even to a scratch branch.
- This stage creates the three tools and `tests/Paneport.TrimTest`, and completes `ci.yml`.

**Done when.**

- The verification passes, and the pull request links the planted-failure run.
- Before each push the session ran the personal-data scan, gitleaks if it is installed, and the format check locally (C-0011 section 2).
- `docs/design/repository-checks.md` is merged with the stage.
- recelilious has merged the branch, the status is Done, and the session log is updated.

**Risks and fallbacks.**

- **False positives.** Some values the repository must hold look like values the scanner hunts for. Evidence records carry commit hashes (C-0012 section 4), compat files carry SHA-256 values (C-0008 section 2), and four-part version numbers look like IP addresses. The exact formats of UDIDs, ECIDs, serial numbers, team identifiers, Steam IDs, depot keys and manifest request codes are to be confirmed in this stage from public documentation. Fallback: match by label and context, not by shape alone, and give each known false positive a self-test fixture. An allow-list entry names one file and one rule and states its reason. There is no blanket exception.
- **Leaks into logs.** While the repository is public, its CI logs are public too. The tools report the file, the line and the rule, never the full matched value. gitleaks runs with its findings redacted; the option that does this is to be confirmed in this stage.
- **Fixtures inside scanned folders.** Literal fixture values under `tests/` would trip the scanner. Self-test fixtures stay inside the tools or are built in memory. The same holds for later stages: tests never keep such values as literal strings.
- **gitleaks findings in old history.** The first full scan may flag a past commit. Each finding is checked. A false positive goes into the gitleaks configuration under `eng/` with its reason. A real secret is reported to recelilious at once and is never quoted.
- **Link rot.** The research notes hold many external links. Rot or rate limits can fail lychee without any change to the repository. Fallback, agreed with recelilious before merging: retries and accepted status codes, or a narrower external check for `docs/research/`. Whether lychee should use the workflow's token for GitHub links is to be confirmed in this stage.
- **Trimming warnings from dependencies.** Whether Avalonia, and after M0.3 CommunityToolkit.Mvvm, publish without trimming warnings is to be confirmed in this stage. If they do not, the session lists the warnings in the pull request and asks recelilious whether `dotnet` fails on them or keeps an approved list under `eng/`. C-0010 section 2 allows suppressions in code only in Paneport.Steam, for SteamKit2 and protobuf-net.
- **Locked restore for a trimmed publish.** A trimmed publish needs runtime-specific packages. The test app declares the runtime identifiers it is published for, so that its lock file covers them (to be confirmed in this stage).
- **Parsing the register.** If the table in C-0003 section 6 changes shape, the checker fails with a clear message instead of guessing.
- **File-based tools and the root props files.** Whether `Directory.Build.props` and `Directory.Packages.props` apply to file-based apps is to be confirmed in this stage. Either way, the tools build without warnings, use only the base class library, and depend on nothing under `src/` (C-0004 section 2).
- **Local runs.** The local gitleaks run of C-0011 section 2 needs the pinned release on the workstation. The session asks recelilious before downloading it.
- **Where the trimmed test app lives.** It tests the four libraries together, so it references each of them. C-0004 section 3 needs a contract change only for a new top-level folder or root file, so `tests/Paneport.TrimTest/` needs none. It can join the layout tree in M4.2's change for `tests/runtime/` and `tests/win32/` (plan section 8).

**Hands over to.**

- L0: both complete jobs, to run together with M0.3's code in the integration session.
- M1.1: adds the `ios` job and the pins for the macOS runner image and Xcode. `CheckToolchain` gains the Xcode check against `xcodebuild -version` (C-0009 section 2).
- M1.2: adds the `native` job and the CMake and LLVM pins for clang-format and clang-tidy.
- M4.1: `.gitmodules` and the forks' build tools go through the license checker, after the register change of plan section 8.
- M4.2: adds the `runtime` job (plan section 8).
- M8.3: adds the compatibility data step to `checks`. The scanner already covers `compat/`.
- M9.1: the `release` job. The `.ipa` scan of C-0009 section 6 can reuse the scanner's rules.
- Every later stage: evidence records and compat files must pass the scanner, and every new package, tool or action must pass the license checker.

**Session log.**

_No sessions yet._

## M0.3 Shell skeleton with a fake host

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L0 |
| Depends on | M0.1 |
| Size | M |
| Branch | `feat/m0-3-shell-skeleton` |
| Decisions needed | None from plan section 4.2. |
| Design documents | Writes `docs/design/app-shell.md` (Paneport.Core, Paneport.UI and Paneport.Desktop) and adds it to `docs/design/README.md`. Reads `docs/design/solution-and-build.md`. |

**Goal.** Build the Paneport shell on the desktop head: the domain model and service interfaces in Paneport.Core, view models made with the CommunityToolkit.Mvvm generators, the Library, Title details, Settings and Diagnostics pages in Paneport.UI, and fakes in Paneport.Desktop. Unit tests and headless UI tests cover all of it.

**Scope.**

In:

- The domain model in Paneport.Core (S0.3.1):
  - a title, identified the way C-0008 section 1 names compat files: a Steam app ID, or a local slug of lowercase letters, digits and single hyphens. It has a display name;
  - the install state, for example not installed, downloading with progress, installed, and update pending (C-0007 section 4);
  - the launch path: Direct, Dock, or not yet classified (C-0007 section 5, C-0008 section 2);
  - the host ABI version, with the major and minor versions in the high and low 16 bits (C-0005 section 1);
  - the memory budget: memory limit, available memory, current footprint, and whether the limit reached the Game Mode level (C-0005 section 2).
- The interfaces in Paneport.Core (S0.3.2):
  - the runtime host, for now only its ABI version and memory budget;
  - the library service (C-0007 section 2);
  - a Steam service stub that reports only a signed-out state;
  - the settings store;
  - the token store, behind which the iOS keychain store comes later (C-0007 section 2, C-0004 section 4);
  - the logging interface (C-0010 section 2);
  - navigation.
- A secret value type whose text form is redacted, so that a token cannot reach a log line by accident (C-0010 section 2).
- View models in Paneport.Core, made with the CommunityToolkit.Mvvm generators (C-0010 section 2): shell, library, title details, settings and diagnostics. Commands are asynchronous where needed and never `async void`. View models reach the native host only through the runtime host interface (C-0004 section 1).
- Paneport.UI (S0.3.3 to S0.3.5): the navigation shell and the four pages, with compiled bindings, which Avalonia 12 uses by default (`avalonia_ui_layer.md` section 2.4). Data templates match views to view models; there is no lookup by type name, which the trimming analyzers would reject. The main view is a control that the desktop window hosts, because the iOS head shows a single main view (`avalonia_ui_layer.md` section 2.1).
- The Settings page holds at least the locale that sessions will hand to Wine, since the locale comes from the app settings (C-0005 section 2). The design document lists every setting.
- The Diagnostics page names the host implementation, so that a fake value is never mistaken for a device reading.
- Paneport.Desktop (S0.3.6): a hand-written composition root, the window titled Paneport, and the fakes. The fake host reports the values that C-0009 section 4 cites, a memory limit of 6,144 MB without Game Mode. The fake library holds fictional sample titles that cover every install state and launch path, including one local title and one with a very long name. The Steam service is signed out. The settings and token stores live in memory, and a simple log sink records log lines.
- Unit tests in Paneport.Core.Tests and headless UI tests in Paneport.UI.Tests, with hand-written fakes kept in each test project (C-0011 section 1, C-0004 section 2).
- `docs/design/app-shell.md` (S0.3.7).

Out:

- Real host bindings and the iOS head (M1.2, M1.3), and the in-game overlay (M2.1).
- Steam sign-in, the library from Steam, and downloads (M2.3, M7.1).
- Compatibility data (M6.4); import, launch and the relaunch prompt (M6.5, D9); the open-source licenses screen (M9.1).
- Saving settings to disk. It comes with a later stage, starting with the iOS head and the settings folder of C-0004 section 4.
- Any token on disk. Paneport.Desktop never stores a Steam token on disk (C-0004 section 2, C-0007 section 3).

**Read first.**

- C-0001 sections 6 and 7; C-0004 sections 1, 2 and 4; C-0005 sections 1 and 2; C-0007 sections 2 to 5; C-0008 sections 1, 2 and 5; C-0009 section 4; C-0010 sections 2, 5 and 6; C-0011 sections 1 and 2; C-0012 section 1.
- `avalonia_ui_layer.md`: section 1, what the shell covers; section 2.1, the iOS single-view lifetime; section 2.4 part 0, compiled bindings by default in Avalonia 12; section 3.4 item 2, compiled XAML and no runtime XAML loading.
- `roadmap-2026-10.md`, the solution layout in the section on build and signing.
- `docs/design/solution-and-build.md`, and `docs/design/repository-checks.md` if M0.2 has merged.

**Deliverables.**

- `src/Paneport.Core/`: the domain model, the interfaces, the secret value type and the view models, one public type per file (C-0010 section 2), with XML documentation (C-0010 section 5). CommunityToolkit.Mvvm is added through `Directory.Packages.props`.
- `src/Paneport.UI/`: the shell view, the four pages, styles and the data templates.
- `src/Paneport.Desktop/`: the composition root, the window and the fakes.
- `tests/Paneport.Core.Tests/` and `tests/Paneport.UI.Tests/`: the tests below and their fakes.
- The updated lock files.
- `docs/design/app-shell.md`. It covers the three components and their dependency directions; the domain model; each interface with the stage that implements it for real; navigation and view matching; threading (UI thread, asynchronous services, no `async void`); the fakes, the table of sample titles and the fake host's values; open questions; and the public sources consulted (C-0001 section 6).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S0.3.1 | Domain model | `src/Paneport.Core/` | Unit tests: identities follow C-0008 section 1, and bad slugs are rejected. The ABI version decodes as C-0005 section 1 says, and a different major version is reported as incompatible. The memory budget keeps its four values. |
| S0.3.2 | Service interfaces | `src/Paneport.Core/` | They build with XML documentation on every member and use no platform type (C-0004 section 2). A unit test shows that the secret value type's text form never contains the secret. |
| S0.3.3 | Navigation and view-model base | `src/Paneport.Core/`, `src/Paneport.UI/` | Unit tests navigate to every page and back with fakes. A headless test shows the shell with its navigation entries. |
| S0.3.4 | Library and Title details pages | `src/Paneport.Core/`, `src/Paneport.UI/` | Unit tests for listing, selection and details. A headless test lists every fake title and opens one. |
| S0.3.5 | Settings and Diagnostics pages | `src/Paneport.Core/`, `src/Paneport.UI/` | Unit tests: a setting round-trips through a fake store, and diagnostics shows the fake host's values with the Game Mode level both reached and not reached. A headless test shows both pages. |
| S0.3.6 | Fake host and fake services | `src/Paneport.Desktop/` | The desktop head builds and starts with them. Review: the token store keeps the token in memory only, and every sample title is fictional. |
| S0.3.7 | App shell design document | `docs/design/app-shell.md` | Its sample table and fake host values match the fakes, it lists its public sources, and lychee passes on it. |

**Verification.** On the Windows workstation, from the repository root, the M0.1 commands pass. In particular:

| Command | Passes when |
|---|---|
| `dotnet build eng/ci.slnf -c Release` | It reports 0 warnings and 0 errors, with the AOT and trimming analyzers on in Paneport.Core and Paneport.UI. |
| The test command of M0.1, on `eng/ci.slnf` | Every test passes. Paneport.Core.Tests and Paneport.UI.Tests hold the tests of S0.3.1 to S0.3.5. |
| `dotnet format eng/ci.slnf --verify-no-changes` | It exits with 0. |

- The headless tests also fail when a page logs a binding error. How to catch it, for example through Avalonia's logging, is to be confirmed in this stage.
- One headless test builds the same shell that the desktop head shows, from the same view models over the test project's fakes. The visible check in L0 then has an automated twin.
- In CI, the `dotnet` job passes on Windows and on Ubuntu on the pull request's last commit. If M0.2 has merged, `checks` passes too and the trimmed publish shows no new warning. Otherwise the L0 integration session checks both.
- If M0.2's scanner exists, the session runs it locally before each push (C-0011 section 2). The fakes and tests hold no e-mail address, IP address or other value that it looks for.
- This stage creates no script.

**Done when.**

- The verification passes.
- Every new public API in Paneport.Core has tests (C-0011 section 1).
- `docs/design/app-shell.md` is merged with the stage.
- recelilious has merged the branch, the status is Done, and the session log is updated.

**Risks and fallbacks.**

- **Interfaces fixed too early.** Later stages know more. Keep each interface to what the shell shows now, and record open questions in `app-shell.md`. Changing a C# interface before the iOS head exists is cheap.
- **Trimming and the MVVM generators.** Whether CommunityToolkit.Mvvm's generated code is clean under the trimming analyzers and in the trimmed publish is to be confirmed in this stage. If a warning comes from the package itself, list it and ask recelilious. Do not suppress it: C-0010 section 2 allows suppressions only in Paneport.Steam.
- **Headless tests on Ubuntu.** Rendering details such as fonts may differ between runners. Tests check structure and bound values, not pixels.
- **Sample data.** Sample titles are fictional and carry no game artwork or other game material, following the rule of C-0008 section 5. Nothing in the shell suggests an affiliation with Microsoft, Valve or Apple (C-0001 section 7 item 4). Its layout and code are our own (C-0001 section 6).
- **Parallel work with M0.2.** Both stages add a row to `docs/design/README.md`, so the later one to merge rebases. M0.2's checks may arrive while this stage runs; the tests must then pass the scanner.

**Hands over to.**

- L0: the shell for acceptance.
- M1.3 and L1: the iOS head shows the same main view and view models. It implements the runtime host interface on top of Paneport.Interop, which depends on nothing else in the solution (C-0004 section 2), and the token store on the keychain (C-0004 section 4). The Diagnostics page then shows the real host's ABI version and memory limit on the iPhone (plan section 5.1).
- M2.1: the overlay controls live in Paneport.UI next to the shell.
- M2.3 and M7.1: the Steam service and the library service get real implementations behind the same interfaces.
- M6.4: the compatibility model joins the domain model.
- M6.5: the import and session flow, and the relaunch prompt (D9).

**Session log.**

_No sessions yet._
