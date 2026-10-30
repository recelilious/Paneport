// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Logging;
using Paneport.Core.Logging;

namespace Paneport.UI;

/// <summary>
/// Passes what Avalonia logs at warning level or above, such as a binding that fails at run time,
/// to the app's log. Without a sink, Avalonia drops these lines.
/// </summary>
/// <remarks>
/// A log line never holds personal data (C-0010 section 2), but Avalonia's values can: an
/// exception's message and stack trace, and a binding error's message, can hold a local path. So
/// the forwarder writes Avalonia's message template with only those values whose type cannot
/// carry such text: a property's or a type's name, a number, a Boolean, an enum value, and the
/// property and binding path of a binding error, which come from our own XAML. An exception or any
/// other object is written as its type's name, and any other text keeps its hole, such as
/// <c>{Message}</c>.
/// </remarks>
public sealed class AvaloniaLogForwarder : ILogSink
{
    // The holes of Avalonia's binding errors that hold a property name or a binding path.
    private static readonly HashSet<string> s_pathHoles = ["Property", "Expression", "ExpressionErrorPoint"];

    private readonly ILog _log;

    /// <summary>Creates a forwarder to the app's log.</summary>
    /// <param name="log">The log that receives Avalonia's lines.</param>
    public AvaloniaLogForwarder(ILog log)
    {
        ArgumentNullException.ThrowIfNull(log);
        _log = log;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    /// <inheritdoc />
    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Forward(level, area, source, messageTemplate, []);

    /// <inheritdoc />
    public void Log(
        LogEventLevel level,
        string area,
        object? source,
        string messageTemplate,
        params object?[] propertyValues) =>
        Forward(level, area, source, messageTemplate, propertyValues);

    private void Forward(LogEventLevel level, string area, object? source, string messageTemplate, object?[] propertyValues)
    {
        if (!IsEnabled(level, area))
        {
            return;
        }

        var message = Render(messageTemplate, propertyValues);
        if (source is not null)
        {
            message = $"{message} ({source.GetType().Name})";
        }

        _log.Write(level >= LogEventLevel.Error ? LogLevel.Error : LogLevel.Warning, $"Avalonia {area}", message);
    }

    // Fills the holes of the template with the values, in order, as far as Describe allows.
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

            var hole = messageTemplate[open..(close + 1)];
            text.Append(messageTemplate, position, open - position)
                .Append(Describe(HoleName(hole), value) ?? hole);
            position = close + 1;
        }

        return text.Append(messageTemplate, position, messageTemplate.Length - position).ToString();
    }

    // A hole such as {$Value} or {Value:l} is named Value.
    private static string HoleName(string hole)
    {
        var name = hole[1..^1].TrimStart('@', '$');
        var end = name.AsSpan().IndexOfAny(':', ',');
        return end < 0 ? name : name[..end];
    }

    // The text that may stand for a value, or null when the value's text could carry personal data.
    private static string? Describe(string holeName, object? value) => value switch
    {
        null => "null",
        Exception exception => exception.GetType().Name,
        AvaloniaProperty property => property.Name,
        Type type => type.Name,
        string text => s_pathHoles.Contains(holeName) ? text : null,
        bool or Enum or byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal =>
            Convert.ToString(value, CultureInfo.InvariantCulture),
        _ => value.GetType().Name,
    };
}
