// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Scans app packages for what a build step may leave behind that names an Apple account
// (C-0009 section 6, S1.3.9): a provisioning profile; a property list, XML or binary, or the
// entitlements of a code signature, XML or DER, that name a team identifier or an
// application-identifier prefix; and a code directory or CMS blob that names a team. Of each code
// signature in every 64-bit Mach-O file, thin or in a fat file, it reads the code directories,
// every entitlements blob and the CMS blob, but not the requirements, nor the code directory's
// identifier or hashes. A Mach-O file of a kind that it does not parse fails as unreadable. It
// lists the entitlements of every code signature it finds, and fails a package that holds no app.
// The rest of the scan of C-0009 section 6 comes with M9.1, which extends this tool and keeps its
// name.
//
//   dotnet run tools/ScanPackage.cs -- <package.ipa>...   scan packages
//   dotnet run tools/ScanPackage.cs -- --self-test        check every rule on generated packages
//
// A finding names the package, the file inside it and the rule, never the value, because CI
// logs are as public as the repository. Exit codes: 0 clean, 1 findings, 2 usage or a package
// that cannot be read. docs/design/ios-app-head.md section 11 describes the rules.

#:property PublishAot=false

using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

try
{
    return args switch
    {
        ["--self-test"] => SelfTest.Run(),
        [_, ..] when args.All(argument => !argument.StartsWith("--", StringComparison.Ordinal)) => Scanner.ScanPackages(args),
        _ => usage(),
    };
}
catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

static int usage()
{
    Console.Error.WriteLine("usage: dotnet run tools/ScanPackage.cs -- <package.ipa>... | --self-test");
    return 2;
}

// A finding: the file inside the package and the rule it breaks.
internal sealed record Finding(string Entry, string Rule);

// What one package holds: its findings, and the entitlements of each code signature in it.
internal sealed record PackageReport(IReadOnlyList<Finding> Findings, IReadOnlyList<string> Entitlements);

internal static class Rules
{
    public const string ProvisioningProfile = "provisioning-profile";
    public const string TeamIdentifier = "team-identifier";
    public const string AppIdentifierPrefix = "app-identifier-prefix";
    public const string UnreadableSignature = "unreadable-signature";
    public const string UnreadablePropertyList = "unreadable-property-list";
    public const string NoApp = "no-app";

