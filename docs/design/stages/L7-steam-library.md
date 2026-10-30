# L7 Steam library and downloads

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-29T00:46Z |

Back to the [development plan](../development-plan.md).

## Stage status

| Stage | Title | Level | Status | Branch or PR | Last session |
|---|---|---|---|---|---|
| L7 | Steam library and downloads | Large | Planned | | |
| S7.0.1 | Desktop download check | Small | Planned | | |
| S7.0.2 | Save backup notice and export | Small | Planned | | |
| S7.0.3 | Compatibility entries for the test titles | Small | Planned | | |
| S7.0.4 | 32-bit Direct3D 11 path | Small | Planned | | |
| M7.1 | Sign-in, library and downloads | Medium | Planned | | |
| S7.1.1 | Library sync | Small | Planned | | |
| S7.1.2 | Download queue, free-space check and resume state | Small | Planned | | |
| S7.1.3 | Chunk writer and file assembly | Small | Planned | | |
| S7.1.4 | Background task integration | Small | Planned | | |
| S7.1.5 | App manifest writer | Small | Planned | | |
| S7.1.6 | Updates | Small | Planned | | |
| S7.1.7 | Sign-in UI | Small | Planned | | |
| S7.1.8 | Library and downloads UI | Small | Planned | | |
| S7.1.9 | Token store and sign-out | Small | Planned | | |
| M7.2 | Classification and direct launch of Steam titles | Medium | Planned | | |
| S7.2.1 | Classifier on Steam install folders | Small | Planned | | |
| S7.2.2 | SteamStub detector reuse | Small | Planned | | |
| S7.2.3 | Launch decision | Small | Planned | | |
| S7.2.4 | Direct launch wiring | Small | Planned | | |
| M7.3 | 32-bit and fixed-base programs | Medium | Planned | | |
| S7.3.1 | i686 and fixed-base test programs | Small | Planned | | |
| S7.3.2 | WoW64 module build | Small | Planned | | |
| S7.3.3 | 32-bit address window | Small | Planned | | |
| S7.3.4 | Fixed-base mapping | Small | Planned | | |
| S7.3.5 | C-0004 change proposal for D11 | Small | Planned | | |
| S7.3.6 | 32-bit pieces in the template prefix and the app bundle | Small | Planned | | |

## L7 Steam library and downloads

| Field | Value |
|---|---|
| Level | Large |
| Depends on | L6, M2.3 |
| C-0012 gate | Phase 3, "Steam download" (C-0012 section 3) |
| Size | M |
| Branch | `feat/l7-steam-library` |
| Decisions needed | D6 (the two test titles, with the conditions listed under the acceptance steps) and D10 (whether exporting saves to `Documents/` is enough). |

**Goal.** recelilious signs in to Steam on the iPhone with their own account, browses the library, downloads a DRM-free game they own and plays it, and starts a fixed-base or 32-bit game that loads correctly. That is the phase 3 gate (C-0012 section 3). The stage also leaves L8 a library with verified installs, Steam app manifests and a launch decision that already sends protected titles to the Dock path.

**What recelilious will see.**

- A sign-in page that takes the account name and password and then asks for approval in the Steam mobile app on the same iPhone. It also offers a Steam Guard code instead, and a QR code for a Steam app on another device (C-0007 section 3).
- A library page with the owned titles that have Windows depots, each with its size and install state.
- A title details page that says whether the title starts directly. A title that needs the Dock path says so and cannot start until L8.
- A downloads page with progress, pause, resume and cancel. While Paneport is in the background, iOS shows the download's progress in a Live Activity.
- The DRM-free game, installed through Paneport, running full screen.
- The fixed-base or 32-bit game reaching its main menu.
- A notice that saves inside the prefix are not backed up, with the export that D10 settles (C-0004 section 4).

**Acceptance steps.**

Before the run:

- M7.1, M7.2 and M7.3 are Done. S7.0.2 and S7.0.3 are done, and S7.0.4 is done or Dropped. Step 1 runs S7.0.1.
- D6 names two titles from recelilious's own library. Both must take the Direct path: no `steam_api.dll` or `steam_api64.dll` anywhere in the install folder or in any program's imports, no SteamStub, and `ceg` set to `no` in a reviewed entry (C-0007 section 5, C-0008 section 5). Both should use Direct3D 10 or 11, which DXMT covers (C-0004 section 2), because the Direct3D 9 path may still be open (plan section 8). Both should fit the memory limit.
  - Title A is a DRM-free Steam game, ideally a small download.
  - Title B is a fixed-base x86-64 game or a 32-bit game. It may come from Steam or be a copy the user imports, which makes it a `local-` title (C-0008 section 1). A 32-bit Direct3D game also needs S7.0.4. The fixed-base examples in the research are large commercial titles (`process_and_memory_model.md` Q3), which often need the Dock path, so a suitable title B may be hard to find. If none exists, recelilious decides how to proceed, and only recelilious can waive a gate (C-0012 section 3).
- D10 is recorded.
- The build under test comes from the L7 branch, its bundled `compat/` snapshot holds the entries of S7.0.3, and it is installed through a supported route (C-0009 section 5). StikDebug and its pairing setup work as they did for L3 (C-0006).
- Paneport starts signed out. A token left in the keychain by an earlier gate survives the switch from the device test app (plan section 7.3), so sign out first if needed.

Steps:

