// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using Paneport.Core.Security;
using Security;

namespace Paneport.IOS;

// The secure items of Paneport.Core in the keychain (C-0004 section 4): one generic-password item
// per key, with the key as its account and Paneport as its service, accessible after the first
// unlock on this device only, and not synchronizable (avalonia_ui_layer.md section 2.3). An item's
// bytes never reach a log line or an exception's message.
internal sealed class KeychainItemStore : ISecureItemStore
{
    public const string Service = "Paneport";

    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var query = Query(key);
        using var data = SecKeyChain.QueryAsData(query, false, out var status);
        return status switch
        {
            SecStatusCode.Success => Task.FromResult<byte[]?>(data?.ToArray()),
            SecStatusCode.ItemNotFound => Task.FromResult<byte[]?>(null),
            _ => throw Failure("read", status),
        };
    }

    public Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var data = NSData.FromArray(value.ToArray());

        // An update keeps the item in place; only a missing item is added, so a failure never
        // leaves the key without its previous value.
        using var query = Query(key);
        using var changes = new SecRecord
        {
            ValueData = data,
            Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly,
        };
        var status = SecKeyChain.Update(query, changes);
        if (status == SecStatusCode.ItemNotFound)
        {
            using var item = Query(key);
            item.ValueData = data;
            item.Accessible = SecAccessible.AfterFirstUnlockThisDeviceOnly;
            item.Synchronizable = false;
            status = SecKeyChain.Add(item);
        }

        return status == SecStatusCode.Success ? Task.CompletedTask : throw Failure("write", status);
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var query = Query(key);
        var status = SecKeyChain.Remove(query);
        return status is SecStatusCode.Success or SecStatusCode.ItemNotFound ? Task.CompletedTask : throw Failure("delete", status);
    }

    // The attributes of an item, for the developer menu's token store page: whether it is accessible
    // after the first unlock on this device only, and whether it is synchronizable. Null when there
    // is no such item.
    public (bool AfterFirstUnlockThisDeviceOnly, bool Synchronizable, int Length)? ReadAttributes(string key)
    {
        using var query = Query(key);
        using var record = SecKeyChain.QueryAsRecord(query, out var status);
        if (status == SecStatusCode.ItemNotFound || record is null)
        {
            return null;
        }

        if (status != SecStatusCode.Success)
        {
            throw Failure("read the attributes of", status);
        }

        return (record.Accessible == SecAccessible.AfterFirstUnlockThisDeviceOnly, record.Synchronizable, (int)(record.ValueData?.Length ?? 0));
    }

    // Matches only the non-synchronizable item of this service and key.
    private static SecRecord Query(string key) => new(SecKind.GenericPassword)
    {
        Service = Service,
        Account = key,
        Synchronizable = false,
    };

    private static IOException Failure(string action, SecStatusCode status) =>
        new($"The keychain could not {action} the item: {status}.");
}
