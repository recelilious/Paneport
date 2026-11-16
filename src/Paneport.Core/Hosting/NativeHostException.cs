// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// A call into the native host failed. An <see cref="INativeHost"/> implementation throws it with
/// the name of the host's status and the host's own message, which is fixed text that holds no
/// path or secret.
/// </summary>
public sealed class NativeHostException : Exception
{
    /// <summary>Creates an exception without a status.</summary>
    public NativeHostException()
    {
    }

    /// <summary>Creates an exception with a message and no status.</summary>
    /// <param name="message">What went wrong.</param>
    public NativeHostException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public NativeHostException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception for a status that the host returned.</summary>
    /// <param name="status">The name of the status, such as <c>InvalidArgument</c>.</param>
    /// <param name="message">The host's message for it.</param>
    /// <param name="innerException">The exception of the bindings, or null.</param>
    public NativeHostException(string status, string message, Exception? innerException)
        : base(message, innerException)
    {
        Status = status;
    }

    /// <summary>Gets the name of the status that the host returned, or null when there was none.</summary>
    public string? Status { get; }
}
