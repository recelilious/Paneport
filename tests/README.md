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
| [Paneport.UI.Tests](Paneport.UI.Tests/README.md) | Tests of Paneport.UI, including headless tests of the Avalonia application |
| [Shared](Shared/README.md) | Source files that several test projects compile in |

Run every test from the repository root:

```bash
dotnet test eng/ci.slnf -c Release
```

`global.json` makes `dotnet test` use Microsoft.Testing.Platform. [solution-and-build.md](../docs/design/solution-and-build.md) explains the command and the dependency direction tests that each project carries.

The other folders of the C-0004 layout come with later stages: `Paneport.Interop.Tests` and `native` with M1.2, and `device` with M1.3.
