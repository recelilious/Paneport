// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.IOS;

// The log when the log file cannot be opened: the lines go to standard output, which a debugger
// or the device's console shows. They hold no secret (C-0010 section 2).
internal sealed class ConsoleLog : ILog
{
    public void Write(LogLevel level, string source, string message) =>
        Console.WriteLine($"{level} {source}: {message}");
}
