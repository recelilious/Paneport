// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The Steam service stub that both app heads use until M2.3 brings the real service. It never
/// signs in.
/// </summary>
public sealed class SignedOutSteamService : ISteamService
{
    /// <summary>Gets <see cref="SteamSignInState.SignedOut"/>, always.</summary>
    public SteamSignInState SignInState => SteamSignInState.SignedOut;
}
