// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;
using Paneport.Core.Logging;

namespace Paneport.Core.Security;

/// <summary>
/// Keeps the Steam sign-in as one item of a secure item store: on the device, one generic-password
/// item of the keychain (C-0004 section 4). The item holds the account name and the refresh token
/// as a small JSON document with a version.
/// </summary>
/// <remarks>
/// An item that cannot be read as a sign-in of a known version is reported and removed, so the
/// user signs in again. No log line holds the account name, the token or the item's bytes; a
/// failure is logged with its exception's type only.
/// </remarks>
public sealed class SecureTokenStore : ITokenStore
{
    /// <summary>The key of the sign-in item. The developer menu's test item uses another key.</summary>
    public const string ItemKey = "steam-sign-in";

    private const string LogSource = nameof(SecureTokenStore);

    private readonly ISecureItemStore _items;
    private readonly ILog _log;

    /// <summary>Creates the store.</summary>
    /// <param name="items">The secure item store.</param>
    /// <param name="log">The log.</param>
    public SecureTokenStore(ISecureItemStore items, ILog log)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(log);
        _items = items;
        _log = log;
    }

    /// <inheritdoc />
    public async Task<SavedSignIn?> ReadAsync(CancellationToken cancellationToken)
    {
        var bytes = await _items.ReadAsync(ItemKey, cancellationToken).ConfigureAwait(false);
        if (bytes is null)
        {
            return null;
        }

        if (Parse(bytes) is { } signIn)
        {
            return signIn;
        }

        _log.Write(LogLevel.Warning, LogSource, "The saved sign-in could not be read, so it was removed.");
        await _items.DeleteAsync(ItemKey, cancellationToken).ConfigureAwait(false);
        return null;
    }

    /// <inheritdoc />
    public Task WriteAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        var item = new SignInItem
        {
            Version = SignInItem.CurrentVersion,
            AccountName = signIn.AccountName,
            RefreshToken = signIn.RefreshToken.Reveal(),
        };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(item, SecurityJsonContext.Default.SignInItem);
        return _items.WriteAsync(ItemKey, bytes, cancellationToken);
    }

    /// <inheritdoc />
    public Task DeleteAsync(CancellationToken cancellationToken) => _items.DeleteAsync(ItemKey, cancellationToken);

    // The sign-in in an item's bytes, or null when they hold no sign-in of a known version.
    private static SavedSignIn? Parse(byte[] bytes)
    {
        SignInItem? item;
        try
        {
            item = JsonSerializer.Deserialize(bytes, SecurityJsonContext.Default.SignInItem);
        }
        catch (JsonException)
        {
            return null;
        }

        if (item is not { Version: SignInItem.CurrentVersion }
            || string.IsNullOrEmpty(item.AccountName)
            || string.IsNullOrEmpty(item.RefreshToken))
        {
            return null;
        }

        return new SavedSignIn(item.AccountName, new Secret(item.RefreshToken));
    }
}
