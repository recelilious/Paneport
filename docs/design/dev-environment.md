# Development Environment

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-31T02:56Z |

Back to the [design index](README.md).

This document records where each part of Paneport is built and how a machine is set up for it: the Windows workstation, the Mac that builds the iOS side, the CI job that stands in for that Mac, and the iPhone. It covers the toolchain pins that the iOS build needs and why they were chosen. Stage M1.1 wrote it (see the [L1 brief](stages/L1-app-on-iphone.md)). It builds on [solution-and-build.md](solution-and-build.md) and [repository-checks.md](repository-checks.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. The machines

| Machine | Builds and runs | Set up with |
|---|---|---|
| Windows workstation | The .NET libraries, the desktop head, the tests and the tools in `tools/` (C-0009 section 1) | The SDK that `global.json` pins and an IDE (section 3) |
| macOS build host | Paneport.iOS, and from M1.2 on the native host: everything that needs Xcode | A local Mac (section 4), or the `ios` CI job (section 5) |
| iPhone | Paneport, installed by a sideloading tool | Developer Mode and AltStore (section 6) |

There is no local Mac (decision D1 in [plan section 4.2](development-plan.md), 2026-10-29), so every Mac step runs in CI for now. The same script, `eng/mac/bootstrap.sh`, prepares both kinds of Mac, so a Mac that joins later needs nothing new.

## 2. The toolchain pins

C-0009 section 2 pins every tool of the iOS build. The pins form one chain: the SDK selects the workload sets it can install, the workload set fixes the iOS workload, the iOS workload accepts exactly one Xcode version, and only some runner images carry that Xcode.

| Pin | Value | Where it lives | Checked by |
|---|---|---|---|
| .NET SDK | 10.0.403 | `global.json` | `dotnet --version` (every run of `tools/CheckToolchain.cs`) |
| iOS workload set | 10.0.403, which installs the iOS workload 27.1.10018 | `eng/toolchain.json`, as `ios-workload-set` | `dotnet workload --version`, and the `ios` row of `dotnet workload list` (`--ios-workload`) |
| Xcode | 27.1 | `eng/toolchain.json`, as `xcode` | `xcodebuild -version` (`--xcode`) |
| macOS runner image | `xcode-27` | `eng/toolchain.json`, as `macos-runner-image` | The `runs-on` value of every macOS job in `.github/workflows/` (every run) |

**How the pair was found** (checked on 2026-10-31):

- SDK 10.0.403, released on 2026-10-25, is still the newest .NET 10 SDK ([releases.json](https://github.com/dotnet/core/blob/main/release-notes/10.0/releases.json)). Its matching workload set, 10.0.403, was published on the same day as version 10.403.0 of the package `Microsoft.NET.Workloads.10.0.400` ([NuGet](https://www.nuget.org/packages/Microsoft.NET.Workloads.10.0.400/10.403.0)). The repository that builds the workload sets pinned iOS 27.1.10018 from 2026-10-19 to 2026-10-30 ([Version.Details.xml at that time](https://github.com/dotnet/workload-versions/blob/316cdd3ba797b902bcbfbee2280ca714eb76447c/eng/Version.Details.xml)), and the `ios` job confirmed it: `dotnet workload install ios --version 10.0.403` installs manifest 27.1.10018/10.0.100.
- That answers the open question of M0.1 ([solution-and-build.md](solution-and-build.md) section 1): a workload set of the 10.0.400 band installs the iOS manifests that Microsoft publishes only for the 10.0.100 band. A workload set names each manifest with its band, as in `27.1.10018/10.0.100`.
- The release notes of iOS 27.1.10018 say that it requires Xcode 27.1, which requires macOS 26.6 or later ([dotnet/macios release](https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.1-10018)). Its build compares the major and minor version of the selected Xcode with 27.1 and stops with error E0191 on any other ([the check](https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.1-10018/dotnet/targets/Xamarin.Shared.Sdk.targets)). The pin here is exact, so a later Xcode 27.1.x also needs a reviewed change.
- The `macos-26` image carries Xcode 26.0.1 to 26.6 only ([its README](https://github.com/actions/runner-images/blob/main/images/macos/macos-26-arm64-Readme.md)). The `xcode-27` image runs macOS 27.0.1 on Apple silicon and carries Xcode 27.0, 27.1, which is its default, and a release candidate of 27.2. Its README, for the image of 2026-10-26, also lists SDK 10.0.403 ([its README](https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md)), so the brief's concern that the job must install the SDK itself no longer holds; the job still runs `actions/setup-dotnet`, which keeps it working when the image changes.
- GitHub's changelog calls the `xcode-27` image generally available since 2026-10-15 ([changelog](https://github.blog/changelog/2026-10-15-xcode-27-runner-image-generally-available)), while the list of images still marks it as a preview ([runner-images](https://github.com/actions/runner-images)). The pins follow the changelog. If GitHub withdraws the image, the fallback is an older workload set whose Xcode a `macos-*` image carries, such as 10.0.401 with Xcode 26.6; recelilious decides, because there is no local Mac.

**Why the workload set is not in `global.json`.** C-0009 section 2 puts it there, and the SDK reads one key for it, `sdk.workloadVersion`. With SDK 10.0.403 and `"workloadVersion": "10.0.403"`, every build failed on the workstation, which has no workload set installed, also for projects that use no workload: `dotnet build` of Paneport.Core stopped with MSB4242, "Workload version 10.0.403, which was specified in global.json, was not found", and `dotnet run` of the tools stopped the same way. The pin would break the workstation and the `dotnet` and `checks` jobs, or make each of them install the workload set first. So the workload set is pinned in `eng/toolchain.json`, next to the Xcode version it needs, and the bootstrap installs it with `dotnet workload install ios --version`. This needs the C-0009 change that M1.1 proposes on `contract/m1-1-ios-ci`; the stage merges only after recelilious accepts it.

**Changing the pins.** The four pins move together, in one reviewed change (C-0009 section 2):

1. Find the newest workload set for the SDK pin, and the iOS workload it names: its NuGet package, or the dotnet/macios release that names the workload set.
2. Read the Xcode version that the iOS workload requires in its release notes, and find a runner image that carries it.
3. Update `eng/toolchain.json`, the `runs-on` value of the `ios` job, and the SDK pin if it moves. The lock file of Paneport.iOS names the iOS SDK version in its target framework (`net10.0-ios27.1`), so it changes with the workload: take the new one from the log of the `ios` job, as section 5 says.
4. Register any new tool under C-0003 first.

The next move is already visible: on 2026-10-30 the workload set repository took iOS 27.2.10000, for Xcode 27.2, into the next workload set.

## 3. Windows

- **SDK.** The version that `global.json` pins, from <https://dotnet.microsoft.com/download/dotnet/10.0>. `dotnet --version` at the repository root prints it.
- **IDE.** Visual Studio 2026, or Rider 2026.1 or later. Visual Studio 2022 17.14 cannot target `net10.0` (NETSDK1233, [dotnet/sdk#51678](https://github.com/dotnet/sdk/issues/51678)).
- **What builds here.** Everything in `eng/ci.slnf`: the libraries, the desktop head, the tests and the trimmed test app, and the tools. [solution-and-build.md](solution-and-build.md) and [CONTRIBUTING.md](../../CONTRIBUTING.md) list the commands.
- **Paneport.iOS.** It does not build on Windows alone, because Mono's ahead-of-time compiler for iOS and Apple's linker run only on macOS ([avalonia_ui_layer.md](../research/avalonia_ui_layer.md) section 2.2). An IDE without the iOS workload cannot load it from `Paneport.slnx`; `eng/ci.slnf` leaves it out, so the command-line builds are unaffected. Installing the iOS workload on Windows lets the IDE load it, and is the workstation owner's choice.
- **Pair to Mac.** With a Mac on the network, Visual Studio 2026 builds the iOS project on it over SSH ([Pair to Mac](https://learn.microsoft.com/en-us/dotnet/maui/ios/pair-to-mac)). From the command line the same works with `dotnet build` or `dotnet publish` and the properties `ServerAddress`, `ServerUser`, `TcpPort` and `_DotNetRootRemoteDirectory` ([publishing from the command line](https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli); [avalonia_ui_layer.md](../research/avalonia_ui_layer.md) section 3.2). The Mac needs the setup of section 4. Without a Mac, CI builds the app (section 5).

## 4. A local Mac

A Mac with Apple silicon and macOS 26.6 or later, which Xcode 27.1 requires.

1. Install Xcode 27.1 and select it: `sudo xcode-select --switch /Applications/Xcode_27.1.app`, or set `DEVELOPER_DIR` for one shell. Start Xcode once to accept its license and install its components.
2. Install the SDK that `global.json` pins. If it lives in a folder that only an administrator can write, such as the one the official installer uses, the bootstrap cannot install the workload into it; the script then says so, and the owner runs the workload command it names. An SDK installed for the user, as `dotnet-install.sh` does, avoids that.
3. From the repository root, run the bootstrap:

   ```bash
   eng/mac/bootstrap.sh
   ```

   It stops unless the Mac has Apple silicon, checks the SDK and Xcode against their pins with `tools/CheckToolchain.cs -- --xcode`, installs the iOS workload from the pinned workload set with the package source of `eng/nuget.config`, and checks the installed workload set with `--xcode --ios-workload`. Every comparison happens in the tool, so the pins are read in one place. On any mismatch it exits non-zero after naming both versions and saying what to do. It never touches certificates, provisioning profiles or pairing files, and it changes no system setting.
4. Build the app:

   ```bash
   dotnet build src/Paneport.iOS/Paneport.iOS.csproj -c Release
   ```

   The result is `src/Paneport.iOS/bin/Release/net10.0-ios/ios-arm64/Paneport.iOS.app`, unsigned. M1.3 adds the ad-hoc signature and the `.ipa`, and section 6 how to install it.

M1.2 extends the bootstrap with CMake and the LLVM release of clang-format and clang-tidy, which it installs outside the repository.

## 5. CI only

The `ios` job in `.github/workflows/ci.yml` is the macOS build host while there is no local Mac. [The workflows' README](../../.github/workflows/README.md) lists its steps, and [repository-checks.md](repository-checks.md) section 6 describes them.

- **When it runs.** recelilious decided that the job runs only before merging (D7, 2026-10-29). It runs for a pull request that carries the `ios-ci` label: when the label is added, and on every push after that. Add the label once the pull request is otherwise ready; C-0011 section 3 requires the job to pass on the last commit before the merge. A newer push cancels the job's run for the same pull request.
- **What it checks.** The bootstrap with the pinned Xcode passes, and the bootstrap against another Xcode on the image fails and names both versions. Paneport.iOS restores from its lock file and builds for `ios-arm64` in Release without signing, its formatting passes, and its app names the scene delegate, requires iOS 26.0, holds arm64 code only, carries no signature beyond the one that Apple's linker may give arm64 code, which holds no identity, and no provisioning profile.
- **What it leaves.** The app, zipped so that the bundle keeps its name and the executable its file mode, as an artifact named by the pull request's commit. CI holds no Apple certificate or profile (C-0009 section 5).
- **How long it takes.** Its first green run, on 2026-10-31, took 2 min 39 s: 40 s for the bootstrap, which installs the workload, 90 s for the Release build with full ahead-of-time compilation, and the rest for the checks and the upload. M1.2 adds the native host and M1.3 two full builds of the app, so the job will take longer.
- **A lock file without a Mac.** Restoring Paneport.iOS needs the iOS workload, so its lock file cannot be made on a workstation without it. When a change needs a new lock file, the job's step "Check that no lock file was written" fails and prints the file that the restore wrote; commit that file. The lock file of M1.1 was made that way.
- **Cost.** macOS runners are free while the repository is public (C-0011 section 4).

## 6. The iPhone

- **Device.** An iPhone 17 Pro, 17 Pro Max, 18 Pro or 18 Pro Max on iOS 26 or 27 (C-0001 section 2), with Developer Mode on, in Settings, Privacy & Security ([Apple](https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device)).
- **Installing from Windows.** AltServer on Windows, which needs Apple's own iTunes and iCloud rather than their Microsoft Store versions, installs AltStore 2.2 or later on the iPhone; AltStore then re-signs and installs Paneport with the user's Apple ID (C-0009 section 5). AltStore 2.2 states support for apps with the increased memory limit entitlement, which Paneport needs ([avalonia_ui_layer.md](../research/avalonia_ui_layer.md) section 2.2). The re-signing profile also provides `get-task-allow`.
- **Free or paid account.** recelilious uses a free account (D2, 2026-10-29). A free account signs at most three apps per device and its profiles expire after seven days, so AltStore re-signs Paneport within a week, from the same Windows machine on the same network. AltStore, StikDebug and Paneport already use the three apps, which is why the device test flavor shares the main app's bundle identifier (D12). A paid account removes the limit and gives one-year profiles.
- **Installing from a Mac.** Xcode, `xcrun devicectl` or Apple Configurator, with a development profile of the user's own (C-0009 section 5). The bootstrap signs nothing, and nothing from the user's account enters the repository.
- **Routes that are not supported yet.** SideStore 0.7.0 is reported to drop `increased-memory-limit` for free accounts ([SideStore issue 1616](https://github.com/SideStore/SideStore/issues/1616), open), and Sideloadly is untested. Neither is supported until a device test measures the memory limit after installing with it (C-0009 section 5, C-0012).
- **JIT.** StikDebug, a pairing file and LocalDevVPN come with L3 (C-0006).
- **The app.** M1.3 produces the ad-hoc-signed `.ipa` files that these tools install, and S1.0.2 records the install route that worked, with its exact steps, in this section.

## 7. Never in the repository

- Certificates, provisioning profiles and pairing files, and any Apple ID, Steam account, password or token (C-0002 section 6). `.gitignore` keeps out their file types, and the personal-data check fails on them (repository-checks.md section 3). CI never holds an Apple-issued certificate or profile (C-0009 section 5).
- Paths and names of the developer's own machines. The documents name tools and folders relative to the repository or by their documented install locations.
- Hot Restart is not used (C-0009 section 1). Visual Studio 2026 does not support it, it needs a paid account, and it applies no custom entitlements, so it could not run Paneport ([Hot Restart](https://learn.microsoft.com/en-us/dotnet/maui/ios/hot-restart)).

## 8. Open items

- **The preview mark of `xcode-27`.** The image list still marks the image as a preview after GitHub's changelog announced it as generally available (section 2). If GitHub changes the image or its label, the pins move in a reviewed change.
- **The linker's signature.** Apple's linker may sign arm64 executables ad hoc by itself. The job accepts such a signature only when it holds no identity, and M1.3 replaces it with the ad-hoc signature of the whole app.
- **The pin location.** The workload set stays in `eng/toolchain.json` only if recelilious accepts the proposed C-0009 change; otherwise the stage needs another way to pin it (section 2).

## 9. Sources

Public material consulted (C-0001 section 6):

- .NET 10 release metadata: <https://github.com/dotnet/core/blob/main/release-notes/10.0/releases.json>
- Workload sets, and `sdk.workloadVersion` in `global.json`: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-sets>
- `dotnet workload install`: <https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-install>
- The workload set 10.0.403: <https://www.nuget.org/packages/Microsoft.NET.Workloads.10.0.400/10.403.0>
- The manifests that the workload sets of .NET 10 pinned on 2026-10-27: <https://github.com/dotnet/workload-versions/blob/316cdd3ba797b902bcbfbee2280ca714eb76447c/eng/Version.Details.xml>
- iOS workload 27.1.10018 and its Xcode requirement: <https://github.com/dotnet/macios/releases/tag/dotnet-10.0.1xx-xcode27.1-10018>
- The Xcode check of .NET for iOS, error E0191: <https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.1-10018/dotnet/targets/Xamarin.Shared.Sdk.targets>
- Build properties of .NET for iOS, among them `EnableCodeSigning`, `UseInterpreter` and `TrimMode`: <https://github.com/dotnet/macios/blob/dotnet-10.0.1xx-xcode27.1-10018/docs/building-apps/build-properties.md>
- Xcode 27.1 release notes: <https://developer.apple.com/documentation/xcode-release-notes/xcode-27_1-release-notes>
- GitHub-hosted runner images: <https://github.com/actions/runner-images>
- The `xcode-27` image: <https://github.com/actions/runner-images/blob/main/images/macos/xcode-27-arm64-Readme.md>
- The `macos-26` image: <https://github.com/actions/runner-images/blob/main/images/macos/macos-26-arm64-Readme.md>
- The `xcode-27` image announced as generally available: <https://github.blog/changelog/2026-10-15-xcode-27-runner-image-generally-available>
- Events that trigger workflows, among them the `labeled` activity of `pull_request`: <https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows>
- actions/upload-artifact: <https://github.com/actions/upload-artifact>
- Visual Studio 2022 and `net10.0`: <https://github.com/dotnet/sdk/issues/51678>
- Pair to Mac: <https://learn.microsoft.com/en-us/dotnet/maui/ios/pair-to-mac>
- Publishing an iOS app from the command line: <https://learn.microsoft.com/en-us/dotnet/maui/ios/deployment/publish-cli>
- Hot Restart: <https://learn.microsoft.com/en-us/dotnet/maui/ios/hot-restart>
- Developer Mode: <https://developer.apple.com/documentation/xcode/enabling-developer-mode-on-a-device>
- SideStore issue 1616: <https://github.com/SideStore/SideStore/issues/1616>
- Research note `avalonia_ui_layer.md`, sections 2.2 and 3.2: [avalonia_ui_layer.md](../research/avalonia_ui_layer.md)
- Research note `ios_platform_constraints.md`, section 4: [ios_platform_constraints.md](../research/ios_platform_constraints.md)
