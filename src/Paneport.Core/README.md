<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Core

The domain model, the view models, the settings, the compatibility database reader, and the interfaces that platform code implements (C-0004 section 2). It uses no platform API.

| Folder or file | What it holds |
|---|---|
| `Library/` | Titles: their identity, install state and launch path, the library service interface, and the empty library of the iOS head |
| `Compat/` | The compatibility database core (C-0008): the typed model of title entries, the reader and writer of title files, the precedence rules and the launch rule, the bundled snapshot, the device layer store, and the database over both |
| `Hosting/` | The runtime host interface, the host ABI version, the memory budget and iOS's thermal states; the native host's calls as an interface, the guard for functions that a later minor version added, the host adapter over them, the startup sequence and the UI dispatcher interface |
| `Steam/` | The Steam service interface and the signed-out stub that the heads use until M2.3 |
| `Settings/` | The app settings, the locales on offer, the settings store interface, and the settings file of the device |
| `Security/` | The secret value type, whose text form is redacted, the saved sign-in, the token store interface, and the token store over a secure-item interface that the keychain implements on the device |
| `Logging/` | The logging interface, and the log file of the device |
| `Storage/` | The on-device storage layout of C-0004 section 4, its bootstrap with the backup marks, and the file-system interface with its System.IO implementation |
| `Diagnostics/` | What the Diagnostics page reads: the memory timeline, the crash record reader and store, the entitlement readers for the code signature and the profile, the build stamp, the device facts with the reading of the model identifier, and what became of the call that turns off Mono's crash chaining |
| `Gates/` | The verdict of the signal gate, with the criteria of L2 step 13, the hang threshold and the heartbeat |
| `ViewModels/` | The shell, its navigation and the Library, Title details, Settings and Diagnostics pages, made with the CommunityToolkit.Mvvm generators |
| `AppServices.cs` | The services that an app head provides to the shell |
| `AppStart.cs` | The start of the run, once per process: the memory timeline's first sample and timer, then the shell's first load |

- **Depends on:** nothing else in the solution, and the CommunityToolkit.Mvvm package.
- **Build:** a library with `IsAotCompatible`, so the trimming and AOT analyzers check it, and with an XML documentation file, so a public API without a documentation comment fails the build (C-0010 sections 2 and 5).
- **Tests:** [Paneport.Core.Tests](../../tests/Paneport.Core.Tests/README.md).
- **Design:** [app-shell.md](../../docs/design/app-shell.md), [compat-db.md](../../docs/design/compat-db.md) for `Compat/`, and [ios-app-head.md](../../docs/design/ios-app-head.md) for what the iOS head uses from `Hosting/`, `Storage/`, `Settings/`, `Security/`, `Logging/` and `Diagnostics/`, and [signals.md](../../docs/design/signals.md) for `Gates/` and the crash-chaining state. [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution.