    // The Info.plist of an app at the top of its bundle, in the package's Payload folder.
    public static readonly Regex AppInfo = new(@"^Payload/[^/]+\.app/Info\.plist$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // Keys whose value is a team identifier or a list of them, in profiles, entitlements and the
    // property lists that Xcode or a signing tool writes.
    public static readonly HashSet<string> TeamKeys = new(StringComparer.Ordinal)
    {
        "TeamIdentifier",
        "com.apple.developer.team-identifier",
        "AppIdentifierPrefix",
        "ApplicationIdentifierPrefix",
        "DEVELOPMENT_TEAM",
    };

    // The keys of the DER entitlements that name an account, with the rule that each breaks:
    // application-identifier holds a team identifier in front of the bundle identifier, and a team
    // key holds a team identifier. DER keeps the keys as UTF-8 text, so a search of the blob's
    // bytes finds each.
    public static readonly (string Key, string Rule)[] DerKeys =
        [("application-identifier", AppIdentifierPrefix), .. TeamKeys.Select(key => (Key: key, Rule: TeamIdentifier))];

    // A team identifier: ten capital letters and digits, with at least one of each.
    public static readonly Regex Team = new("^(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // A team identifier in front of a bundle identifier, as application-identifier and
    // keychain-access-groups hold it, or appended to one, as sideloading tools re-sign apps.
    public static readonly Regex Prefixed = new(
        @"(?:^|[^A-Za-z0-9])(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}\.[A-Za-z*]|[A-Za-z0-9-]\.(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}(?:$|[^A-Za-z0-9.])",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
}

internal static class Scanner
{
    public static int ScanPackages(IReadOnlyList<string> paths) => ScanPackages(paths, File.OpenRead, Console.Out, Console.Error);

    // Scans the packages in turn. One that cannot be read is reported and the scan goes on with
    // the next, so that one run shows every package; the result is then 2, otherwise 1 for
    // findings and 0 for none.
    internal static int ScanPackages(IReadOnlyList<string> paths, Func<string, Stream> open, TextWriter output, TextWriter error)
    {
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var total = 0;
        var unreadable = 0;
        foreach (var path in paths)
        {
            var package = Path.GetFileName(path);
            PackageReport report;
            try
            {
                using var stream = open(path);
                report = Scan(stream);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                unreadable++;
                error.WriteLine($"{package}: error: {exception.Message}");
                if (annotate)
                {
                    output.WriteLine($"::error title=Package scan::{package}: cannot be read");
                }

                continue;
            }

            foreach (var entitlement in report.Entitlements)
            {
                output.WriteLine($"{package}: {entitlement}");
            }

            foreach (var finding in report.Findings)
            {
                output.WriteLine($"{package}: {finding.Entry}: {finding.Rule}");
                if (annotate)
                {
                    output.WriteLine($"::error title=Package scan::{package}: {finding.Entry}: {finding.Rule}");
                }
            }

            total += report.Findings.Count;
            output.WriteLine($"{package}: {report.Findings.Count} findings.");
        }

        return unreadable != 0 ? 2 : total == 0 ? 0 : 1;
    }

    // Scans one package, a zip archive such as an .ipa. A package needs an app: an Info.plist in
    // Payload/<name>.app/ whose CFBundleExecutable names a Mach-O file beside it, so that a
    // packaging mistake cannot pass for a clean package.
    public static PackageReport Scan(Stream package)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        var findings = new List<Finding>();
        var entitlements = new List<string>();
        var machOFiles = new HashSet<string>(StringComparer.Ordinal);
        var executables = new List<string>();
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            var name = entry.FullName;
            if (name.EndsWith(".mobileprovision", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".provisionprofile", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(name, Rules.ProvisioningProfile));
                continue;
            }

            var bytes = Read(entry);
            if (MachO.IsMachO(bytes))
            {
                machOFiles.Add(name);
                ScanSignature(name, bytes, findings, entitlements);
            }
            else if (Plist.IsPropertyList(name, bytes))
            {
                try
                {
                    var tree = Plist.Parse(bytes);
                    Check(name, tree, findings);
                    if (Rules.AppInfo.IsMatch(name) && tree is Dictionary<string, object> info
                        && info.GetValueOrDefault("CFBundleExecutable") is string executable)
                    {
                        executables.Add(name[..^"Info.plist".Length] + executable);
                    }
                }
                catch (InvalidDataException)
                {
                    findings.Add(new Finding(name, Rules.UnreadablePropertyList));
                }
            }
        }

        if (!executables.Any(machOFiles.Contains))
        {
            findings.Add(new Finding("Payload/", Rules.NoApp));
        }

        return new PackageReport([.. findings.Distinct()], [.. entitlements.Distinct()]);
    }

    private static void ScanSignature(string name, byte[] bytes, List<Finding> findings, List<string> entitlements)
    {
        Signature signature;
        try
        {
            signature = MachO.ReadSignatures(bytes);
        }
        catch (InvalidDataException)
        {
            findings.Add(new Finding(name, Rules.UnreadableSignature));
            return;
        }

        var signed = $"{name} (code signature)";

        // A code directory names a team, and the CMS blob holds a certificate, only when an Apple
        // identity signed the file; an ad-hoc signature has neither.
        if (signature.NamesTeam)
        {
            findings.Add(new Finding(signed, Rules.TeamIdentifier));
        }

        foreach (var der in signature.DerEntitlements)
        {
            foreach (var (key, rule) in Rules.DerKeys)
            {
                if (der.AsSpan().IndexOf(Encoding.UTF8.GetBytes(key)) >= 0)
                {
                    findings.Add(new Finding(signed, rule));
                }
            }
        }

        foreach (var xml in signature.Entitlements)
        {
            try
            {
                var tree = Plist.Parse(xml);
                if (tree is Dictionary<string, object> dictionary)
                {
                    foreach (var (key, value) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        // Only a Boolean value is printed; any other value could name the account.
                        var shown = value is bool flag ? (flag ? "true" : "false") : "(not shown)";
                        entitlements.Add($"{name}: entitlement {key} = {shown}");
                    }
                }

                Check(signed, tree, findings);
            }
            catch (InvalidDataException)
            {
                findings.Add(new Finding(name, Rules.UnreadableSignature));
            }
        }
    }

    // Walks a property list: a team key with a team identifier as its value, and any text that
    // holds a prefixed identifier.
    private static void Check(string name, object node, List<Finding> findings)
    {
        switch (node)
        {
            case Dictionary<string, object> dictionary:
                foreach (var (key, value) in dictionary)
                {
                    if (Rules.TeamKeys.Contains(key) && Texts(value).Any(text => Rules.Team.IsMatch(text)))
                    {
                        findings.Add(new Finding(name, Rules.TeamIdentifier));
                    }

                    if (Rules.Prefixed.IsMatch(key))
                    {
                        findings.Add(new Finding(name, Rules.AppIdentifierPrefix));
                    }

                    Check(name, value, findings);
                }

                break;
            case List<object> list:
                foreach (var item in list)
                {
                    Check(name, item, findings);
                }

                break;
            case string text when Rules.Prefixed.IsMatch(text):
                findings.Add(new Finding(name, Rules.AppIdentifierPrefix));
                break;
        }
    }

    private static IEnumerable<string> Texts(object value) => value switch
    {
        string text => [text],
        List<object> list => list.OfType<string>(),
        _ => [],
    };

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}

// What the code signatures of a Mach-O file hold that the scan reads: every entitlements blob, in
// XML and in DER, and whether a code directory or a CMS blob names a team.
internal sealed class Signature
{
    public List<byte[]> Entitlements { get; } = [];

    public List<byte[]> DerEntitlements { get; } = [];

