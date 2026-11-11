<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# eng

Engineering configuration (C-0004 section 3). Build output never goes here: `build/`, `bin/` and `obj/` are reserved for it.

| File | What it is |
|---|---|
| `ci.slnf` | The solution filter that CI builds and tests (C-0011 section 4). It lists every project of `Paneport.slnx` except Paneport.iOS, `tests/device` and `tests/Paneport.Interop.Tests`. |
| `nuget.config` | The only package source, nuget.org. `Directory.Build.props` points every restore at this file, so the machine's own NuGet configuration plays no part. |
| `toolchain.json` | The pinned versions of the tools that `global.json` does not pin (C-0009 section 2): the REUSE tool, gitleaks and lychee for the `checks` job; Xcode, the iOS workload set and the macOS runner image for the `ios` job; and CMake and the LLVM release of clang-format and clang-tidy for the native host. |
| `license-map.json` | The reviewed mapping from each package ID, tool, action and submodule to the row of the C-0003 register that covers it, which `tools/CheckLicenses.cs` reads. |
| `personal-data-allow-list.json` | The exceptions to the personal-data scan of `tools/ScanPersonalData.cs`: each entry excuses one rule in one file and states why. It is empty. |
| `gitleaks.toml` | The gitleaks configuration. It extends gitleaks' default rules, and is where a false positive is allowed, with its reason. |
| `lychee.toml` | The lychee configuration: which files are checked, and how failed requests are retried. |
| `mac/bootstrap.sh` | Prepares a Mac to build the iOS side and the native host, in CI and on a local Mac: it checks the SDK and Xcode against their pins; with `--ios` it installs the iOS workload from the pinned workload set and CMake, with `--native` CMake and the LLVM release of clang-format and clang-tidy, and with neither option both; it checks what it installed and exits non-zero after saying what to do when something is missing or does not match. CMake and LLVM come from their pinned downloads, each checked against its SHA-256 before it is unpacked, into a folder outside the repository. It runs on Apple silicon only, with the Bash 3.2 that macOS ships (C-0010 section 1), and never touches certificates, profiles or pairing files. |
| `mac/select-xcode.sh` | Selects the pinned Xcode on a GitHub-hosted runner image for the later steps of a job, through `DEVELOPER_DIR`, and refuses a version-named folder that resolves to a release candidate or a beta. CI only. |
| `mac/check-native-style.sh` | Checks our own C, Objective-C and C++ code with the pinned clang-format, in check mode, and with clang-tidy on the compile databases of given build folders. |
| `mac/check-framework.sh` | Checks the two builds of `paneport_host.framework`: the install name, arm64 code for iOS devices, the Info.plist, and that the exports are exactly the functions that the public headers declare. |
| `cmake/base-presets.json` | The hidden CMake presets that `src/native/CMakePresets.json` builds on: the generator, the build folder `build/native/<preset>/`, the compile databases, and the two toolchains. |
| `cmake/ios.cmake`, `cmake/macos.cmake` | The toolchain files of the native host: `arm64-apple-ios` from iOS 26.0, and arm64 macOS for the test builds. |

`toolchain.json` holds one entry per tool under `tools`, keyed by the tool's name, and nothing else. Each entry has the exact `version` and the `source` it is installed from. A tool that CI downloads also has `downloads`, keyed by runtime identifier, each with the download's `url` and `sha256`. `tools/CheckToolchain.cs` rejects any other key or field, and `tools/CheckLicenses.cs` any other key. The `macos-runner-image` entry names the image by the label that the macOS jobs give `runs-on`, such as `xcode-27`, and the toolchain check fails when a macOS job runs on another one. The `ios-workload-set` entry is the workload set that the iOS workload is installed from, such as `10.0.403`; `global.json` cannot hold it, because the SDK then fails every build on a machine without that workload set ([dev-environment.md](../docs/design/dev-environment.md) section 2). The `cmake` and `llvm` entries pin the releases whose macOS downloads the bootstrap installs, keyed `osx-arm64`; `llvm` stands for clang-format and clang-tidy, which `tools/CheckToolchain.cs -- --llvm` both check. A change to a pin is a reviewed change of its own, and every tool is registered under C-0003 before it is pinned.

[solution-and-build.md](../docs/design/solution-and-build.md) explains the build files, and [repository-checks.md](../docs/design/repository-checks.md) the check files and how to change a pin.
