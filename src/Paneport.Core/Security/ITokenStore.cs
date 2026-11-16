// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Security;

/// <summary>
/// Keeps the Steam sign-in: the refresh token, the only secret that Paneport keeps
/// (C-0007 section 3), with the account name that signing in with it needs.
/// </summary>
/// <remarks>
/// On the device the keychain stands behind it (<see cref="SecureTokenStore"/>, C-0004 section 4).
/// The desktop head keeps the sign-in in memory for one run and never writes it to disk
/// (C-0004 section 2, C-0007 section 3).
/// </remarks>
public interface ITokenStore
{
    /// <summary>Reads the saved sign-in.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The sign-in, or null when none is saved.</returns>
    Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Saves a sign-in, replacing a saved one.</summary>
    /// <param name="signIn">The sign-in to save.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the sign-in is saved.</returns>
    Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken);

    /// <summary>Deletes the saved sign-in, if there is one.</summary>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when no sign-in is saved.</returns>
    Task DeleteAsync(CancellationToken cancellationToken);
}
