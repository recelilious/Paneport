<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# Paneport.UI.Tests

Tests of [Paneport.UI](../../src/Paneport.UI/README.md), including headless tests that run the Avalonia application without a screen (C-0011 section 1).

| File | What it tests |
|---|---|
| `TestAppBuilder.cs` | Not a test. It tells Avalonia.Headless.XUnit to start the real Paneport application on the headless platform for every `[AvaloniaFact]` test. |
| `HeadlessApplicationTests.cs` | The application starts on the headless platform and shows a window. |
| `DependencyDirectionTests.cs` | Paneport.UI references no project other than Paneport.Core, and this test project references only Paneport.UI and its dependencies (C-0004 section 2). |

M0.3 adds the tests of the shell and its pages.
