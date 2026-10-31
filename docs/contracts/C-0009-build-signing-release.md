# C-0009 Build, Signing and Release

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines the environments, the pinned toolchain, the build stages, the app bundle, signing and installation, and what a release contains.

## 1. Environments

| Environment | Used for |
|---|---|
| Windows 11 workstation | Writing code, .NET builds and tests, the Avalonia desktop head, signing and sideloading from Windows. |
| macOS build host | Native builds, linking and packaging the iOS app. A local Apple silicon Mac, or a GitHub-hosted macOS runner whose image has the Xcode version the pinned workload needs. |
| Test device | An iPhone 17 Pro, 17 Pro Max, 18 Pro or 18 Pro Max (C-0001 section 2) with Developer Mode enabled. |

Linking and packaging an iOS app needs macOS, because every official toolchain depends on Xcode. Hot Restart is not used.

## 2. Pinned toolchain

Every version below is recorded in the repository, and changing one is a reviewed change of its own. A tool in `tools/` checks the installed versions against the pins before a build.

| Tool | Where the pin lives |
|---|---|
| .NET 10 SDK and the iOS workload set | `global.json` |
| The Xcode version that workload set needs | `eng/toolchain.json`, checked against `xcodebuild -version` |
| llvm-mingw release | `eng/toolchain.json` |
| CMake, and the LLVM release of clang-format and clang-tidy | `eng/toolchain.json` |
| Build tools the forks need, such as Meson for DXMT | `eng/toolchain.json` |
| macOS runner image | `eng/toolchain.json` |
| gitleaks, lychee and the REUSE tool | `eng/toolchain.json` |
| NuGet packages | `Directory.Packages.props` (central package management) with lock files |
| Fork and upstream revisions | The commits recorded for the submodules in `external/` |

The runtime libraries that llvm-mingw links into shipped binaries (the mingw-w64 runtime, libc++, libc++abi, libunwind and compiler-rt) are registered under C-0003 before the first Windows-side build.

## 3. Build stages

| Stage | Runs on | Produces |
|---|---|---|
| 1. Native, Mach-O side | macOS | The native host, and the Unix sides of the Wine and DXMT forks for `arm64-apple-ios`, as frameworks. |
| 2. Native, Windows side | macOS, with llvm-mingw | Wine's PE modules (ARM64EC, AArch64 and i386), FEX's emulation modules, DXMT's PE modules and the Dock host (x86-64). |
| 3. .NET app | macOS, through Pair to Mac or CI | The app on the Mono runtime with full ahead-of-time compilation. The interpreter stays off unless the phase 0 SteamKit2 gate shows it is needed (C-0012); then it is enabled only for the assemblies listed in `eng/toolchain.json`, for example `MtouchInterpreter=-all,protobuf-net`. |
| 4. Package | macOS | The `.ipa`. |
| 5. Sign and install | Windows or macOS | The app installed on the test device. |

Development builds may come from a local Mac. Release builds always come from the `release` CI workflow (C-0011 section 4).

## 4. App bundle

| Location in the bundle | Holds |
|---|---|
| `Frameworks/` | The native host and the Mach-O sides of the Wine and DXMT forks, as dynamic frameworks that a user can replace (C-0003 section 3). |
| `wine/aarch64-windows/`, `wine/arm64ec-windows/`, `wine/i386-windows/` | The PE modules of Wine, FEX and DXMT, as plain files that Wine's loader maps. A user can replace them in the same way. |
| `dockhost/` | The Dock host. |
| `jit-broker/` | The JIT broker script (C-0006). |
| `compat/` | The snapshot of the compatibility database (C-0008). |

- Every embedded framework is signed with the same identity as the app.
- The .NET bindings import the native host as `@rpath/paneport_host.framework/paneport_host`, and the framework is embedded with a `NativeReference` of kind `Framework`. The phase 1 gate checks on a device that this resolves under Mono; if it does not, the bindings use `__Internal`.
- Neither the bundle nor a release contains anything listed in C-0003 section 8 (Microsoft's Windows runtimes, Valve's client components, Wine Mono, Wine Gecko, game files), or anything C-0003 section 5 marks as under review, such as Apple's Metal Shader Converter, until its review is recorded. The MIT-licensed .NET runtime and .NET for iOS assemblies are part of the app and are listed in `THIRD_PARTY_NOTICES.md`.

**Entitlements**

| Entitlement | Rule |
|---|---|
| `get-task-allow` | Comes from the development provisioning profile when the app is signed. Needed for debugger-based JIT (C-0006). |
| `com.apple.developer.kernel.increased-memory-limit` | Declared explicitly in `Entitlements.plist`, because the .NET build does not copy it from the profile. |
| `com.apple.developer.kernel.extended-virtual-addressing` | Reported, but not confirmed by Apple, to need a paid developer account. Declared only when the build property `PaneportExtendedVirtualAddressing` is `true`. |

**Info.plist keys**

