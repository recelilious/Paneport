<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Core

The domain model, the view models, the settings, the compatibility database reader, and the interfaces that platform code implements (C-0004 section 2). It uses no platform API.

| Folder or file | What it holds |
|---|---|
| `Library/` | Titles: their identity, install state and launch path, and the library service interface |
| `Hosting/` | The runtime host interface, the host ABI version and the memory budget |
| `Steam/` | The Steam service interface and the signed-out stub that the heads use until M2.3 |
| `Settings/` | The app settings, the locales on offer and the settings store interface |
| `Security/` | The secret value type, whose text form is redacted, and the token store interface |
| `Logging/` | The logging interface |
| `ViewModels/` | The shell, its navigation and the Library, Title details, Settings and Diagnostics pages, made with the CommunityToolkit.Mvvm generators |
| `AppServices.cs` | The services that an app head provides to the shell |

- **Depends on:** nothing else in the solution, and the CommunityToolkit.Mvvm package.
- **Build:** a library with `IsAotCompatible`, so the trimming and AOT analyzers check it, and with an XML documentation file, so a public API without a documentation comment fails the build (C-0010 sections 2 and 5).
- **Tests:** [Paneport.Core.Tests](../../tests/Paneport.Core.Tests/README.md).
- **Design:** [app-shell.md](../../docs/design/app-shell.md). [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution.
