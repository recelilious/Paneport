<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tools

C# file-based tools that check the repository (C-0004 section 3, C-0011 section 2). Each tool is one file, uses only the base class library, and runs from the repository root with the SDK that `global.json` pins.

| Tool | What it checks | Configuration |
|---|---|---|
| `ScanPersonalData.cs` | Personal data in `docs/evidence/`, `compat/` and `tests/`, and files that C-0002 section 6 forbids anywhere | `eng/personal-data-allow-list.json` |
| `CheckLicenses.cs` | That every package, tool, action and submodule is covered by the register in C-0003 section 6 | `eng/license-map.json` |
| `CheckToolchain.cs` | The installed SDK against `global.json` and the macOS jobs' runner image against `eng/toolchain.json`; with `--ci-tools` also the tools of the `checks` job, with `--xcode` Xcode, and with `--ios-workload` the installed iOS workload set (C-0009 section 2) | `global.json`, `eng/toolchain.json`, `.github/workflows/` |

Run a tool, or its self-test over synthetic fixtures:

```bash
dotnet run tools/ScanPersonalData.cs
```

```bash
dotnet run tools/ScanPersonalData.cs -- --self-test
```

A tool exits with 0 when its check passes, 1 when it fails, and 2 for a wrong argument or a file it cannot read. Findings name the file and the line, never the value they matched.

`packages.lock.json` is the lock file that the three tools share. It lists no package, because each tool turns off `PublishAot` with a `#:property` directive and uses nothing beyond the base class library. A tool that ever needs a package gets a lock file of its own, `packages.<Tool>.cs.lock.json`, which NuGet then uses in place of the shared one; `CheckLicenses.cs` requires it.

[repository-checks.md](../docs/design/repository-checks.md) describes every rule, the mapping policy, the pins, the CI jobs, how to run each check locally, and the planted-failure procedure.
