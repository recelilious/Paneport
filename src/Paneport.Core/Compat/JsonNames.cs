// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Frozen;

namespace Paneport.Core.Compat;

// The exact names under which a title file writes the values of an enumeration. Reading matches
// them ordinally, so a name in another letter case or a number is rejected, as the schema rejects
// it. A value without a name, such as LaunchPath.Unclassified, is not a value of the field.
internal sealed class JsonNames<TEnum>
    where TEnum : struct, Enum
{
    private readonly FrozenDictionary<string, TEnum> _values;
    private readonly FrozenDictionary<TEnum, string> _names;

    public JsonNames(params ReadOnlySpan<(TEnum Value, string Name)> entries)
    {
        var names = new string[entries.Length];
        var values = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        var byValue = new Dictionary<TEnum, string>();
        for (var i = 0; i < entries.Length; i++)
        {
            names[i] = entries[i].Name;
            values.Add(entries[i].Name, entries[i].Value);
            byValue.Add(entries[i].Value, entries[i].Name);
        }

        Names = names;
        _values = values.ToFrozenDictionary(StringComparer.Ordinal);
        _names = byValue.ToFrozenDictionary();
    }

    // The names in the order the schema lists them.
    public IReadOnlyList<string> Names { get; }

    // The message for a name that is not on the list. It lists the allowed names, never the value.
    public string Expected => $"The value must be one of {string.Join(", ", Names)}.";

    public bool TryParse(string name, out TEnum value) => _values.TryGetValue(name, out value);

    public bool HasName(TEnum value) => _names.ContainsKey(value);

    public string NameOf(TEnum value) => _names[value];
}
