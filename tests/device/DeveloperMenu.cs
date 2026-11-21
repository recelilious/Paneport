// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Paneport.Core.ViewModels;
using Paneport.DeviceTest;
using Paneport.Steam;

namespace Paneport.IOS;

// The device test flavor's part of the composition root: the Developer section with its menu,
// whose pages later stages extend (plan section 7.3). The main app compiles the same composition
// root without this file, so it has no Developer section.
internal static partial class CompositionRoot
{
    // The name under which Steam lists the gate's sign-ins: fixed text, never the device's name.
    private const string SteamDeviceName = "Paneport (iPhone)";

    static partial void AddDeveloperMenu(DeveloperContext context, ref Func<INavigator, PageViewModel>? developerPage)
    {
        if (Application.Current is { } application)
        {
            DeveloperViews.Register(application);
        }

        var services = context.Services;
        var log = services.Log;
        var keychain = new KeychainItemStore();

        // One signal gate page for the life of the app: a run outlives the page that started it,
        // so the page opened next shows that run, its Stop and its verdict, instead of a new page
        // that cannot reach it (signals.md section 9).
        var signalGate = new SignalGateViewModel(services.Diagnostics.CrashChaining, log);

        // The Steam service starts with the Steam gate page's first opening, never at launch, and
        // follows the app's returns to the foreground from then on (steam-service.md). One page for
        // the life of the app keeps its results and its summary when the tester looks elsewhere.
        var steam = new Lazy<SteamServices>(() =>
        {
            var created = SteamServices.Create(new SteamServiceOptions
            {
                Log = log,
                Tokens = services.Tokens,
                DeviceName = SteamDeviceName,
                DownloadsFolder = context.DownloadsFolder,
                MachineIdentity = MachineIdentity.LoadOrCreateAsync(keychain, CancellationToken.None).GetAwaiter().GetResult(),
            });
            context.Lifecycle.EnteredForeground += (_, _) => created.Service.NotifyForegroundReturned();
            return created;
        });
        var steamGate = new Lazy<SteamGateViewModel>(() => new SteamGateViewModel(
            steam.Value.Service,
            steam.Value.Gate,
            services.Diagnostics.Build,
            services.Diagnostics.Memory,
            new AvaloniaUiDispatcher(),
            log));

        developerPage = navigator => new DeveloperMenuViewModel(
            navigator,
            [
                new DeveloperEntry(
                    "Interface conformance",
                    "Compares every struct of the host interface between the host and Paneport.Interop (C-0005 section 5).",
                    () => DeveloperTools.InterfaceConformance(log)),
                new DeveloperEntry(
                    "Token store",
                    "Writes, reads and deletes a test item in the keychain, under a key that is never the sign-in's.",
                    () => DeveloperTools.TokenStore(keychain, log)),
                new DeveloperEntry(
                    "Crash record",
                    "Writes a crash record marked as a test, which Diagnostics shows after the next launch.",
                    () => DeveloperTools.CrashRecord(log)),
                new DeveloperEntry(
                    "Signal gate",
                    "Runs the signal dispatcher's stress test for four hours, or for ten minutes, and gives the verdict of L2 step 13.",
                    () => signalGate),
                new DeveloperEntry(
                    "Steam gate",
                    "Signs in to Steam by approval, by code or by QR code, then reads licenses and app information, gets a depot key and a manifest request code, and downloads a manifest and verified chunks (L2 steps 16 to 28).",
                    () => steamGate.Value),
            ],
            log);
    }
}
