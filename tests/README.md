<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests

Paneport's automated tests (C-0011 section 1). They use xunit.v3 on Microsoft.Testing.Platform, and hand-written fakes instead of mocking libraries.

| Folder | What it holds |
|---|---|
| [Paneport.Core.Tests](Paneport.Core.Tests/README.md) | Tests of Paneport.Core |
| [Paneport.Steam.Tests](Paneport.Steam.Tests/README.md) | Tests of Paneport.Steam |
| [Paneport.TrimTest](Paneport.TrimTest/README.md) | A program whose trimmed publish checks our libraries together with their dependencies. It holds no tests, and `dotnet test` skips it. |
| [Paneport.UI.Tests](Paneport.UI.Tests/README.md) | Tests of Paneport.UI, including headless tests of the Avalonia application |
| [Shared](Shared/README.md) | Source files that several test projects compile in |

Run every test from the repository root:

```bash
dotnet test eng/ci.slnf -c Release
```

`global.json` makes `dotnet test` use Microsoft.Testing.Platform. [solution-and-build.md](../docs/design/solution-and-build.md) explains the command and the dependency direction tests that each project carries.

Everything in this folder passes the personal-data scan of `tools/ScanPersonalData.cs` (C-0011 section 2). Tests never keep e-mail addresses, IP addresses, tokens or other values that it looks for as literal strings; they build such values in memory. [repository-checks.md](../docs/design/repository-checks.md) lists the rules.

The other folders of the C-0004 layout come with later stages: `Paneport.Interop.Tests` and `native` with M1.2, and `device` with M1.3.

Planted for the M0.2 check run, not real data: planted.tester@example.com

[Planted broken link](planted-missing-file.md)
