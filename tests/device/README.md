<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# device

The device test flavor of the iOS app head (plan section 7.3): the full app plus the developer menu, which the device stages use on the iPhone. It shares the main app's bundle identifier (D12), so installing it replaces the main app and keeps the free account's three apps; it has its own display name, Paneport Test, and embeds the `ios-device-test` build of the host with its test exports.

| File | What it is |
|---|---|
| `Paneport.DeviceTest.csproj` | The project. It imports the head's build from `src/Paneport.iOS/AppHead.props` and compiles the head's sources and Info.plist, instead of referencing the head's project, on which nothing may depend (C-0004 section 2). |
| `DeveloperMenu.cs` | The flavor's part of the composition root: it adds the Developer section with its menu. |
| `DeveloperMenuViewModel.cs`, `DeveloperEntry.cs` | The menu, one entry per developer page. |
| `DeveloperToolViewModel.cs`, `DeveloperAction.cs` | A developer page: a description, its actions and the result of the last one. |
| `DeveloperTools.cs` | The first pages: Interface conformance, Token store and Crash record. |
| `SignalGateViewModel.cs`, `SignalGateRun.cs` | The signal gate page of L2 steps 11 to 13: the startup check, a run of four hours or ten minutes with live counters, and the verdict; and the run itself, with the host's stress harness, a managed null-reference loop, garbage collection pressure and a heartbeat ([signals.md](../../docs/design/signals.md) section 9). |
| `DeveloperViews.cs` | The pages' views, built in code and matched to their view models by type, through templates from `ViewTemplates.For<T>` in Paneport.UI. A view template must never match null, and Avalonia's generic `FuncDataTemplate<T>` does: with it, the Settings page built the developer menu's view for null and ended the app ([app-shell.md](../../docs/design/app-shell.md) section 5). |
| `../../build/test-programs/` | Not in this folder: the x86-64 Win32 test programs of `tests/win32` and their `SHA256SUMS`, which `eng/mac/build-win32-tests.sh x86_64` and `eng/mac/stage-test-programs.sh` write and the project bundles as `test-programs/`. The folder is the property `PaneportTestProgramsDir`. The project builds without it outside CI, and fails in CI when it is missing. The main app bundles none. |
| `packages.lock.json` | The lock file, which only a restore with the iOS workload can write; to renew it without a Mac, follow [dev-environment.md](../../docs/design/dev-environment.md) section 5. |

Building it needs what the head needs: a Mac with the pinned Xcode and the iOS workload, and the `ios-device-test` framework, built first with the CMake preset of that name in `src/native`. From the repository root:

```bash
dotnet build tests/device/Paneport.DeviceTest.csproj -c Release
```

The `ios` job builds it, signs it ad hoc, checks and scans it, and uploads it as `Paneport-test-<commit>`. [ios-app-head.md](../../docs/design/ios-app-head.md) section 10 describes the flavor and its pages, and L1's acceptance steps use them.

- **Depends on:** Paneport.Core, Paneport.Steam, Paneport.UI and Paneport.Interop, as the head does (C-0004 section 2).
- **Not in `eng/ci.slnf`:** it needs the iOS workload (C-0011 section 4).
