<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.Desktop

The Avalonia desktop head, for working on the Paneport UI from Windows without a device (C-0004 section 2). It is never shipped, and it never writes a Steam sign-in token to disk.

| File or folder | What it is |
|---|---|
| `Program.cs` | The entry point. It creates the desktop application lifetime, so that Paneport.UI holds no desktop-only concept. |
| `CompositionRoot.cs` | Wires the head by hand: the log, Paneport.UI's forwarder of Avalonia's warnings to it, the fakes, the shell over them, and the window titled Paneport that hosts the shell's view. |
| `Fakes/` | The fake runtime host, the fictional sample library, the in-memory settings and token stores, and a log sink that writes to the terminal. |

Run it from the repository root:

```bash
dotnet run --project src/Paneport.Desktop
```

The window opens on the Library page with the sample titles. Diagnostics says that the host is a fake and shows its values.

- **Depends on:** Paneport.Core, Paneport.Steam and Paneport.UI, and the Avalonia.Desktop package.
- **Build:** a console executable, so that a run from a terminal shows the app's log there, with Avalonia's warnings and errors, and any unhandled exception.
- **Design:** [app-shell.md](../../docs/design/app-shell.md) lists the sample titles and the fake host's values. [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution.
