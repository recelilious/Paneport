# C-0004 Architecture, Components and Layout

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract fixes how Paneport is split into components, who owns what at run time, and where every kind of file lives, both in the repository and on the device. Later contracts refine the individual interfaces. The reasoning behind these choices is in [the roadmap](../research/roadmap-2026-10.md).

## 1. Runtime model

- Paneport's code runs in **one iOS process** and never creates child processes (C-0001 section 3). Adding any app-extension process, such as an in-app JIT enabler (C-0006) or a memory donor, needs a change to this contract.
- Each app launch runs **at most one Wine session**. A session runs either one game title, together with the helper programs it starts, or one setup task, such as installing the runtimes a title needs (C-0008).
- **Switching games needs a relaunch.** iOS has no public API for an app to relaunch itself, so after a session ends the user relaunches Paneport, and JIT authorization (C-0006) is repeated on every launch. A design document decides how the app asks for the relaunch, and a device test confirms it (C-0012).
- Every Windows process is a **pseudo-process**: a group of threads inside the iOS process, created by the Wine fork.
- Code from a Windows program runs only on threads the Wine fork creates (the wineserver and pseudo-process threads) and on the translator's and graphics layers' own threads. **None of these threads ever attaches to the .NET runtime.**
- .NET code never calls into Wine, FEX or DXMT directly. Everything crosses through the host interface (C-0005).
- Data between those threads and managed code passes through lock-free queues owned by the native host.

| Thread | Owner | Work |
|---|---|---|
| Main thread | iOS and .NET | UIKit run loop, scene life cycle, Avalonia UI thread (layout, input, bindings) |
| Avalonia render thread | Avalonia (.NET) | Rendering of the shell and the overlay, driven by a display link on its own thread |
| Managed worker threads | .NET | Steam service, downloads, library and file work |
| Host event thread | Native host | Delivers host events and log lines to .NET |
| wineserver thread | Wine fork | The wineserver event loop |
| Pseudo-process threads | Wine fork | The threads of each Windows process |
| Render threads | Graphics layers | Metal command encoding and presentation |

## 2. Components

| Component | Language | License | Location | Responsibility |
|---|---|---|---|---|
| Paneport.Core | C# | MIT | `src/Paneport.Core` | Domain model, view models, settings, compatibility database reader, and the interfaces that platform code implements. No platform APIs. |
| Paneport.Steam | C# | MIT | `src/Paneport.Steam` | Steam sign-in, licenses, app information and depot downloads, built on SteamKit2 (C-0007). No UI code. |
| Paneport.UI | C#, AXAML | MIT | `src/Paneport.UI` | Avalonia views, styles and the in-game overlay controls, shared by both app heads. |
| Paneport.Interop | C# | MIT | `src/Paneport.Interop` | Source-generated bindings to the host interface (C-0005). The only project allowed to use unsafe code. |
| Paneport.iOS | C# | MIT | `src/Paneport.iOS` | App entry point, the UIKit root view controller, the game view that owns the `CAMetalLayer`, the transparent Avalonia overlay, GameController input, the keychain token store and background tasks. |
| Paneport.Desktop | C# | MIT | `src/Paneport.Desktop` | An Avalonia desktop head with a fake host, for working on the UI from Windows. Never shipped. It never writes a Steam sign-in token to disk. |
| Native host | C17, Objective-C | MIT | `src/native/host` | Process-wide setup, the single signal dispatcher, the JIT code pool (C-0006), the memory and thermal governor, Wine session and pseudo-process control, display and input bridging, logging. |
| Host interface headers | C17 | MIT | `src/native/include/paneport` | The public C headers of the host interface and the Wine bridge (C-0005). |
| JIT broker script | JavaScript | MIT | `src/jit-broker` | The script StikDebug runs to authorize Paneport's code pool (C-0006). Bundled in the app. |
| Dock host | C17 | MIT | `src/guest/dockhost` | The headless host for Valve's Steam client library inside the Wine session (C-0007). A Windows x86-64 program built with llvm-mingw. |
| Wine fork | C, Objective-C | LGPL-2.1-or-later | `external/Paneport-wine` | Single-process wineserver, pseudo-processes, the iOS display and input driver, RemoteIO audio, Darwin and iOS fixes. |
| FEX fork | C++ | MIT | `external/Paneport-FEX` | The ARM64EC and WoW64 emulation modules, with iOS host hooks. |
| DXMT fork | C++ | LGPL-2.1-or-later | `external/Paneport-dxmt` | Direct3D 10 and 11 on Metal. |
| Tools | C# | MIT | `tools` | Build orchestration and repository checks. |
| Compatibility database | JSON | MIT | `compat` | Per-title settings and test reports (C-0008). |

Windows-side binaries built with llvm-mingw also contain the mingw-w64 runtime and LLVM's runtime libraries. They ship in the app, so they are registered under C-0003 before the first Windows-side build, and their notices go into the generated third-party notices (C-0009 section 6).

**Dependency direction.** Dependencies point only in these directions:

- Paneport.Steam and Paneport.UI depend on Paneport.Core.
- Paneport.Interop depends on nothing else in the solution.
- Paneport.iOS depends on Core, Steam, UI and Interop. Paneport.Desktop depends on Core, Steam and UI.
- Nothing depends on Paneport.iOS or Paneport.Desktop.
- A test project depends only on the project it tests, that project's dependencies and registered test libraries. The device test app in `tests/device` may also depend on Paneport.Interop.
- Tools may depend on Paneport.Core and on nothing else under `src/`.
- The native host does not depend on .NET.
- The Wine fork reaches the host only through the bridge header defined in C-0005, and the host reaches the Wine fork only through the fork's own entry header.
- The Dock host reaches the native host only through a call that the Wine fork forwards over the bridge (C-0005 section 3).

