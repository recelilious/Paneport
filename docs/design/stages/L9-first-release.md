# L9 First release

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L9 | First release | Large | Planned |  |  |
| S9.0.1 | Open-source licenses screen | Small | Planned |  |  |
| S9.0.2 | About section with version and source link | Small | Planned |  |  |
| M9.1 | Release workflow and compliance artifacts | Medium | Planned |  |  |
| S9.1.1 | Notices generator | Small | Planned |  |  |
| S9.1.2 | Source archiver | Small | Planned |  |  |
| S9.1.3 | Full .ipa scanner | Small | Planned |  |  |
| S9.1.4 | Release workflow | Small | Planned |  |  |
| S9.1.5 | Release notes template and checklist | Small | Planned |  |  |
| S9.1.6 | Version rules | Small | Planned |  |  |
| S9.1.7 | mingw-w64 runtime review | Small | Planned |  |  |

## L9 First release

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L8 |
| C-0012 gate | Release acceptance (C-0012 section 5) |
| Size | M, for the integration session. M9.1 has its own size. |
| Branch | `feat/l9-first-release` |
| Decisions needed | These are not in plan section 4.2 yet, so the session asks recelilious and records the answers in the session log. (1) The decision to release 0.1.0 (C-0009 section 6). (2) Making this repository and every fork public (C-0002 section 1, C-0009 section 6). (3) Whether to wait for the professional license review that the note at the top of C-0003 recommends. C-0009 section 6 waits for it "where possible". (4) Which gates the release notes list as in scope (C-0012 section 5 item 1). |

**Goal.** Publish Paneport 0.1.0, the first public binary release, as C-0009 section 6 describes, and accept it under C-0012 section 5. The release comes only from the `release` workflow (C-0009 section 3). recelilious decides to release, creates the `v0.1.0` tag on `main` and publishes the draft release. Claude prepares everything else. Claude never creates the release tag and never publishes a release.

**What recelilious will see.**

- A GitHub release `v0.1.0`, first as a draft and then published. It holds the ad-hoc-signed `.ipa`, SHA-256 checksums, `THIRD_PARTY_NOTICES.md`, the full license texts, and source archives of this repository and of every fork at the released revisions.
- Release notes that link to the tag and to each fork revision, list the gates in scope with their verdicts, and list the known issues.
- Paneport installed from that `.ipa` through AltStore, showing version 0.1.0 and its build number.
- An open-source licenses screen that lists the same components as `THIRD_PARTY_NOTICES.md` and opens the full text of each license.
- The release build running the in-scope games the way the L8 build did.

**Acceptance steps.** recelilious performs these steps on Windows and on the iPhone. This stage is the release acceptance of C-0012 section 5, which reads:

> A release is accepted when recelilious approves it and:
>
> 1. every gate that the release notes list as in scope has passed or been waived by recelilious;
> 2. the generated `THIRD_PARTY_NOTICES.md` and license texts (C-0009 section 6) cover every component in the package, checked against the register in C-0003 section 6, and the app's open-source licenses screen matches them;
> 3. the package contains nothing that C-0009 section 4 excludes, and the scan in C-0009 section 6 has passed;
> 4. this repository and every fork are public, the source archives are attached, and the source tag and every fork revision are recorded in the release notes;
> 5. checksums are published;
> 6. known issues are listed in the release notes.

R1 to R6 stand for items 1 to 6. Each step names the items it checks, and the table after the steps maps every criterion to its steps. Steps 1 to 4 lead to the draft release, steps 5 to 13 check the draft, and steps 14 and 15 publish and decide.

