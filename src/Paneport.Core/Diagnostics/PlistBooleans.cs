// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Xml;
using System.Xml.Linq;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Reads Boolean values of chosen keys from an XML property list, and nothing else: the other keys
/// and every value that is not one of the chosen keys' are discarded without being kept or logged.
/// </summary>
public static class PlistBooleans
{
    /// <summary>
    /// Reads chosen Boolean keys of the top-level dictionary of a property list, or of the
    /// dictionary under one of its keys.
    /// </summary>
    /// <param name="xml">The property list, as XML.</param>
    /// <param name="keys">The keys to read.</param>
    /// <param name="nestedDictionaryKey">
    /// The key of a dictionary inside the top-level one to read instead, such as
    /// <c>Entitlements</c> in a provisioning profile; or null for the top-level one.
    /// </param>
    /// <returns>
    /// Each chosen key with its value: true or false, or null when the key is missing or its value
    /// is not a Boolean.
    /// </returns>
    /// <exception cref="InvalidDataException">The text is not a property list with a dictionary.</exception>
    public static IReadOnlyDictionary<string, bool?> Read(ReadOnlySpan<byte> xml, IReadOnlyList<string> keys, string? nestedDictionaryKey)
    {
        ArgumentNullException.ThrowIfNull(keys);
        var root = Load(xml);
        var dictionary = root.Element("dict") ?? throw new InvalidDataException("The property list holds no dictionary.");
        if (nestedDictionaryKey is not null)
        {
            dictionary = ValueOf(dictionary, nestedDictionaryKey) is { Name.LocalName: "dict" } nested
                ? nested
                : throw new InvalidDataException("The property list has no such dictionary.");
        }

        var values = new Dictionary<string, bool?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            values[key] = ValueOf(dictionary, key)?.Name.LocalName switch
            {
                "true" => true,
                "false" => false,
                _ => null,
            };
        }

        return values;
    }

    private static XElement Load(ReadOnlySpan<byte> xml)
    {
        var settings = new XmlReaderSettings
        {
            // A property list names Apple's DTD, which must never be fetched or expanded.
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true,
        };
        try
        {
            using var stream = new MemoryStream(xml.ToArray(), writable: false);
            using var reader = XmlReader.Create(stream, settings);
            var document = XDocument.Load(reader);
            return document.Root is { Name.LocalName: "plist" } root
                ? root
                : throw new InvalidDataException("The text is not a property list.");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("The text is not well-formed XML.", exception);
        }
    }

    // The value element right after a key element with the given text, or null.
    private static XElement? ValueOf(XElement dictionary, string key) =>
        dictionary.Elements()
            .Where(element => element.Name.LocalName == "key" && element.Value == key)
            .Select(element => element.ElementsAfterSelf().FirstOrDefault())
            .FirstOrDefault();
}
