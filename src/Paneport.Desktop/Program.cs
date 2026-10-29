// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Paneport.UI;

namespace Paneport.Desktop;

internal static class Program
{
    // Avalonia's Windows backend needs a single-threaded apartment on the UI thread.
    [STAThread]
    public static int Main(string[] args)
    {
        var lifetime = new ClassicDesktopStyleApplicationLifetime
        {
            Args = args,
            ShutdownMode = ShutdownMode.OnMainWindowClose,
        };

        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .SetupWithLifetime(lifetime);

        // The window belongs to this head, so Paneport.UI never depends on a desktop concept.
        lifetime.MainWindow = CompositionRoot.CreateMainWindow();
        return lifetime.Start(args);
    }
}