1. Open the L9 pull request and read `docs/releases/v0.1.0.md`. Expected: a table of the gates in scope in which every gate shows Pass or Waived and links its latest evidence record, with the same verdicts as the gate table in `docs/evidence/README.md`. The known issues section is not empty. Checks R1 and R6.
2. In a private browser window, without signing in, open this repository and every repository listed in `.gitmodules`, at least `Paneport-wine`, `Paneport-FEX` and `Paneport-dxmt`. Expected: every page opens. Checks R4.
3. Merge the pull request and update your local `main`. Then create and push the tag, for example with `git tag -a v0.1.0 -m "Paneport 0.1.0"` and `git push origin v0.1.0`. Expected: a `release` run for `v0.1.0` starts in the Actions tab of the repository.
4. Wait for the run to finish. Expected: the run passes. A draft release for `v0.1.0` appears under Releases and is not published. The run summary shows the version, the build number and the SHA-256 of every asset.
5. Open the draft release. Expected assets: the `.ipa`, the checksum file, `THIRD_PARTY_NOTICES.md`, the license texts, source archives of this repository and of every repository in `.gitmodules`, and the source archive of SteamKit2. Expected body: the release notes, a link to the tag, a link to each fork revision, the checksums and the known issues. Checks R4, R5 and R6.
6. Follow the tag link and each fork revision link. Expected: each opens the matching tree or commit on GitHub. Each fork revision is the one that the tagged tree shows for that submodule under `external/`. Checks R4.
7. Read the scan section of the run summary. Expected: no unreviewed finding, and a reason next to every allowed finding. No file of a kind that C-0009 section 4 excludes, such as Valve client files, Microsoft's Windows runtimes, Wine Mono, Wine Gecko or game files. Checks R3.
8. Read the coverage section of the run summary and the table at the top of `THIRD_PARTY_NOTICES.md`. Expected: no unattributed file. Every listed component appears in the register of C-0003 section 6, and none has the category Under review or Forbidden. Every component has its full license text. Checks R2, and R3 for the components under review that C-0009 section 4 excludes.
9. On Windows, download the `.ipa`, the checksum file and the source archives of this repository and of `Paneport-wine`. Compute the SHA-256 of the `.ipa`, for example with `Get-FileHash -Algorithm SHA256` in PowerShell, and unpack both archives. Expected: the hash equals the value in the checksum file, in the release body and in the run summary. Each archive unpacks to a source tree with that repository's license files at its top. Checks R4 and R5.
10. Install the `.ipa` with AltStore 2.2 or later through AltServer on Windows, signed with your own Apple ID (C-0009 section 5). Note the AltStore version for the record. Expected: it installs and replaces the existing Paneport, as the test app does (plan section 7.3). If AltStore offers to install it as a second app instead, note it in the record and decide whether to go on, because a second app counts against the free account's three-app limit (plan section 4.1). Deleting the existing app first would also delete its prefixes, games and saves, which live in its container (C-0004 section 4).
11. Open Paneport's About section. Expected: version `0.1.0`, the build number from the release notes, and a link to the source of this version.
12. Open the open-source licenses screen. Expected: the same components, in the same order, as the table at the top of `THIRD_PARTY_NOTICES.md`. Opening Wine, SteamKit2 and one Apache-2.0 component, such as protobuf-net, shows the full license text and notices of each. Checks R2.
13. Smoke test the release build. Start the DRM-free Steam game of L7 (Direct path), enable JIT with StikDebug when the app asks, and play for about 5 minutes. Export the session metrics and read the diagnostics page. Then close Paneport in the app switcher and open it again, because switching games needs a relaunch (C-0004 section 1), and do the same with the SteamStub game of L8 (Dock path). Expected: JIT reaches `ready` on each launch. Both games reach gameplay, and the app is not killed. The memory limit is close to the one in the L8 record, and not the much lower limit of an install without `increased-memory-limit`. On Windows, `tools/SessionMetrics.cs` (L6) gives each game's median and 1% low frame rates, peak memory footprint and thermal state over time (C-0011 section 1). Record the entitlement values the same way the earlier evidence records did.
14. Publish the draft. Then open the release page in a private browser window. Expected: the release, its notes and every asset are visible and can be downloaded without signing in. Checks R4 and R5.
15. Give the verdict in the evidence record. Expected: Pass only if steps 1 to 14 passed and recelilious approves the release.

| Criterion (C-0012 section 5) | Checked in steps |
|---|---|
| recelilious approves the release | 15 |
| R1: every gate in scope has passed or been waived | 1 |
| R2: the notices and license texts cover every component, checked against the register, and the licenses screen matches them | 8, 12 |
| R3: the package holds nothing that C-0009 section 4 excludes, and the scan has passed | 7, 8 |
| R4: this repository and every fork are public, the source archives are attached, and the tag and every fork revision are in the notes | 2, 5, 6, 9, 14 |
| R5: checksums are published | 5, 9, 14 |
| R6: known issues are listed in the notes | 1, 5 |

If a step before step 14 fails, the draft stays unpublished. The fix goes through a branch. recelilious then deletes the draft and moves the `v0.1.0` tag to the fixed commit, or tags `v0.1.1` instead. The tag of a published release is never moved.

Optional: to see a first launch from scratch, install the release where no Paneport data exists, or delete Paneport first if losing its prefixes, games and saves is acceptable.

**Evidence record.** The file is `docs/evidence/YYYY-MM-DD-l9-release.md`, dated in UTC on the day of the steps (C-0012 section 4). It contains:

- the gate, release acceptance of version 0.1.0 (C-0012 section 5), and the build: the tag `v0.1.0`, its commit hash, the version and the build number;
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the AltStore version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app (C-0009 section 4);
- the JIT enabler (StikDebug) and its version, whether Game Mode was active, and the memory limit read during step 13;
- the steps with their expected and observed results: one line for each of R1 to R6, the gates in scope with the verdict and record of each, the SHA-256 of the `.ipa`, the number of components on the licenses screen and in `THIRD_PARTY_NOTICES.md`, and links to the release page and to the workflow run;
- the metrics of step 13 for each game: the median and 1% low frame rates, the peak memory footprint and the thermal state over time (C-0011 section 1), and scrubbed logs if there are any;
- the verdict (Pass or Fail), the tester as `recelilious`, and the date.

