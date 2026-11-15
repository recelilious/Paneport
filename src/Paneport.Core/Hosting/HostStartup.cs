// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Hosting;

/// <summary>
/// Starts the native host at app launch, on the main thread: the interface version check, then
/// <c>pp_init</c> with the crash record folder, then the handlers (docs/design/ios-app-head.md
/// section 3).
/// </summary>
public static class HostStartup
{
    private const string LogSource = nameof(HostStartup);

    /// <summary>
    /// Checks the host's major version and starts it. On a mismatch the host is not started, and
    /// the result carries the message that the app shows instead of the shell (C-0005 section 1).
    /// Main thread only.
    /// </summary>
    /// <param name="host">The host's calls.</param>
    /// <param name="crashRecordFolder">The absolute path of the folder for crash records, which exists.</param>
    /// <param name="dispatcher">Runs the host's events on the UI thread.</param>
    /// <param name="log">The log.</param>
    /// <returns>The running host, or the message to show.</returns>
    public static HostStartResult Start(INativeHost host, string crashRecordFolder, IUiDispatcher dispatcher, ILog log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(crashRecordFolder);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(log);

        HostAbiVersion version;
        try
        {
            version = host.ReadAbiVersion();
        }
        catch (Exception exception)
        {
            // The library or its export is missing, as when the framework did not resolve.
            log.Write(LogLevel.Error, LogSource, $"Reading the host's interface version failed with {exception.GetType().Name}.");
            return HostStartResult.Failure(
                "Paneport cannot start: the native host could not be loaded. Install the app again.");
        }

        var expected = host.BindingsVersion;
        if (!version.IsCompatibleWith(expected))
        {
            log.Write(LogLevel.Error, LogSource, $"The host implements interface version {version}; the app needs {expected.Major}.x.");
            return HostStartResult.Failure(
                $"Paneport cannot start: the native host implements interface version {version}, "
                    + $"but this build of the app needs version {expected.Major}.x. Install a matching build.");
        }

        try
        {
            host.Initialize(crashRecordFolder);
        }
        catch (Exception exception)
        {
            // The host's messages are fixed text without a path; anything else shows its type only.
            var detail = exception is NativeHostException { Status: { } status } hostException
                ? $"{status}: {hostException.Message}"
                : exception.GetType().Name;
            log.Write(LogLevel.Error, LogSource, $"The host did not start ({detail}).");
            return HostStartResult.Failure("Paneport cannot start: the native host did not start.");
        }

        var runtimeHost = new NativeRuntimeHost(host, version, dispatcher, log);
        try
        {
            runtimeHost.Attach();
        }
        catch (Exception exception)
        {
            log.Write(LogLevel.Error, LogSource, $"Setting the host's handlers failed with {exception.GetType().Name}.");
            return HostStartResult.Failure("Paneport cannot start: the native host did not accept its handlers.");
        }

        log.Write(LogLevel.Information, LogSource, $"The host started: interface version {version}, import {host.ImportName}.");
        return HostStartResult.Success(runtimeHost);
    }
}
