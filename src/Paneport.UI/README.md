<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.UI

The Avalonia views, styles and in-game overlay controls that the desktop and iOS heads share (C-0004 section 2).

| File | What it is |
|---|---|
| `App.axaml`, `App.axaml.cs` | The application class with the Fluent theme. Each head creates its own application lifetime and its own top-level view, because the desktop head shows a window and the iOS head shows a single main view. |

- **Depends on:** Paneport.Core only, and the Avalonia and Avalonia.Themes.Fluent packages.
- **Build:** a library with `IsAotCompatible` and an XML documentation file (C-0010 sections 2 and 5). XAML is compiled at build time and never loaded at run time.
- **Tests:** [Paneport.UI.Tests](../../tests/Paneport.UI.Tests/README.md) starts this application on Avalonia's headless platform.
- **Design:** [solution-and-build.md](../../docs/design/solution-and-build.md) records its role. M0.3 adds the pages and writes the full design in `docs/design/app-shell.md`.
