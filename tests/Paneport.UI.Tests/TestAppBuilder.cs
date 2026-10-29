// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Headless;
using Paneport.UI;
using Paneport.UI.Tests;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

// AvaloniaWarnings installs a sink in Avalonia's global logger for the length of a test, so tests
// in this assembly must not run at the same time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Paneport.UI.Tests;

// Every [AvaloniaFact] test runs inside the real Paneport application on the headless platform.
public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