| Key | Value | Why |
|---|---|---|
| `LSApplicationCategoryType` | `public.app-category.games` | Identifies the app as a game. |
| `LSSupportsGameMode` | `true` | Apple's Game Mode opt-in on iOS 18.6 and later. A third party measured the memory limit rising from 6,144 MB to 8,192 MB once Game Mode was active. |
| `GCSupportsGameMode` | `true` | The Game Mode opt-in on iOS 18.0 to 18.5. |
| `CADisableMinimumFrameDurationOnPhone` | `true` | Allows 120 Hz presentation on ProMotion iPhones. |
| `UIFileSharingEnabled`, `LSSupportsOpeningDocumentsInPlace` | `true` | Lets the user import files through the Files app. |
| `BGTaskSchedulerPermittedIdentifiers` | The download task identifier | Required for the background download task (C-0007 section 4). |

The bundle identifier is chosen by recelilious.

## 5. Signing and installation

- Development builds are signed with an Apple Development certificate and a development profile. A free Personal Team works, with a re-sign every 7 days; a paid account adds a one-year profile.
- Installation goes through the Mac (Xcode, `devicectl` or Apple Configurator), or from Windows through AltServer with AltStore 2.2 or later, whose release notes state support for apps with the increased memory limit entitlement when it re-signs with the user's own Apple ID.
- SideStore 0.7.0 is reported to drop `increased-memory-limit` for free accounts (SideStore issue 1616, open and not confirmed by its maintainers), and Sideloadly is untested. Neither is a supported route until a device test measures the memory limit after installing with it (C-0012).
- Certificates, profiles and pairing files are never committed (C-0002 section 6). CI never holds or uses an Apple-issued certificate or profile.

## 6. Releases

- recelilious decides when to release, creates the `v<version>` tag on `main` and publishes the GitHub release. Claude prepares the release notes on a branch. The `release` workflow builds the `.ipa` and the source archives, generates the third-party notices, runs the scan, computes the checksums and attaches all of them to a draft release that recelilious publishes. The first public binary release waits for recelilious's decision on the repository's visibility (C-0002 section 1) and, where possible, for the professional license review that C-0003 recommends.
- Versions follow Semantic Versioning, starting at `0.1.0`. `CFBundleShortVersionString` carries only `MAJOR.MINOR.PATCH`, and `CFBundleVersion` is an integer that grows with every published build.
- Before tagging, every submodule revision in `external/` is pushed to its repository, and this repository and every fork are public.
- The `release` workflow builds the release from the tag on a GitHub-hosted macOS runner. The `.app` inside the `.ipa` is **ad-hoc signed**: each embedded framework with `codesign --force --sign -`, then the app with `codesign --force --sign - --entitlements Entitlements.plist`. Its entitlements are embedded, so the user's sideloading tool can read the ones it must request when it re-signs, and it carries no Apple identity and no provisioning profile.
- Native builds map source paths to relative ones. .NET builds set `ContinuousIntegrationBuild` with `Deterministic`, which maps source roots in compiler output. Release device builds are stripped of symbols. Before publishing, a tool in `tools/` scans the `.ipa` for provisioning profiles, team identifiers, UDIDs, ECIDs, serial numbers, e-mail addresses and local paths left by any build step, and fails if it finds any. Device model identifiers, such as those in the bundled `compat/` snapshot, are allowed.
- A GitHub release contains:
  - the ad-hoc-signed `.ipa`;
  - SHA-256 checksums;
  - `THIRD_PARTY_NOTICES.md` and the full license texts, generated from the register in C-0003 section 6 (they are also placed in the app bundle and are not committed to the repository);
  - source archives of this repository and of every fork at exactly the released revisions, with release notes that link to the tag and to each fork revision, so the source of every LGPL component is offered from the same place as the `.ipa` (LGPL-2.1 section 6).
- Release acceptance follows C-0012 section 5.

## Proposed changes for 1.1

**Not yet accepted.** Version 1.0 above stays binding until recelilious approves these changes. They come from stage M1.1, which found that the place section 2 gives the iOS workload set breaks the builds that do not use it.

The .NET SDK reads one key for a workload set in `global.json`, `sdk.workloadVersion`. With SDK 10.0.403 and `"workloadVersion": "10.0.403"`, every build failed on a machine without that workload set, also for projects that use no workload: `dotnet build` of Paneport.Core stopped with MSB4242, "Workload version 10.0.403, which was specified in global.json, was not found", and so did `dotnet run` of the tools in `tools/`. Only the macOS build host installs the iOS workload, so the pin would break the Windows workstation and the `dotnet` and `checks` jobs, or force each of them to install the workload set first.

1. **Section 2, table.** Replace the row ".NET 10 SDK and the iOS workload set | `global.json`" with these two rows:

   | Tool | Where the pin lives |
   |---|---|
   | .NET 10 SDK | `global.json` |
   | The iOS workload set: the workload set version that the iOS workload is installed from, such as `10.0.403` | `eng/toolchain.json`, installed with `dotnet workload install ios --version` and checked against `dotnet workload --version` |

2. **Section 2.** After the table, add: "`global.json` does not name the workload set (`sdk.workloadVersion`), because the SDK then fails every build on a machine without that workload set, even for projects that use no workload. The macOS build host installs it from the pin in `eng/toolchain.json`."

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
