# C-0007 Steam Integration

| Field | Value |
|---|---|
| Status | Draft |
| Version | 0.1 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines how Paneport signs in to Steam, shows the user's library, downloads and updates games and launches them, and where the compliance boundary lies. The research behind it is in [steam_integration.md](../research/steam_integration.md) and [steam_launch_single_process.md](../research/steam_launch_single_process.md).

## 1. Principles

1. Valve's own code performs ownership checks and DRM checks. Paneport never bypasses, emulates or modifies any DRM (C-0001 section 7).
2. The user's credentials go only to Valve's servers.
3. Valve's binaries come only from Valve's servers, on the user's device. They are never committed or bundled (C-0003 section 8).
4. Paneport writes game files only with content from Valve's depots, verified against the manifest, and never patches or alters them otherwise.

## 2. Components

| Component | Location | Role |
|---|---|---|
| Steam service | `src/Paneport.Steam` | Sign-in, licenses, app information, depot downloads and updates, built on SteamKit2 (LGPL-2.1-only). |
| Library manager | `src/Paneport.Core` | Install state, updates, disk space, classification and launch decisions. |
| Platform glue | `src/Paneport.iOS` | The keychain token store and background download tasks, behind interfaces declared in Paneport.Core. |
| Dock host | `src/guest/dockhost` | A headless host that loads Valve's genuine `steamclient64.dll` inside the Wine session, so games that need a running Steam client can start. |

**How the download code is written.** It is written against SteamKit2's public API and documentation and our own design document. DepotDownloader is GPL-2.0: nobody writing Paneport.Steam opens its source, and none of its code is copied, translated or used as a template. The published research summarizes DepotDownloader's behavior; that summary may be used only for observable behavior, not as a code outline.

**Fallbacks.** If the "SteamKit2 on the device" gate fails (C-0012 section 3), the proposed fallback is a protocol client of our own. If the "signal dispatcher" gate fails, the proposed fallback changes how Steam runs in the app. In both cases recelilious decides, and this contract and C-0004 change first.

## 3. Sign-in

- **Default:** account name and password, confirmed by approving the sign-in in the Steam mobile app, which works on the same phone. Entering a Steam Guard code from the authenticator or from email is the fallback.
- **QR code** sign-in is offered for users whose Steam mobile app runs on another device, since a phone cannot scan its own screen.
- Sign-in requests a refresh token for the platform type that Valve's client accepts, so the Dock host can use the same token.
- The refresh token is the only secret kept, in the keychain (C-0004 section 4). Passwords are never stored.
- The Dock host receives the token from the native host in memory, through `pp_steam_set_token` and the Wine bridge (C-0005). Paneport's own code, including the Dock host, never writes the token to the Wine prefix or any other file.
- Valve's client may cache its own login state in its folder inside the prefix. Paneport deletes that state at every app launch before any session starts, when a Dock session ends, and when the user signs out. The Dock host design document lists those files, and the phase 4 gate confirms the list (C-0012).
- That cache is a sign-in token outside the keychain, so it is a bounded exception to C-0001 section 7 item 3. recelilious decides on it when accepting this contract; if it is accepted, C-0001 gets a matching change in its own contract commit.
- Signing out deletes the token from the keychain and the cached state above.
- Paneport itself sends no telemetry about the user's account or library anywhere. What SteamKit2 and Valve's client send to Valve while signing in and running games is outside Paneport's control.
- Paneport.Desktop never stores a Steam token on disk; a token obtained on Windows lives in memory for that run only.

## 4. Library, downloads and updates

- The library lists owned apps that have Windows depots, built from the user's licenses and Steam's app information.
- Downloads fetch depot manifests and content chunks from Valve's content servers, and check every chunk against the hash in its manifest.
- Downloads resume after interruption. Before a download starts, the free space must cover the install size plus a safety margin.
- A download the user starts continues in the background through `BGContinuedProcessingTask` (iOS 26 and later) and reports its progress. iOS may queue or expire that task, and the user can cancel it from its Live Activity, which runs the expiration handler. Closing the app in the app switcher always cancels it without notifying the app. The download therefore saves its resume state continuously as chunks complete, and resumes from that state the next time the app runs. Downloads the user did not start, such as automatic updates, run only while the app is open.
- Updates come from the current depot manifests. A title with a pending update launches only after the update has finished. The Dock host never downloads game content.
- Installed games live in the library folder (C-0004 section 4), with Steam app manifests that Valve's client recognizes.
- Before the Dock host signs in, Paneport.Steam pauses its downloads and signs off, and it reconnects when the session ends. Whether both can stay signed in at once is settled by a device test (C-0012) and then by a change to this contract.

