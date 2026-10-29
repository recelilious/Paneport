// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Security;

/// <summary>
/// Keeps the Steam refresh token, the only secret that Paneport keeps (C-0007 section 3).
/// </summary>
/// <remarks>
/// On the device the keychain stands behind it from M1.3 on (C-0004 section 4). The desktop head
/// keeps the token in memory for one run and never writes it to disk (C-0004 section 2,
/// C-0007 section 3).
/// </remarks>
public interface ITokenStore
{
    /// <summary>Reads the stored token.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The token, or null when none is stored.</returns>
    Task<Secret?> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Stores the token, replacing a stored one.</summary>
    /// <param name="token">The token to store.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the token is stored.</returns>
    Task WriteAsync(Secret token, CancellationToken cancellationToken);

    /// <summary>Deletes the stored token, if there is one.</summary>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when no token is stored.</returns>
    Task DeleteAsync(CancellationToken cancellationToken);
}
