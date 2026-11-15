// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Hosting;

/// <summary>
/// The host adapter: the native host of C-0005, through Paneport.Interop, as the app's
/// <see cref="IRuntimeHost"/>. <see cref="HostStartup"/> creates it once the host has started.
/// </summary>
/// <remarks>
/// The host's handlers run on the host event thread. They never wait for the UI thread: an event
/// is posted to it through the <see cref="IUiDispatcher"/>, and a log line goes to the log, which
/// accepts any thread (interop.md section 5).
/// </remarks>
public sealed class NativeRuntimeHost : IRuntimeHost
{
    /// <summary>The name that Diagnostics shows for the native host.</summary>
    public const string NativeImplementationName = "Native host (paneport_host)";

    private const string LogSource = "NativeHost";

    private readonly INativeHost _host;
    private readonly IUiDispatcher _dispatcher;
    private readonly ILog _log;

    /// <summary>Creates the adapter over a host that has started.</summary>
    /// <param name="host">The host's calls.</param>
    /// <param name="abiVersion">The version that the host reported at startup.</param>
    /// <param name="dispatcher">Runs work on the UI thread.</param>
    /// <param name="log">The log, which receives the host's lines and the handlers' failures.</param>
    public NativeRuntimeHost(INativeHost host, HostAbiVersion abiVersion, IUiDispatcher dispatcher, ILog log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(log);
        _host = host;
        _dispatcher = dispatcher;
        _log = log;
        AbiVersion = abiVersion;
    }

    /// <inheritdoc />
    public event EventHandler<HostEventInfo>? EventReceived;

    /// <inheritdoc />
    public string ImplementationName => NativeImplementationName;

    /// <inheritdoc />
    public HostAbiVersion AbiVersion { get; }

    /// <inheritdoc />
    public HostAbiVersion BindingsVersion => _host.BindingsVersion;

    /// <inheritdoc />
    public string ImportName => _host.ImportName;

    /// <inheritdoc />
    public int EventCount { get; private set; }

    /// <inheritdoc />
    public HostEventInfo? LastEvent { get; private set; }

    /// <inheritdoc />
    public MemoryBudget GetMemoryBudget() => _host.GetMemoryBudget();

    /// <summary>
    /// Sets the host's handlers: failures and log lines go to the log, events to the UI thread.
    /// Main thread only, after the host started.
    /// </summary>
    internal void Attach()
    {
        _host.SetCallbackFailureHandler(exception =>
            _log.Write(LogLevel.Error, LogSource, $"A host callback failed with {exception.GetType().Name}."));
        _host.SetLogHandler(line => _log.Write(line.Level, LogSource, line.Text));
        _host.SetEventHandler(hostEvent => _dispatcher.Post(() => Deliver(hostEvent)));
    }

    // Runs on the UI thread, through the dispatcher.
    private void Deliver(HostEventInfo hostEvent)
    {
        EventCount++;
        LastEvent = hostEvent;
        EventReceived?.Invoke(this, hostEvent);
    }
}