1. On Windows, run S7.0.1 with the same commit. Expected: the install completes and verifies, the resumed download did not start from zero, the app manifest appeared only at the end, and after a restart the desktop app asks for sign-in again.
2. On the iPhone, open Paneport and its diagnostics page. Expected: the native host and the memory limit show. Note the values the evidence record needs, read the same way as in earlier records. If Paneport warns that this iOS build is not on its verified list (C-0006 section 6), note that too.
3. Sign in with your account name and password, then approve the sign-in in the Steam mobile app on the same iPhone. Expected: Paneport shows you as signed in and loads the library. If approval is not offered, use a Steam Guard code, and note which way you used.
4. Close Paneport in the app switcher and open it again. Expected: still signed in, with no password prompt.
5. Open the library. Expected: owned titles with Windows depots are listed with their sizes. Title A, and title B if it comes from Steam, show as not installed.
6. Open title A and start the install. Expected: the app shows the space the title needs next to the free space, then the download starts and its progress shows on the downloads page.
7. During the download, go to the Home Screen for at least two minutes. Expected: a Live Activity shows the progress. Back in Paneport, the progress has moved on.
8. During the download, swipe Paneport away in the app switcher, then open it again. Expected: the download continues from about where it stopped, not from zero, after a tap on Resume if the app asks for one (C-0007 section 4).
9. Wait for the download to finish. Expected: title A shows as installed and classified for the Direct path, with no Steam API, no SteamStub and `ceg` `no`. The details also say whether the game ships a `steam_appid.txt`. Note the classification for the record (C-0007 section 5).
10. Start title A and enable JIT on the JIT setup screen as the app guides you. Expected: the JIT state reaches `ready` (C-0006 section 5), and the game starts full screen and reaches gameplay.
11. Start the 10-minute measurement from the in-game menu (M6.5) and play until it ends. Expected: no crash and no memory kill. Note the median and 1% low frame rates, the peak footprint, the thermal state over time, the memory limit and whether Game Mode was active (C-0011 section 1). C-0012 sets no frame-rate threshold for this gate.
12. Quit the game and relaunch Paneport as it asks (D9). Expected: title A still shows as installed.
13. Install or import title B, check its details, and start it, enabling JIT as in step 10. Expected: its details show the Direct path. It reaches its main menu or first interactive screen and stays responsive for at least two minutes. The session log, in the in-app console or the diagnostics export, shows the main image at its preferred base for a fixed-base title, or a 32-bit guest window set up for a 32-bit title. Note which case you tested. Then quit the game and relaunch Paneport as it asks.
14. Open the notice about saves and run the export that D10 settled. Expected: the notice says saves in the prefix are not backed up, and the exported folder appears in the Files app under Paneport's folder on the iPhone.
15. Optional, if you own a small title that uses the Steam API: install it and open its details. Expected: it says it needs the Dock path, which is not available yet, and it offers no Launch button.
16. Optional: sign out, then sign in again once with a Steam Guard code and once with the QR code scanned by the Steam app on another device. Expected: both work.
17. Optional, if one of the titles gets an update during the stage: open it. Expected: the update shows as pending, and the title starts only after the update finishes (C-0007 section 4).
18. Sign out. Expected: the sign-in page shows, and Paneport is still signed out after you close and reopen it. The installed titles behave as `docs/design/library-and-downloads.md` says for sign-out; the integration session writes that behavior into this step.
19. Export the diagnostics. Expected: the export contains none of the items that C-0012 section 4 forbids, such as the account name, the Steam ID or a token. Attach it to the record.

How the steps check the gate. The criteria are quoted from C-0012 section 3: "The user signs in with their own account, downloads a DRM-free game they own and plays it, and a fixed-base or 32-bit game loads correctly."

| Gate criterion (C-0012 section 3) | Checked by |
|---|---|
| The user signs in with their own account | Step 3 |
| Downloads a DRM-free game they own | Steps 5 to 9: the library lists title A as owned, and its classification shows no Steam API, no SteamStub and `ceg` `no` |
| And plays it | Steps 10 and 11 |
| A fixed-base or 32-bit game loads correctly | Step 13 |

The gate passes when the steps in the table meet their expected results. The other steps check the rest of the stage: the token store, background progress and resume, the save notice and export, sign-out and the scrubbed export. A failure there is recorded and fixed, and recelilious decides whether L7 is accepted.

**Evidence record.**

- File: `docs/evidence/YYYY-MM-DD-l7-steam.md`, with the date in UTC (C-0012 section 4). The integration session writes it as a template. recelilious fills in the observed results or attaches the logs (C-0012 section 2 item 3).
- Fields that C-0012 section 4 requires:
  - the gate (phase 3, Steam download) and the build, as commit hash and version;
  - the device model identifier, and the iOS version and build;
  - the signing route (free or paid team), the sideloading tool and its version, and the values of `get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing` in the installed app;
  - the JIT enabler and its version, whether Game Mode was active, and the memory limit read during the run;
  - the steps, the expected and observed results, the metrics, and scrubbed logs;
  - the verdict (Pass, Fail or Waived), the tester as a project handle, and the date.
- For this gate the record also holds: each test title's name, Steam app ID or local slug, and Steam build ID; the classification results of both titles (C-0007 section 5); the download size and time; how background progress and resume behaved; the sign-in method used; frame rates, peak footprint and thermal states (C-0011 section 1); which of fixed-base or 32-bit was tested and what showed it; and the outcome of S7.0.1.
- It never contains a UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). Screenshots show no account name, no avatar and no other titles of the library.
- Once the record is merged, the phase 3 row of `docs/evidence/README.md` links to it (C-0012 section 3), and the entries of S7.0.3 gain test reports that name it (C-0008 section 4). Reports with a commit hash as `paneportVersion` do not count toward the rating until a release (C-0008 section 3).

**Integration session.**

The L7 session starts when M7.1, M7.2 and M7.3 are Done and D6 and D10 are recorded. Besides the files the kickoff prompt names, it reads C-0004 section 4, C-0007 sections 3 to 5, C-0008 sections 1, 4 and 5, and C-0012 sections 3 and 4. Then it:

1. Wires the stages together in both app heads. Sign-in and the library (M7.1) feed classification and the launch decision (M7.2), and Direct titles start through the session flow of M6.5 with the 32-bit and fixed-base support of M7.3. It checks the flow on Paneport.Desktop with fake services up to the launch request, which the fake host receives but cannot run.
2. Does S7.0.3 with recelilious, S7.0.2 as D10 decides, and S7.0.4 if D6 picks a 32-bit Direct3D title.
3. Confirms that the app from the `ios` job carries the 32-bit pieces of M7.3, the entries of S7.0.3 in its `compat/` snapshot, and the download task identifier in its Info.plist.
4. Runs the `dotnet`, `native`, `ios` and `checks` jobs (C-0011 section 3), writes the evidence record template and the acceptance checklist, and opens the pull request with device behavior marked "not yet verified on device" (C-0012 section 1 item 9).
5. Hands S7.0.1 and the acceptance steps to recelilious. Small fixes found on the device go on this branch. Larger problems become new stages (plan section 2.3) once recelilious agrees.
6. After the run, completes the record with recelilious's results, updates the phase 3 row of `docs/evidence/README.md`, adds the test reports to the entries of S7.0.3, and sets L7 to Accepted once recelilious has merged the record.

