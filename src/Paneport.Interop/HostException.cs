// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// A call into the native host failed. The message is the host's own, from
/// <c>pp_last_error</c>, read on the thread that made the call.
/// </summary>
public sealed class HostException : Exception
{
    /// <summary>Creates an exception without a status.</summary>
    public HostException()
    {
    }

    /// <summary>Creates an exception with a message and no status.</summary>
    /// <param name="message">What went wrong.</param>
    public HostException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message, the exception that caused it, and no status.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public HostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for a status that the host returned.</summary>
    /// <param name="status">The status.</param>
    /// <param name="message">The host's message for it.</param>
    public HostException(HostStatus status, string message)
        : base(message)
    {
        Status = status;
    }

    /// <summary>Gets the status that the host returned, or <see cref="HostStatus.Ok"/> when there was none.</summary>
    public HostStatus Status { get; }
}
