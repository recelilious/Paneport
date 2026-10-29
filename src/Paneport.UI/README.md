<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.UI

The Avalonia views, styles and in-game overlay controls that the desktop and iOS heads share (C-0004 section 2).

| File | What it is |
|---|---|
| `App.axaml`, `App.axaml.cs` | The application class with the Fluent theme, the shell's styles, and the data templates that match each page view model to its view. Each head creates its own application lifetime and its own top-level view, because the desktop head shows a window and the iOS head shows a single main view. |
| `Views/MainView.axaml` | The shell's view: the navigation bar, the Back button and heading, and the page shown. Each head hosts it as its main view. |
| `Views/LibraryPage.axaml`, `Views/TitleDetailsPage.axaml` | The Library page, which lists the titles, and the Title details page. |
| `Views/SettingsPage.axaml`, `Views/DiagnosticsPage.axaml` | The Settings page and the Diagnostics page. |

- **Depends on:** Paneport.Core only, and the Avalonia and Avalonia.Themes.Fluent packages.
- **Build:** a library with `IsAotCompatible` and an XML documentation file (C-0010 sections 2 and 5). XAML and its bindings are compiled at build time, and nothing is loaded or looked up by name at run time.
- **Tests:** [Paneport.UI.Tests](../../tests/Paneport.UI.Tests/README.md) runs the shell on Avalonia's headless platform.
- **Design:** [app-shell.md](../../docs/design/app-shell.md). [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution.
