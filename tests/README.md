<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests

Paneport's automated tests (C-0011 section 1). They use xunit.v3 on Microsoft.Testing.Platform, and hand-written fakes instead of mocking libraries.

| Folder | What it holds |
|---|---|
| [Paneport.Core.Tests](Paneport.Core.Tests/README.md) | Tests of Paneport.Core |
| [Paneport.Interop.Tests](Paneport.Interop.Tests/README.md) | Tests of Paneport.Interop, which run on macOS against the host test library, outside `eng/ci.slnf` (C-0011 section 4) |
| [Paneport.Steam.Tests](Paneport.Steam.Tests/README.md) | Tests of Paneport.Steam |
| [Paneport.TrimTest](Paneport.TrimTest/README.md) | A program whose trimmed publish checks our libraries together with their dependencies. It holds no tests, and `dotnet test` skips it. |
| [Paneport.UI.Tests](Paneport.UI.Tests/README.md) | Tests of Paneport.UI, including headless tests of the Avalonia application |
| [native](native/README.md) | The CTest suite of the native host, which runs on macOS with the sanitizer presets `asan-ubsan` and `tsan` |
| [Shared](Shared/README.md) | Source files that several test projects compile in |

Run the tests of `eng/ci.slnf`, the ones that run on any machine, from the repository root:

```bash
dotnet test eng/ci.slnf -c Release
```

`global.json` makes `dotnet test` use Microsoft.Testing.Platform. [solution-and-build.md](../docs/design/solution-and-build.md) explains the command and the dependency direction tests that each project carries.

Everything in this folder passes the personal-data scan of `tools/ScanPersonalData.cs` (C-0011 section 2). Tests never keep e-mail addresses, IP addresses, tokens or other values that it looks for as literal strings; they build such values in memory. [repository-checks.md](../docs/design/repository-checks.md) lists the rules.

`eng/ci.slnf` covers the .NET tests that run on Windows and Linux. The `native` CI job runs `native` and `Paneport.Interop.Tests` on macOS; [native-host.md](../docs/design/native-host.md) section 11 and [interop.md](../docs/design/interop.md) section 8 say how to run them on a Mac.

The last folder of the C-0004 layout, `device`, comes with M1.3.
