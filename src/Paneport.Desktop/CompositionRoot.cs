// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Logging;
using Paneport.Core;
using Paneport.Core.Steam;
using Paneport.Core.ViewModels;
using Paneport.Desktop.Fakes;
using Paneport.UI;
using Paneport.UI.Views;

namespace Paneport.Desktop;

// Wires the desktop head by hand: the log, the fakes, the shell over them, and the window that
// hosts the shell's view. The iOS head will do the same with the device implementations.
internal static class CompositionRoot
{
    public static Window CreateMainWindow()
    {
        var log = new ConsoleLog();

        // Set before any view exists, so that a binding error in a page reaches the terminal.
        Logger.Sink = new AvaloniaLogForwarder(log);

        var services = new AppServices(
            host: new FakeRuntimeHost(),
            library: new SampleLibrary(),
            steam: new SignedOutSteamService(),
            settings: new InMemorySettingsStore(),
            tokens: new InMemoryTokenStore(),
            log: log);
        var shell = new ShellViewModel(services);

        var window = new Window
        {
            Title = "Paneport",
            Width = 1024,
            Height = 720,
            MinWidth = 400,
            MinHeight = 400,
            Content = new MainView { DataContext = shell },
        };

        // Starting once the window is open runs the first load on the UI thread, where the view
        // models live.
        window.Opened += (_, _) => shell.StartCommand.Execute(null);
        return window;
    }
}
