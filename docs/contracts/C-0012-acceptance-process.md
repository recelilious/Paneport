# C-0012 Acceptance Process

| Field | Value |
|---|---|
| Status | Draft |
| Version | 0.1 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines when a change, a milestone and a release count as accepted, and how evidence from the device is recorded.

## 1. Definition of done for a change

A change is done when all of these hold:

1. It builds, and every automated test and check in C-0011 passes.
2. Tests are added or updated as C-0011 section 1 requires.
3. It follows the contracts. Any contract change it needs was made first, in its own commit, and approved.
4. New dependencies are registered under C-0003.
5. It contains nothing listed in C-0002 section 6 and no personal data. It contains no code under a license that C-0003 section 5 forbids, including GPL and AGPL, and no vendor SDK headers or decompiled code.
6. It contains no code copied, translated or rewritten from the products named in C-0001 section 6.
7. It does not bypass, remove or emulate any DRM, and adds no `steam_api` emulation (C-0001 section 7).
8. Documentation is updated: a design document for each new component listed in C-0004 section 2, and a README wherever C-0004 section 3 requires one.
9. Behavior that can only be checked on the device either has an evidence record or is marked "not yet verified on device" in the pull request.
10. Commit messages follow C-0002.

## 2. Workflow

1. Claude works on a short-lived branch (C-0002 section 2).
2. Claude checks its work against section 1, pushes the branch, and opens a pull request that states what changed, why, which tests ran, which evidence exists and which risks remain.
3. Claude cannot operate recelilious's iPhone. For anything that needs the device, Claude writes the test steps and an evidence record template. recelilious runs the steps and fills in the results, or attaches the logs.
4. recelilious reviews and merges. When recelilious explicitly asks, Claude performs the merge for them.

**Proposed standing exceptions.** On 2026-10-28 recelilious gave these instructions for that day's research documents and contract drafts. They become standing rules only if recelilious accepts this contract, and then C-0002 sections 2 and 3 are changed in their own contract commit to point here:

- **Research documents.** Claude may branch, push and merge changes that touch only `docs/research/`, after checking them against section 1 and after the CI `checks` job has passed. Roadmap files (`docs/research/roadmap-*.md`) are excluded and go through normal review, because the roadmap shapes the design (C-0001 section 4).
- **Contract drafts.** New contracts and changes to contracts are committed locally on a `contract/<summary>` branch and are not pushed until recelilious has reviewed them.

## 3. Milestone gates

The phases come from [the roadmap](../research/roadmap-2026-10.md). The table below is authoritative: a later change to the roadmap changes a gate only through a change to this contract. The numeric thresholds are proposals that recelilious sets by accepting this contract.

| Phase | Gate | Passes when |
|---|---|---|
| 0 | Transparent overlay | A transparent Avalonia view over the game view blends correctly pixel for pixel, lets touches outside its controls fall through to the game view, and routes multi-touch correctly. |
| 0 | Signal dispatcher | Over at least 4 hours of continuous running on the device, managed null-reference faults are caught as exceptions, guest faults take the guest path, and there are no native crashes and no garbage-collector hangs. The record confirms the call that turns off Mono's crash chaining (C-0005 section 4). |
| 0 | SteamKit2 on the device | A Release device build on Mono with full ahead-of-time compilation completes sign-in with Steam Guard and with a QR code, reads the license list and app information, gets depot keys and manifest request codes, and downloads a manifest and some chunks. The record states the interpreter setting it needed and the time for sign-in and for the manifest download. |
| 1 | One triangle | A Direct3D 11 test program renders a rotating triangle full screen on the device. JIT authorization of the default pool succeeds, the debugger detaches, and the pool is then verified (C-0006). A scan of the process's memory regions during the run finds no mapping that is both writable and executable. Translated code keeps running for at least 10 minutes after the debugger detaches, including after the app goes to the background and returns. The record states the pool size, how long authorization took and the iOS build, which is then added to `compat/ios-builds.json`. The native host framework resolves as C-0009 section 4 describes. |
| 2 | A 2D game | An imported 2D or indie DX11 game plays for 10 minutes on an iPhone 17 Pro with a median of at least 55 fps and a 1% low of at least 45 fps, and the app is not killed for memory. |
| 3 | Steam download | The user signs in with their own account, downloads a DRM-free game they own and plays it, and a fixed-base or 32-bit game loads correctly. |
| 4 | Alpha with Dock | A SteamStub-protected AAA game from the 2010s starts through the Dock host and plays with a median of at least 30 fps at 720p. A game that needs a launcher service gets in-game. The session's peak footprint stays below the memory limit the host read during that session, and the record states that limit and whether Game Mode was active. The record also lists the files Valve's client wrote into the prefix (C-0007 section 3). |
| Before 2027-05-11 | Runtime migration | The app runs on CoreCLR instead of Mono, because .NET for iOS 10, the last release with Mono, loses support on 2027-05-11 (C-0010 section 1). The signal dispatcher gate passes again on CoreCLR, with C-0005 section 4 changed first to handle CoreCLR's use of `SIGUSR1`. |

- Each gate ends as **Pass**, **Fail** or **Waived**. Only recelilious can waive a gate, and the waiver is a new evidence record for that gate stating the reason.
- `docs/evidence/README.md` tracks each gate's current verdict, one row per gate, linking its latest record.

If a phase 0 gate fails, recelilious decides what to adopt. The table lists the fallback the research proposes. The decision is recorded through changes to the affected contracts.

| Failed gate | Proposed fallback | Contracts that change |
|---|---|---|
| Transparent overlay | Draw the on-screen gamepad inside the game's own Metal pass. | C-0004 |
| Signal dispatcher | Move the app shell to Swift; Steam access then needs a design of its own outside .NET. | C-0001 section 4, C-0003 section 3, C-0004, C-0007, C-0010 |
| SteamKit2 on the device | Write our own Steam protocol client. | C-0003, C-0004, C-0007 |

## 4. Evidence records

- Each record is a file named `docs/evidence/YYYY-MM-DD-<topic>.md`, with the date in UTC.
- A record contains:
  - the gate or feature, and the build (commit hash and version);
  - the device model identifier, and the iOS version and build;
  - the signing route (free or paid team), the sideloading tool and its version, and the values of the entitlements listed in C-0009 section 4 (`get-task-allow`, `increased-memory-limit`, `extended-virtual-addressing`) in the installed app;
  - the JIT enabler and its version, whether Game Mode was active, and the memory limit read during the run;
  - the steps, the expected and observed results, the metrics, and scrubbed logs;
  - the verdict, the tester as a project handle (`recelilious` or `Claude`, never a real name), and the date.
- A record and its attachments never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path.

## 5. Release acceptance

A release is accepted when recelilious approves it and:

1. every gate that the release notes list as in scope has passed or been waived by recelilious;
2. the generated `THIRD_PARTY_NOTICES.md` and license texts (C-0009 section 6) cover every component in the package, checked against the register in C-0003 section 6, and the app's open-source licenses screen matches them;
3. the package contains nothing that C-0009 section 4 excludes, and the scan in C-0009 section 6 has passed;
4. this repository and every fork are public, the source archives are attached, and the source tag and every fork revision are recorded in the release notes;
5. checksums are published;
6. known issues are listed in the release notes.

## 6. Contract acceptance

A contract becomes Accepted, and an Accepted contract changes, only on recelilious's explicit approval, as rule 3 of the [contracts index](README.md) says.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
