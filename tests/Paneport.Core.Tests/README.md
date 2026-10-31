<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Core.Tests

Tests of [Paneport.Core](../../src/Paneport.Core/README.md).

| Files | What they test |
|---|---|
| `TitleIdTests.cs`, `TitleTests.cs`, `InstallStateTests.cs` | Identities and slugs follow C-0008 section 1 and nothing but ASCII digits counts in an app ID, a local title never takes the Dock path, and download progress stays within 0 to 1. |
| `HostAbiVersionTests.cs`, `MemoryBudgetTests.cs` | The ABI version decodes as C-0005 section 1 packs it and a different major version is incompatible, and the memory budget keeps its four values. |
| `SecretTests.cs` | The secret value type's text forms, and a log line that holds one, never contain the secret. |
| `SettingsModelTests.cs`, `AppServicesTests.cs`, `SignedOutSteamServiceTests.cs` | The settings and locales, the services record, and the Steam stub. |
| `PageViewModelTests.cs`, `ShellViewModelTests.cs` | Page loading and its failures; navigating to every page and back; choosing the marked section; and a newer navigation while a page loads, whose cancelled load ends quietly. |
| `LibraryViewModelTests.cs`, `TitleViewModelTests.cs`, `TitleDetailsViewModelTests.cs` | Listing, choosing and showing titles, with exact download percentages. |
| `SettingsViewModelTests.cs`, `DiagnosticsViewModelTests.cs` | A setting that round-trips through the store, a choice made while the page loads, a save that a newer change supersedes, and the host's values with the Game Mode level reached and not reached. |
| `FakeNavigator.cs` | Not a test. A navigator that records the pages it shows. |
| `Compat/TitleSchemaTests.cs` | The title schema read as plain JSON: draft 2020-12, the fields of C-0008, no `Steam` variable on the environment allow-list, the target devices of `compat-db.md`, and the lists, defaults and names that the code checks. |
| `Compat/TitleEntryJsonTests.cs` | Every valid fixture loads, every invalid fixture is rejected for the rule it breaks, and loading, writing and loading again keeps every value. |
| `Compat/CompatPrecedenceTests.cs`, `Compat/LaunchRuleTests.cs` | Each precedence rule of C-0008 section 1 with both exceptions, how the layers merge, and the launch rule for Steam and local titles. |
| `Compat/DeviceLayerStoreTests.cs`, `Compat/CompatDatabaseTests.cs` | The device layer store in a temporary folder: one file per title named after its id, and an atomic replace that leaves the old file when a write fails. The database over both layers, and every file in `compat/titles/`. |
| `Compat/Fixtures/` | Not tests. Synthetic title files, valid and invalid, for each layer, with made-up titles and values only (C-0008 section 5). `TitleEntryJsonTests` lists each invalid one with the rule it breaks. |
| `Compat/TestEntries.cs`, `Compat/CompatTestFiles.cs`, `Compat/TemporaryFolder.cs` | Not tests. Entries made in code, the places of the fixtures and of the repository files that the project copies into its output, and a temporary folder. |
| `DependencyDirectionTests.cs` | Paneport.Core references no other project of the solution, and this test project references Paneport.Core only (C-0004 section 2). |

The fakes of the services come from [Shared](../Shared/README.md).
