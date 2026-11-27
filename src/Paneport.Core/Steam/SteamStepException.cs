// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// A step of the Steam service that could not run, with a message that names no secret, no path
/// and no personal data, so that a page may show it.
/// </summary>
public sealed class SteamStepException : Exception
{
    /// <summary>Creates the exception with a generic message.</summary>
    public SteamStepException()
        : base("The Steam step could not run.")
    {
    }

    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong, naming no secret.</param>
    public SteamStepException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with the failure that caused it.</summary>
    /// <param name="message">What went wrong, naming no secret.</param>
    /// <param name="innerException">The failure, whose message is never shown.</param>
    public SteamStepException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
