// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// How <see cref="HostStartup.Start"/> ended: the host runs, or the app shows a message instead
/// of the shell.
/// </summary>
public sealed class HostStartResult
{
    private HostStartResult(NativeRuntimeHost? host, string? failureMessage)
    {
        Host = host;
        FailureMessage = failureMessage;
    }

    /// <summary>Gets the running host, or null when the start failed.</summary>
    public NativeRuntimeHost? Host { get; }

    /// <summary>Gets the message that the app shows instead of the shell, or null when the host runs.</summary>
    public string? FailureMessage { get; }

    /// <summary>Gets a value indicating whether the host runs.</summary>
    public bool Started => Host is not null;

    /// <summary>Creates the result of a start that worked.</summary>
    /// <param name="host">The running host.</param>
    /// <returns>The result.</returns>
    public static HostStartResult Success(NativeRuntimeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return new(host, null);
    }

    /// <summary>Creates the result of a start that failed.</summary>
    /// <param name="message">What the app shows instead of the shell.</param>
    /// <returns>The result.</returns>
    public static HostStartResult Failure(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        return new(null, message);
    }
}
