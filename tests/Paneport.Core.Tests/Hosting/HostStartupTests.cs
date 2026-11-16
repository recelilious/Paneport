// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Hosting;

// S1.3.2: the startup sequence of the iOS head, with a fake host: the major version check stops
// startup with its message, and the host's events reach the view models through the UI dispatcher.
public sealed class HostStartupTests
{
    private const string CrashFolder = "/container/Library/Caches/Paneport";

    [Fact]
    public void AMajorMismatchStopsStartupWithItsMessage()
    {
        var host = new FakeNativeHost { Version = new(2, 0), BindingsVersion = new(1, 1) };
        var log = new RecordingLog();

        var result = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), log);

        Assert.False(result.Started);
        Assert.Null(result.Host);
        Assert.Contains("interface version 2.0", result.FailureMessage, StringComparison.Ordinal);
        Assert.Contains("needs version 1.x", result.FailureMessage, StringComparison.Ordinal);
        Assert.Equal([nameof(INativeHost.ReadAbiVersion)], host.Calls);
        Assert.Null(host.CrashRecordFolder);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Error && line.Source == nameof(HostStartup));
    }

    [Fact]
    public void AMatchingMajorVersionStartsTheHostThenSetsTheHandlers()
    {
        var host = new FakeNativeHost { Version = new(1, 4), BindingsVersion = new(1, 1) };

        var result = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), new RecordingLog());

        Assert.True(result.Started);
        Assert.Null(result.FailureMessage);
        Assert.Equal(CrashFolder, host.CrashRecordFolder);
        Assert.Equal(
            [
                nameof(INativeHost.ReadAbiVersion),
                nameof(INativeHost.Initialize),
                nameof(INativeHost.SetCallbackFailureHandler),
                nameof(INativeHost.SetLogHandler),
                nameof(INativeHost.SetEventHandler),
            ],
            host.Calls);
        Assert.Equal(new HostAbiVersion(1, 4), result.Host!.AbiVersion);
        Assert.Equal(new HostAbiVersion(1, 1), result.Host.BindingsVersion);
        Assert.Equal(NativeRuntimeHost.NativeImplementationName, result.Host.ImplementationName);
        Assert.Equal("Framework", result.Host.ImportName);
    }

    [Fact]
    public void AnOlderMinorHostStartsAndShowsTheModelAsUnknown()
    {
        // C-0005 section 1 counts a 1.0 host as compatible with bindings of 1.1, but it lacks
        // pp_device_get_model, which 1.1 added: the app starts, and the model reads as unknown.
        var host = new FakeNativeHost { Version = new(1, 0), BindingsVersion = new(1, 1) };
        var fakes = new FakeServices();

        var result = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), fakes.Log);
        var device = new FakeDeviceInfo { ModelIdentifier = DeviceModel.Read(host, fakes.Log) };
        var page = new DiagnosticsViewModel(
            result.Host!,
            new DiagnosticsSources(fakes.Memory, device, fakes.Build, [], null, () => []),
            fakes.Log);

        Assert.True(result.Started);
        Assert.Equal("Compatible: the app's bindings use version 1.1", page.AbiCompatibility);
        Assert.Contains(new DiagnosticsRow("Model identifier", DeviceModel.Unknown), page.DeviceRows);
        Assert.Contains(fakes.Log.Lines, line => line.Message == "Reading the model identifier failed (Unsupported).");
    }

    [Fact]
    public void AHostThatDoesNotStartShowsAMessageAndLogsItsStatus()
    {
        var host = new FakeNativeHost
        {
            InitializeFailure = new NativeHostException("InvalidArgument", "pp_init needs an absolute folder.", null),
        };
        var log = new RecordingLog();

        var result = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), log);

        Assert.False(result.Started);
        Assert.Equal("Paneport cannot start: the native host did not start.", result.FailureMessage);
        Assert.DoesNotContain(nameof(INativeHost.SetEventHandler), host.Calls);
        Assert.Contains(log.Lines, line => line.Message.Contains("InvalidArgument: pp_init needs an absolute folder.", StringComparison.Ordinal));
    }

    [Fact]
    public void AHostThatCannotBeLoadedShowsAMessageAndLogsTheTypeOnly()
    {
        var host = new FakeNativeHost { ReadVersionFailure = new DllNotFoundException("a message that could name a path") };
        var log = new RecordingLog();

        var result = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), log);

        Assert.False(result.Started);
        Assert.Contains("could not be loaded", result.FailureMessage, StringComparison.Ordinal);
        var line = Assert.Single(log.Lines);
        Assert.Contains(nameof(DllNotFoundException), line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("path", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EventsReachTheViewModelsOnlyThroughTheUiDispatcher()
    {
        var host = new FakeNativeHost();
        var dispatcher = new FakeUiDispatcher();
        var fakes = new FakeServices();
        var runtimeHost = HostStartup.Start(host, CrashFolder, dispatcher, fakes.Log).Host!;
        var diagnostics = new DiagnosticsViewModel(runtimeHost, fakes.ToDiagnosticsSources(), fakes.Log);
        diagnostics.NotifyShown();

        // The host event thread sends the event; nothing reaches the page until the UI thread runs.
        var sender = new Thread(() => host.EventHandler!(new HostEventInfo(Kind: 7, Code: 42, Handle: 3, Address: 0)));
        sender.Start();
        sender.Join();

        Assert.Equal(1, dispatcher.Pending);
        Assert.Equal("None yet", diagnostics.HostEvents);
        Assert.Equal(0, runtimeHost.EventCount);

        Assert.Equal(1, dispatcher.RunPending());
        Assert.Equal("1 received; the last: kind 7, code 42", diagnostics.HostEvents);
        Assert.Equal(new HostEventInfo(7, 42, 3, 0), runtimeHost.LastEvent);
    }

    [Fact]
    public void AHiddenPageStopsFollowingTheHostsEvents()
    {
        var host = new FakeNativeHost();
        var dispatcher = new FakeUiDispatcher();
        var fakes = new FakeServices();
        var runtimeHost = HostStartup.Start(host, CrashFolder, dispatcher, fakes.Log).Host!;
        var diagnostics = new DiagnosticsViewModel(runtimeHost, fakes.ToDiagnosticsSources(), fakes.Log);
        diagnostics.NotifyShown();
        diagnostics.NotifyHidden();

        host.EventHandler!(new HostEventInfo(1, 2, 0, 0));
        dispatcher.RunPending();

        Assert.Equal("None yet", diagnostics.HostEvents);
        Assert.Equal(1, runtimeHost.EventCount);
    }

    [Fact]
    public void HostLogLinesGoToTheLogFromTheEventThread()
    {
        var host = new FakeNativeHost();
        var log = new RecordingLog();
        HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), log);

        var sender = new Thread(() => host.LogHandler!(new HostLogEntry(LogLevel.Warning, 99, "the event queue overflowed once")));
        sender.Start();
        sender.Join();

        Assert.Contains(new LogLine(LogLevel.Warning, "NativeHost", "the event queue overflowed once"), log.Lines);
    }

    [Fact]
    public void AFailingHandlerIsLoggedWithItsTypeOnly()
    {
        var host = new FakeNativeHost();
        var log = new RecordingLog();
        HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), log);

        host.FailureHandler!(new InvalidOperationException("a message that could name a path"));

        var line = Assert.Single(log.Lines, line => line.Level == LogLevel.Error);
        Assert.Equal("A host callback failed with InvalidOperationException.", line.Message);
    }

    [Fact]
    public void TheAdapterReadsTheBudgetFromTheHost()
    {
        var budget = new MemoryBudget(6_144UL << 20, 5_000UL << 20, 1_144UL << 20, gameModeLevelReached: false);
        var host = new FakeNativeHost { Budget = budget };

        var runtimeHost = HostStartup.Start(host, CrashFolder, new FakeUiDispatcher(), new RecordingLog()).Host!;

        Assert.Equal(budget, runtimeHost.GetMemoryBudget());
    }
}
