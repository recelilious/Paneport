# C-0001 Project Charter

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

## 1. Goals

1. Run Windows x86 and x86-64 programs natively on iPhone.
2. Support the user's own Steam library: sign-in, browsing, installing and updating.
3. Launch Windows games through Steam, so that each game's own Steam API and Steam DRM work as intended, backed by Valve's official components.

## 2. Target devices and systems

| Item | Scope |
|---|---|
| Devices | iPhone 17 Pro and 17 Pro Max (A19 Pro); iPhone 18 Pro and 18 Pro Max (A20 Pro) |
| Systems | iOS 26.x and iOS 27.x |

Other devices and system versions are neither guaranteed nor deliberately blocked.

## 3. Accepted premises

- The user enables Developer Mode.
- The user signs the app with their own Apple ID and sideloads it.
- JIT is obtained by attaching a debugger.
- The app will never be published on the App Store.
- iOS does not let apps create child processes, so every Windows process must run inside a single iOS process.

## 4. Technical direction

These directions guide the early research. The roadmap and later contracts set the final design.

| Layer | Direction |
|---|---|
| UI shell | Avalonia (.NET), chosen by recelilious |
| Windows compatibility layer | ARM64 build of upstream Wine |
| CPU translation | Upstream FEX-Emu, with Box64 as the fallback |
| Graphics | Direct3D 9 to 11 translated directly to Metal; Direct3D 12 evaluated separately |
| Steam | Decided by C-0007 |

## 5. Non-goals

- Publishing on the App Store.
- Supporting games that depend on anti-cheat systems.
- Bypassing, removing or emulating any DRM.
- Distributing game files, or binaries from Microsoft, Valve or game publishers.

## 6. Originality rules

recelilious requires that no code is copied from existing products, while existing technology stacks may be used and integrated. In practice:

1. **May integrate:** upstream libraries and runtimes such as Wine, FEX-Emu, Box64, DXMT, MoltenVK, SteamKit2, Avalonia, .NET, SDL and FAudio. They may be used as dependencies, or forked and modified, under C-0003.
2. **Must not copy:** code from existing products, whatever their license. Examples are Madeira and its forks (BCD, WiniOS, Playport, iDeck), GameNative, Winlator, GameHub, LiveContainer, StikDebug, iSH, Juice, UTM and CrossOver. No copying, no line-by-line translation, and no rewriting into another language.
3. **May learn from:** the public documentation, READMEs, issue discussions and test data of those products, only to learn what is feasible. Implementations follow upstream library documentation, Apple documentation and our own design documents.
4. **Record sources:** every design document lists the public material it consulted.

## 7. Compliance red lines

1. Never bypass, remove or emulate any DRM, including SteamStub and CEG. Never use or distribute steam_api emulators or DRM-removal tools.
2. Neither the repository nor the app package contains any binary from Microsoft, Valve or a game. When one is needed, the user's device downloads it from the official source, or the user provides it.
3. The user's Steam credentials are used only to talk directly to Valve's servers and are never sent to any third party. Sign-in tokens are stored only in the device keychain.
4. The name, icon and interface must not suggest any affiliation with Microsoft, Valve or Apple.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious; translated to English |