    public bool NamesTeam { get; set; }
}

// The code signatures of a Mach-O file, as Apple's open-source mach-o/loader.h, mach-o/fat.h and
// the code signing blobs of cs_blobs.h describe them. Each LC_CODE_SIGNATURE of a 64-bit slice,
// thin or in a fat file, points at a super blob, whose index names the code directories, the
// entitlements in XML and in DER, and the CMS blob. A code directory of version 0x20200 or later
// holds the offset of a team identifier at byte 48, which an ad-hoc signature leaves at zero, and
// only a signature by an identity fills the CMS blob, with a certificate that names the team. The
// requirements, the code directory's identifier and hashes, and the other slots are not read. A
// 32-bit or big-endian Mach-O file and a fat file with 64-bit offsets are not parsed, and so are
// unreadable, as are more slices or blobs than a real file holds. Mach-O headers are
// little-endian on arm64; fat headers and signature blobs are big-endian. Offsets are long, so
// that no sum of the file's numbers overflows.
internal static class MachO
{
    private const uint Magic64 = 0xFEEDFACF;

    // The other Mach-O headers, as a little-endian read sees them: 32-bit, and 32-bit or 64-bit in
    // big-endian order.
    private const uint Magic32 = 0xFEEDFACE;
    private const uint Cigam32 = 0xCEFAEDFE;
    private const uint Cigam64 = 0xCFFAEDFE;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagic64 = 0xCAFEBABF;
    private const uint MaxSlices = 16;
    private const uint MaxBlobs = 64;
    private const uint CodeSignatureCommand = 0x1D;
    private const uint SuperBlobMagic = 0xFADE0CC0;
    private const uint CodeDirectoryMagic = 0xFADE0C02;
    private const uint EntitlementsMagic = 0xFADE7171;
    private const uint DerEntitlementsMagic = 0xFADE7172;
    private const uint CmsMagic = 0xFADE0B01;
    private const uint CodeDirectorySlot = 0;
    private const uint EntitlementsSlot = 5;
    private const uint DerEntitlementsSlot = 7;
    private const uint AlternateCodeDirectorySlot = 0x1000;
    private const uint AlternateCodeDirectories = 5;
    private const uint CmsSlot = 0x10000;
    private const uint TeamOffsetVersion = 0x20200;
    private const int TeamOffsetField = 48;
    private const uint BlobHeader = 8;

    public static bool IsMachO(byte[] bytes) =>
        bytes.Length >= 32
        && (BinaryPrimitives.ReadUInt32LittleEndian(bytes) is Magic64 or Magic32 or Cigam32 or Cigam64
            || BinaryPrimitives.ReadUInt32BigEndian(bytes) is FatMagic or FatMagic64);

    // Reads the code signatures of every slice.
    public static Signature ReadSignatures(byte[] bytes)
    {
        var signature = new Signature();
        var magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (magic == FatMagic64)
        {
            throw new InvalidDataException("a fat file with 64-bit offsets, which this reader does not parse");
        }

        if (magic != FatMagic)
        {
            ReadThin(bytes, 0, signature);
            return signature;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, 4, 4));
        if (count > MaxSlices)
        {
            throw new InvalidDataException("the fat file has more slices than this reader reads");
        }

        for (var index = 0; index < count; index++)
        {
            ReadThin(bytes, BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, 8 + (index * 20) + 8, 4)), signature);
        }

        return signature;
    }

    private static void ReadThin(byte[] bytes, long start, Signature signature)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start, 4)) != Magic64)
        {
            throw new InvalidDataException("not a 64-bit little-endian Mach-O file");
        }

        // Every LC_CODE_SIGNATURE is read, although a valid file holds one.
        var commands = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start + 16, 4));
        var offset = start + 32;
        for (var index = 0L; index < commands; index++)
        {
            var command = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 4, 4));
            if (size < 8)
            {
                throw new InvalidDataException("a load command is too short");
            }

            if (command == CodeSignatureCommand)
            {
                ReadSuperBlob(bytes, start + BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 8, 4)), signature);
            }

            offset += size;
        }
    }

    private static void ReadSuperBlob(byte[] bytes, long superBlob, Signature signature)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob, 4)) != SuperBlobMagic)
        {
            throw new InvalidDataException("the code signature is not a super blob");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 8, 4));
        if (count > MaxBlobs)
        {
            throw new InvalidDataException("the code signature has more blobs than this reader reads");
        }

        for (var index = 0; index < count; index++)
        {
            var slot = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 12 + (index * 8), 4));
            var blob = superBlob + BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 16 + (index * 8), 4));
            switch (slot)
            {
                case EntitlementsSlot:
                    signature.Entitlements.Add(Content(bytes, blob, EntitlementsMagic).ToArray());
                    break;
                case DerEntitlementsSlot:
                    signature.DerEntitlements.Add(Content(bytes, blob, DerEntitlementsMagic).ToArray());
                    break;
                case CmsSlot:
                    // An ad-hoc signature's CMS blob is its 8-byte header alone.
                    signature.NamesTeam |= !Content(bytes, blob, CmsMagic).IsEmpty;
                    break;
                case CodeDirectorySlot or (>= AlternateCodeDirectorySlot and < AlternateCodeDirectorySlot + AlternateCodeDirectories):
                    signature.NamesTeam |= NamesTeam(bytes, blob);
                    break;
            }
        }
    }

    // A blob's content, after its magic and its length.
    private static ReadOnlySpan<byte> Content(byte[] bytes, long blob, uint magic)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, blob, 4)) != magic)
        {
            throw new InvalidDataException("a blob of the code signature has another magic");
        }

        return Slice(bytes, blob + BlobHeader, (long)BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, blob + 4, 4)) - BlobHeader);
    }

    private static bool NamesTeam(byte[] bytes, long codeDirectory)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, codeDirectory, 4)) != CodeDirectoryMagic)
        {
            throw new InvalidDataException("a code directory has another magic");
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, codeDirectory + 8, 4));
        return version >= TeamOffsetVersion
            && BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, codeDirectory + TeamOffsetField, 4)) != 0;
    }

    private static ReadOnlySpan<byte> Slice(byte[] bytes, long offset, long length) =>
        offset < 0 || length < 0 || offset + length > bytes.Length
            ? throw new InvalidDataException("a structure lies outside the file")
            : bytes.AsSpan((int)offset, (int)length);
}