**Small stages directly under this stage.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S7.0.1 | Desktop download check | `src/Paneport.Desktop` on recelilious's Windows workstation; downloads go to a folder outside the repository, so no game file can be committed (C-0002 section 6) | recelilious signs in (Claude never handles the account name, the password or the codes), downloads a small owned title, closes the app halfway and finishes the download after reopening it. Pass: the app reports the install as complete and verified, the resumed download does not start from zero, the app manifest appears only at the end, and after a restart the app asks for sign-in again because no token was written to disk (C-0007 section 3). The desktop head does not classify the real files, because C-0007 section 5 checks real game files only on the device. The result goes into the evidence record without account data or paths. |
| S7.0.2 | Save backup notice and export | `src/Paneport.Core`, `src/Paneport.UI`, `src/Paneport.iOS` | Applies D10 to the open item of C-0004 section 4. A Core test exports a synthetic prefix user folder into a temporary folder that stands in for `Documents/` and compares the copy file by file. A headless UI test shows the notice. If an earlier stage already built the notice and the export, this stage only checks them against D10. Work beyond the notice and the export becomes a new stage. |
| S7.0.3 | Compatibility entries for the test titles | `compat/titles/` | Entries for both D6 titles (`steam-<appid>.json`, or `local-<slug>.json` for an imported copy) validate against `compat/schema/title.schema.json` with M6.4's loader, and each file name matches its `id`. `executable`, `architecture` and `ceg` come from recelilious's own checks and nothing else (C-0008 section 5). Whether the schema lets an entry set `ceg` while the other `drm` values come from the device classification is to be confirmed against M6.4's schema; if it does not, the other values come from the classification the device showed (C-0007 section 5). The `checks` job passes. |
| S7.0.4 | 32-bit Direct3D 11 path | `external/Paneport-dxmt` on `paneport/L7`, `tests/win32/`, `tests/runtime/` | Runs only if D6 picks a 32-bit Direct3D title. The i686 build of the Direct3D 11 triangle passes the same pixel check on the macOS harness as the x86-64 build (M5.2), with DXMT's 32-bit PE modules in `wine/i386-windows/` (C-0009 section 4). Whether DXMT builds for i386 is to be confirmed in this stage. If the work outgrows part of a session, it becomes a new stage and D6 picks a fixed-base title instead. If D6 picks a fixed-base title, this stage is Dropped with that reason. |

**Session log.**

_No sessions yet._

## M7.1 Sign-in, library and downloads

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L7 |
| Depends on | M2.3, M6.4 |
| Size | L |
| Branch | `feat/m7-1-library-and-downloads` |
| Decisions needed | None new. The download task identifier builds on the bundle identifier chosen in D3. |
| Design documents | `docs/design/library-and-downloads.md` (new); M2.3's Steam service document, updated where this stage extends the service |

**Goal.** A signed-in user sees the owned titles that have Windows depots and can install, update and remove them. Downloads are verified chunk by chunk, resume after any interruption, and continue in the background when the user started them (C-0007 sections 3 and 4). The library manager lives in Paneport.Core, the Steam-facing parts in Paneport.Steam, and the platform glue in Paneport.iOS behind interfaces declared in Paneport.Core (C-0007 section 2).

**Scope.**

In:

- Library sync (S7.1.1): the owned apps that have Windows depots, built from the user's licenses and Steam's app information (C-0007 section 4). Titles available only through family sharing are left out (C-0007 section 7). Title details show the effective compatibility entry, such as its rating, through M6.4's loader.
- Install state for each title: not installed, queued, downloading, paused, installed, update pending or failed.
- The free-space check (S7.1.2): a download starts only when the free space covers the install size plus a safety margin (C-0007 section 4). The design document sets the margin, for example as a fixed amount plus a share of the install size, for recelilious to review.
- The download queue (S7.1.2): start, pause, resume, cancel and order. Downloads the user did not start, such as automatic updates, run only while the app is open (C-0007 section 4). The design document decides whether downloads pause while a game session runs; pausing is the safe default under the memory limit.
- Resume state (S7.1.2), saved continuously in `downloads/` as chunks complete (C-0004 section 4, C-0007 section 4), and used to resume on the next run.
- Chunk verification and file assembly (S7.1.3): every chunk is checked against the hash its manifest records before it is written (C-0007 sections 1 and 4). Files are assembled in `downloads/` and moved into the title's install folder in `library/` once they verify. Paths that differ only in letter case share one folder, as on Windows, because APFS on iOS is case-sensitive (`precedents_and_ux_layer.md` KQ5).
- Background continuation (S7.1.4) of downloads the user starts, through `BGContinuedProcessingTask` (iOS 26 and later). It reports progress, and its expiration handler stops work and keeps the saved resume state (C-0007 section 4). Info.plist lists the task identifier under `BGTaskSchedulerPermittedIdentifiers` (C-0009 section 4). The identifier's exact form, including whether it uses a wildcard, is to be confirmed in this stage.
- App manifests (S7.1.5): `appmanifest_<appid>.acf` in the library folder, written after every file has verified, in a layout that Valve's client recognizes (C-0007 section 4). The layout of `library/`, the install folder's name and the manifest's fields are to be confirmed in this stage. No app manifest copied from a real Steam install is committed; tests use files our own code generates (C-0011 section 1).
- Updates (S7.1.6) from the current depot manifests, fetching only content that changed. A title with a pending update reports it, and M7.2's launch decision blocks its launch until the update finishes (C-0007 section 4).
- Removing an installed title: its files, its app manifest and any resume state.
- Backup exclusion: `library/` and `downloads/` are marked as excluded from backup, and marked again after files in them are replaced (C-0004 section 4).
- Sign-in pages (S7.1.7) on M2.3's sign-in service: account name and password with approval in the Steam mobile app by default, a Steam Guard code from the authenticator or from email as the fallback, and a QR code for a Steam app on another device (C-0007 section 3).
- Library, title details and downloads pages (S7.1.8), with their view models in Paneport.Core (C-0004 section 2).
- The token store and sign-out (S7.1.9). Only the refresh token is kept, in the keychain with the attributes of C-0004 section 4, and passwords are never stored. Other values that sign-in returns, such as new Steam Guard data, are not kept, because C-0007 section 3 allows only the token. The account name the service needs to sign in again is not a secret; the design document proposes keeping it in the same keychain item so it never enters backups. Sign-out deletes the token and stops the downloads. If M2.3 already built the store, this stage uses it unchanged.
- Privacy: Paneport sends no telemetry about the account or the library (C-0007 section 3), and logs and crash records never contain the password, the token or the account name (C-0004 section 4, C-0010 section 2). Any cache of the library list stays on the device in `Library/Caches/Paneport/`; anything else needs a change to C-0004 section 4 first.
- Paneport.Desktop runs the same flows, keeps the token in memory for one run only (C-0007 section 3) and downloads into a folder chosen in its settings.
- `docs/design/library-and-downloads.md`, listing the public sources it consulted (C-0001 section 6).