The record and its attachments never contain a UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). This stage has two traps. Screenshots of AltStore or of Settings can show the Apple ID or the device name, so crop them or leave them out. Hash tools print the local path of the file, so copy only the hash. The personal-data check of C-0011 section 2 runs on the record in CI before it is merged.

**Integration session.** The session starts when M9.1 is Done and L8 is Accepted (plan section 3.1). It works on `feat/l9-first-release`.

Read first: C-0009 sections 3 to 6; C-0012 sections 2 to 5; C-0003 sections 1, 3, 5 and 6; C-0002 sections 1 and 6; C-0001 sections 5 and 7; C-0004 sections 1 and 4; C-0006 section 6; C-0007 section 6; C-0008 section 3; C-0010 section 2; C-0011 section 1; `docs/design/release-engineering.md`; `docs/releases/README.md`; `docs/evidence/README.md` and the latest record of each gate; the research notes `ios_platform_constraints.md` (section 7) and `precedents_and_ux_layer.md` (KQ6).

1. Check that M9.1 is Done and L8 is Accepted. Ask recelilious for the decisions in the field table and record the answers in the session log.
2. Build S9.0.1 and S9.0.2 with their tests. Describe them in a new section of `docs/design/release-engineering.md`.
3. Run the M9.1 dry run on the stage branch with the complete app: every fork, the Dock host, the `compat/` snapshot and the JIT broker. Claude pushes and deletes the test tag only if recelilious gave M9.1 that permission; otherwise recelilious does it. Resolve each finding:
   - A shipped component that is missing from the register, or whose license differs from it, needs a change to C-0003 on its own `contract/<summary>` branch, pushed only after recelilious has reviewed it (C-0012 section 2). The release waits for that change to be accepted.
   - An unattributed file gets an entry in `eng/release/bundle-components.json`.
   - A scan finding gets a fix in our build, or a fix in a fork on its `paneport/L9` branch with the submodule pointer moved on the stage branch (plan section 3.3), or a reviewed allow-list entry.
4. Confirm that nothing in the bundle is Under review (C-0009 section 4). This includes the mingw-w64 runtime and Apple's Metal Shader Converter.
5. Write `docs/releases/v0.1.0.md` from the template. The proposed gates in scope are those of phases 0 to 4, plus the runtime migration gate if L10 has been accepted, and recelilious confirms them (decision 4). Known issues come from the evidence records and session logs of L2 to L8 and from the contracts. Examples: switching games needs a relaunch (C-0004 section 1); saves inside the prefixes are not backed up (C-0004 section 4); 32-bit titles that use the Steam API do not run through the Dock host (C-0007 section 6); games that need anti-cheat are out of scope (C-0001 section 5); SideStore and Sideloadly are not supported install routes, and a free account re-signs every 7 days (C-0009 section 5); JIT is verified only on the iOS builds listed in `compat/ios-builds.json` (C-0006 section 6); and, unless L10 has been accepted, the app runs on Mono from .NET for iOS 10, whose support ends on 2027-05-11 (C-0010 section 1).
6. Add version 0.1.0 to the release history in `docs/releases/README.md`. The evidence record is linked there after acceptance.
7. Prepare the evidence record template with the fields above, following the convention that S2.0.1 set.
8. Run the checks, push the branch and open the pull request with the dry-run link and the acceptance steps. Give recelilious the summary in Chinese (plan section 3.3). If the merge creates a new commit, the release checklist recommends a last dry run on it before tagging.
9. After recelilious has run the steps, a continuation session commits the filled-in record, links it from `docs/releases/README.md` and sets L9 to Accepted. From then on, compatibility reports whose `paneportVersion` is `v0.1.0` count towards ratings (C-0008 section 3).

If something goes wrong:

