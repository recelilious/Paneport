// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Logging;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.UI.Tests;

// C-0010 section 2: a log line holds no personal data, and the text of an exception or of a
// binding error's message can hold a local path.
public sealed class AvaloniaLogForwarderTests
{
    // Built in memory, so that no test file holds a local path (C-0011 section 2).
    private static readonly string s_profileFile = string.Join('\\', "C:", "Users", "someone", "settings.json");

    [AvaloniaFact]
    public void AnExceptionShowsOnlyItsType()
    {
        var log = new RecordingLog();

        new AvaloniaLogForwarder(log).Log(
            LogEventLevel.Error,
            LogArea.Control,
            null,
            "Tab transition threw an unhandled exception: {Exception}",
            Thrown(new IOException($"Access to the path '{s_profileFile}' is denied.")));

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Equal("Avalonia Control", line.Source);
        Assert.Equal("Tab transition threw an unhandled exception: IOException", line.Message);
    }

    [AvaloniaFact]
    public void ABindingErrorKeepsItsBindingButNotItsMessage()
    {
        var log = new RecordingLog();

        new AvaloniaLogForwarder(log).Log(
            LogEventLevel.Warning,
            LogArea.Binding,
            new TextBlock(),
            "An error occurred binding {Property} to {Expression} at {ExpressionErrorPoint}: {Message}",
            TextBlock.TextProperty,
            "CurrentPage.Header",
            "CurrentPage",
            $"Could not find file '{s_profileFile}'.");

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("Avalonia Binding", line.Source);
        Assert.Equal("An error occurred binding Text to CurrentPage.Header at CurrentPage: {Message} (TextBlock)", line.Message);
    }

    [AvaloniaFact]
    public void OtherTextKeepsItsHoleAndObjectsShowTheirType()
    {
        var log = new RecordingLog();

        new AvaloniaLogForwarder(log).Log(
            LogEventLevel.Warning,
            LogArea.Property,
            null,
            "Error setting {Property} on {Target}: {Exception}; {Note}, {Count}, {Flag}, {Kind}, {Missing}",
            TextBlock.TextProperty,
            new Border(),
            Thrown(new InvalidOperationException(s_profileFile)),
            "text that names " + s_profileFile,
            3,
            true,
            LogEventLevel.Warning,
            null);

        var line = Assert.Single(log.Lines);
        Assert.Equal("Error setting Text on Border: InvalidOperationException; {Note}, 3, True, Warning, null", line.Message);
        Assert.DoesNotContain("someone", line.Message, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public void LinesBelowWarningLevelAreNotForwarded()
    {
        var log = new RecordingLog();
        var forwarder = new AvaloniaLogForwarder(log);

        forwarder.Log(LogEventLevel.Information, LogArea.Layout, null, "Measured {Count} controls", 3);

        Assert.False(forwarder.IsEnabled(LogEventLevel.Information, LogArea.Layout));
        Assert.True(forwarder.IsEnabled(LogEventLevel.Warning, LogArea.Layout));
        Assert.Empty(log.Lines);
    }

    // A thrown exception carries a stack trace, which the forwarder must not write either.
    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }
}
