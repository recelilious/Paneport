// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The session with Steam as a page shows it. It holds no secret and no personal data.
/// </summary>
/// <param name="State">Where the session stands.</param>
/// <param name="SignInSaved">Whether a sign-in is saved in the token store; null until the service has looked.</param>
/// <param name="Transport">How the connection to Steam's servers runs.</param>
/// <param name="Problem">
/// What went wrong last, in words that name no secret, such as a logon result; null when nothing did.
/// </param>
public sealed record SteamSessionStatus(
    SteamSessionState State,
    bool? SignInSaved,
    SteamTransport Transport,
    string? Problem)
{
    /// <summary>Gets the status of a session that has not started.</summary>
    public static SteamSessionStatus Initial { get; } =
        new(SteamSessionState.SignedOut, SignInSaved: null, SteamTransport.None, Problem: null);
}
