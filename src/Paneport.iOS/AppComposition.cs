// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;

namespace Paneport.IOS;

// What the composition root made at launch: the shell over the device's services and the memory
// timeline that the root view controller samples, or the message that it shows instead of the
// shell when the native host did not start.
internal sealed class AppComposition
{
    private AppComposition(ShellViewModel? shell, MemoryMonitor? memory, string? failureMessage, ILog log)
    {
        Shell = shell;
        Memory = memory;
        FailureMessage = failureMessage;
        Log = log;
    }

    public ShellViewModel? Shell { get; }

    public MemoryMonitor? Memory { get; }

    public string? FailureMessage { get; }

    public ILog Log { get; }

    public static AppComposition Running(ShellViewModel shell, MemoryMonitor memory, ILog log) =>
        new(shell, memory, null, log);

    public static AppComposition Failed(string message, ILog log) => new(null, null, message, log);
}