- A phase 0 gate of L2, which this stage reaches through L4 (plan section 5.1), was waived rather than passed. The release notes say so, and recelilious confirms the gates in scope (decision 4).
- A gate in scope failed or has no record. The release waits, or recelilious waives the gate with a new evidence record (C-0012 section 3).
- AltStore cannot install the release `.ipa`, or the memory limit shows that `increased-memory-limit` was dropped. Stop, compare with the `.ipa` of the `ios` job, and let recelilious decide.
- The session runs after 2027-05-11 while the app still uses Mono. .NET for iOS 10 is out of support from that date (C-0010 section 1), so recelilious decides whether to release before L10.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S9.0.1 | Open-source licenses screen (C-0003 section 3). It reads the notices index and the license texts that M9.1 places in the bundle, lists the components in the order of the index, and shows each full text. The index is parsed with `System.Text.Json` source generation (C-0010 section 2). Paneport.Desktop, which is never shipped, shows a placeholder. | `src/Paneport.Core` (index model, view model and the interface for reading the notices, C-0004 section 2), `src/Paneport.UI` (view), `src/Paneport.iOS` (reads the bundle); tests in `tests/Paneport.Core.Tests` and `tests/Paneport.UI.Tests` | A headless UI test (C-0011 section 1) with a fake notices source built from a sample index: the screen lists every component in order, opens each full text, and shows an error message instead of crashing when the index is missing or malformed. A unit test parses the sample index. |
| S9.0.2 | About section. It shows `CFBundleShortVersionString` and `CFBundleVersion` from the bundle, Paneport's own MIT license, a notice that the app contains LGPL components with a link to the source of this version, and the entry to the licenses screen. LGPL-2.1 section 6 asks for prominent notice (`wine_on_ios.md`, KQ8). If the diagnostics page of L1 already reads the version, this module reuses that reader. | `src/Paneport.Core` (view model and the version interface), `src/Paneport.UI` (view), `src/Paneport.iOS` (reads the bundle); tests in `tests/Paneport.UI.Tests` | A headless UI test with a fake version source: the section shows `0.1.0 (123)`, a source link for `v0.1.0` and the entry to the licenses screen. |

**Session log.**

_No sessions yet._

## M9.1 Release workflow and compliance artifacts

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L9 |
| Depends on | M1.3 (the release itself waits for L8) |
| Decisions needed | None from plan section 4.2. One new decision, not in plan section 4.2 yet: whether Claude may push and delete `v0.0.0-dryrun.<n>` test tags and delete the dry-run draft releases. C-0009 section 6 gives release tags to recelilious, so without that permission recelilious performs these steps. |
| Size | L |
| Branch | `ci/m9-1-release-workflow` |
| Design documents | Writes `docs/design/release-engineering.md` and adds it to `docs/design/README.md`. Reads `docs/design/repository-checks.md` (M0.2) and the design document of M1.3 (the iOS app head, its packaging, its ad-hoc signing and its `.ipa` scan). |

**Goal.** Build the `release` CI job of C-0011 section 4 and the tools it uses, so that a `v*` tag on `main` produces a complete draft release that meets C-0009 section 6 without any manual packaging. Prove it with a dry run on a test tag that is never published. This stage can start any time after M1.3. The release itself waits for L8.

**Scope.**

In:

- **Triggers and modes.** The job runs only for `v*` tags (C-0011 section 4). A tag of the form `v<MAJOR>.<MINOR>.<PATCH>` runs in release mode. The tagged commit must then be on `main` and `docs/releases/<tag>.md` must exist, and the run ends in a draft release. A tag with a pre-release part, such as `v0.0.0-dryrun.1`, runs in dry-run mode. It uses the release notes template and ends in a draft release that is titled and marked as a dry run and is deleted after checking. A `workflow_dispatch` entry re-runs a dry run on an existing tag. The workflow never publishes a release.
- **Build.** The job builds from the tag on the GitHub-hosted macOS runner image pinned in `eng/toolchain.json` (C-0009 sections 2 and 6). It runs C-0009 stages 1 to 4 with signing disabled, reusing the steps of the `ios` job from M1.3. `PaneportExtendedVirtualAddressing` stays off, so the release does not ask for an entitlement that C-0009 section 4 reports may need a paid account. The interpreter setting comes from `eng/toolchain.json` (C-0009 section 3).
- **Clean binaries.** Native builds map source paths to relative ones, .NET builds set `ContinuousIntegrationBuild` with `Deterministic`, and release device builds are stripped of symbols (C-0009 section 6). The research lists a `NoSymbolStrip` build property (`avalonia_ui_layer.md`, section 2.2 part 3). How the .NET build strips the app by default is to be confirmed in this stage.
- **Ad-hoc signing.** The job reuses the ad-hoc signing step that M1.3 wrote for the `ios` job, whose `.ipa` is already ad-hoc signed (plan section 5.2): each embedded framework is signed with `codesign --force --sign -`, then the app with `codesign --force --sign - --entitlements Entitlements.plist` (C-0009 section 6). No Apple identity, certificate or profile is held or used (C-0009 section 5).
- **Versions.** `CFBundleShortVersionString` is the `MAJOR.MINOR.PATCH` of the tag. `CFBundleVersion` is an integer that grows with every published build (C-0009 section 6). The proposed rule is the number of commits in the history of the tagged commit, which grows with every release tagged on `main`. The design document confirms or replaces that rule. How the build sets both keys is to be confirmed in this stage.
- **Notices.** `THIRD_PARTY_NOTICES.md`, the license texts and an index for the app's screen are generated from the register in C-0003 section 6. They go into the bundle of every app build, the `ios` job's included, and into the release (C-0009 section 6, C-0004 section 3). They cover the .NET runtime and .NET for iOS (C-0009 section 4), and the mingw-w64 runtime and LLVM runtime libraries inside Windows-side binaries (C-0004 section 2). In the `ios` job, a component that is Under review or Forbidden fails the build (C-0003 section 5, C-0009 section 4), and an unattributed file is a warning. In the `release` job both fail. The mingw-w64 runtime counts too: while its register entry is Under review, a bundle with Windows-side binaries fails (S9.1.7).
- **Scan.** The full `.ipa` scan of C-0009 section 6, and the exclusions of C-0009 section 4 (S9.1.3). It extends the `.ipa` scan tool that M1.3 wrote for the `ios` job and keeps that tool's name, so that one tool in `tools/` does the scan of C-0009 section 6. Only if M1.3 wrote no such tool does this stage create `tools/ScanPackage.cs`.
- **Sources.** Archives of this repository and of every submodule at the released revisions (S9.1.2). Also the source of each LGPL component that ships as a package rather than as a submodule, today SteamKit2 at the exact version in the lock file. C-0003 section 3 needs the complete source of the library to accompany it, and C-0009 section 6 offers the source of every LGPL component from the same place as the `.ipa`.
- **Checksums.** The SHA-256 of every asset, checked again after upload.
- **Release notes.** The template (S9.1.5) has sections for the summary; the gates in scope with their verdicts and records; supported devices and systems (C-0001 section 2) with the verified iOS builds (C-0006 section 6); installation, covering AltStore 2.2 or later, Developer Mode, StikDebug for JIT, the free-account limits and the unsupported routes of C-0009 section 5; known issues; licenses, noting that the package as a whole is not MIT (C-0003 section 1); compliance, meaning no Microsoft, Valve or game binaries, no DRM bypass and no affiliation (C-0001 section 7); sources; and checksums. The workflow composes the release body from the notes for the tag, plus generated sections for the tag, every submodule revision with its link, the build number and the checksums.

