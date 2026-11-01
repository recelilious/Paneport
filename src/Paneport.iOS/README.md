<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.iOS

The iOS app head (C-0004 section 2): .NET for iOS on Mono (C-0010 section 1), built for iPhones with arm64 and iOS 26.0 or later (C-0001 section 2). For now it is a minimal app that shows its name; M1.3 turns it into the app head with the shell, the native host and the device's storage.

| File | What it is |
|---|---|
| `Program.cs` | The entry point, which starts UIKit with the app delegate. |
| `AppDelegate.cs` | The application delegate. It owns no window, because apps built with Xcode 27 must use the scene life cycle. |
| `SceneDelegate.cs` | The delegate of the window scene, registered as `PaneportSceneDelegate`. It gives the scene a window that shows the main view controller. |
| `MainViewController.cs` | An empty view controller that shows the app's name. |
| `Info.plist` | The scene manifest, which names the scene delegate, the launch screen, the device family and the orientations. The build adds the bundle identifier, the name, the versions and the minimum iOS version from the project file. |
| `packages.lock.json` | The lock file. The restore for `ios-arm64` writes it, so it is updated on a Mac, or from the log of the `ios` job. |

Building it needs a Mac with Apple silicon, the Xcode that `eng/toolchain.json` pins and the iOS workload, which `eng/mac/bootstrap.sh` checks and installs. From the repository root:

```bash
eng/mac/bootstrap.sh
```

```bash
dotnet build src/Paneport.iOS/Paneport.iOS.csproj -c Release
```

The build makes `bin/Release/net10.0-ios/ios-arm64/Paneport.iOS.app`, unsigned. On Windows without a Mac, the `ios` job of CI builds it (docs/design/dev-environment.md).

- **Depends on:** nothing yet. M1.3 adds Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop. Nothing may depend on this project.
- **Build:** `net10.0-ios` for `ios-arm64` only. Release compiles everything ahead of time with the interpreter off (C-0009 section 3). No build signs the app (C-0009 section 5). The bundle identifier `com.example.paneport` is a placeholder until recelilious chooses one (D3); M1.3 replaces it. `eng/ci.slnf` leaves the project out (C-0011 section 4).
- **Design:** [dev-environment.md](../../docs/design/dev-environment.md) describes how it is built, and [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution. M1.3 writes its full design in `docs/design/ios-app-head.md`.