// Property lists, XML or binary, read into dictionaries, lists, texts, Booleans and other values.
internal static class Plist
{
    private static readonly byte[] s_binary = "bplist00"u8.ToArray();

    public static bool IsPropertyList(string name, byte[] bytes) =>
        bytes.AsSpan().StartsWith(s_binary)
        || name.EndsWith(".plist", StringComparison.OrdinalIgnoreCase)
        || (bytes.AsSpan().StartsWith("<?xml"u8) && bytes.AsSpan(0, Math.Min(bytes.Length, 512)).IndexOf("<plist"u8) >= 0);

    public static object Parse(byte[] bytes) =>
        bytes.AsSpan().StartsWith(s_binary) ? Binary.Parse(bytes) : Xml(bytes);

    private static object Xml(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null, IgnoreComments = true };
        try
        {
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
            dictionary[children[index].Value] = Node(children[index + 1]);
        }

        return dictionary;
    }

    // The binary format bplist00 of Apple's open-source CFBinaryPList.c: objects, an offset table
    // and a 32-byte trailer that sizes them.
    private static class Binary
    {
        public static object Parse(byte[] bytes)
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

            return new Reader(bytes, offsets, referenceSize).Read((int)top, 0);
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

        private sealed class Reader(byte[] bytes, int[] offsets, int referenceSize)
        {
            // An object is read again for each reference to it, which the format allows, so a
            // list whose arrays each reference the next one twice would expand to 2^depth
            // objects. The objects read are therefore counted, against 16 for each byte of the
            // file; a list that a writer made expands to fewer objects than it has bytes.
            private long _read;

            public object Read(int reference, int depth)
            {
                if (++_read > 16L * bytes.Length)
                {
                    throw new InvalidDataException("the binary property list expands to more objects than its size allows");
                }

                if (depth > 64 || reference < 0 || reference >= offsets.Length || offsets[reference] >= bytes.Length)
                {
                    throw new InvalidDataException("the binary property list is malformed");
                }

                var offset = offsets[reference];
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
                                dictionary[key] = Read(Reference(start + ((length + index) * referenceSize)), depth + 1);
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

// Checks every rule on packages that it builds in memory: a clean package, whose signatures are ad
// hoc, passes; a planted provisioning profile, team identifier or application-identifier prefix
// each fails and names its file, in every part of a code signature that the scan reads; a package
// without an app fails; a Mach-O file or binary property list that is malformed, hostile or of a
// kind the scan does not parse is reported, never crashed on; and the scan goes on after a package
// that it cannot read. The planted values are made up and exist only in memory.
internal static class SelfTest
{
    private const string App = "Payload/Paneport.app/";
    private const string Team = "AB12CD34EF";
    private const string BundleId = "io.github.recelilious.paneport";
    private const string AppIdentifier = $"<key>application-identifier</key><string>{Team}.{BundleId}</string>";

    private static int s_failures;

    public static int Run()
    {
        var clean = Files();
        Expect("a clean package passes", clean, []);
        ExpectEntitlement("the clean package lists its entitlement", clean,
            $"{App}Paneport.iOS: entitlement com.apple.developer.kernel.increased-memory-limit = true");

        Expect("a provisioning profile fails", Files(("embedded.mobileprovision", [0x30, 0x82, 0x01, 0x00])),
            [new Finding($"{App}embedded.mobileprovision", Rules.ProvisioningProfile)]);
        Expect("a profile in another folder and case fails", Files(("Frameworks/Old.MobileProvision", [0x30])),
            [new Finding($"{App}Frameworks/Old.MobileProvision", Rules.ProvisioningProfile)]);

        Expect("a team identifier in an XML property list fails",
            Files(("Info.plist", Xml(InfoBody($"<key>TeamIdentifier</key><array><string>{Team}</string></array>")))),
            [new Finding($"{App}Info.plist", Rules.TeamIdentifier)]);
        Expect("a team identifier in a binary property list fails",
            Files(("Settings.bundle/Root.plist", BinaryPlist(new Dictionary<string, object>
            {
                ["com.apple.developer.team-identifier"] = Team,
                ["Title"] = "Paneport",
            }))),
            [new Finding($"{App}Settings.bundle/Root.plist", Rules.TeamIdentifier)]);
        Expect("an application-identifier prefix in the entitlements fails",
            Files(("Paneport.iOS", MachO(Entitlements($"<key>application-identifier</key><string>{Team}.{BundleId}</string>")))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("a team identifier appended to a bundle identifier fails",
            Files(("Info.plist", Xml(InfoBody($"<key>CFBundleURLName</key><string>{BundleId}.{Team}</string>")))),
            [new Finding($"{App}Info.plist", Rules.AppIdentifierPrefix)]);
        Expect("a keychain group with a prefix in a binary property list fails",
            Files(("Extra.plist", BinaryPlist(new Dictionary<string, object>
            {
                ["keychain-access-groups"] = new List<object> { $"{Team}.*" },
            }))),
            [new Finding($"{App}Extra.plist", Rules.AppIdentifierPrefix)]);

        Expect("a ten-character value under another key passes",
            Files(("Info.plist", Xml(InfoBody($"<key>DTPlatformBuild</key><string>{Team}</string>")))),
            []);
        Expect("bundle identifiers, namespaces and versions pass",
            Files(("Info.plist", Xml(InfoBody(
                "<key>A</key><string>io.github.recelilious.paneport.host</string>"
                    + "<key>B</key><string>Paneport.IOS.CompositionRoot</string><key>C</key><string>0.1.0</string>")))),
            []);
        Expect("an unreadable property list fails", Files(("Broken.plist", "<plist><dict>"u8.ToArray())),
            [new Finding($"{App}Broken.plist", Rules.UnreadablePropertyList)]);

        Expect("a code directory that names a team fails",
            Files(("Paneport.iOS", MachO(Entitlements(string.Empty), Team))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("a framework's code directory that names a team fails",
            Files(("Frameworks/paneport_host.framework/paneport_host", MachO(Entitlements(string.Empty), Team))),
            [new Finding($"{App}Frameworks/paneport_host.framework/paneport_host (code signature)", Rules.TeamIdentifier)]);
        Expect("an alternate code directory that names a team fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (0x1000, CodeDirectory(Team))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("a fat file whose second slice names a team fails",
            Files(("Paneport.iOS", Fat(MachO(Entitlements(string.Empty)), MachO(Entitlements(string.Empty), Team)))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("a code directory of version 0x20200 that names a team fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory(Team, 0x20200)), (5, XmlEntitlements(string.Empty))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("a code directory of version 0x201FF has no team offset to read",
            Files(("Paneport.iOS", Signed((0, CodeDirectory(Team, 0x201FF)), (5, XmlEntitlements(string.Empty))))),
            []);
        Expect("a code directory in slot 0x1005 is not one that the kernel reads",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (0x1005, CodeDirectory(Team))))),
            []);
        Expect("a team in a second code signature of the file fails",
            Files(("Paneport.iOS", Thin(SuperBlob(AdHoc(Entitlements(string.Empty))), SuperBlob((0, CodeDirectory(Team)))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("a CMS blob that holds a signature fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (0x10000, Blob(0xFADE0B01, [0x30, 0x80, 0x06, 0x09]))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("prefixed entitlements in a fat file's second slice fail",
            Files(("Paneport.iOS", Fat(MachO(Entitlements(string.Empty)), MachO(Entitlements(AppIdentifier))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("prefixed entitlements in the first of two entitlements blobs fail",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(AppIdentifier)), (5, XmlEntitlements(string.Empty))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("DER entitlements that name a team fail without XML ones",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(
                ("application-identifier", $"{Team}.{BundleId}"), ("com.apple.developer.team-identifier", Team)))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix),
                new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("prefixed DER entitlements beside clean XML ones fail",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (7, DerEntitlements(
                ("com.apple.developer.kernel.increased-memory-limit", true), ("application-identifier", $"{Team}.{BundleId}")))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("32-bit and big-endian Mach-O files and a fat file with 64-bit offsets are unreadable",
            Files(
                ("Frameworks/A.framework/A", Little(0xFEEDFACE, 12, 0, 6, 0, 0, 0, 0)),
                ("Frameworks/B.framework/B", Big(0xFEEDFACE, 18, 0, 6, 0, 0, 0, 0)),
                ("Frameworks/C.framework/C", Big(0xFEEDFACF, 0x01000012, 0, 6, 0, 0, 0, 0)),
                ("Frameworks/D.framework/D", [.. Big(0xCAFEBABF, 1), .. new byte[32]])),
            [new Finding($"{App}Frameworks/A.framework/A", Rules.UnreadableSignature),
                new Finding($"{App}Frameworks/B.framework/B", Rules.UnreadableSignature),
                new Finding($"{App}Frameworks/C.framework/C", Rules.UnreadableSignature),
                new Finding($"{App}Frameworks/D.framework/D", Rules.UnreadableSignature)]);
        Expect("a fat file of more than 16 slices is unreadable",
            Files(("Paneport.iOS", Fat([.. Enumerable.Repeat(MachO(Entitlements(string.Empty)), 17)]))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a code signature of more than 64 blobs is unreadable",
            Files(("Paneport.iOS", Signed([.. Enumerable.Repeat((2U, Requirements()), 64), (0U, CodeDirectory(Team))]))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a fat slice at 0x7FFFFFFD is unreadable",
            Files(("Paneport.iOS", [.. Big(0xCAFEBABE, 1, 0x0100000C, 0, 0x7FFFFFFD, 64, 4), 0, 0, 0, 0])),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("an entitlements blob that claims 0x7FFFFFF0 bytes is unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, Blob(0xFADE7171, Xml(Entitlements(string.Empty)), 0x7FFFFFF0))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a code directory that the index puts at 0x7FFFFFFE is unreadable",
            Files(("Paneport.iOS", FarCodeDirectory())),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("an empty package fails", Package(), [new Finding("Payload/", Rules.NoApp)]);
        Expect("a package without an app fails", Package(("README.txt", "Paneport"u8.ToArray())),
            [new Finding("Payload/", Rules.NoApp)]);
        Expect("an app without its executable fails", Files(("Paneport.iOS", null)),
            [new Finding("Payload/", Rules.NoApp)]);
        Expect("a binary property list that claims more objects than it holds is unreadable",
            Files(("Huge.plist", RawBinaryPlist([0x09], count: 1UL << 40))),
            [new Finding($"{App}Huge.plist", Rules.UnreadablePropertyList)]);
        Expect("a binary property list whose array is longer than the file is unreadable",
            Files(("Long.plist", RawBinaryPlist([0xAF, 0x12, 0x7F, 0xFF, 0xFF, 0xFF], count: 1))),
            [new Finding($"{App}Long.plist", Rules.UnreadablePropertyList)]);
        Expect("a binary property list of 40 arrays that share references is unreadable",
            Files(("Shared.plist", SharedReferencePlist(40))),
            [new Finding($"{App}Shared.plist", Rules.UnreadablePropertyList)]);
        ExpectTheScanGoesOn();

        Console.WriteLine(s_failures == 0 ? "Self-test passed." : $"Self-test failed: {s_failures} cases.");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Expect(string name, byte[] package, Finding[] expected)
    {
        PackageReport report;
        try
        {
            report = Scanner.Scan(new MemoryStream(package));
        }
        catch (Exception exception)
        {
            s_failures++;
            Console.WriteLine($"FAIL {name}: the scan threw {exception.GetType().Name}.");
            return;
        }

        var found = report.Findings.OrderBy(finding => finding.Entry, StringComparer.Ordinal)
            .ThenBy(finding => finding.Rule, StringComparer.Ordinal).ToList();
        var wanted = expected.OrderBy(finding => finding.Entry, StringComparer.Ordinal)
            .ThenBy(finding => finding.Rule, StringComparer.Ordinal).ToList();
        var leaks = report.Entitlements.Concat(found.Select(finding => $"{finding.Entry} {finding.Rule}"))
            .Where(line => line.Contains(Team, StringComparison.Ordinal)).ToList();
        if (!found.SequenceEqual(wanted) || leaks.Count != 0)
        {
            s_failures++;
            var leak = leaks.Count != 0 ? ", and a report line shows the value" : string.Empty;
            Console.WriteLine($"FAIL {name}: found [{string.Join(", ", found)}], expected [{string.Join(", ", wanted)}]{leak}.");
            return;
        }

        Console.WriteLine($"ok   {name}");
    }

    private static void ExpectEntitlement(string name, byte[] package, string line)
    {
        var report = Scanner.Scan(new MemoryStream(package));
        if (!report.Entitlements.Contains(line))
        {
            s_failures++;
            Console.WriteLine($"FAIL {name}: [{string.Join("; ", report.Entitlements)}].");
            return;
        }

        Console.WriteLine($"ok   {name}");
    }

    // A scan of two packages, the first of which is no archive: it reports that one, scans the
    // second and exits with 2.
    private static void ExpectTheScanGoesOn()
    {
        var name = "the scan goes on after a package that it cannot read";
        var packages = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["broken.ipa"] = "not a package"u8.ToArray(),
            ["flagged.ipa"] = Files(("embedded.mobileprovision", [0x30])),
        };
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        int result;
        try
        {
            result = Scanner.ScanPackages(["broken.ipa", "flagged.ipa"], path => new MemoryStream(packages[path]), output, error);
        }
        catch (Exception exception)
        {
            s_failures++;
            Console.WriteLine($"FAIL {name}: the scan threw {exception.GetType().Name}.");
            return;
        }

        if (result != 2 || !error.ToString().StartsWith("broken.ipa: error: ", StringComparison.Ordinal)
            || !output.ToString().Contains("flagged.ipa: 1 findings.", StringComparison.Ordinal))
        {
            s_failures++;
            Console.WriteLine($"FAIL {name}: exit {result}, output [{output}], errors [{error}].");
            return;
        }

        Console.WriteLine($"ok   {name}");
    }

    // The files of a clean app, with the given files added, replaced or, for null, removed, as a
    // package.
    private static byte[] Files(params (string Name, byte[]? Bytes)[] changes)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["Info.plist"] = Xml(InfoBody(string.Empty)),
            ["Paneport.iOS"] = MachO(Entitlements(string.Empty)),
            ["Frameworks/paneport_host.framework/paneport_host"] = MachO(null),
            ["Frameworks/paneport_host.framework/Info.plist"] = BinaryPlist(new Dictionary<string, object>
            {
                ["CFBundleIdentifier"] = BundleId + ".host",
                ["CFBundleSupportedPlatforms"] = new List<object> { "iPhoneOS" },
                ["UIRequiresFullScreen"] = true,
            }),
            ["_CodeSignature/CodeResources"] = Xml("<dict><key>files</key><dict/></dict>"),
        };
        foreach (var (name, bytes) in changes)
        {
            if (bytes is null)
            {
                files.Remove(name);
            }
            else
            {
                files[name] = bytes;
            }
        }

        return Package([.. files.Select(file => (App + file.Key, file.Value))]);
    }

    // A package of the given entries, by their full names.
    private static byte[] Package(params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var entry = archive.CreateEntry(name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static string InfoBody(string extra) =>
        $"<dict><key>CFBundleIdentifier</key><string>{BundleId}</string><key>CFBundleDisplayName</key><string>Paneport</string>"
            + $"<key>CFBundleExecutable</key><string>Paneport.iOS</string>{extra}</dict>";

    private static string Entitlements(string extra) =>
        $"<dict><key>com.apple.developer.kernel.increased-memory-limit</key><true/>{extra}</dict>";

    private static byte[] Xml(string body) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + $"<plist version=\"1.0\">{body}</plist>\n");

    // A thin 64-bit Mach-O file signed ad hoc with the given entitlements, as eng/mac/sign-adhoc.sh
    // signs the app, or with null an unsigned one. With a team, its first code directory names it,
    // as an Apple identity's signature does.
    private static byte[] MachO(string? entitlementsBody, string? team = null) =>
        entitlementsBody is null ? Thin() : Signed(AdHoc(entitlementsBody, team));

    // The blobs of an ad-hoc signature as codesign writes it: a code directory in slot 0 and one in
    // slot 0x1000, an empty set of requirements, the entitlements in XML and in DER, and a CMS blob
    // that is its header alone.
    private static (uint Slot, byte[] Blob)[] AdHoc(string entitlementsBody, string? team = null) =>
    [
        (0, CodeDirectory(team)),
        (2, Requirements()),
        (5, Blob(0xFADE7171, Xml(entitlementsBody))),
        (7, DerEntitlements(("com.apple.developer.kernel.increased-memory-limit", true))),
        (0x1000, CodeDirectory()),
        (0x10000, Blob(0xFADE0B01, [])),
    ];

    // A thin 64-bit Mach-O file signed with one super blob of the given blobs.
    private static byte[] Signed(params (uint Slot, byte[] Blob)[] blobs) => Thin(SuperBlob(blobs));

    // A thin 64-bit Mach-O header with an LC_CODE_SIGNATURE command for each given super blob, which
    // follow the load commands; with none, an unsigned binary.
    private static byte[] Thin(params byte[][] superBlobs)
    {
        const int Header = 32;
        const int Command = 16;
        var commands = new List<uint>();
        var offset = Header + (superBlobs.Length * Command);
        foreach (var superBlob in superBlobs)
        {
            commands.AddRange([0x1D, Command, (uint)offset, (uint)superBlob.Length]);
            offset += superBlob.Length;
        }

        return
        [
            .. Little(0xFEEDFACF, 0x0100000C, 0, 2, (uint)superBlobs.Length, (uint)(superBlobs.Length * Command), 0, 0),
            .. Little([.. commands]),
            .. superBlobs.SelectMany(superBlob => superBlob),
        ];
    }

    // A super blob whose index names each blob under its slot, in the given order.
    private static byte[] SuperBlob(params (uint Slot, byte[] Blob)[] blobs)
    {
        var index = new List<uint>();
        var offset = 12 + (blobs.Length * 8);
        foreach (var (slot, blob) in blobs)
        {
            index.AddRange([slot, (uint)offset]);
            offset += blob.Length;
        }

        return [.. Big(0xFADE0CC0, (uint)offset, (uint)blobs.Length), .. Big([.. index]), .. blobs.SelectMany(blob => blob.Blob)];
    }

    // A code directory of the given version: ad hoc without a team, or naming the team after its
    // fields, at byte 88, with that offset at byte 48.
    private static byte[] CodeDirectory(string? team = null, uint version = 0x20400)
    {
        const int Fields = 88;
        byte[] teamBytes = team is null ? [] : [.. Encoding.ASCII.GetBytes(team), 0];
        var directory = new byte[Fields + teamBytes.Length];
        Big(0xFADE0C02, (uint)directory.Length, version, team is null ? 0x2U : 0U).CopyTo(directory, 0);
        if (team is not null)
        {
            BinaryPrimitives.WriteUInt32BigEndian(directory.AsSpan(48), Fields);
        }

        teamBytes.CopyTo(directory, Fields);
        return directory;
    }

    // An empty set of requirements, as an ad-hoc signature holds.
    private static byte[] Requirements() => Blob(0xFADE0C01, Big(0));

    // The XML entitlements blob of the clean app, with the given keys added.
    private static byte[] XmlEntitlements(string extra) => Blob(0xFADE7171, Xml(Entitlements(extra)));

    // Entitlements in the DER form that codesign writes beside the XML form: a version, then a
    // dictionary of keys as UTF-8 texts, with Boolean or text values.
    private static byte[] DerEntitlements(params (string Key, object Value)[] entries)
    {
        static byte[] tlv(byte tag, byte[] content)
        {
            byte[] length = content.Length switch
            {
                < 0x80 => [(byte)content.Length],
                < 0x100 => [0x81, (byte)content.Length],
                _ => [0x82, (byte)(content.Length >> 8), (byte)content.Length],
            };
            return [tag, .. length, .. content];
        }

        static byte[] item(object value) =>
            value is bool flag ? tlv(0x01, [flag ? (byte)0xFF : (byte)0]) : tlv(0x0C, Encoding.UTF8.GetBytes((string)value));

        var pairs = entries.SelectMany(entry => tlv(0x30, [.. tlv(0x0C, Encoding.UTF8.GetBytes(entry.Key)), .. item(entry.Value)])).ToArray();
        return Blob(0xFADE7172, tlv(0x70, [.. tlv(0x02, [1]), .. tlv(0xB0, pairs)]));
    }

    // A blob of a code signature: its magic, its length, which a given value replaces, and its
    // content.
    private static byte[] Blob(uint magic, byte[] content, uint? length = null) =>
        [.. Big(magic, length ?? (uint)(8 + content.Length)), .. content];

    // A fat file of the given arm64 slices, each at a 16-byte boundary after the header.
    private static byte[] Fat(params byte[][] slices)
    {
        static int align(int offset) => (offset + 15) & ~15;
        var header = new List<uint> { 0xCAFEBABE, (uint)slices.Length };
        var starts = new List<int>();
        var end = align(8 + (slices.Length * 20));
        foreach (var slice in slices)
        {
            header.AddRange([0x0100000C, 0, (uint)end, (uint)slice.Length, 4]);
            starts.Add(end);
            end = align(end + slice.Length);
        }

        var file = new byte[end];
        Big([.. header]).CopyTo(file, 0);
        for (var index = 0; index < slices.Length; index++)
        {
            slices[index].CopyTo(file, starts[index]);
        }

        return file;
    }

    // A signed binary whose index puts its code directory at 0x7FFFFFFE. The super blob of Signed
    // follows the header and its one load command, at byte 48, so the offset of its first blob is
    // at byte 64.
    private static byte[] FarCodeDirectory()
    {
        var bytes = Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(64), 0x7FFFFFFE - 48);
        return bytes;
    }

    private static byte[] Big(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(index * 4), words[index]);
        }

        return bytes;
    }

    private static byte[] Little(params uint[] words)
    {
        var bytes = new byte[words.Length * 4];
        for (var index = 0; index < words.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(index * 4), words[index]);
        }

        return bytes;
    }

    // A bplist00 file of the given object bytes at offset 8, with offsets of four bytes, one-byte
    // references and a trailer that claims the given number of objects.
    private static byte[] RawBinaryPlist(byte[] objectBytes, ulong count)
    {
        var file = new List<byte>("bplist00"u8.ToArray());
        file.AddRange(objectBytes);
        var table = file.Count;
        file.AddRange([0, 0, 0, 8]);
        var trailer = new byte[32];
        trailer[6] = 4;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        file.AddRange(trailer);
        return [.. file];
    }

    // A well-formed bplist00 file of the given number of arrays, each holding two references to
    // the next, and a Boolean last, with one-byte references and offsets of four bytes. Read in
    // full, it would be 2^depth objects.
    private static byte[] SharedReferencePlist(int depth)
    {
        var file = new List<byte>("bplist00"u8.ToArray());
        var offsets = new List<uint>();
        for (var index = 0; index < depth; index++)
        {
            offsets.Add((uint)file.Count);
            file.AddRange([0xA2, (byte)(index + 1), (byte)(index + 1)]);
        }

        offsets.Add((uint)file.Count);
        file.Add(0x09);
        var table = file.Count;
        file.AddRange(Big([.. offsets]));
        var trailer = new byte[32];
        trailer[6] = 4;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)offsets.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        file.AddRange(trailer);
        return [.. file];
    }

    // A bplist00 file with one-byte references and offsets of four bytes, for dictionaries,
    // lists, ASCII texts and Booleans.
    private static byte[] BinaryPlist(Dictionary<string, object> top)
    {
        var objects = new List<byte[]>();
        int add(object value)
        {
            switch (value)
            {
                case bool flag:
                    objects.Add([(byte)(flag ? 0x09 : 0x08)]);
                    return objects.Count - 1;
                case string text:
                    var ascii = Encoding.ASCII.GetBytes(text);
                    objects.Add([.. Marker(0x50, ascii.Length), .. ascii]);
                    return objects.Count - 1;
                case List<object> list:
                    var items = list.Select(add).ToList();
                    objects.Add([.. Marker(0xA0, items.Count), .. items.Select(item => (byte)item)]);
                    return objects.Count - 1;
                case Dictionary<string, object> dictionary:
                    var keys = dictionary.Keys.Select(key => add(key)).ToList();
                    var values = dictionary.Values.Select(add).ToList();
                    objects.Add([.. Marker(0xD0, keys.Count), .. keys.Select(key => (byte)key), .. values.Select(item => (byte)item)]);
                    return objects.Count - 1;
                default:
                    throw new ArgumentException("The self-test writes no such value.", nameof(value));
            }
        }

        var root = add(top);
        var file = new List<byte>("bplist00"u8.ToArray());
        var offsets = new List<int>();
        foreach (var item in objects)
        {
            offsets.Add(file.Count);
            file.AddRange(item);
        }

        var table = file.Count;
        foreach (var offset in offsets)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, offset);
            file.AddRange(buffer);
        }

        var trailer = new byte[32];
        trailer[6] = 4;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)objects.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(16), (ulong)root);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        file.AddRange(trailer);
        return [.. file];
    }

    private static byte[] Marker(int type, int length) =>
        length < 15 ? [(byte)(type | length)] : [(byte)(type | 0xF), 0x10, (byte)length];
}