Out:

- Creating the release tag and publishing a release. recelilious does both (C-0009 section 6).
- The licenses screen and the About section in the app (S9.0.1 and S9.0.2), and the notes for 0.1.0. They belong to L9.
- A paid-account variant with extended virtual addressing.
- Any Valve, Microsoft or game binary in CI, in fixtures or in the bundle (plan section 7.6, C-0001 section 7).
- New CI jobs. The job list of C-0011 section 4 stays as it is.
- An AltStore source or another update feed, and published debug symbols. Both could be proposed later.

**Read first.**

- Contracts: C-0009 sections 2 to 6; C-0003 sections 1, 3, 4, 5, 6 and 8, and the note at its top; C-0011 sections 1 to 4; C-0012 sections 1 to 5; C-0004 sections 2 and 3; C-0010 sections 1, 2, 6 and 7; C-0002 sections 5 and 6; C-0001 sections 6 and 7; C-0008 section 1 and C-0006 section 6, for what `compat/` puts in the bundle.
- Plan: sections 3.3, 3.4, 5.2, 7.6 and 7.7.
- Research: `roadmap-2026-10.md`, the build and signing section; `avalonia_ui_layer.md`, section 2.2 parts 3 and 4, section 3.2, and experiments E13 and E15 in section 4; `ios_platform_constraints.md`, sections 4 and 7; `wine_on_ios.md`, KQ8; `precedents_and_ux_layer.md`, KQ6.
- Design documents: `docs/design/repository-checks.md` (M0.2), whose register parsing in `tools/CheckLicenses.cs` and personal-data detectors in `tools/ScanPersonalData.cs` this stage reuses, and the design document of M1.3, whose build, packaging, ad-hoc signing and `.ipa` scan the `release` job reuses.

**Deliverables.** File names follow C-0010 section 7. The design document may rename them within that rule.

- `.github/workflows/release.yml` with the `release` job, or the same job in `.github/workflows/ci.yml`, the workflow file of M0.1 and M0.2.
- `eng/release/`: Bash 3.2 scripts for the macOS-only steps that M1.3 does not already provide (C-0010 section 1), for example `strip-binaries.sh`, and `plant-scan-failures.sh` for the planted-failure check; `bundle-components.json`, the reviewed mapping from bundle files to components; `scan-allowlist.json`, the reviewed allow-list of the scanner.
- `tools/GenerateNotices.cs`, `tools/ArchiveSources.cs` and `tools/ReleaseVersion.cs`, and the scanner of S9.1.3. These are C# file-based tools run with `dotnet run` (C-0010 section 1). Each has a `--self-test` mode over fixtures that it generates at run time, the convention of M0.2's tools (`docs/design/repository-checks.md`).
- The packaging step that places the notices in every app bundle, wherever M1.3 put packaging (`src/Paneport.iOS` or `eng/`).
- `docs/releases/README.md` and `docs/releases/release-notes-template.md`. `docs/releases/` is a new folder under `docs/`. C-0004 section 3 asks for a contract change only for a new top-level folder. The folder gets its own README and a row in `docs/README.md`.
- `docs/design/release-engineering.md`, listing the public sources it consulted (C-0001 section 6), and its row in `docs/design/README.md`.
- Updated READMEs in `tools/`, `eng/` and `.github/workflows/` (C-0004 section 3).
- Contract proposals where needed, each on its own `contract/<summary>` branch and pushed only after review (C-0012 section 2). One is the C-0003 section 6 change from S9.1.7, together with any shipped component missing from the register. Another, if recelilious wants the rules explicit, is a C-0009 change that names the bundle location of the notices in section 4 and the LGPL package source archives in section 6.