**Open items for recelilious.**

- **Direct3D 12.** Accepted C-0001 section 4 says Direct3D 12 is evaluated separately. The only approach proven on iOS is a Metal-native layer that depends on Apple's Metal Shader Converter, which is under review (C-0003 section 5). Whether Paneport writes such a layer is decided through a change to this contract.
- **Direct3D 9.** The research did not establish whether DXMT covers Direct3D 9. The path is decided after the phase 1 gate (C-0012).
- **32-bit guests and Box64.** Box64 is the fallback translator in C-0001 section 4 but is not a component yet. Whether 32-bit guests use FEX's WoW64 module or Box64 is decided after the phase 1 gate.
- **Extension processes.** Isolating risky helper programs in app-extension processes stays a research item.

## 3. Repository layout

```
Paneport/
├── src/
│   ├── Paneport.Core/
│   ├── Paneport.Steam/
│   ├── Paneport.UI/
│   ├── Paneport.Interop/
│   ├── Paneport.iOS/
│   ├── Paneport.Desktop/
│   ├── native/
│   │   ├── include/paneport/
│   │   └── host/
│   ├── jit-broker/
│   └── guest/
│       └── dockhost/
├── tests/
│   ├── Paneport.Core.Tests/
│   ├── Paneport.Steam.Tests/
│   ├── Paneport.UI.Tests/
│   ├── Paneport.Interop.Tests/
│   ├── native/
│   └── device/
├── external/
├── compat/
│   ├── schema/
│   └── titles/
├── tools/
├── eng/
├── docs/
│   ├── contracts/
│   ├── research/
│   ├── design/
│   └── evidence/
├── LICENSES/
├── .github/workflows/
├── .clang-format, .clang-tidy, .editorconfig
├── .gitattributes, .gitignore, .gitmessage, .gitmodules
├── REUSE.toml
├── Paneport.slnx, global.json
├── Directory.Build.props, Directory.Packages.props
└── README.md, LICENSE, CLAUDE.md, CONTRIBUTING.md
```

| Folder | Holds |
|---|---|
| `src/` | Original code only, all MIT (C-0003 section 4). |
| `tests/` | Automated tests and the on-device test app (C-0011). |
| `external/` | Git submodules only: our fork repositories (`Paneport-<upstream>`, C-0003 section 4), and unmodified upstream source dependencies registered under C-0003, each pinned to an upstream release tag. |
| `compat/` | The compatibility database, the list of verified iOS builds, and their JSON schemas (C-0006, C-0008). |
| `tools/` | C# file-based tools for building and checking the repository. |
| `eng/` | Engineering configuration: toolchain pins, CMake presets and toolchain files, Xcode configuration, solution filters, CI helper scripts and the Bash scripts for macOS-only build steps (C-0010). |
| `docs/design/` | Design documents. Each one lists the public sources it consulted (C-0001 section 6). |
| `docs/evidence/` | Acceptance evidence records (C-0012). |
| `LICENSES/` | Texts of the licenses that files in this repository use (C-0003 section 4). |

The native CMake project's root is `src/native/`, which holds its `CMakeLists.txt` and `CMakePresets.json`; shared presets and toolchain files live in `eng/cmake/`. `THIRD_PARTY_NOTICES.md` is generated into each release and into the app bundle and is not committed (C-0009 section 6).

Rules:

- A new top-level folder or a new root file needs a change to this contract. Folders ignored by git are outside this rule.
- `build/`, `bin/` and `obj/` are reserved for build output and stay ignored by git, which is why engineering files live in `eng/`.
- Every folder shown in the tree, except `external/` with the submodules inside it and `LICENSES/`, has a `README.md` that says what it holds.

## 4. On-device storage layout

All paths are inside the app container.

| Path | Holds | In backups |
|---|---|---|
| `Library/Application Support/Paneport/prefixes/` | Wine prefixes, including the games' save files. One shared prefix by default; a title may ask for its own in its compatibility entry. | No |
| `Library/Application Support/Paneport/library/` | Installed game files and Steam app manifests. | No |
| `Library/Application Support/Paneport/downloads/` | Downloads in progress. | No |
| `Library/Application Support/Paneport/runtime/` | Components downloaded on the device, such as Valve client packages and Wine Mono (C-0003 section 8). | No |
| `Library/Application Support/Paneport/compat/` | The device's own compatibility layer: computed classifications and the user's overrides (C-0008 section 1). | Yes |
| `Library/Application Support/Paneport/settings/` | App settings. | Yes |
| `Library/Caches/Paneport/` | Shader caches, logs and crash records that iOS may purge. | No |
| `Documents/Import/` | Files the user brings in through the Files app, such as their own game copies or runtime installers. | User's choice |
| Keychain | Steam refresh tokens and any other secret, stored with `kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly` and not synchronizable. | Never leaves the device |

- Paneport marks the folders that say "No" as excluded from backup and marks them again after replacing files in them. iOS treats the mark as guidance, and `Library/Caches` is excluded by iOS itself.
- Logs never contain passwords, tokens or other credentials. Before the user exports logs, the app removes the personal data listed in C-0012 section 4.

**Open item for recelilious.** Save files inside the prefixes are not backed up. Until cloud saves work (C-0007 section 7), the app tells the user so and offers to export a prefix's user folder to `Documents/`. Whether that is enough is recelilious's decision.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
