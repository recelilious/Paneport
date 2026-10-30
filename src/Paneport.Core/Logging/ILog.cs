// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Logging;

/// <summary>
/// The logging interface of the managed code (C-0010 section 2).
/// </summary>
/// <remarks>
/// A message never contains a password, token or other personal data (C-0010 section 2), and a
/// <see cref="Security.Secret"/> written into one shows only as redacted. Implementations
/// accept calls from any thread.
/// </remarks>
public interface ILog
{
    /// <summary>Writes one log line.</summary>
    /// <param name="level">How important the line is.</param>
    /// <param name="source">The part of the app that writes it, such as a view model's name.</param>
    /// <param name="message">The message.</param>
    void Write(LogLevel level, string source, string message);
}
