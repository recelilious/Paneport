// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;

namespace Paneport.Tests.Fakes;

// Secure items in memory, by key.
internal sealed class FakeSecureItemStore : ISecureItemStore
{
    private readonly Dictionary<string, byte[]> _items = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Items => _items;

    public int Deletions { get; private set; }

    public Task<byte[]?> ReadAsync(string key, CancellationToken cancellationToken) =>
        Task.FromResult(_items.TryGetValue(key, out var value) ? value.ToArray() : null);

    public Task WriteAsync(string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
    {
        _items[key] = value.ToArray();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        Deletions++;
        _items.Remove(key);
        return Task.CompletedTask;
    }

    public void Plant(string key, byte[] value) => _items[key] = value;
}