Every new file carries the SPDX lines of C-0010 section 6 or is covered by `REUSE.toml`. No new dependency is planned. One that turns out to be needed is registered in C-0003 section 6 first (C-0012 section 1 item 4).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S9.1.1 | Notices generator. It inventories the assembled `.app` and attributes each file to Paneport or to register components through the mapping. The main executable maps to every package in the app's lock file, to the .NET runtime and to .NET for iOS. It writes `THIRD_PARTY_NOTICES.md`, the license texts and the index into build output and the bundle, never into git. It keeps Apache-2.0 NOTICE files and the FreeType credit (C-0003 section 5), and reuses the register parsing of `tools/CheckLicenses.cs` (M0.2). | `tools/GenerateNotices.cs`, `eng/release/bundle-components.json` | Generated fixture bundles. A fully registered one passes and lists each component once, in the same order in all three outputs. An unregistered, Under review or Forbidden component, a missing license text, an unattributed file, and an LGPL component other than SteamKit2 inside the main executable (C-0003 section 3) each fail with a message that names it. |
| S9.1.2 | Source archiver. It archives this repository at the tag, every submodule in `.gitmodules` at its recorded revision, and each LGPL package that is not a submodule at its exact version. How it finds the source revision of a package version is to be confirmed in this stage. In release mode, each revision must be fetchable from its remote without credentials and each repository must be public (C-0009 section 6). In dry-run mode, a private repository is reported instead, and its archive comes from the job's own checkout. It writes the source list for the release notes. | `tools/ArchiveSources.cs` | In a temporary repository with a temporary submodule, each archive holds exactly the recorded revision. A revision that is missing from its remote fails. |
| S9.1.3 | Full `.ipa` scanner. It unpacks the signed `.ipa` and scans every file, including binary property lists and the strings inside binaries, and also the composed release notes. It looks for provisioning profiles, team identifiers, UDIDs, ECIDs, serial numbers, e-mail addresses and local paths, and allows device model identifiers (C-0009 section 6). It checks that each signature is ad hoc without a team identifier, that the entitlements are exactly the declared ones, and the exclusions of C-0009 section 4. Allowed hits are listed by file and by the SHA-256 of the matched text, each with a reason. It extends the `.ipa` scan tool of M1.3 and reuses the detectors of `tools/ScanPersonalData.cs` (M0.2). | The `.ipa` scan tool of M1.3 in `tools/`, or `tools/ScanPackage.cs` if M1.3 wrote none; `eng/release/scan-allowlist.json` | Generated fixture packages. A clean one passes. A planted provisioning profile, team identifier, UDID, ECID, serial number, e-mail address, local path, extra entitlement or file named like a Valve client library each fails. A device model identifier such as `iPhone18,1` in `compat/` passes. An allowed hit passes only in the file the allow-list names. |
| S9.1.4 | Release workflow. The `release` job with its triggers and modes, the build from the tag, stripping, ad-hoc signing, the tools of this stage, checksums, the composed notes and the draft release. In release mode it also checks that every gate the notes list as in scope shows Pass or Waived in `docs/evidence/README.md`, and that the known issues section is not empty. | `.github/workflows/release.yml`, scripts in `eng/release/` | A push of `v0.0.0-dryrun.1` runs it in dry-run mode, and a push to a branch does not run it. The full check is the dry run under Verification. |
| S9.1.5 | Release notes template and checklist. The template has the sections listed under Scope, with markers where the workflow inserts generated text. `docs/releases/README.md` holds the release process, the checklist, the dry-run and clean-up steps, and the release history. | `docs/releases/README.md`, `docs/releases/release-notes-template.md` | Review checklist: one section or step for each item of C-0012 section 5 and for each release item of C-0009 section 6. lychee passes on both files (C-0011 section 2). |
| S9.1.6 | Version rules. It parses the tag, picks release or dry-run mode, and derives `CFBundleShortVersionString` and `CFBundleVersion` (C-0009 section 6). It checks that a release tag is on `main` and has its notes, and it checks the built `Info.plist`. | `tools/ReleaseVersion.cs` | Self-test cases in a temporary repository. `v0.1.0` gives `0.1.0` in release mode. `v0.0.0-dryrun.1` gives `0.0.0` in dry-run mode. `v1.2`, `0.1.0` and `v01.2.3` fail. A later commit gets a larger build number. A release tag off `main` or without notes fails. |
| S9.1.7 | mingw-w64 runtime review (C-0003 section 6), done only if no earlier stage recorded it. It lists the runtime objects that our Windows-side link steps pull into shipped binaries, for example from the linker's map output. It matches each to the notices listed in `COPYING.MinGW-w64-runtime.txt`, and proposes the C-0003 change that moves the entry out of Under review. C-0003 section 6 asks for this review before the first Windows-side build. That build comes in M4.1 or M4.2, which come before this stage in the order of plan section 6.3 but are not among its dependencies. If no earlier stage did the review, the session tells recelilious that this timing was missed. | `docs/design/release-engineering.md`, `eng/release/bundle-components.json`, a `contract/<summary>` branch | Review checklist: every linked runtime object matches a notice, no code of unclear license is linked, and the conclusion is recorded. If an earlier stage already did the review, the check is only that the register entry has left Under review. |

