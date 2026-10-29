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
| `toolchain.json` | The pinned versions of the CI tools (C-0009 section 2): the REUSE tool, gitleaks and lychee. |
| `license-map.json` | The reviewed mapping from each package ID, tool, action and submodule to the row of the C-0003 register that covers it, which `tools/CheckLicenses.cs` reads. |
| `personal-data-allow-list.json` | The exceptions to the personal-data scan of `tools/ScanPersonalData.cs`: each entry excuses one rule in one file and states why. It is empty. |
| `gitleaks.toml` | The gitleaks configuration. It extends gitleaks' default rules, and is where a false positive is allowed, with its reason. |
| `lychee.toml` | The lychee configuration: which files are checked, and how failed requests are retried. |

`toolchain.json` holds one entry per tool under `tools`, keyed by the tool's name, and nothing else. Each entry has the exact `version` and the `source` it is installed from. A tool that CI downloads also has `downloads`, keyed by runtime identifier, each with the download's `url` and `sha256`. `tools/CheckToolchain.cs` rejects any other key or field. A change to a pin is a reviewed change of its own, and every tool is registered under C-0003 before it is pinned. Later stages add tools, such as the Xcode version (M1.1).

[solution-and-build.md](../docs/design/solution-and-build.md) explains the build files, and [repository-checks.md](../docs/design/repository-checks.md) the check files and how to change a pin.
