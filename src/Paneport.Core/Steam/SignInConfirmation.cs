// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// How the user confirms a sign-in with an account name and a password (C-0007 section 3).
/// </summary>
public enum SignInConfirmation
{
    /// <summary>
    /// Approve the sign-in in the Steam mobile app, which works on the same phone: the default. If
    /// the account offers no approval, Steam asks for a code instead.
    /// </summary>
    ApproveInSteamApp,

    /// <summary>Enter a Steam Guard code from the authenticator or from email: the fallback.</summary>
    EnterCode,
}
