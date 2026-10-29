<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Core.Tests

Tests of [Paneport.Core](../../src/Paneport.Core/README.md).

| Files | What they test |
|---|---|
| `TitleIdTests.cs`, `TitleTests.cs`, `InstallStateTests.cs` | Identities and slugs follow C-0008 section 1, a local title never takes the Dock path, and download progress stays within 0 to 1. |
| `HostAbiVersionTests.cs`, `MemoryBudgetTests.cs` | The ABI version decodes as C-0005 section 1 packs it and a different major version is incompatible, and the memory budget keeps its four values. |
| `SecretTests.cs` | The secret value type's text forms, and a log line that holds one, never contain the secret. |
| `SettingsModelTests.cs`, `AppServicesTests.cs`, `SignedOutSteamServiceTests.cs` | The settings and locales, the services record, and the Steam stub. |
| `PageViewModelTests.cs`, `ShellViewModelTests.cs` | Page loading and its failures, and navigating to every page and back. |
| `LibraryViewModelTests.cs`, `TitleViewModelTests.cs`, `TitleDetailsViewModelTests.cs` | Listing, choosing and showing titles. |
| `SettingsViewModelTests.cs`, `DiagnosticsViewModelTests.cs` | A setting that round-trips through the store, and the host's values with the Game Mode level reached and not reached. |
| `FakeNavigator.cs` | Not a test. A navigator that records the pages it shows. |
| `DependencyDirectionTests.cs` | Paneport.Core references no other project of the solution, and this test project references Paneport.Core only (C-0004 section 2). |

The fakes of the services come from [Shared](../Shared/README.md).
