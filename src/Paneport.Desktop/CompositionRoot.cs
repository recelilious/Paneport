// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Logging;
using Avalonia.Threading;
using Paneport.Core;
using Paneport.Core.Diagnostics;
using Paneport.Core.ViewModels;
using Paneport.Desktop.Fakes;
using Paneport.Steam;
using Paneport.UI;
using Paneport.UI.Views;

namespace Paneport.Desktop;

// Wires the desktop head by hand: the log, the fakes, the shell over them, and the window that
// hosts the shell's view. The iOS head does the same with the device implementations.
internal static class CompositionRoot
{
    // The name under which Steam lists the desktop run's sign-ins: fixed text, never the computer's name.
    private const string SteamDeviceName = "Paneport (desktop head)";

    public static Window CreateMainWindow()
    {
        var log = new ConsoleLog();

        // Set before any view exists, so that a binding error in a page reaches the terminal.
        Logger.Sink = new AvaloniaLogForwarder(log);

        var host = new FakeRuntimeHost();
        var memory = new MemoryMonitor(host, new DesktopDevice(), TimeProvider.System, log);
        var diagnostics = new DiagnosticsSources(
            memory,
            new DesktopDevice(),
            DesktopBuild,
            entitlements: [],
            crashRecords: null,
            storage: () => []);
        // The Steam service on SteamKit2, for the developer Steam page of the desktop run (L2 brief,
        // M2.3). Its sign-in stays in memory for this run and is never written to disk
        // (C-0007 section 3); SteamKit2 derives the machine identity from this computer's own.
        var tokens = new InMemoryTokenStore();
        var steam = SteamServices.Create(new SteamServiceOptions
        {
            Log = log,
            Tokens = tokens,
            DeviceName = SteamDeviceName,
            DownloadsFolder = LocalFolders.Downloads,
        });
        var services = new AppServices(
            host: host,
            library: new SampleLibrary(),
            steam: steam.Service,
            settings: new InMemorySettingsStore(),
            tokens: tokens,
            log: log,
            diagnostics: diagnostics);

        // The Developer section opens the Steam gate page; one page for the run keeps its results.
        SteamGateViewModel? steamGate = null;
        var shell = new ShellViewModel(
            services,
            _ => steamGate ??= new SteamGateViewModel(steam.Service, steam.Gate, DesktopBuild, memory, new AvaloniaUiDispatcher(), log));

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
        // models live. The memory timeline samples the fake host once a second, as the iOS head
        // samples the device, and the run starts as the iOS head's does.
        var run = new AppStart(shell, memory, (tick, interval) => DispatcherTimer.Run(tick, interval));
        window.Opened += (_, _) => run.TryStart();
        window.Closed += (_, _) => steam.Dispose();
        return window;
    }

    private static BuildStamp DesktopBuild { get; } = new(
        Commit: "None: a local desktop build",
        Version: "Desktop head",
        Configuration:
#if DEBUG
            "Debug",
#else
            "Release",
#endif
        Runtime: "CoreCLR",
        CompilationMode: "Just-in-time",
        Interpreter: "Not used",
        HostImport: "None: fake host");
}
