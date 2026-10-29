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
| `toolchain.json` | The pinned versions of the CI tools (C-0009 section 2). So far it pins only the REUSE tool. |

`toolchain.json` holds one entry per tool under `tools`, keyed by the tool's name. Each entry has the exact `version` and the `source` it is installed from. Later stages add tools and fields, such as a download's SHA-256 (M0.2) and the Xcode version (M1.1). A change to a pin is a reviewed change of its own, and every tool is registered under C-0003 before it is pinned.

[solution-and-build.md](../docs/design/solution-and-build.md) explains how these files are used.
