// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Apps;

// A node of a key-value tree, the shape of Steam's app information: a name with either a value
// or children. The SteamKit2 adapter copies SteamKit2's KeyValue into it, so that the reader and
// its tests need no SteamKit2 type and the tests build trees of their own (C-0011 section 1).
internal sealed class KeyValueNode
{
    public KeyValueNode(string name, string? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Value = value;
        Children = [];
    }

    public KeyValueNode(string name, IReadOnlyList<KeyValueNode> children)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(children);
        Name = name;
        Children = children;
    }

    public string Name { get; }

    public string? Value { get; }

    public IReadOnlyList<KeyValueNode> Children { get; }

    // The first child with the name, compared without case as Steam's own key-values are.
    public KeyValueNode? this[string name] =>
        Children.FirstOrDefault(child => string.Equals(child.Name, name, StringComparison.OrdinalIgnoreCase));
}
