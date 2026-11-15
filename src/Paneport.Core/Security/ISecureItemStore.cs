// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Security;

/// <summary>
/// A store of small secret items, each a block of bytes under a key. The iOS head keeps each item
/// as one generic-password item of the keychain, accessible after the first unlock on this device
/// only and not synchronizable (C-0004 section 4); tests use a fake.
/// </summary>
/// <remarks>
/// An item's bytes are secret: implementations never log them, and callers log only an
/// exception's type.
/// </remarks>
public interface ISecureItemStore
{
    /// <summary>Reads an item.</summary>
    /// <param name="key">The item's key.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The item's bytes, or null when there is no such item.</returns>
    Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken);

    /// <summary>Writes an item, replacing one with the same key.</summary>
    /// <param name="key">The item's key.</param>
    /// <param name="value">The item's bytes.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the item is stored.</returns>
    Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken);

    /// <summary>Deletes an item; a missing item is not an error.</summary>
    /// <param name="key">The item's key.</param>
    /// <param name="cancellationToken">Cancels the deletion.</param>
    /// <returns>A task that completes when no such item is stored.</returns>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
