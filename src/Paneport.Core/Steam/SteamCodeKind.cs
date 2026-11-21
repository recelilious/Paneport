// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// Where the Steam Guard code that a sign-in asks for comes from.
/// </summary>
public enum SteamCodeKind
{
    /// <summary>No code is asked for.</summary>
    None,

    /// <summary>A code from the Steam Guard authenticator in the Steam mobile app.</summary>
    Authenticator,

    /// <summary>A code that Steam sent to the account's email address.</summary>
    Email,
}
