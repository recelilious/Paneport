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
| `CheckToolchain.cs` | The installed SDK against `global.json` and the macOS jobs' runner image against `eng/toolchain.json`; with `--ci-tools` also the tools of the `checks` job, with `--xcode` Xcode, with `--ios-workload` the installed iOS workload set, with `--cmake` CMake, with `--llvm` clang-format and clang-tidy, and with `--fork-tools` the build tools of the forks (C-0009 section 2) | `global.json`, `eng/toolchain.json`, `.github/workflows/` |
| `ScanPackage.cs` | App packages, such as the `.ipa` files of the `ios` job: it fails on a provisioning profile; on a property list or a code signature's entitlements, XML or DER, that name a team identifier or an application-identifier prefix, and on a code directory or CMS blob that names a team; on a property list or signature that it cannot read, and on a Mach-O file of a kind that it does not parse; and on a package without an app. It lists the entitlements it finds. This is the part of the scan of C-0009 section 6 that finds what an Apple account leaves; its UDIDs, ECIDs, serial numbers, e-mail addresses and local paths come with M9.1 (S9.1.3), and from then on the `ios` job runs the whole scan (C-0011 section 4) | None; the packages are its arguments |

Run a tool, or its self-test over synthetic fixtures:

```bash
dotnet run tools/ScanPersonalData.cs
```

```bash
dotnet run tools/ScanPersonalData.cs -- --self-test
```

`ScanPackage.cs` takes the packages to scan as its arguments, after `--`, and `--self-test` builds its packages in memory.

A tool exits with 0 when its check passes, 1 when it fails, and 2 for a wrong argument or a file it cannot read. Findings name the file and the line, never the value they matched.

`packages.lock.json` is the lock file that the four tools share. It lists no package, because each tool turns off `PublishAot` with a `#:property` directive and uses nothing beyond the base class library. A tool that ever needs a package gets a lock file of its own, `packages.<Tool>.cs.lock.json`, which NuGet then uses in place of the shared one; `CheckLicenses.cs` requires it.

[repository-checks.md](../docs/design/repository-checks.md) describes every rule, the mapping policy, the pins, the CI jobs, how to run each check locally, and the planted-failure procedure.
