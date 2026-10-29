// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Logging;

namespace Paneport.Desktop.Fakes;

// The desktop head's log sink: it records each line in the terminal that runs the head.
// Console writes are thread-safe, so any thread may log.
internal sealed class ConsoleLog : ILog
{
    public void Write(LogLevel level, string source, string message) =>
        Console.Out.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{DateTime.UtcNow:HH:mm:ss} {level,-11} {source}: {message}"));
}
