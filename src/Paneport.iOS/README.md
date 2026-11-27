<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.iOS

The iOS app head (C-0004 section 2): .NET for iOS on Mono (C-0010 section 1), built for iPhones with arm64 and iOS 26.0 or later (C-0001 section 2). It shows the shell of Paneport.UI over an idle game view, starts the native host behind the interface of C-0005, and gives the shell the device's storage, keychain and diagnostics. [ios-app-head.md](../../docs/design/ios-app-head.md) describes it.

| File | What it is |
|---|---|
| `Program.cs` | The entry point, which starts UIKit with the app delegate. |
| `AppDelegate.cs` | The application delegate, which composes the app at launch. It owns no window, because apps built with Xcode 27 must use the scene life cycle. |
| `SceneDelegate.cs` | The delegate of the window scene, registered as `PaneportSceneDelegate`, which Info.plist names. It gives the scene a window with the root view controller. |
| `RootViewController.cs` | The root view controller: the game view under one `AvaloniaView` with the shell, kept out of the safe area, or the startup's message when the host did not start. |
| `GameView.cs` | The full-screen view whose layer is an opaque, idle `CAMetalLayer`. |
| `CompositionRoot.cs`, `AppComposition.cs` | The startup on the main thread and what it made: storage, log, crash record, Avalonia's platform, the call that turns off Mono's crash chaining, the native host and the shell. |
| `AppLifecycle.cs`, `DeveloperContext.cs` | The scene's returns to the foreground, after which the Steam service logs on again; and what the composition root gives the device test flavor's developer menu. |
| `InteropNativeHost.cs` | Paneport.Core's `INativeHost` over the bindings of Paneport.Interop. |
| `AvaloniaUiDispatcher.cs` | Runs the host's events on the UI thread. |
| `KeychainItemStore.cs` | The secure items of the token store in the keychain. |
| `BackupMarks.cs` | The backup marks of the storage folders. |
| `DeviceInfo.cs` | The model identifier, the iOS version and build, and the thermal state. |
| `BuildInfo.cs` | The build stamp of Diagnostics. |
| `ContainerFolders.cs`, `ConsoleLog.cs` | The app container's root, and the log when the log file cannot be opened. |
| `AppHead.props` | The build of the head, which the device test flavor in `tests/device` imports too: the target, the bundle identifier, the versions, the signing settings, the embedded host framework, the build stamp and the references. |
| `Info.plist` | The scene manifest, the launch screen, the device family, the orientations and the keys of C-0009 section 4. The build adds the bundle identifier, the name, the versions and the minimum iOS version. |
| `Entitlements.plist` | The entitlements that the app declares: `increased-memory-limit`. `eng/mac/sign-adhoc.sh` signs the app with it. |
| `packages.lock.json` | The lock file. Only a restore with the iOS workload can write it: on a Mac, or in the `ios` job, which prints it when the file is missing. To renew it without a Mac, follow [dev-environment.md](../../docs/design/dev-environment.md) section 5. |

Building it needs a Mac with Apple silicon, the Xcode that `eng/toolchain.json` pins and the iOS workload, which `eng/mac/bootstrap.sh` checks and installs, and the host framework of the `ios-device` preset. From the repository root:

```bash
eng/mac/bootstrap.sh --ios
```

```bash
(cd src/native && cmake --preset ios-device && cmake --build --preset ios-device --parallel)
```

```bash
dotnet build src/Paneport.iOS/Paneport.iOS.csproj -c Release
```

The build makes `bin/Release/net10.0-ios/ios-arm64/Paneport.iOS.app`, unsigned; `tools/GenerateNotices.cs` places its third-party notices in it, and `eng/mac/sign-adhoc.sh`, `eng/mac/check-app.sh` and `eng/mac/package-ipa.sh` sign, check and package it as the `ios` job does. On Windows without a Mac, the `ios` job of CI builds both flavors and uploads their `.ipa` files ([dev-environment.md](../../docs/design/dev-environment.md)).

- **Depends on:** Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop, and the Avalonia.iOS package. Nothing may depend on this project; the device test flavor compiles its sources instead.
- **Build:** `net10.0-ios` for `ios-arm64` only. Release compiles everything ahead of time with the interpreter off (C-0009 section 3). The build signs nothing; CI signs the app ad hoc afterwards, with no Apple identity (C-0009 section 5). The bundle identifier is `io.github.recelilious.paneport` (D3). `eng/ci.slnf` leaves the project out (C-0011 section 4).
