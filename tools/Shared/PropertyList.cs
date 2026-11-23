// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Property lists, XML or binary, as tools/ScanPackage.cs and tools/ReleaseVersion.cs read them.
// Both include this file with #:include; a shared file carries no #: directive of its own
// (docs/design/repository-checks.md section 2).

using System.Buffers.Binary;
using System.Text;
using System.Xml;
using System.Xml.Linq;

// The nodes that reading one property list or blob of DER, and checking it, may visit together:
// far more than any list of the app holds, and fixed, so that neither bytes that no object uses
// nor references that the list shares can raise it.
internal sealed class Budget
{
    public const long Nodes = 1L << 20;

    private long _left = Nodes;

    public void Spend()
    {
        if (--_left < 0)
        {
            throw new InvalidDataException("the list holds more nodes than the scan visits");
        }
    }
}

// Property lists, XML or binary, read into dictionaries, lists, texts, Booleans and other values.
internal static class Plist
{
    private static readonly byte[] s_binary = "bplist00"u8.ToArray();

    public static bool IsPropertyList(string name, byte[] bytes) =>
        bytes.AsSpan().StartsWith(s_binary)
        || name.EndsWith(".plist", StringComparison.OrdinalIgnoreCase)
        || (bytes.AsSpan().StartsWith("<?xml"u8) && bytes.AsSpan(0, Math.Min(bytes.Length, 512)).IndexOf("<plist"u8) >= 0);

    // The levels that a list, or a blob of DER, may nest. A real one nests a few.
    public const int MaxDepth = 64;

    public static object Parse(byte[] bytes, Budget budget) =>
        bytes.AsSpan().StartsWith(s_binary) ? Binary.Parse(bytes, budget) : Xml(bytes, budget);

    // Adds a value under its key. A key given twice, which no writer of property lists or DER
    // does, would hide its first value from the checks, and makes the list unreadable.
    public static void Add(Dictionary<string, object> dictionary, string key, object value)
    {
        if (!dictionary.TryAdd(key, value))
        {
            throw new InvalidDataException("a key is given twice");
        }
    }

