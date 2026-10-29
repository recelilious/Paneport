// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using Avalonia.Logging;
using Paneport.Core.Logging;

namespace Paneport.Desktop;

// Passes what Avalonia logs at warning level or above, such as a binding that fails at run time,
// to the head's log, so that a run from a terminal shows it. Without a sink, Avalonia drops it.
internal sealed class AvaloniaLogForwarder : ILogSink
{
    private readonly ILog _log;

    public AvaloniaLogForwarder(ILog log)
    {
        _log = log;
    }

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Forward(level, area, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Forward(level, area, messageTemplate, propertyValues);

    private void Forward(LogEventLevel level, string area, string messageTemplate, object?[] propertyValues)
    {
        if (IsEnabled(level, area))
        {
            _log.Write(
                level >= LogEventLevel.Error ? LogLevel.Error : LogLevel.Warning,
                $"Avalonia {area}",
                Render(messageTemplate, propertyValues));
        }
    }

    // Fills the {Name} holes of Avalonia's message template with the values, in order.
    private static string Render(string messageTemplate, object?[] propertyValues)
    {
        var text = new StringBuilder();
        var position = 0;
        foreach (var value in propertyValues)
        {
            var open = messageTemplate.IndexOf('{', position);
            var close = open < 0 ? -1 : messageTemplate.IndexOf('}', open);
            if (close < 0)
            {
                break;
            }

            text.Append(messageTemplate, position, open - position)
                .Append(Convert.ToString(value, CultureInfo.InvariantCulture));
            position = close + 1;
        }

        return text.Append(messageTemplate, position, messageTemplate.Length - position).ToString();
    }
}