Out:

- Classification, the launch decision and launching (M7.2).
- The Dock host, deleting Valve's cached login state at sign-out, pausing downloads and signing off while the Dock host signs in, and telling Valve's client where the library is (M8.2; C-0007 sections 3 and 4).
- Cloud saves (C-0007 section 7, plan section 10), DLC selection, beta branches, Workshop and family sharing (C-0007 section 7).
- A background `NSURLSession` transport. C-0007 section 4 names only `BGContinuedProcessingTask`, so adding one needs a change to that section first.
- Checking an installed title's files on demand. It is proposed as a follow-up if L7 shows a need.

**Read first.**

- Contracts: C-0001 sections 6 and 7; C-0003 sections 5 to 7 (SteamKit2 is LGPL-2.1-only; DepotDownloader is GPL-2.0 and reference-only); C-0004 sections 1, 2 and 4; C-0007 sections 1 to 4 and 8; C-0008 sections 1 and 3; C-0009 sections 3 and 4; C-0010 section 2; C-0011 sections 1 to 4; C-0012 sections 1 and 4.
- Research: `steam_integration.md` (Key Questions 2 and 6); `avalonia_ui_layer.md` section 2.3, parts 3 to 5 (SteamKit2's sign-in, library, depot and CDN calls, and background execution on iOS); `precedents_and_ux_layer.md` KQ5 (storage, backup exclusion and case sensitivity); `roadmap-2026-10.md` (phase 3). Parts of the research describe DepotDownloader's behavior. Under C-0007 section 2 they may inform observable behavior only, never the structure of our code, and nobody working on this stage opens DepotDownloader's source.
- Design documents: M2.3's Steam service document (its sign-in flow, the token's platform type and the interpreter setting), M6.4's compatibility database document, and the shell documents of M0.3 and M1.3.

**Deliverables.**