**Verification.** Everything here runs without recelilious's iPhone, except the last item, which is a marked device check. This stage creates these scripts: the self-tests of `tools/GenerateNotices.cs`, `tools/ArchiveSources.cs`, `tools/ReleaseVersion.cs` and the scanner of S9.1.3; the planted-failure script `eng/release/plant-scan-failures.sh`; the `release` workflow; and the dry-run and clean-up steps in `docs/releases/README.md`.

1. **Tool self-tests.** Run each tool with `--self-test` on Windows and on macOS, for example `dotnet run tools/GenerateNotices.cs -- --self-test`. The signature checks of S9.1.3 run only on macOS. Pass: every case in the Small stages table gives the stated result, every failing case exits non-zero with a message that names the file, component or tag, and each tool exits with 0 after reporting every case as expected. The self-tests generate their fixtures at run time in a temporary folder, so no `.ipa`, `.mobileprovision` or binary is ever committed (C-0002 section 6, C-0011 section 1). The `release` job runs the same self-tests before it uses the tools.
2. **Dry run.** Push the test tag `v0.0.0-dryrun.1` on the head of the stage branch, as the decision in the field table allows. The first dry runs start from a tag push, which runs the workflow file of that commit. Whether `workflow_dispatch` can do the same before the workflow is on `main` is to be confirmed in this stage. Pass when all of these hold:
   - The `release` run passes and ends in a draft release marked as a dry run.
   - The draft holds the `.ipa`, the checksum file, `THIRD_PARTY_NOTICES.md`, the license texts, a source archive of this repository, one for each submodule in `.gitmodules`, and one for SteamKit2.
   - The run summary holds the scan report with no unreviewed finding and the coverage report with no unattributed file.
   - The job's own check of every uploaded asset against the checksum file passes.
   - The composed notes name the tag and every submodule revision, each with a working link.
   - The `Info.plist` in the `.ipa` has `CFBundleShortVersionString` `0.0.0` and the build number that the version rule gives for that commit.
   - On the runner, `codesign -d --entitlements` shows the entitlements of `Entitlements.plist` and no extended-virtual-addressing key, and every framework and the app carry an ad-hoc signature without a team identifier.
3. **Planted failure.** On macOS, `eng/release/plant-scan-failures.sh` copies the dry-run `.ipa`, adds a generated fake provisioning profile inside the app, and runs the scanner on the copy. Pass: the scanner exits non-zero and names the planted file. A second copy with a generated local path inside a binary also fails. Neither copy is uploaded or committed.
4. **Coverage review.** The pull request lists every component from the coverage report of the dry run next to its row in the register of C-0003 section 6. Pass: every component has a row, none is Under review or Forbidden, and every file of the bundle is attributed.
5. **Guards.** The self-test cases of S9.1.6 pass: a release-mode tag on a commit that is not on `main` is refused, a release-mode tag without `docs/releases/<tag>.md` is refused, and a pre-release tag selects dry-run mode. Pushing the stage branch itself starts no `release` run.
6. **No regressions.** The `dotnet`, `native`, `ios` and `checks` jobs pass on the pull request (C-0011 section 3), and the `.ipa` of the `ios` job now holds the notices.
7. **Device check (optional, needs the iPhone).** recelilious installs the dry-run `.ipa` through AltStore. Pass: it installs and launches, and the diagnostics page shows a memory limit that matches an install with `increased-memory-limit`. This is not required for Done. L9 step 13 repeats it on the complete app.

**Done when.**

- Every check under Verification passes, except the optional device check, and the pull request records the results with links to the runs. If the device check did not run, the pull request marks the AltStore install of the release `.ipa` as "not yet verified on device" (C-0012 section 1 item 9).
- `docs/design/release-engineering.md` describes the workflow and its modes, each tool, the attribution mapping, the allow-list rules, where the notices sit in the bundle and the format of their index, the version rules, and the public sources it consulted. C-0009 section 4 names no location for the notices in the bundle, so the design document fixes one, for example a `licenses/` folder at the bundle root.
- The mingw-w64 review is recorded and its register change is accepted, or the session log lists it as pending and blocking L9.
- The dry-run drafts and test tags are deleted.
- recelilious has merged the branch, and the stage status is Done.

