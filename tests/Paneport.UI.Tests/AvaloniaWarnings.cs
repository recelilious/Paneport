// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Avalonia.Logging;

namespace Paneport.UI.Tests;

// Records what Avalonia logs at warning level or above while it is installed. Avalonia reports a
// binding that fails at run time through its logger, in the Binding area, so a test that ends with
// no recorded entry also had no binding error. The logger is global; TestAppBuilder.cs therefore
// turns off parallel test runs in this assembly.
internal sealed class AvaloniaWarnings : ILogSink, IDisposable
{
    private readonly ILogSink? _previous;
    private readonly List<string> _entries = [];

    private AvaloniaWarnings()
    {
        _previous = Logger.Sink;
        Logger.Sink = this;
    }

    public IReadOnlyList<string> Entries => _entries;

    public static AvaloniaWarnings Record() => new();

    public bool IsEnabled(LogEventLevel level, string area) => level >= LogEventLevel.Warning;

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate) =>
        Add(level, area, messageTemplate, []);

    public void Log(LogEventLevel level, string area, object? source, string messageTemplate, params object?[] propertyValues) =>
        Add(level, area, messageTemplate, propertyValues);

    public void Dispose()
    {
        Logger.Sink = _previous;
    }

    private void Add(LogEventLevel level, string area, string messageTemplate, object?[] propertyValues)
    {
        if (IsEnabled(level, area))
        {
            var values = string.Join(", ", propertyValues.Select(value => Convert.ToString(value, CultureInfo.InvariantCulture)));
            _entries.Add($"{level} in {area}: {messageTemplate} [{values}]");
        }
    }
}
