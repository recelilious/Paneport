// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia;
using Paneport.Core;
using Paneport.Core.ViewModels;
using Paneport.DeviceTest;

namespace Paneport.IOS;

// The device test flavor's part of the composition root: the Developer section with its menu,
// whose pages later stages extend (plan section 7.3). The main app compiles the same composition
// root without this file, so it has no Developer section.
internal static partial class CompositionRoot
{
    static partial void AddDeveloperMenu(AppServices services, ref Func<INavigator, PageViewModel>? developerPage)
    {
        if (Application.Current is { } application)
        {
            DeveloperViews.Register(application);
        }

        var log = services.Log;
        var keychain = new KeychainItemStore();

        // One signal gate page for the life of the app: a run outlives the page that started it,
        // so the page opened next shows that run, its Stop and its verdict, instead of a new page
        // that cannot reach it (signals.md section 9).
        var signalGate = new SignalGateViewModel(services.Diagnostics.CrashChaining, log);
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
            ],
            log);
    }
}
