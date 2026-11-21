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
| `Views/SteamGatePage.axaml` | The Steam gate page of L2 steps 16 to 28, which only the developer entries of the heads open (M2.3). |
| `Controls/QrCodeView.cs` | Draws a text as a QR code with Net.Codecrete.QrCodeGenerator, black on white inside the quiet zone, and never shows the text (plan decision D13). |
| `ViewTemplates.cs` | Makes the data templates of views built in code, such as the device test flavor's developer pages. Each matches an instance of its view model type and never null, which Avalonia's generic `FuncDataTemplate<T>` would match ([app-shell.md](../../docs/design/app-shell.md) section 5). |
| `AvaloniaLogForwarder.cs` | Passes what Avalonia logs at warning level or above, such as a binding that fails at run time, to the app's log, without the exception text or error message that could name a local path. A head installs it as Avalonia's log sink. |

- **Depends on:** Paneport.Core only, and the Avalonia, Avalonia.Themes.Fluent and Net.Codecrete.QrCodeGenerator packages.
- **Build:** a library with `IsAotCompatible` and an XML documentation file (C-0010 sections 2 and 5). XAML and its bindings are compiled at build time, and nothing is loaded or looked up by name at run time.
- **Tests:** [Paneport.UI.Tests](../../tests/Paneport.UI.Tests/README.md) runs the shell on Avalonia's headless platform.
- **Design:** [app-shell.md](../../docs/design/app-shell.md). [solution-and-build.md](../../docs/design/solution-and-build.md) records its role in the solution.
