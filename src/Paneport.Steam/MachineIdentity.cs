// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using Paneport.Core.Security;

namespace Paneport.Steam;

/// <summary>
/// The installation's random identity, from which the machine identity and the login ID that each
/// logon sends to Steam are derived, so that nothing about the device reaches Steam (C-0007 section 3,
/// <c>docs/design/steam-service.md</c>).
/// </summary>
/// <remarks>
/// It is kept as one item of the secure item store, the keychain on the device, beside the saved
/// sign-in but not part of it: signing out keeps it, so the next sign-in from this installation looks
/// like the same machine to Steam.
/// </remarks>
public static class MachineIdentity
{
    /// <summary>The identity's length in bytes.</summary>
    public const int Length = 32;

    /// <summary>The key of the identity's item, which is neither the sign-in's nor the developer menu's test item.</summary>
    public const string ItemKey = "steam-machine-identity";

    /// <summary>Reads the identity, or creates and stores a new one when there is none or it is unreadable.</summary>
    /// <param name="store">The secure item store.</param>
    /// <param name="cancellationToken">Cancels the read and the write.</param>
    /// <returns>The identity.</returns>
    public static async Task<byte[]> LoadOrCreateAsync(ISecureItemStore store, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var stored = await store.ReadAsync(ItemKey, cancellationToken).ConfigureAwait(false);
        if (stored is { Length: Length })
        {
            return stored;
        }

        var identity = RandomNumberGenerator.GetBytes(Length);
        await store.WriteAsync(ItemKey, identity, cancellationToken).ConfigureAwait(false);
        return identity;
    }
}
