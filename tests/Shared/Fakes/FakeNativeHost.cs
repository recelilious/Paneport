// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;

namespace Paneport.Tests.Fakes;

// The host's calls as Paneport.Interop offers them, recorded in order. A test sets the version
// the host reports and can make a call fail; it reaches the handlers that the adapter set, to
// send an event or a log line from any thread as the host event thread does.
internal sealed class FakeNativeHost : INativeHost
{
    private readonly List<string> _calls = [];

    public string ImportName { get; init; } = "Framework";

    public HostAbiVersion BindingsVersion { get; init; } = new(1, 1);

    public HostAbiVersion Version { get; set; } = new(1, 1);

    public Exception? ReadVersionFailure { get; set; }

    public Exception? InitializeFailure { get; set; }

    public string? CrashRecordFolder { get; private set; }

    public Action<HostEventInfo>? EventHandler { get; private set; }

    public Action<HostLogEntry>? LogHandler { get; private set; }

    public Action<Exception>? FailureHandler { get; private set; }

    public MemoryBudget Budget { get; set; }

    public string Model { get; set; } = "iPhone18,1";

    public IReadOnlyList<string> Calls
    {
        get
        {
            lock (_calls)
            {
                return [.. _calls];
            }
        }
    }

    public HostAbiVersion ReadAbiVersion()
    {
        Record(nameof(ReadAbiVersion));
        return ReadVersionFailure is { } failure ? throw failure : Version;
    }

    public void Initialize(string crashRecordFolder)
    {
        Record(nameof(Initialize));
        if (InitializeFailure is { } failure)
        {
            throw failure;
        }

        CrashRecordFolder = crashRecordFolder;
    }

    public void SetEventHandler(Action<HostEventInfo>? handler)
    {
        Record(nameof(SetEventHandler));
        EventHandler = handler;
    }

    public void SetLogHandler(Action<HostLogEntry>? handler)
    {
        Record(nameof(SetLogHandler));
        LogHandler = handler;
    }

    public void SetCallbackFailureHandler(Action<Exception>? handler)
    {
        Record(nameof(SetCallbackFailureHandler));
        FailureHandler = handler;
    }

    public MemoryBudget GetMemoryBudget() => Budget;

    // A host older than the version that added pp_device_get_model lacks the function, so the
    // bindings' call throws EntryPointNotFoundException, which the adapter guards as the iOS
    // head's does.
    public string ReadDeviceModel() => HostExports.Call(
        DeviceModel.AddedIn,
        () => Version.Minor >= DeviceModel.AddedIn.Minor ? Model : throw new EntryPointNotFoundException("pp_device_get_model"));

    private void Record(string call)
    {
        lock (_calls)
        {
            _calls.Add(call);
        }
    }
}
