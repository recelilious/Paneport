// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The Steam service as the shell sees it (C-0007 section 2).
/// </summary>
/// <remarks>
/// For now it only reports whether the user is signed in, and both app heads use
/// <see cref="SignedOutSteamService"/>. M2.3 implements sign-in in Paneport.Steam behind this
/// interface.
/// </remarks>
public interface ISteamService
{
    /// <summary>Gets whether the user is signed in to Steam.</summary>
    SteamSignInState SignInState { get; }
}
