// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Tests.Fakes;

// A log that records its lines, so that a test can check what was written and what was not.
internal sealed class RecordingLog : ILog
{
    private readonly Lock _lock = new();
    private readonly List<LogLine> _lines = [];

    public IReadOnlyList<LogLine> Lines
    {
        get
        {
            lock (_lock)
            {
                return [.. _lines];
            }
        }
    }

    public void Write(LogLevel level, string source, string message)
    {
        lock (_lock)
        {
            _lines.Add(new LogLine(level, source, message));
        }
    }
}
