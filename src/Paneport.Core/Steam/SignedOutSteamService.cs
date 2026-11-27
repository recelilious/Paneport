// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The Steam service of a build that has none: it never connects and never signs in. A sign-in
/// fails at once and says so.
/// </summary>
public sealed class SignedOutSteamService : ISteamService
{
    /// <summary>The problem that every sign-in reports.</summary>
    public const string NotAvailable = "Steam sign-in is not available in this build.";

    /// <inheritdoc />
    event EventHandler? ISteamService.StatusChanged
    {
        add
        {
        }

        remove
        {
        }
    }

    /// <summary>Gets <see cref="SteamSignInState.SignedOut"/>, always.</summary>
    public SteamSignInState SignInState => SteamSignInState.SignedOut;

    /// <summary>Gets a signed-out status that does not know whether a sign-in is saved.</summary>
    public SteamSessionStatus Status => SteamSessionStatus.Initial;

    /// <summary>Finds nothing to resume.</summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns><see cref="SignInResult.NothingSaved"/>.</returns>
    public Task<SignInResult> ResumeAsync(CancellationToken cancellationToken) => Task.FromResult(SignInResult.NothingSaved);

    /// <summary>Returns an attempt that has failed already.</summary>
    /// <param name="accountName">Not used.</param>
    /// <param name="password">Not used, and not kept.</param>
    /// <param name="confirmation">Not used.</param>
    /// <returns>The failed attempt.</returns>
    public ISignInAttempt StartPasswordSignIn(string accountName, string password, SignInConfirmation confirmation) =>
        new EndedSignInAttempt(NotAvailable);

    /// <summary>Returns an attempt that has failed already.</summary>
    /// <returns>The failed attempt.</returns>
    public ISignInAttempt StartQrSignIn() => new EndedSignInAttempt(NotAvailable);

    /// <summary>Does nothing: nothing is signed in.</summary>
    /// <param name="cancellationToken">Not used.</param>
    /// <returns>A completed task.</returns>
    public Task SignOutAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