- `src/Paneport.Core/`: the library manager, the download engine, the view models, and the interfaces that Paneport.Steam and the app heads implement (library source, depot content source, token store, background task, free space).
- `src/Paneport.Steam/`: implementations of the library and content interfaces on M2.3's SteamKit2 service.
- `src/Paneport.UI/`: the sign-in, library, title details and downloads pages.
- `src/Paneport.iOS/`: the background download task, the free-space provider, the keychain token store if M2.3 did not build it, and the `BGTaskSchedulerPermittedIdentifiers` entry in Info.plist.
- `src/Paneport.Desktop/`: the in-memory token store and the desktop download folder setting.
- `tests/Paneport.Core.Tests/`: library manager tests with a fake Steam service and a fake content server. The fakes serve synthetic licenses, apps, manifests and chunks that our own code generates, and can stop after a set number of chunks, corrupt a chunk, refuse a manifest or expire a token.
- `tests/Paneport.Steam.Tests/`: tests of the mapping from SteamKit2's results to the Core interfaces and of the retry and refresh rules, with fakes at our own seams. The real transport is checked by the desktop download.
- `tests/Paneport.UI.Tests/`: headless tests of the new pages.
- `docs/design/library-and-downloads.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S7.1.1 | Library sync | `src/Paneport.Core/`, `src/Paneport.Steam/` | Unit tests with the fake Steam service: a synthetic set of licenses and apps yields exactly the apps that have Windows depots, and a license change updates the list. |
| S7.1.2 | Download queue, free-space check and resume state | `src/Paneport.Core/` | Unit tests: queue order, pause, resume and cancel; the resume state on disk matches the last completed chunk; a new engine instance resumes from it; a fake free-space value below the install size plus the margin refuses the download. |
| S7.1.3 | Chunk writer and file assembly | `src/Paneport.Core/` | Unit tests in a temporary folder: chunks land at their offsets, a corrupted chunk is rejected and never written, verified files move into the install folder, and paths that differ only in letter case share one folder. |
| S7.1.4 | Background task integration | `src/Paneport.iOS/`, interface in `src/Paneport.Core/` | Core unit tests with a fake background task: begin, progress and expiration, with work stopped and the resume state kept. The behavior on the iPhone is a device check that L7 verifies. |
| S7.1.5 | App manifest writer | `src/Paneport.Core/` | Unit tests: the manifest appears only after every file has verified, and it round-trips through our own reader with the fields the design document lists. |
| S7.1.6 | Updates | `src/Paneport.Core/` | Unit tests with a second synthetic manifest: only changed chunks are fetched, the title stays update pending until done, and automatic checks never begin a background task. |
| S7.1.7 | Sign-in UI | `src/Paneport.UI/`, view models in `src/Paneport.Core/` | Headless UI tests with a fake sign-in service: waiting for approval, the code fallback, the QR code and the error messages. No password remains in a view model after sign-in. |
| S7.1.8 | Library and downloads UI | `src/Paneport.UI/`, view models in `src/Paneport.Core/` | Headless UI tests over fake data: the library list, every install state on the title details page, and the actions of the downloads page. |
| S7.1.9 | Token store and sign-out | `src/Paneport.iOS/`, `src/Paneport.Desktop/`, interface in `src/Paneport.Core/` | Core tests with a fake store: only the refresh token, and the account name if the design keeps it there, reach the store, and sign-out removes them. A test of the desktop store finds no file written in a temporary profile folder. |

**Verification.**

Everything here runs without recelilious's iPhone, except the item marked as a device check.

1. Run `dotnet test tests/Paneport.Core.Tests`, `dotnet test tests/Paneport.Steam.Tests` and `dotnet test tests/Paneport.UI.Tests`. Pass: no failures and no skipped tests in the new groups, and the groups include these scenarios:
   - Resume: the fake content server stops after a set number of chunks. A new engine instance fetches only the chunks not yet recorded, and the assembled files match the synthetic manifest.
   - Interrupted write: a failure between writing a chunk and recording it makes only that chunk be fetched again.
   - Corrupted chunk: a chunk that arrives with wrong bytes once is rejected, fetched again, and the install completes. A chunk that is always wrong stops the download in the failed state with a message, and nothing reaches `library/`.
   - No space: free space below the install size plus the margin keeps the download from starting, shows both numbers and creates no file.
   - App manifest: stopping at any chunk leaves no app manifest, and a completed install has one.
   - Update: only changed chunks are fetched, and the title reports a pending update until it is done.
   - Background expiration: work stops within the time the design document sets, measured on a fake clock, and the resume state matches the last completed chunk.
   - Privacy: a capturing fake logger sees no password, token or account name in any test.
2. The `dotnet` job (C-0011 section 4) passes: locked restore, build, tests, the format check (`dotnet format --verify-no-changes`) and the trimmed test app, with no new trimming or AOT warnings from our code. Suppressions for SteamKit2 or protobuf-net follow C-0010 section 2.
3. The `ios` job builds the app, and the built Info.plist lists the download task identifier under `BGTaskSchedulerPermittedIdentifiers`. The pull request shows how this was read; the method is to be confirmed in this stage.
4. The `checks` job passes. Synthetic test data contains nothing that looks like a Steam ID, token, depot key or manifest request code, so the personal-data check stays green (C-0011 section 2). Claude runs the personal-data, secrets and format checks locally before every push (C-0011 section 2).
5. Desktop download, manual, by recelilious on Windows before merging: the steps of S7.0.1 on this branch. The result goes into the pull request without account data or local paths.
6. Device check, optional before merging: start a download on the iPhone, go to the Home Screen and come back. Until L7 verifies it, the pull request marks background behavior "not yet verified on device" (C-0012 section 1 item 9).

Scripts this stage creates: the fakes and the synthetic data builders in the test projects. It adds no CI job.

**Done when.**

- The verification above passes, and the desktop download is recorded in the pull request.
- `docs/design/library-and-downloads.md` is merged, and M2.3's design document reflects any change to the Steam service.
- recelilious has merged the branch, and M7.1's status is Done with its session log updated.

**Risks and fallbacks.**

- SteamKit2 under Mono with full ahead-of-time compilation: this stage relies on the interpreter setting the phase 0 gate recorded (C-0009 section 3, C-0012 section 3). New SteamKit2 calls are checked on the device in L7. If that gate failed and recelilious adopted our own protocol client instead (C-0012 section 3), this brief applies to that client through the same Core interfaces.
- iOS can expire or cancel the background task, and closing the app in the app switcher always cancels it (C-0007 section 4). The continuous resume state is the answer. A background `NSURLSession` fallback would need a change to C-0007 section 4, and the research says it drains only low hundreds of chunks per wake (`avalonia_ui_layer.md` section 2.3).
- Manifest request codes and CDN tokens can expire, and the research found no documented validity window. The content source asks for them again when a request is refused, and the tests cover this with the fake.
- The connection to Steam drops while the app is suspended, so the service signs in again with the stored token when the app returns. Approval in the Steam mobile app also puts Paneport in the background during sign-in, so the wait for approval must pick up again when Paneport returns.
- The research does not document the app manifest's fields or the folder layout Valve's client expects. A wrong guess shows up when Valve's client reads them in M8.2, so the writer is kept separate and easy to correct there.
- Large titles: the margin and an honest size estimate keep the device from filling up, and D6 picks a small title for the first run.
- New dependencies, such as a QR code library, need registration under C-0003 first, which is a contract change. Reuse whatever M2.3 used for the phase 0 gate.
- Valve may treat Paneport as a new kind of client. C-0007 section 8 records the accepted legal tier.

**Hands over to.**

- M7.2: install states, install folders, the pending-update flag, and the "install finished" and "update finished" events that start classification.
- M8.2: the token store for `pp_steam_set_token` (C-0005 section 2), the sign-out hook where Valve's cached state gets deleted, the hooks that pause downloads and sign off before the Dock host signs in, and the app manifests in the library folder (C-0007 sections 3 and 4).
- L7: the pages and flows that the acceptance steps use, and the desktop download procedure.
- M8.4: downloads of large titles.

**Notes from earlier stages.**

- M0.3 ([L0 brief](L0-foundation.md), session log of 2026-10-30): `docs/design/app-shell.md` section 10 holds open questions for this stage: the token store's layout, whether leaving a page cancels its load, and a virtualizing Library whose download progress raises change notifications.

**Session log.**

_No sessions yet._

## M7.2 Classification and direct launch of Steam titles

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L7 |
| Depends on | M7.1, M6.5 |
| Size | M |
| Branch | `feat/m7-2-steam-classification` |
| Decisions needed | None new. The relaunch prompt follows D9, settled for M6.5. |
| Design documents | `docs/design/steam-classification.md` (new), which refers to M6.5's classifier document for the PE reader and the SteamStub detector |

**Goal.** Before a Steam title first starts, Paneport classifies it by reading its install folder without changing anything, stores the result in the device's compatibility layer, and decides its launch path (C-0007 section 5, C-0008 sections 1 and 5). A title on the Direct path starts with the program its compatibility entry names. A title that needs the Dock path says so and waits for L8.

**Scope.**

In:

- The install-folder scan (S7.2.1): whether `steam_api.dll` or `steam_api64.dll` is present anywhere in the folder, in any letter case, or is imported by any program file in it (C-0007 section 5). The design document defines which files count as program files and which import tables count, at least the normal and the delay-load imports. A program file that cannot be parsed leaves the Steam API marker uncertain, so the title takes the Dock path and the reason shows.
- SteamStub (S7.2.2): M6.5's detector, run on the main program that the effective entry names. When no entry names one, the marker stays unknown and the title takes the Dock path.
- `steam_appid.txt`: recorded when the game ships one, never created (C-0007 section 5).
- CEG: it cannot be read from files and is set by hand in the reviewed entry (C-0007 section 5). The computed classification leaves it `unknown`.
- When the scan runs: after an install finishes, after every update, and before a launch when no current result exists.
- Storing the result (S7.2.3) in the device layer through M6.4's store, one file per title in `Library/Application Support/Paneport/compat/` (C-0008 section 1). A `drm` value set in the bundled entry wins over a computed one, and the device layer never turns `launch` into `direct` for a title whose `drm` requires the Dock path (C-0008 section 1).
- The launch decision (S7.2.3): Direct only when `steamApi` and `steamStub` are false and `ceg` is `no`; otherwise Dock (C-0008 section 5). A title with a pending update does not start (C-0007 section 4). The decision carries its reasons for the UI.
- Direct launch (S7.2.4): the effective entry's `executable.path`, `workingDirectory` and `arguments` (C-0008 section 2), handed to M6.5's session flow. How the library folder appears inside the Wine session is to be confirmed in this stage, building on M6.5's mapping for imported titles.
- Dock-path titles show "needs the Dock path, not available yet" and offer no Launch button until L8. The title details page shows the classification and its reasons.
- Guarantees, each covered by a test: no `steam_appid.txt` is created; no `SteamAppId`, `SteamGameId`, `SteamAppPath` or other variable that `steam_api` or a DRM wrapper reads is set; no `steam_api` library is replaced; and no file in the install folder is written or patched (C-0007 section 5, C-0008 section 5).
- On Paneport.Desktop the scan does not run on real downloads, because C-0007 section 5 checks real game files only on the device. The desktop shows such titles as not classified.

Out:

- The Dock path itself (M8.2).
- Classification of imported titles, which M6.5 already covers.
- New SteamStub logic. A fix to M6.5's detector updates M6.5's design document, and the source of DRM-removal tools such as Steamless is never read (C-0007 section 5).
- The CI compatibility check (M8.3).

**Read first.**

- Contracts: C-0001 sections 5 to 7; C-0004 section 1; C-0005 section 2 (the session configuration); C-0007 sections 1, 5 and 8; C-0008 sections 1, 2 and 5; C-0011 section 1; C-0012 section 1.
- Research: `steam_launch_single_process.md` (Key Question 6: detection signals that only read files, and the warning that the publicly reported SteamStub signature is unverified); `steam_integration.md` (Key Question 3: how `steam_api` finds the client and what `steam_appid.txt` changes).
- Design documents: M6.5's classifier and session flow documents, M6.4's compatibility database document, and `docs/design/library-and-downloads.md` from M7.1.

**Deliverables.**

- `src/Paneport.Core/`: the install-folder scanner, the launch decision and the builder of Direct launch requests.
- `src/Paneport.UI/`: the classification and launch states on the title details page.
- `tests/Paneport.Core.Tests/` and `tests/Paneport.UI.Tests/`: the tests below. Synthetic folders and PE files are generated at test time by our own code, extending M6.5's generator (C-0011 section 1). Nothing generated is committed.
- `docs/design/steam-classification.md`.

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S7.2.1 | Install-folder scanner | `src/Paneport.Core/` | Unit tests on synthetic folders: a `steam_api64.dll` in a nested folder, a program that imports `STEAM_API.DLL` through its normal or its delay-load imports, a shipped `steam_appid.txt`, an unparseable program file, and a clean folder each give the expected markers. A snapshot of names, sizes, hashes and timestamps is the same before and after the scan. |
| S7.2.2 | SteamStub detector reuse | `src/Paneport.Core/` | Unit tests with M6.5's synthetic wrapped and unwrapped programs: the marker follows the main program the entry names, and stays unknown without one. |
| S7.2.3 | Launch decision | `src/Paneport.Core/` | Table-driven unit tests over every combination of computed markers, the reviewed `ceg` value, entry presence, device-layer overrides and a pending update, checked against C-0008 sections 1 and 5. |
| S7.2.4 | Direct launch wiring | `src/Paneport.Core/`, `src/Paneport.UI/` | Unit tests with a fake host: the launch request carries the entry's program, working directory and arguments, its environment holds no Steam variable, and no file appears in the install folder. A headless UI test shows a Dock-path title without a Launch button. |

**Verification.**

1. Run `dotnet test tests/Paneport.Core.Tests` and `dotnet test tests/Paneport.UI.Tests`. Pass: no failures and no skipped tests in the new groups, and the groups include at least:
   - every marker case of S7.2.1, and the read-only snapshot test;
   - a clean folder with a reviewed `ceg` of `no` gives Direct, and the same folder with no entry, or with `ceg` `unknown`, gives Dock;
   - a bundled `drm` value that overrides a computed one, and a device-layer override that tries to make a Dock title Direct and is refused (C-0008 section 1);
   - a pending update that refuses the launch;
   - classification that runs again after an update adds `steam_api.dll`, which moves the title to Dock;
   - a Direct launch request with the entry's program and arguments and none of the forbidden environment variables (C-0008 section 5).
2. The `dotnet`, `ios` and `checks` jobs pass on the last commit (C-0011 section 3).

All of it runs without the iPhone. Real titles are classified only on the device, in L7's acceptance steps (C-0007 section 5). Scripts this stage creates: the synthetic folder builder and the additions to M6.5's PE generator.

**Done when.**

- The verification above passes.
- `docs/design/steam-classification.md` is merged.
- recelilious has merged the branch, and M7.2's status is Done with its session log updated.

**Risks and fallbacks.**

- Direct launch needs a reviewed entry with `ceg` set to `no`, because CEG cannot be read from files (C-0007 section 5). Until M8.2, a Steam title without such an entry cannot start at all. Widening this needs changes to C-0007 section 5 and C-0008 section 5, so this stage does not try.
- M6.5's SteamStub detector is tested only with synthetic files, and the research says the publicly reported signature is unverified. Any misreading errs toward the Dock path or shows up in L7's device run, and a reviewed `drm` value then wins over the computed one.
- Large install folders: the scan reads only headers and import tables. L7 notes its time on the device.
- A game may need a working directory other than its program's folder. The entry's `workingDirectory` covers it.

**Hands over to.**

- L7: Direct launch for the acceptance steps.
- M8.2: Dock-path titles and their classification, including `ceg` for titles whose executables Valve's client prepares (C-0007 section 5), and the launch entry point that M8.2 connects to the Dock host.
- M8.3: the same launch rules in the CI compatibility check, since tools may depend on Paneport.Core (C-0004 section 2).

**Session log.**

_No sessions yet._

## M7.3 32-bit and fixed-base programs

| Field | Value |
|---|---|
| Level | Medium |
| Parent | L7 |
| Depends on | M4.4; D11 |
| Size | L |
| Branch | `feat/m7-3-wow64-and-fixed-base`; fork changes on `paneport/M7.3` in `Paneport-FEX` and `Paneport-wine`; S7.3.5 on `contract/c-0004-32-bit-guests` |
| Decisions needed | D11 for 32-bit guests: FEX's WoW64 module or Box64, recorded through a change to C-0004 (plan section 8). |
| Design documents | `docs/design/wow64.md` (new); M4.3's design document where it plans the address space |

**Goal.** 32-bit x86 programs and fixed-base x86-64 programs load and run in the single-process runtime, first on the macOS harness and then on iOS. Each 32-bit process gets its own 4 GB-aligned host window, and a fixed-base program is mapped at its preferred base when that range is free.

**Scope.**

In:

- S7.3.5 first: the C-0004 change that records D11. The research favors FEX's WoW64 module. Box64 has no Darwin port (`x86_to_arm_translation.md` KQ2 and KQ5), the FEX fork already owns the WoW64 module (C-0004 section 2), and the iOS precedents run 32-bit guests in FEX with a guest window (`process_and_memory_model.md` Q4). Box64 stays the fallback that C-0001 section 4 names. C-0004 section 2 says this choice is made after the phase 1 gate. If L5 has no verdict yet when this stage starts, the session asks recelilious whether to wait or to record the choice now through the same change. S7.3.1 and S7.3.4 do not depend on the choice and can start meanwhile.
- The WoW64 module (S7.3.2): FEX built for the `aarch64-w64-mingw32` target, which emulates x86 (`wine_on_ios.md` KQ2), for the macOS harness and for iOS, with the same host hooks as the ARM64EC module. It allocates executable memory only from the JIT pool (C-0006 section 3 item 7) and registers its fault handler through the bridge, never with `sigaction` or `signal` (C-0005 sections 3 and 4).
- Wine's i386 PE side, built as C-0009 section 3 stage 2 lists and shipped in `wine/i386-windows/` (C-0009 section 4).
- The 32-bit address window (S7.3.3): one 4 GB-aligned host range per 32-bit pseudo-process, with guest address `a` at host address `B + a`. The research describes FEX's 32-bit mode pinning one register to `B`, and Wine's WoW64 thunks adding or removing `B` (`process_and_memory_model.md` Q4). The window is registered as a guest reservation through the bridge, so faults inside it take the guest path (C-0005 sections 3 and 4). Nothing inside it is mapped executable. When the address map has no room for a window, the session stops with a clear message.
- The `x86` value of the entry's `architecture` field (C-0008 section 2) selects the 32-bit path in the session configuration (C-0005 section 2).
- Fixed-base mapping (S7.3.4): a main image without relocations is mapped at its preferred base when that range is free, and the load fails with a clear message when it is not. The default 64-bit image base lies above XNU's 4 GB page zero (`process_and_memory_model.md` Q3), but early mappings could still take it, such as the JIT pool regions that the debugger places (C-0006 section 1). How the range is kept free until the loader needs it is to be confirmed in this stage. A host function added for it raises the ABI minor version (C-0005 section 1). A 32-bit fixed-base image loads at its base inside its own window.
- The session log states the main image's base and, for a 32-bit process, that its window was set up, so L7 can show either case.
- Test programs (S7.3.1) in `tests/win32/`, harness cases in `tests/runtime/`, and the template prefix and bundle pieces (S7.3.6).
- `docs/design/wow64.md`, listing the public sources it consulted (C-0001 section 6).

Out:

- Several 32-bit pseudo-processes at once, 64-bit programs that start 32-bit ones, and two live fixed-base images that need the same base (M8.1).
- 32-bit titles through the Dock host (C-0007 section 6).
- Graphics for 32-bit games (S7.0.4 in L7).
- Relocating images that cannot get their preferred base. The research rates rebuilt relocations as high-risk.
- 16-bit programs, and Wine Mono for 32-bit .NET games (plan section 10).
- Box64, unless D11 picks it. If it does, the session stops after S7.3.5 and proposes a revised brief, because S7.3.2, S7.3.3 and S7.3.6 change completely.

**Read first.**

- Contracts: C-0001 sections 4 and 6; C-0003 sections 1, 4, 6, 7 and 10 (our FEX changes stay in our fork and are never proposed upstream); C-0004 sections 1 to 3; C-0005 sections 1 to 4; C-0006 sections 1, 3 and 4; C-0008 section 2; C-0009 sections 2 to 4; C-0010 sections 1, 3 and 4; C-0011 sections 1 and 4; C-0012 section 2.
- Research: `process_and_memory_model.md` Q3 (address-space conflicts, default image bases, fixed-base examples) and Q4 (guest windows); `x86_to_arm_translation.md` KQ1 to KQ3 and KQ5; `wine_on_ios.md` KQ2 (the new WoW64, the emulator registry keys and the toolchain); `ios_platform_constraints.md` sections 4 and 5 (address map sizes and extended virtual addressing); the roadmap's paragraph on 32-bit guests. The research quotes Madeira's documentation and code comments. Use them only to learn what is feasible; implementations follow upstream Wine and FEX documentation, the documentation of FEX-darwin (a registered MIT component, C-0003 section 6) and our own design document (C-0001 section 6 item 3).
- Design documents: those of M4.2, M4.3 and M4.4, and M3.1's JIT provider document.

**Deliverables.**

- `external/Paneport-FEX` on `paneport/M7.3`: the WoW64 module for Darwin and iOS, with the guest window addressing. The stage branch moves the submodule pointer.
- `external/Paneport-wine` on `paneport/M7.3`: the i386 PE side in the build, window allocation and address conversion at the WoW64 boundary, fixed-base mapping, and registration of windows as guest reservations. iOS-specific code goes in separate files where upstream allows (C-0010 section 4).
- `src/native/host/`: keeping the preferred base free, if the design needs the host for that.
- `eng/`: build steps for the WoW64 target and the i386 PE side, in Bash where they run only on macOS (C-0010 section 1).
- `tests/win32/`: i686 builds of the existing programs and the new programs of S7.3.1.
- `tests/runtime/`: harness cases for them.
- The template prefix build (plan section 7.4) and M4.4's bundle check, both extended by S7.3.6.
- `docs/design/wow64.md`.
- Outside the stage branch: the C-0004 change on `contract/c-0004-32-bit-guests` (plan section 3.3).

**Small stages.**

| ID | Module | Location | Independent check |
|---|---|---|---|
| S7.3.1 | i686 and fixed-base test programs | `tests/win32/` | The existing programs gain i686 builds with the pinned llvm-mingw (C-0009 section 2). A new loader-report program, built for i686, for x86-64 and as a fixed-base x86-64 variant without relocations (linker options to be confirmed in this stage), reads its own headers at run time and prints its machine type, its image base, whether it has a relocation directory and whether it loaded at its preferred base. An i686 program also uses addresses above 2 GB (the PE flag that allows this is to be confirmed). On Windows 11 the programs print the expected lines natively. |
| S7.3.2 | WoW64 module build | `external/Paneport-FEX`, `eng/` | The module builds for the macOS harness and for iOS. Its exports match what Wine's WoW64 layer loads, to be confirmed in this stage from Wine's source. A harness case shows that executable memory outside the JIT pool is refused. |
| S7.3.3 | 32-bit address window | `external/Paneport-wine`, `external/Paneport-FEX`, `src/native/host/` | Harness cases: an i686 program writes and reads memory across its address range; the i686 structured exception handling program catches an access violation it causes in its own memory, which shows the fault reached the guest path; with no room for a window, the session stops with a clear message. How the harness limits the address map is to be confirmed in this stage. |
| S7.3.4 | Fixed-base mapping | `external/Paneport-wine`, `src/native/host/` | Harness cases: the fixed-base program reports that it loaded at its preferred base, and with that range taken on purpose the load fails with a clear message and no crash. |
| S7.3.5 | C-0004 change proposal for D11 | `docs/contracts/C-0004-architecture-and-layout.md` on `contract/c-0004-32-bit-guests` | Review checklist: the 32-bit open item of section 2 is replaced by the decision, the change log has a new row, the change is one commit of type `contract`, and it is pushed only after recelilious has reviewed it (C-0012 section 2). |
| S7.3.6 | 32-bit pieces in the template prefix and the app bundle | the template prefix build (plan section 7.4), `eng/`, M4.4's bundle check | The template prefix names the WoW64 module as the x86 emulator, which Wine reads from `HKLM\Software\Microsoft\Wow64\x86` (`wine_on_ios.md` KQ2), and holds the 32-bit files programs need (to be confirmed in this stage). The app from the `ios` job has `wine/i386-windows/` with Wine's i386 modules and the WoW64 module (C-0009 section 4). |

**Verification.**

1. Fork builds: the Wine PE side for ARM64EC, AArch64 and i386, and the FEX WoW64 module for macOS and iOS, build with the pinned toolchain from a clean checkout.
2. The macOS harness, run from `tests/runtime/` as M4.2 documents it, in single-process mode (M4.3). Pass: every i686 case and every fixed-base case prints its expected lines and exits with code 0; every x86-64 case that passed before still passes; no `fork`, `exec` or `posix_spawn` trap fires (plan section 7.1); and the dispatcher check after `pp_session_start` passes (C-0005 section 4). This runs in the `runtime` CI job if C-0011 has it by then (plan section 8); otherwise it runs on the local Mac (D1), with the output in the pull request.
3. The `native` job passes, with CTest under the `asan-ubsan` and `tsan` presets for any host change (C-0011 section 4).
4. The `ios` job builds the app, and M4.4's bundle check, extended by S7.3.6, passes.
5. Windows check, local: the S7.3.1 programs, taken from the harness build, run natively on the Windows 11 workstation and print the same lines as on the harness.
6. Device check, optional: an i686 console program runs in the app's console on the iPhone. Note the signing route, because a free team may lack extended virtual addressing and a smaller address map may leave no room for a window (`ios_platform_constraints.md` sections 4 and 5). Otherwise L7 verifies the device side.

Scripts this stage creates: the i686 build steps and the new programs in `tests/win32/`, the harness cases in `tests/runtime/`, the build steps in `eng/`, and the additions to M4.4's bundle check.

**Done when.**

- recelilious has accepted the C-0004 change that records D11.
- The verification above passes. The fork branches are pushed, and the stage branch points the submodules at them.
- `docs/design/wow64.md` is merged, recelilious has merged the stage branch, and M7.3's status is Done with its session log updated.

**Risks and fallbacks.**

- Address space on iOS: a window needs a free, 4 GB-aligned range. The roadmap reports a 63 GB map for free installs on iOS 26, and `process_and_memory_model.md` Q4 cites a 32-bit title that got no window on an iPhone 17 with a free Apple ID and extended virtual addressing off. The design document plans where windows go. The fallback is to require the paid account route of D2 for 32-bit titles, with `extended-virtual-addressing` declared through `PaneportExtendedVirtualAddressing` (C-0009 section 4).
- FEX's 32-bit mode on Darwin and iOS exists only in forks, and upstream FEX has no macOS plans (`x86_to_arm_translation.md` KQ2). Our changes stay in our fork (C-0003 section 10).
- Thread control for 32-bit threads goes through Mach thread calls (C-0005 section 4). The research reports that suspending a thread does not stop it at once on iOS, so thread context cases belong in the harness.
- Fixed-base images larger than the space kept free fail to load. The research cites a 441 MB image that failed to load and a 183 MB image that loaded only after the kept space was enlarged (`process_and_memory_model.md` Q3). The design document sets the size and the error message.
- If FEX's module cannot be made to work, Box64 is the fallback (C-0001 section 4). It needs a new fork and a change to C-0004 first.

**Hands over to.**

- L7: the fixed-base or 32-bit acceptance step, and the base for S7.0.4.
- M8.1: one window per 32-bit pseudo-process when several run at once, and fixed-base conflicts between processes.
- M8.4: large fixed-base titles.
- A later stage that brings 32-bit titles using the Steam API to the Dock host, which first needs a design document and a change to C-0007 (C-0007 section 6).

**Session log.**

_No sessions yet._
