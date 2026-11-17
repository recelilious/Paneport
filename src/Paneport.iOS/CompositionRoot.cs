// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Avalonia.Logging;
using Avalonia.Threading;
using Foundation;
using Paneport.Core;
using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Library;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Settings;
using Paneport.Core.Steam;
using Paneport.Core.Storage;
using Paneport.Core.ViewModels;
using Paneport.Interop;
using Paneport.UI;

namespace Paneport.IOS;

// Wires the iOS head by hand, at launch, on the main thread (ios-app-head.md section 3): the
// storage folders and the log first, so that every later step can log; the previous run's crash
// record; Avalonia's platform, without its app delegate or scene delegate; the call that turns
// off Mono's crash chaining; then the native host, whose interface version is checked before
// pp_init, which installs the signal dispatcher; and the shell over the device's services.
// The device test flavor adds its developer menu through AddDeveloperMenu, which only its own
// sources implement (tests/device).
internal static partial class CompositionRoot
{
    private const string LogSource = nameof(CompositionRoot);

    public static AppComposition Compose()
    {
        ILog log = new ConsoleLog();
        try
        {
            var root = ContainerFolders.FindRoot();
            log = OpenLog(root);
            return Compose(root, log);
        }
        catch (Exception exception)
        {
            // A failure at launch shows its message in place of the shell rather than ending the
            // app before anything is on screen. Only the type is logged; the message can hold a path.
            log.Write(LogLevel.Error, LogSource, $"Startup failed with {exception.GetType().Name}.");
            return AppComposition.Failed($"Paneport could not start ({exception.GetType().Name}).", log);
        }
    }

    private static AppComposition Compose(string root, ILog log)
    {
        // Set before any view exists, so that a binding error in a page reaches the log.
        Logger.Sink = new AvaloniaLogForwarder(log);
        log.Write(LogLevel.Information, LogSource, "Paneport is starting.");

        var files = new PhysicalFileSystem();
        var storage = new StorageBootstrap(root, files, new BackupMarks(), log);
        storage.Run();
        var crashFolder = storage.PathOf(StorageLayout.Caches);
        var crashRecords = new CrashRecordStore(crashFolder, files, log);
        crashRecords.ReadAtLaunchAsync(CancellationToken.None).GetAwaiter().GetResult();

        // Avalonia's platform starts without a lifetime: the root view controller hosts the
        // shell's AvaloniaView itself, so no app delegate or scene delegate of Avalonia's is used.
        AppBuilder.Configure<App>().UseiOS().SetupWithoutStarting();

        // With Mono's crash chaining off, a fault that Mono does not take goes to the handler it
        // found when it started, the host's last-resort handler, which writes the crash record
        // (C-0005 section 4, signals.md section 5). The runtime runs; pp_init comes next.
        var crashChaining = CrashChainingState.Run(MonoRuntime.TurnOffCrashChaining, log);

        var nativeHost = new InteropNativeHost();
        var start = HostStartup.Start(nativeHost, crashFolder, new AvaloniaUiDispatcher(), log);
        if (start.Host is not { } host)
        {
            return AppComposition.Failed(start.FailureMessage ?? "Paneport cannot start.", log);
        }

        var device = new DeviceInfo(nativeHost, log);
        var memory = new MemoryMonitor(host, device, TimeProvider.System, log);
        var entitlements = EntitlementReport.Read(OpenExecutable, ReadEmbeddedProfile, log);
        var diagnostics = new DiagnosticsSources(
            memory, device, BuildInfo.Read(), entitlements, crashRecords, storage.Inspect, crashChaining);
        var services = new AppServices(
            host: host,
            library: new EmptyLibraryService(),
            steam: new SignedOutSteamService(),
            settings: new FileSettingsStore(storage.PathOf(StorageLayout.Settings), files, log),
            tokens: new SecureTokenStore(new KeychainItemStore(), log),
            log: log,
            diagnostics: diagnostics);

        Func<INavigator, PageViewModel>? developerPage = null;
        AddDeveloperMenu(services, ref developerPage);
        var shell = new ShellViewModel(services, developerPage);
        var run = new AppStart(shell, memory, (tick, interval) => DispatcherTimer.Run(tick, interval));
        return AppComposition.Running(shell, run, log);
    }

    // The device test flavor's developer menu (tests/device). The main app has no implementation,
    // so the call compiles to nothing there.
    static partial void AddDeveloperMenu(AppServices services, ref Func<INavigator, PageViewModel>? developerPage);

    // The log file below Library/Caches/Paneport/, or the console when it cannot be opened.
    private static ILog OpenLog(string root)
    {
        try
        {
            return new FileLog(Path.Combine(StorageLayout.PathOf(root, StorageLayout.Caches), "logs"), TimeProvider.System);
        }
        catch (Exception exception)
        {
            var fallback = new ConsoleLog();
            fallback.Write(LogLevel.Error, LogSource, $"Opening the log file failed with {exception.GetType().Name}.");
            return fallback;
        }
    }

    private static Stream OpenExecutable() =>
        new FileStream(
            NSBundle.MainBundle.ExecutablePath ?? throw new FileNotFoundException("The bundle names no executable."),
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);

    // The profile that the user's sideloading tool embeds when it re-signs the app; an ad-hoc-signed
    // build from CI has none.
    private static byte[]? ReadEmbeddedProfile()
    {
        var path = Path.Combine(NSBundle.MainBundle.BundlePath, "embedded.mobileprovision");
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }
}