## 5. Classification and launch

Before the first launch, the library manager classifies the title by reading files, never changing anything:

- **Steam API:** whether `steam_api.dll` or `steam_api64.dll` is present anywhere in the install folder, or is imported by any program file in it. Engines such as Unity and Unreal load it at run time, so the main program's import table alone is not enough.
- **SteamStub:** whether the main program carries a SteamStub wrapper.
- **`steam_appid.txt`:** whether the game ships one. It is recorded, never created.
- **CEG:** cannot be detected by reading files. It is set by hand in the title's compatibility entry, and CEG support is verified title by title.

Rules for the classifier:

- It is written from public PE format documentation. The source code of DRM-removal tools such as Steamless is never read.
- Its tests use only synthetic files generated by our own code (C-0011 section 1). Real game or Valve files are checked only on the device, and the results go into evidence records, never into git.
- The result is stored in the device's compatibility layer (C-0008 section 1). A value set in the reviewed repository entry always wins over the computed one.

| Path | Used for | How |
|---|---|---|
| Direct | Games with no Steam API, no SteamStub and no CEG | The program named in the compatibility entry starts directly in the Wine session. Titles not obtained through Steam can use only this path, so the app refuses to launch one that shows any Steam marker. |
| Dock | Every other game, including any whose classification is uncertain | The Dock host starts first, loads Valve's `steamclient64.dll`, signs in with the refresh token and checks the license. For CEG titles it asks Valve's client to prepare the user's executables. It then calls Valve's own launch function, which starts the title's default launch option. The game's own `steam_api` connects to it as on a PC. |

Never allowed: placing a `steam_appid.txt` file or setting environment variables to skip a launch check, replacing any `steam_api` library, removing SteamStub or CEG, or patching any program file.

## 6. Dock host rules

- Valve's client packages are downloaded on the device from Valve's official client update servers.
- Every supported client build is pinned by its SHA-256 hash and size in `src/guest/dockhost/client-pins.json`, which ships in the app. The app never downloads pins. Adding a build is a reviewed change with an evidence record of a Dock launch on that build (C-0012).
- On an unknown build the Dock host refuses to run and says why. There is no fallback.
- The interface definitions the Dock host uses are written by us. The design document records, for each client build, every interface slot and signature and how it was established. It never contains disassembly or decompiled code (C-0003 section 5).
- Nobody who writes the Dock host or its design document reads the source code of madeira-dock, Madeira or their forks, SteamRE/open-steamworks, or the Steamworks SDK headers. Only their READMEs and public documentation may be read (C-0001 section 6).
- The Dock host never loads `steam.exe` or the Steam web helper.
- 32-bit titles that use the Steam API are not supported through the Dock host until a design document and a change to this contract cover them.

## 7. Later work

- On the Dock path, cloud saves, achievements and multiplayer are handled by Valve's client and are verified title by title. Whether Direct-path games get cloud saves through SteamKit2 is decided by a later change to this contract.
- The Steam overlay, Steam Workshop and family sharing are not supported at first.
- Games that need anti-cheat stay out of scope (C-0001 section 5).

## 8. Legal position

recelilious accepts the first two tiers below by accepting this contract. This section records the project's decision; it is not legal advice.

| Tier | What | Status | Residual risk |
|---|---|---|---|
| 1 | Valve's genuine client code, driven by the Dock host | Accepted | Steam Subscriber Agreement section 2.G forbids reverse engineering and may cover working out the client's interface layout; whether it does is untested. Section 2.G(ii) on adding components and section 4.B on unauthorized third-party software may apply to the Dock host. Valve may terminate the account. Valve does not support the platform. |
| 2 | A protocol client (SteamKit2) for sign-in and downloads | Accepted | Steam Subscriber Agreement sections 2.G(ii), 4.B and 4.C on their face. The research found no legal action against such clients up to 2026-10-27. |
| 3 | steam_api emulation, SteamStub or CEG removal, program patching | Forbidden | Circumvention under DMCA section 1201. |

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
