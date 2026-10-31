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
| `toolchain.json` | The pinned versions of the tools that `global.json` does not pin (C-0009 section 2): the REUSE tool, gitleaks and lychee for the `checks` job, and Xcode, the iOS workload set and the macOS runner image for the `ios` job. |
| `license-map.json` | The reviewed mapping from each package ID, tool, action and submodule to the row of the C-0003 register that covers it, which `tools/CheckLicenses.cs` reads. |
| `personal-data-allow-list.json` | The exceptions to the personal-data scan of `tools/ScanPersonalData.cs`: each entry excuses one rule in one file and states why. It is empty. |
| `gitleaks.toml` | The gitleaks configuration. It extends gitleaks' default rules, and is where a false positive is allowed, with its reason. |
| `lychee.toml` | The lychee configuration: which files are checked, and how failed requests are retried. |
| `mac/bootstrap.sh` | Prepares a Mac to build the iOS side, in CI and on a local Mac: it checks the SDK and Xcode against their pins, installs the iOS workload from the pinned workload set and checks it, and exits non-zero after saying what to do when something is missing or does not match. It runs on Apple silicon only, with the Bash 3.2 that macOS ships (C-0010 section 1), and never touches certificates, profiles or pairing files. M1.2 adds the CMake and LLVM steps. |

`toolchain.json` holds one entry per tool under `tools`, keyed by the tool's name, and nothing else. Each entry has the exact `version` and the `source` it is installed from. A tool that CI downloads also has `downloads`, keyed by runtime identifier, each with the download's `url` and `sha256`. `tools/CheckToolchain.cs` rejects any other key or field, and `tools/CheckLicenses.cs` any other key. The `macos-runner-image` entry names the image by the label that the macOS jobs give `runs-on`, such as `xcode-27`, and the toolchain check fails when a macOS job runs on another one. The `ios-workload-set` entry is the workload set that the iOS workload is installed from, such as `10.0.403`; `global.json` cannot hold it, because the SDK then fails every build on a machine without that workload set ([dev-environment.md](../docs/design/dev-environment.md) section 2). A change to a pin is a reviewed change of its own, and every tool is registered under C-0003 before it is pinned. M1.2 adds CMake and the LLVM release of clang-format and clang-tidy.

[solution-and-build.md](../docs/design/solution-and-build.md) explains the build files, and [repository-checks.md](../docs/design/repository-checks.md) the check files and how to change a pin.
