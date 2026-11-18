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
| `AppStartTests.cs` | The run starts once per process: the first start samples the memory timeline, starts its timer and loads the shell, and a later start, such as from a scene that iOS connected again, does nothing. |
| `LibraryViewModelTests.cs`, `TitleViewModelTests.cs`, `TitleDetailsViewModelTests.cs` | Listing, choosing and showing titles, with exact download percentages. |
| `SettingsViewModelTests.cs`, `DiagnosticsViewModelTests.cs` | A setting that round-trips through the store, a choice made while the page loads, a save that a newer change supersedes, and the host's values with the Game Mode level reached and not reached. |
| `DiagnosticsSectionsTests.cs` | S1.3.7: the sections of the Diagnostics page over a fake host, clock and device: the host and its compatibility, the memory timeline with its warning, the entitlements, the crash record, the storage folders, the build and the device. |
| `ShellPageLifecycleTests.cs` | The shell tells a page when it starts and stops being shown, also on the way back, and a head that gives a developer menu gets a Developer section. |
| `Hosting/` | S1.3.2: the startup sequence over a fake host, with the major version check, a 1.0 host for bindings of 1.1, and the host's events reaching the view models through the UI dispatcher; the guard of functions that a later minor version added, which lets a missing library or a type that cannot load pass through; and, from the source of the iOS head's `InteropNativeHost`, which no test can run, that each binding of such a function goes through that guard, and that every member of `NativeHost` it uses is either of interface 1.0 or listed as such a function. |
| `Storage/` | S1.3.4: the layout table against C-0004 section 4 itself, and the bootstrap's folders and backup marks; and the System.IO file system in a temporary folder. |
| `Settings/` | S1.3.5: the settings file over a fake file system: a round trip, an interrupted write, an unreadable file, and saves and loads in order. |
| `Security/` | S1.3.6: the token store over a fake secure-item store, a corrupt item, and no token or account name in a log line or a text form. |
| `Diagnostics/` | S1.3.7 and S1.3.8: the memory timeline, the entitlement readers on synthetic signatures and profiles, the crash record reader and store, the model identifier's reading, and the iOS build in the system's version text. |
| `Logging/` | The log file in a temporary folder: lines from many threads, the previous run's file, the size limit, and a failed write that stops the log and drops the lines queued while it was in progress. |
| `Library/` | The empty library of the iOS head. |
| `FakeNavigator.cs` | Not a test. A navigator that records the pages it shows. |
| `Compat/TitleSchemaTests.cs` | The title schema read as plain JSON: draft 2020-12, the fields of C-0008, no `Steam` variable on the environment allow-list, the target devices of `compat-db.md`, and the lists, ranges, defaults and names that the code checks. |
| `Compat/TitleEntryJsonTests.cs` | Every valid fixture loads, every invalid fixture is rejected for the rule it breaks, and loading, writing and loading again keeps every value. |
| `Compat/CompatPathTests.cs` | The paths of the serializer's problems show no name that is not plain. No file reaches this guard, so the test calls the internal `CompatPath` directly. Paneport.Core lets this project see its internals, which the tests use only where no public path reaches what they check. |
| `Compat/CompatPrecedenceTests.cs`, `Compat/LaunchRuleTests.cs` | Each precedence rule of C-0008 section 1 with both exceptions, how the layers merge, and the launch rule for Steam and local titles. |
| `Compat/DeviceLayerStoreTests.cs`, `Compat/CompatDatabaseTests.cs` | The device layer store in a temporary folder: one file per title named after its id, and an atomic replace that leaves the old file when a write fails and deletes its temporary file when the replace fails. The database over both layers, and every file in `compat/titles/`. |
| `Compat/Fixtures/` | Not tests. Synthetic title files, valid and invalid, for each layer, with made-up titles and values only (C-0008 section 5). `TitleEntryJsonTests` lists each invalid one with the rule it breaks. |
| `Compat/TestEntries.cs`, `Compat/CompatTestFiles.cs`, `Compat/TemporaryFolder.cs` | Not tests. Entries made in code, the places of the fixtures and of the repository files that the project copies into its output, and a temporary folder. |
| `Gates/SignalGateTests.cs`, `Gates/HeartbeatMonitorTests.cs` | S2.2.6: on synthetic counters the signal gate's verdict is PASS only when every criterion of L2 step 13 holds, and each criterion alone fails it, named; the heartbeat keeps its longest gap, an open one included. |
| `Diagnostics/CrashChainingStateTests.cs` | S2.2.5: every outcome of the call that turns off Mono's crash chaining is recorded and logged, none ends the startup, and a head without the call says so. |
| `DependencyDirectionTests.cs` | Paneport.Core references no other project of the solution, and this test project references Paneport.Core only (C-0004 section 2). |

The fakes of the services come from [Shared](../Shared/README.md).
