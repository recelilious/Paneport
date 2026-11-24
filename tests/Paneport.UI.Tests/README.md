<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.UI.Tests

Tests of [Paneport.UI](../../src/Paneport.UI/README.md), including headless tests that run the Avalonia application without a screen (C-0011 section 1).

| File | What it tests |
|---|---|
| `TestAppBuilder.cs` | Not a test. It tells Avalonia.Headless.XUnit to start the real Paneport application on the headless platform for every `[AvaloniaFact]` test, and runs the tests one at a time, because `AvaloniaWarnings` uses Avalonia's global logger. |
| `ShellHarness.cs` | Not a test. It shows the shell the way the desktop head does, over the fakes from [Shared](../Shared/README.md), and clicks controls, the navigation bar's buttons among them, with the mouse. |
| `AvaloniaWarnings.cs` | Not a test. It records what Avalonia logs at warning level or above, which includes every binding that fails at run time; each headless test ends by asserting that nothing was recorded. |
| `MainViewTests.cs` | The shell shows its navigation entries, marks Library and opens on it, and every page view model has its view. |
| `LibraryPageTests.cs` | The Library lists every title with its state and launch path, wraps the long name, and opens a title's details, from which Back returns, and so does choosing Library in the navigation bar. |
| `SettingsAndDiagnosticsPageTests.cs` | Settings shows the locale and the Steam state, keeps the locale choice disabled until it has loaded, and keeps a changed locale after another page was opened. Opening another section while Settings loads raises nothing. Diagnostics shows the host's values. |
| `DiagnosticsPageTests.cs` | S1.3.7: the Diagnostics page, rendered headless, shows every section, the warning for a limit far below the expected one, and a crash record until it is dismissed; no forbidden item reaches its text, though the sample profile carries a team identifier, an application-identifier prefix and a device identifier. |
| `ViewTemplatesTests.cs` | A view template never matches null ([app-shell.md](../../docs/design/app-shell.md) section 5). `ViewTemplates.For<T>` matches an instance of its type only. The Settings page, shown while its settings load or with a saved locale that it does not offer, builds no application-level template for null; Avalonia's generic `FuncDataTemplate<T>`, the negative control, is built for null inside the locale box and ends the layout with a `NullReferenceException`, as it ended the device test flavor on the device. No template of the application or of a page matches null, and no source in `src/` or `tests/device` names the generic template. |
| `HeadlessApplicationTests.cs` | The application starts on the headless platform and shows a window, and Avalonia logs no warning. |
| `AvaloniaLogForwarderTests.cs` | The forwarder of Avalonia's warnings writes an exception as its type only, keeps a binding error's property and path but not its message, and forwards nothing below warning level. The values name a local path that the test builds in memory. |
| `DependencyDirectionTests.cs` | Paneport.UI references no project other than Paneport.Core, and this test project references only Paneport.UI and its dependencies (C-0004 section 2). |
