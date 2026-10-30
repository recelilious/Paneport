// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// Whether the user is signed in to Steam.
/// </summary>
public enum SteamSignInState
{
    /// <summary>The user is not signed in to Steam.</summary>
    SignedOut,

    /// <summary>The user is signed in to Steam.</summary>
    SignedIn,
}
