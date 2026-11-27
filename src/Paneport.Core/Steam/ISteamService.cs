// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The Steam service as the shell sees it: the session with Steam and the ways to sign in
/// (C-0007 sections 2 and 3).
/// </summary>
/// <remarks>
/// <para>
/// Paneport.Steam implements it on SteamKit2 (<c>docs/design/steam-service.md</c>);
/// <see cref="SignedOutSteamService"/> stands in where no Steam service runs. Nothing it reports
/// holds a password, token, key, code or Steam ID; the account name is passed in once, at sign-in,
/// and never reported back.
/// </para>
/// <para>
/// Members may be called from any thread. <see cref="StatusChanged"/> is raised on whichever
/// thread changed the status, so a view model posts its update to the UI thread.
/// </para>
/// </remarks>
public interface ISteamService
{
    /// <summary>Gets whether the user is signed in to Steam.</summary>
    SteamSignInState SignInState { get; }

    /// <summary>Gets the session's state, whether a sign-in is saved, and how the connection runs.</summary>
    SteamSessionStatus Status { get; }

    /// <summary>Raised after <see cref="Status"/> changed, on the thread that changed it.</summary>
    event EventHandler? StatusChanged;

    /// <summary>
    /// Logs on with the saved sign-in, when there is one and the session is not logged on yet.
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the result.</param>
    /// <returns>The result; <see cref="SignInOutcome.NothingSaved"/> when no sign-in is saved.</returns>
    Task<SignInResult> ResumeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Starts signing in with an account name and a password. The service drops its reference to
    /// the password when the attempt ends; the caller drops its own at once.
    /// </summary>
    /// <param name="accountName">The Steam account name.</param>
    /// <param name="password">The password, which goes only to Valve's servers (C-0007 section 1).</param>
    /// <param name="confirmation">How the user confirms the sign-in.</param>
    /// <returns>The attempt, already running.</returns>
    ISignInAttempt StartPasswordSignIn(string accountName, string password, SignInConfirmation confirmation);

    /// <summary>Starts signing in with a QR code that the Steam mobile app on another device scans.</summary>
    /// <returns>The attempt, already running.</returns>
    ISignInAttempt StartQrSignIn();

    /// <summary>
    /// Signs out: logs off, disconnects and deletes the saved sign-in (C-0007 section 3).
    /// </summary>
    /// <param name="cancellationToken">Stops waiting for the disconnection; the sign-in is deleted first.</param>
    /// <returns>A task that completes when the session is signed out.</returns>
    Task SignOutAsync(CancellationToken cancellationToken);
}
