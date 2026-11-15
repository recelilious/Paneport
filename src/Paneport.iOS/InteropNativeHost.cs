// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Interop;

namespace Paneport.IOS;

// The host's calls through Paneport.Interop, as Paneport.Core's INativeHost: the bindings of
// C-0005, with their records mapped to Paneport.Core's and a HostException turned into a
// NativeHostException, which carries the status and the host's own fixed message.
internal sealed class InteropNativeHost : INativeHost
{
    public string ImportName => $"Framework: {NativeHost.LibraryName}";

    public HostAbiVersion BindingsVersion { get; } = new(HostAbi.Major, HostAbi.Minor);

    public HostAbiVersion ReadAbiVersion() => HostAbiVersion.FromPacked(NativeHost.ReadAbiVersion());

    public void Initialize(string crashRecordFolder) => Call(() => NativeHost.Initialize(crashRecordFolder));

    public void SetEventHandler(Action<HostEventInfo>? handler) => Call(() => NativeHost.SetEventHandler(
        handler is null ? null : hostEvent => handler(new HostEventInfo(hostEvent.Kind, hostEvent.Code, hostEvent.Handle, hostEvent.Address))));

    // HostLogLevel has the values of Paneport.Core's LogLevel (interop.md section 4).
    public void SetLogHandler(Action<HostLogEntry>? handler) => Call(() => NativeHost.SetLogHandler(
        handler is null ? null : line => handler(new HostLogEntry((LogLevel)(int)line.Level, line.ThreadId, line.Text))));

    public void SetCallbackFailureHandler(Action<Exception>? handler) => NativeHost.SetCallbackFailureHandler(handler);

    public MemoryBudget GetMemoryBudget()
    {
        var budget = Call(NativeHost.GetMemoryBudget);
        return new MemoryBudget(budget.LimitBytes, budget.AvailableBytes, budget.FootprintBytes, budget.GameModeLevelReached);
    }

    public string ReadDeviceModel() => Call(NativeHost.ReadDeviceModel);

    private static void Call(Action call)
    {
        try
        {
            call();
        }
        catch (HostException exception)
        {
            throw new NativeHostException(exception.Status.ToString(), exception.Message, exception);
        }
    }

    private static T Call<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (HostException exception)
        {
            throw new NativeHostException(exception.Status.ToString(), exception.Message, exception);
        }
    }
}
