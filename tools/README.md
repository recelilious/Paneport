<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tools

C# file-based tools that check the repository and build its release artifacts (C-0004 section 3, C-0011 section 2). Each tool is one file, which may include the source files of `Shared/`, uses only the base class library, and runs from the repository root with the SDK that `global.json` pins.

| Tool | What it checks or builds | Configuration |
|---|---|---|
| `ScanPersonalData.cs` | Personal data in `docs/evidence/`, `compat/` and `tests/`, and files that C-0002 section 6 forbids anywhere | `eng/personal-data-allow-list.json` |
| `CheckLicenses.cs` | That every package, tool, action and submodule is covered by the register in C-0003 section 6 | `eng/license-map.json` |
| `CheckToolchain.cs` | The installed SDK against `global.json` and the macOS jobs' runner image against `eng/toolchain.json`; with `--ci-tools` also the tools of the `checks` job, with `--xcode` Xcode, with `--ios-workload` the installed iOS workload set, with `--cmake` CMake, with `--llvm` clang-format and clang-tidy, and with `--fork-tools` the build tools of the forks (C-0009 section 2) | `global.json`, `eng/toolchain.json`, `.github/workflows/` |
| `ScanPackage.cs` | App packages, such as the `.ipa` files of the `ios` job: it fails on a provisioning profile; on a team identifier or an application-identifier prefix in a property list or in a code signature, of which it reads the code directories (slots 0 and 0x1000 to 0x1004), the entitlements in XML and in DER (slots 5 and 7), the launch constraints (slots 8 to 11) and the CMS blob (slot 0x10000), but not the requirements (slot 2), slots 0x10001 and 0x10002, or the code directory's identifier and hashes; on a property list, signature or DER that it cannot read, and on a Mach-O file of a kind that it does not parse; on a package without an app; and on a package that it cannot read, such as one with an entry of more than 256 MiB. It lists the entitlements it finds. This is the part of the scan of C-0009 section 6 that finds what an Apple account leaves; its UDIDs, ECIDs, serial numbers, e-mail addresses and local paths come with M9.1 (S9.1.3), and from then on the `ios` job runs the whole scan (C-0011 section 4) | None; the packages are its arguments |
| `GenerateNotices.cs` | Writes the third-party notices of an app bundle into its `licenses/` folder before it is signed, and into a build output folder: `THIRD_PARTY_NOTICES.md`, the license texts and the index of the licenses screen (C-0003 section 3, C-0009 section 6). It attributes every file of the bundle to Paneport or to register components, the main executable to the .NET runtime, .NET for iOS and every package of the app's lock file, and fails on a component that the register lacks or marks Under review, Forbidden or Tool, on a missing license text, on a package license that the register row does not name, on a dropped Apache-2.0 NOTICE file, on an FTL component without its credit and on an LGPL component other than SteamKit2 in the main executable. A file that it cannot attribute is a warning with `--mode ios`, as the `ios` job runs it, and a failure with `--mode release` | `eng/release/bundle-components.json`, `eng/license-map.json`, the register, `LICENSES/MIT.txt` |

Run a tool, or its self-test over synthetic fixtures:

```bash
dotnet run tools/ScanPersonalData.cs
```

```bash
dotnet run tools/ScanPersonalData.cs -- --self-test
```

`ScanPackage.cs` takes the packages to scan as its arguments, after `--`, and `--self-test` builds its packages in memory.

`GenerateNotices.cs` takes a mode, the app's project and its bundle: `dotnet run tools/GenerateNotices.cs -- --mode ios --project src/Paneport.iOS/Paneport.iOS.csproj --app <bundle.app> --out build/notices/main`. Its `--self-test` builds its fixture bundles in a temporary folder.

A tool exits with 0 when its check passes, 1 when it fails, and 2 for a wrong argument or a file it cannot read. Findings name the file and the line, never the value they matched.

`Shared/` holds source files that tools include with `#:include`: `Register.cs`, the parser of the register in C-0003 section 6, and `LicenseMap.cs`, the parser of `eng/license-map.json`, which `CheckLicenses.cs` and `GenerateNotices.cs` both include. A shared file carries no `#:` directive, because the SDK would apply it to every tool that includes the file; a tool that includes one starts with a `#!` line, which the analyzer asks of a program of several files. `CheckLicenses.cs` checks both rules.

`packages.lock.json` is the lock file that the tools share. It lists no package, because each tool turns off `PublishAot` with a `#:property` directive and uses nothing beyond the base class library. A tool that ever needs a package gets a lock file of its own, `packages.<Tool>.cs.lock.json`, which NuGet then uses in place of the shared one; `CheckLicenses.cs` requires it.

[repository-checks.md](../docs/design/repository-checks.md) describes every rule, the mapping policy, the pins, the CI jobs, how to run each check locally, and the planted-failure procedure, and [release-engineering.md](../docs/design/release-engineering.md) the notices generator.
