// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;

namespace Paneport.IOS;

// What the composition root made at launch, once per process: the shell over the device's
// services with the start of the run, or the message that the root view controller shows instead
// of the shell when the native host did not start. The start belongs here rather than to a view
// controller, because iOS may connect the scene again in the same process with a new controller,
// which must not start a second memory timer or load the shell again. The lifecycle carries the
// scene's returns to the foreground to the services that follow them.
internal sealed class AppComposition
{
    private AppComposition(ShellViewModel? shell, AppStart? start, string? failureMessage, ILog log, AppLifecycle lifecycle)
    {
        Shell = shell;
        Start = start;
        FailureMessage = failureMessage;
        Log = log;
        Lifecycle = lifecycle;
    }

    public ShellViewModel? Shell { get; }

    // The start of the run, which keeps the memory timeline's timer; null when the app did not start.
    public AppStart? Start { get; }

    public string? FailureMessage { get; }

    public ILog Log { get; }

    public AppLifecycle Lifecycle { get; }

    public static AppComposition Running(ShellViewModel shell, AppStart start, ILog log, AppLifecycle lifecycle) =>
        new(shell, start, null, log, lifecycle);

    public static AppComposition Failed(string message, ILog log) => new(null, null, message, log, new AppLifecycle());
}