**Risks and fallbacks.**

- **Build paths and names in binaries.** Our builds, the forks or third-party packages may leave build paths in binaries, and the scanner then fails. Fix our builds and the build scripts of the forks first. Fork changes go on `paneport/M9.1` branches (plan section 3.3). A hit inside a third-party binary is reviewed, then either allowed by file and hash with a reason, or handled the way the design document records. Nothing is allowed by pattern alone.
- **E-mail addresses in notices.** Upstream license and copyright notices can contain the e-mail addresses of their authors, and these notices must be kept (C-0003 section 5). The allow-list names such hits by file and by the SHA-256 of the text, so the allow-list itself holds no address.
- **Hashes that look like identifiers.** Commit hashes and SHA-256 values in the notes and in the bundle must not be taken for device identifiers. The scanner tells them apart the way `tools/ScanPersonalData.cs` (M0.2) does, since evidence records carry commit hashes too (C-0012 section 4).
- **Shared code between tools.** How the tools of this stage reuse the register parsing and the detectors of M0.2's tools, for example through a source file they share, is to be confirmed in this stage. Tools may depend on Paneport.Core and on nothing else under `src/` (C-0004 section 2).
- **License texts for packages.** For a package that declares only a license expression, where the full text and the copyright line come from is to be confirmed in this stage. Which shipped packages carry third-party notices files of their own, for example packages with native libraries inside, is also to be confirmed in this stage.
- **Third-party code inside the forks.** An upstream source tree can contain code written by other projects under other licenses. Which of it reaches shipped binaries is to be confirmed in this stage. Anything that ships is registered first, and a license that C-0003 section 5 does not list needs recelilious's decision through a contract change.
- **The mingw-w64 review.** If it finds linked code of unclear license, the release is blocked until recelilious decides (C-0003 section 6).
- **AltStore and the ad-hoc `.ipa`.** Re-signing an `.ipa` that was built without a profile was untested in the research (E13 in `avalonia_ui_layer.md`). L1 may already have shown it with the `.ipa` of the `ios` job. If AltStore cannot install the release `.ipa` or drops `increased-memory-limit`, compare it with that `.ipa` and stop for recelilious's decision. SideStore and Sideloadly stay unsupported until a device test (C-0009 section 5).
- **GitHub limits.** The limits on the size and number of release assets are to be confirmed in this stage. If an archive is too large, it is split, and the notes say how to join it.
- **Early start.** This stage may run long before L8, when the bundle has no forks and no Dock host yet. Its checks are generic, and the L9 integration session repeats the dry run on the complete app.
- **The gate table.** The release-mode check of S9.1.4 reads the gate table in `docs/evidence/README.md`, which S2.0.1 creates in L2. L2 is not among this stage's dependencies, so the table may not exist yet. The check is then written against the format that the L2 brief gives for S2.0.1 (one row per gate, with its verdict and a link to its latest record) and tested with generated fixtures, and the L9 integration session checks it against the real table.
- **Drift.** A new runner image or Xcode can change the output. The release uses the pins of C-0009 section 2, and changing a pin is a reviewed change.

**Hands over to.**

- **L9:** the dry-run procedure, the notices index that S9.0.1 reads, the release notes template and checklist, and the scan and coverage reports that the acceptance steps read.
- **Stages that add files to the bundle after this one has merged, such as M4.4, M5.2, M6.2 and M8.2 if they are not Done yet:** the `ios` job warns about their unattributed files and fails if a shipped component is Under review (C-0009 section 4). They may add attribution entries for their files; the L9 integration session attributes whatever is left.
- **L10:** the `release` job builds whatever runtime the app uses. After the move to CoreCLR, the notices list the runtime that actually ships.
- **Every later release:** the same workflow, template and checklist.

**Notes from earlier stages.**

- M0.1 ([L0 brief](L0-foundation.md), session log of 2026-10-29): the third-party notices of SkiaSharp's and HarfBuzzSharp's native-asset packages list about twenty components, among them libmicrohttpd under LGPL-2.1-or-later; confirm which of them the shipped iOS libraries contain (`docs/design/solution-and-build.md` section 10).
- M1.1 ([L1 brief](L1-app-on-iphone.md), session log of 2026-10-31): the `release` job runs on the image that `macos-runner-image` pins, as a plain `runs-on` value, or `tools/CheckToolchain.cs` fails. It can reuse the `ios` job's bootstrap step, and the `ditto` zip that keeps the app's file modes (`docs/design/repository-checks.md` sections 5 and 6).

**Session log.**

_No sessions yet._