    // A first pass of a reader bounds the depth and the number of elements before XDocument builds
    // the tree, so that neither the tree's recursion, in Node and in an element's Value, nor its
    // size can grow with the file.
    private static object Xml(byte[] bytes, Budget budget)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        try
        {
            using (var pass = XmlReader.Create(new MemoryStream(bytes), settings))
            {
                while (pass.Read())
                {
                    if (pass.NodeType == XmlNodeType.Element)
                    {
                        budget.Spend();
                        if (pass.Depth > MaxDepth)
                        {
                            throw new InvalidDataException("the property list nests deeper than the scan reads");
                        }
                    }
                }
            }

            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            var root = XDocument.Load(reader).Root;
            return root is { Name.LocalName: "plist" } && root.Elements().FirstOrDefault() is { } top
                ? Node(top)
                : throw new InvalidDataException("not a property list");
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException("not well-formed XML", exception);
        }
    }

    private static object Node(XElement element) => element.Name.LocalName switch
    {
        "dict" => Dictionary(element),
        "array" => element.Elements().Select(Node).ToList(),
        "string" or "key" or "date" or "integer" or "real" => element.Value,
        "true" => true,
        "false" => false,
        _ => element.Value,
    };

    private static Dictionary<string, object> Dictionary(XElement element)
    {
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
        var children = element.Elements().ToList();
        for (var index = 0; index + 1 < children.Count; index += 2)
        {
            Add(dictionary, children[index].Value, Node(children[index + 1]));
        }

        return dictionary;
    }

    // The binary format bplist00 of Apple's open-source CFBinaryPList.c: objects, an offset table
    // and a 32-byte trailer that sizes them.
    private static class Binary
    {
        public static object Parse(byte[] bytes, Budget budget)
        {
            if (bytes.Length < 40)
            {
                throw new InvalidDataException("the binary property list is too short");
            }

            // Every number of the trailer is checked against the file's size before it is used, so
            // that a made-up trailer can neither overflow nor size an allocation beyond the file.
            var trailer = bytes.AsSpan(bytes.Length - 32);
            var offsetSize = trailer[6];
            var referenceSize = trailer[7];
            var count = BinaryPrimitives.ReadUInt64BigEndian(trailer[8..]);
            var top = BinaryPrimitives.ReadUInt64BigEndian(trailer[16..]);
            var table = BinaryPrimitives.ReadUInt64BigEndian(trailer[24..]);
            var limit = (ulong)(bytes.Length - 32);
            if (offsetSize is < 1 or > 8 || referenceSize is < 1 or > 8 || count < 1 || count > limit || top >= count
                || table < 8 || table > limit || table + (count * offsetSize) > limit)
            {
                throw new InvalidDataException("the binary property list's trailer is invalid");
            }

            var offsets = new int[(int)count];
            for (var index = 0; index < offsets.Length; index++)
            {
                var offset = Number(bytes.AsSpan((int)table + (index * offsetSize), offsetSize));
                offsets[index] = offset < limit ? (int)offset : throw new InvalidDataException("an object lies outside the binary property list");
            }

            return new Reader(bytes, offsets, referenceSize, new object?[offsets.Length], budget).Read((int)top, 0);
        }

        private static ulong Number(ReadOnlySpan<byte> bytes)
        {
            ulong value = 0;
            foreach (var part in bytes)
            {
                value = (value << 8) | part;
            }

            return value;
        }

        // A list may reference one object many times, which the format allows. Each object is read
        // once and kept by its reference, so that a shared one is neither read nor allocated again,
        // and every reference read, kept or not, counts against the budget.
        private sealed class Reader(byte[] bytes, int[] offsets, int referenceSize, object?[] objects, Budget budget)
        {
            public object Read(int reference, int depth)
            {
                budget.Spend();
                if (depth > MaxDepth || reference < 0 || reference >= offsets.Length || offsets[reference] >= bytes.Length)
                {
                    throw new InvalidDataException("the binary property list is malformed");
                }

                return objects[reference] ??= ReadObject(offsets[reference], depth);
            }

            private object ReadObject(int offset, int depth)
            {
                var marker = bytes[offset];
                var type = marker >> 4;
                var info = marker & 0x0F;
                switch (type)
                {
                    case 0x0:
                        return info == 0x9 ? true : info == 0x8 ? false : string.Empty;
                    case 0x5:
                        {
                            var (length, start) = Length(offset, info);
                            return Encoding.ASCII.GetString(Span(start, length));
                        }

                    case 0x6:
                        {
                            var (length, start) = Length(offset, info);
                            return Encoding.BigEndianUnicode.GetString(Span(start, length * 2));
                        }

                    case 0xA:
                        {
                            var (length, start) = Length(offset, info);
                            var list = new List<object>(length);
                            for (var index = 0; index < length; index++)
                            {
                                list.Add(Read(Reference(start + (index * referenceSize)), depth + 1));
                            }

                            return list;
                        }

                    case 0xD:
                        {
                            var (length, start) = Length(offset, info);
                            var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
                            for (var index = 0; index < length; index++)
                            {
                                var key = Read(Reference(start + (index * referenceSize)), depth + 1) as string ?? string.Empty;
                                Add(dictionary, key, Read(Reference(start + ((length + index) * referenceSize)), depth + 1));
                            }

                            return dictionary;
                        }

                    default:
                        // Numbers, dates, data and the rest hold no account identifier as text.
                        return string.Empty;
                }
            }

            private (int Length, int Start) Length(int offset, int info)
            {
                if (info != 0xF)
                {
                    return (info, offset + 1);
                }

                var marker = Span(offset + 1, 1)[0];
                if (marker >> 4 != 0x1)
                {
                    throw new InvalidDataException("the binary property list has an invalid length");
                }

                // No object holds more items than the file has bytes after its start, so a made-up
                // length fails here instead of sizing an allocation.
                var size = 1 << (marker & 0x0F);
                var start = offset + 2 + size;
                var length = Number(Span(offset + 2, size));
                return length <= (ulong)Math.Max(0, bytes.Length - start)
                    ? ((int)length, start)
                    : throw new InvalidDataException("the binary property list has an invalid length");
            }

            private int Reference(int offset)
            {
                var reference = Number(Span(offset, referenceSize));
                return reference < (ulong)offsets.Length
                    ? (int)reference
                    : throw new InvalidDataException("the binary property list is malformed");
            }

            private ReadOnlySpan<byte> Span(int offset, int length) =>
                offset < 0 || length < 0 || (long)offset + length > bytes.Length
                    ? throw new InvalidDataException("the binary property list is malformed")
                    : bytes.AsSpan(offset, length);
        }
    }
}
