// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Scans app packages for what a build step may leave behind that names an Apple account
// (C-0009 section 6, S1.3.9): a provisioning profile, and a team identifier or an
// application-identifier prefix in a property list, XML or binary, or in a code signature. Of each
// code signature in every 64-bit Mach-O file, thin or in a fat file, it reads the code directories
// (slots 0 and 0x1000 to 0x1004), the entitlements in XML (slot 5) and in DER (slot 7), the launch
// constraints (slots 8 to 11), which are DER too, and the CMS blob (slot 0x10000). It does not
// read the requirements (slot 2), slots 0x10001 and 0x10002 or any other, nor the code
// directory's identifier and hashes. A Mach-O file of a kind that it does not parse fails as
// unreadable. It lists the entitlements of every code signature it finds, and fails a package that
// holds no app. The rest of the scan of C-0009 section 6 comes with M9.1, which extends this tool
// and keeps its name.
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

// What one package holds: its findings, the entitlements of each code signature in it, the number
// of its entries and the size of its largest.
internal sealed record PackageReport(IReadOnlyList<Finding> Findings, IReadOnlyList<string> Entitlements, int Entries, long Largest);

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

    // Keys whose value is a team identifier or a list of them, in profiles, entitlements, launch
    // constraints and the property lists that Xcode or a signing tool writes.
    public static readonly HashSet<string> TeamKeys = new(StringComparer.Ordinal)
    {
        "TeamIdentifier",
        "com.apple.developer.team-identifier",
        "team-identifier",
        "AppIdentifierPrefix",
        "ApplicationIdentifierPrefix",
        "DEVELOPMENT_TEAM",
    };

    private const string TeamPattern = "^(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}$";

    private const string PrefixedPattern =
        @"(?:^|[^A-Za-z0-9])(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}\.[A-Za-z*]|[A-Za-z0-9-]\.(?=[A-Z0-9]*[0-9])(?=[A-Z0-9]*[A-Z])[A-Z0-9]{10}(?:$|[^A-Za-z0-9.])";

    // The longest that matching one text may take. A text that takes longer makes its file
    // unreadable; the self-test shortens the limit, so that a short text shows it.
    public static TimeSpan MatchTimeout { get; set; } = TimeSpan.FromSeconds(1);

    // A team identifier: ten capital letters and digits, with at least one of each.
    public static bool IsTeam(string text) => Regex.IsMatch(text, TeamPattern, RegexOptions.CultureInvariant, MatchTimeout);

    // A team identifier in front of a bundle identifier, as application-identifier and
    // keychain-access-groups hold it, or appended to one, as sideloading tools re-sign apps.
    public static bool IsPrefixed(string text) => Regex.IsMatch(text, PrefixedPattern, RegexOptions.CultureInvariant, MatchTimeout);
}

internal static class Scanner
{
    // An entry of a package may hold at most 256 MiB, which bounds the memory that reading one
    // takes. Each package of the ios job is about 18 MB, compressed, and the scan prints the size of
    // each package's largest entry, so every run shows how far below the cap it stays. A larger
    // declared size, or data that runs past or stops short of the declared size, makes the package
    // unreadable. .NET's reader stops a deflated entry at its declared size by itself; a stored one
    // it reads to its compressed size, which can be larger.
    internal const long MaxEntryBytes = 256L * 1024 * 1024;

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
            output.WriteLine($"{package}: {report.Entries} entries, the largest of {report.Largest} bytes.");
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
        var entries = 0;
        var largest = 0L;
        foreach (var entry in archive.Entries.Where(entry => !entry.FullName.EndsWith('/')).OrderBy(entry => entry.FullName, StringComparer.Ordinal))
        {
            entries++;
            largest = Math.Max(largest, entry.Length);
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
                    var budget = new Budget();
                    var tree = Plist.Parse(bytes, budget);
                    Check(name, tree, findings, budget);
                    if (Rules.AppInfo.IsMatch(name) && tree is Dictionary<string, object> info
                        && info.GetValueOrDefault("CFBundleExecutable") is string executable)
                    {
                        executables.Add(name[..^"Info.plist".Length] + executable);
                    }
                }
                catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
                {
                    findings.Add(new Finding(name, Rules.UnreadablePropertyList));
                }
            }
        }

        if (!executables.Any(machOFiles.Contains))
        {
            findings.Add(new Finding("Payload/", Rules.NoApp));
        }

        return new PackageReport([.. findings.Distinct()], [.. entitlements.Distinct()], entries, largest);
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

        // The DER of the entitlements and of the launch constraints is read as the XML form is, so
        // that each of its texts gets the same checks.
        foreach (var der in signature.Der)
        {
            try
            {
                var budget = new Budget();
                Check(signed, Der.Parse(der, budget), findings, budget);
            }
            catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
            {
                findings.Add(new Finding(name, Rules.UnreadableSignature));
            }
        }

        foreach (var xml in signature.Entitlements)
        {
            try
            {
                var budget = new Budget();
                var tree = Plist.Parse(xml, budget);
                if (tree is Dictionary<string, object> dictionary)
                {
                    foreach (var (key, value) in dictionary.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                    {
                        // Only a Boolean value is printed; any other value could name the account.
                        var shown = value is bool flag ? (flag ? "true" : "false") : "(not shown)";
                        entitlements.Add($"{name}: entitlement {key} = {shown}");
                    }
                }

                Check(signed, tree, findings, budget);
            }
            catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
            {
                findings.Add(new Finding(name, Rules.UnreadableSignature));
            }
        }
    }

    // Walks a property list: a team key with a team identifier as its value, and any text that
    // holds a prefixed identifier. A text or container that the list shares is walked once, so the
    // walk follows the reader's own order and depth, and each visit counts against the budget that
    // reading the list began.
    private static void Check(string name, object tree, List<Finding> findings, Budget budget)
    {
        var walked = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void walk(object node)
        {
            budget.Spend();
            if (node is not (string or List<object> or Dictionary<string, object>) || !walked.Add(node))
            {
                return;
            }

            switch (node)
            {
                case Dictionary<string, object> dictionary:
                    foreach (var (key, value) in dictionary)
                    {
                        if (Rules.TeamKeys.Contains(key) && Texts(value).Any(Rules.IsTeam))
                        {
                            findings.Add(new Finding(name, Rules.TeamIdentifier));
                        }

                        if (Rules.IsPrefixed(key))
                        {
                            findings.Add(new Finding(name, Rules.AppIdentifierPrefix));
                        }

                        walk(value);
                    }

                    break;
                case List<object> list:
                    foreach (var item in list)
                    {
                        walk(item);
                    }

                    break;
                case string text when Rules.IsPrefixed(text):
                    findings.Add(new Finding(name, Rules.AppIdentifierPrefix));
                    break;
            }
        }

        walk(tree);
    }

    private static IEnumerable<string> Texts(object value) => value switch
    {
        string text => [text],
        List<object> list => list.OfType<string>(),
        _ => [],
    };

    private static byte[] Read(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxEntryBytes)
        {
            throw new InvalidDataException($"{entry.FullName} declares more than the {MaxEntryBytes} bytes that the scan reads of an entry");
        }

        var bytes = new byte[entry.Length];
        using var stream = entry.Open();
        try
        {
            stream.ReadExactly(bytes);
        }
        catch (EndOfStreamException exception)
        {
            throw new InvalidDataException($"the data of {entry.FullName} stops short of its declared size", exception);
        }

        return stream.ReadByte() < 0 ? bytes : throw new InvalidDataException($"the data of {entry.FullName} runs past its declared size");
    }
}

// What the code signatures of a Mach-O file hold that the scan reads: every entitlements blob in
// XML, every blob in DER (the entitlements and the launch constraints), and whether a code
// directory or a CMS blob names a team. Each super blob is read once, and the blobs copied from a
// file may hold no more bytes together than the file, which a real signature, whose blobs do not
// overlap, never reaches.
internal sealed class Signature(long fileLength)
{
    private long _copied;

    public List<byte[]> Entitlements { get; } = [];

    public List<byte[]> Der { get; } = [];

    public HashSet<long> SuperBlobs { get; } = [];

    public bool NamesTeam { get; set; }

    public byte[] Copy(ReadOnlySpan<byte> content)
    {
        _copied += content.Length;
        return _copied <= fileLength ? content.ToArray() : throw new InvalidDataException("the blobs of the code signature hold more bytes than the file");
    }
}

// The code signatures of a Mach-O file, as Apple's open-source mach-o/loader.h, mach-o/fat.h and
// the code signing blobs of cs_blobs.h describe them. Each LC_CODE_SIGNATURE of a 64-bit slice,
// thin or in a fat file, points at a super blob, whose index names the code directories (slots 0
// and 0x1000 to 0x1004), the entitlements in XML (slot 5) and in DER (slot 7), the launch
// constraints in DER (slots 8 to 11) and the CMS blob (slot 0x10000). A code directory of version
// 0x20200 or later holds the offset of a team identifier at byte 48, which an ad-hoc signature
// leaves at zero, and only a signature by an identity fills the CMS blob, with a certificate that
// names the team. The requirements (slot 2), slots 0x10001 and 0x10002 and any other, and the code
// directory's identifier and hashes are not read. A 32-bit or big-endian Mach-O file and a fat
// file with 64-bit offsets are not parsed, and so are unreadable, as are more slices or blobs than
// a real file holds. Mach-O headers are little-endian on arm64; fat headers and signature blobs
// are big-endian. Offsets are long, so that no sum of the file's numbers overflows.
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
    private const uint LaunchConstraintMagic = 0xFADE8181;
    private const uint CmsMagic = 0xFADE0B01;
    private const uint CodeDirectorySlot = 0;
    private const uint EntitlementsSlot = 5;
    private const uint DerEntitlementsSlot = 7;
    private const uint FirstLaunchConstraintSlot = 8;
    private const uint LastLaunchConstraintSlot = 11;
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
        var signature = new Signature(bytes.Length);
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

        // Every LC_CODE_SIGNATURE is read, although a valid file holds one, and the super blob of
        // each once, even when several commands name it.
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
                var superBlob = start + BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 8, 4));
                if (signature.SuperBlobs.Add(superBlob))
                {
                    ReadSuperBlob(bytes, superBlob, signature);
                }
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
                    signature.Entitlements.Add(signature.Copy(Content(bytes, blob, EntitlementsMagic)));
                    break;
                case DerEntitlementsSlot:
                    signature.Der.Add(signature.Copy(Content(bytes, blob, DerEntitlementsMagic)));
                    break;
                case >= FirstLaunchConstraintSlot and <= LastLaunchConstraintSlot:
                    signature.Der.Add(signature.Copy(Content(bytes, blob, LaunchConstraintMagic)));
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

// The DER form of entitlements and launch constraints, as codesign writes it beside the XML form:
// one element of tag 0x70 that holds a version and a dictionary. A dictionary (tag 0xB0) holds
// pairs, each a sequence (0x30) of a key, a UTF8String, and a value: a Boolean, an integer, data, a
// UTF8String, a sequence of values or a dictionary. It is read into the trees that the XML form
// gives, so that its texts get the same checks. Any other tag, such as another type of string or a
// constructed one, nesting deeper than a list may, a key given twice, text that is not UTF-8 or a
// length that leaves its element makes it unreadable; only zero bytes may follow the element.
internal static class Der
{
    private static readonly UTF8Encoding s_utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static object Parse(byte[] bytes, Budget budget)
    {
        var offset = 0;
        var tag = Next(bytes, ref offset, out var content);
        return tag == 0x70 && bytes.AsSpan(offset).IndexOfAnyExcept((byte)0) < 0
            ? Values(content, budget, 1)
            : throw new InvalidDataException("the DER is not one element of tag 0x70");
    }

    private static List<object> Values(ReadOnlySpan<byte> bytes, Budget budget, int depth)
    {
        var values = new List<object>();
        var offset = 0;
        while (offset < bytes.Length)
        {
            var tag = Next(bytes, ref offset, out var content);
            values.Add(Value(tag, content, budget, depth));
        }

        return values;
    }

    private static object Value(byte tag, ReadOnlySpan<byte> content, Budget budget, int depth)
    {
        budget.Spend();
        if (depth > Plist.MaxDepth)
        {
            throw new InvalidDataException("the DER nests deeper than the scan reads");
        }

        return tag switch
        {
            0x01 => content.Length == 1 ? content[0] != 0 : throw new InvalidDataException("a DER Boolean is not one byte"),
            0x02 or 0x04 => string.Empty,
            0x0C => Text(content),
            0x30 => Values(content, budget, depth + 1),
            0xB0 => Dictionary(content, budget, depth + 1),
            _ => throw new InvalidDataException("the DER holds a type that codesign does not write"),
        };
    }

    private static Dictionary<string, object> Dictionary(ReadOnlySpan<byte> bytes, Budget budget, int depth)
    {
        var dictionary = new Dictionary<string, object>(StringComparer.Ordinal);
        var offset = 0;
        while (offset < bytes.Length)
        {
            if (Next(bytes, ref offset, out var pair) != 0x30)
            {
                throw new InvalidDataException("a DER dictionary holds something other than pairs");
            }

            var inner = 0;
            if (Next(pair, ref inner, out var key) != 0x0C)
            {
                throw new InvalidDataException("a DER key is not a UTF8String");
            }

            var tag = Next(pair, ref inner, out var value);
            if (inner != pair.Length)
            {
                throw new InvalidDataException("a DER pair holds more than a key and a value");
            }

            Plist.Add(dictionary, Text(key), Value(tag, value, budget, depth));
        }

        return dictionary;
    }

    private static string Text(ReadOnlySpan<byte> bytes)
    {
        try
        {
            return s_utf8.GetString(bytes);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("a DER text is not UTF-8", exception);
        }
    }

    // An element: a tag of one byte, a length in the short form or in a long form of up to four
    // bytes, and content that lies inside bytes.
    private static byte Next(ReadOnlySpan<byte> bytes, ref int offset, out ReadOnlySpan<byte> content)
    {
        if (bytes.Length - offset < 2)
        {
            throw new InvalidDataException("a DER element is cut short");
        }

        var tag = bytes[offset];
        var start = offset + 2;
        long length = bytes[offset + 1];
        if (length >= 0x80)
        {
            var size = (int)length & 0x7F;
            if (size is 0 or > 4 || bytes.Length - start < size)
            {
                throw new InvalidDataException("a DER length is not definite");
            }

            length = 0;
            foreach (var part in bytes.Slice(start, size))
            {
                length = (length << 8) | part;
            }

            start += size;
        }

        if (length > bytes.Length - start)
        {
            throw new InvalidDataException("a DER element runs past its container");
        }

        content = bytes.Slice(start, (int)length);
        offset = start + (int)length;
        return tag;
    }
}

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

// Checks every rule on packages that it builds in memory: a clean package, whose signatures are ad
// hoc, passes; a planted provisioning profile, team identifier or application-identifier prefix
// each fails and names its file, in every part of a code signature that the scan reads; a package
// without an app fails; a Mach-O file, property list or blob of DER that is malformed, hostile or
// of a kind the scan does not parse is reported, never crashed on; an entry larger than the scan
// reads, or with data that does not match its size, makes its package unreadable; and the scan
// goes on after a package that it cannot read. The planted values are made up and exist only in
// memory.
internal static class SelfTest
{
    private const string App = "Payload/Paneport.app/";
    private const string Team = "AB12CD34EF";
    private const string BundleId = "io.github.recelilious.paneport";
    private const string AppIdentifier = $"<key>application-identifier</key><string>{Team}.{BundleId}</string>";

    private static int s_failures;

    // 1,000 nested elements, far deeper than a list may nest.
    private static string Deep { get; } = string.Concat(Enumerable.Repeat("<array>", 1000)) + string.Concat(Enumerable.Repeat("</array>", 1000));

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
        Expect("an alternate code directory in slot 0x1004 that names a team fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (0x1004, CodeDirectory(Team))))),
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
        Expect("CMS blobs whose lengths, 4 and 0, are shorter than their header are unreadable",
            Files(
                ("Frameworks/A.framework/A", Signed((0, CodeDirectory()), (0x10000, Blob(0xFADE0B01, [], 4)))),
                ("Frameworks/B.framework/B", Signed((0, CodeDirectory()), (0x10000, Blob(0xFADE0B01, [], 0))))),
            [new Finding($"{App}Frameworks/A.framework/A", Rules.UnreadableSignature),
                new Finding($"{App}Frameworks/B.framework/B", Rules.UnreadableSignature)]);
        Expect("prefixed entitlements in a fat file's second slice fail",
            Files(("Paneport.iOS", Fat(MachO(Entitlements(string.Empty)), MachO(Entitlements(AppIdentifier))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("prefixed entitlements in the first of two entitlements blobs fail",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(AppIdentifier)), (5, XmlEntitlements(string.Empty))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("1,000 nested elements make the entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements($"<key>com.paneport.self-test</key>{Deep}"))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a key given twice makes the XML entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(
                $"{AppIdentifier}<key>application-identifier</key><string>{BundleId}</string>"))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("DER entitlements that name a team fail without XML ones",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(
                ("application-identifier", $"{Team}.{BundleId}"), ("com.apple.developer.team-identifier", Team)))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix),
                new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
        Expect("prefixed DER entitlements beside clean XML ones fail",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (7, DerEntitlements(
                ("com.apple.developer.kernel.increased-memory-limit", true), ("application-identifier", $"{Team}.{BundleId}")))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("a keychain group with a prefix in DER entitlements fails without XML ones",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(("keychain-access-groups", new object[] { $"{Team}.*" })))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("a prefixed key-value store in DER entitlements beside clean XML ones fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (7, DerEntitlements(
                ("com.apple.developer.kernel.increased-memory-limit", true),
                ("com.apple.developer.ubiquity-kvstore-identifier", $"{Team}.{BundleId}")))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("a key of another string type makes the DER entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, Blob(0xFADE7172, Tlv(0x70, Tlv(0x02, [1]), Tlv(0xB0, Tlv(0x30,
                Tlv(0x1E, Encoding.BigEndianUnicode.GetBytes("application-identifier")), Tlv(0x0C, Encoding.UTF8.GetBytes($"{Team}.{BundleId}")))))))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("DER nested 100 levels deep makes the entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(("com.paneport.self-test", Nested(100))))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a DER length that runs past its blob makes the entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, Blob(0xFADE7172, [0x70, 0x84, 0x7F, 0xFF, 0xFF, 0xFF, 0x02, 0x01, 0x01]))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a key given twice makes the DER entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(
                ("application-identifier", $"{Team}.{BundleId}"), ("application-identifier", BundleId)))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a team in a launch constraint fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (8, LaunchConstraint(
                ("ccat", 0), ("comp", 1), ("reqs", new (string, object)[] { ("team-identifier", Team) }), ("vers", 1)))))),
            [new Finding($"{App}Paneport.iOS (code signature)", Rules.TeamIdentifier)]);
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
        Expect("two code signature commands that name one super blob are read once",
            Files(("Paneport.iOS", TwiceNamed(SuperBlob((0, CodeDirectory()), (5, XmlEntitlements(
                $"<key>com.paneport.self-test</key><string>{new string('a', 4096)}</string>")))))),
            []);
        Expect("64 index entries that name one entitlements blob are unreadable",
            Files(("Paneport.iOS", Thin(OneBlobNamed(5, XmlEntitlements(string.Empty), 64)))),
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
        Expect("a binary property list of 40 arrays that share references is read once",
            Files(("Shared.plist", SharedReferencePlist(40))), []);
        Expect("the same list after 4 MiB of zero bytes is read once",
            Files(("Shared.plist", SharedReferencePlist(40, padding: 4 << 20))), []);
        Expect("200,000 references to one text of 16,384 characters are read once",
            Files(("Shared.plist", RawPlist(0, ArrayOf(200_000, 1), Ascii(new string('a', 16_384))))), []);
        Expect("a binary property list of more nodes than the scan visits is unreadable",
            Files(("Many.plist", RawPlist(0, ArrayOf((int)Budget.Nodes, 1), [0x09]))),
            [new Finding($"{App}Many.plist", Rules.UnreadablePropertyList)]);
        Expect("a key given twice makes a binary property list unreadable",
            Files(("Twice.plist", RawPlist(0, [0xD2, 1, 1, 2, 3], Ascii("application-identifier"), Ascii($"{Team}.{BundleId}"), Ascii(BundleId)))),
            [new Finding($"{App}Twice.plist", Rules.UnreadablePropertyList)]);
        Expect("1,000 nested elements make an XML property list unreadable",
            Files(("Deep.plist", Xml(Deep))),
            [new Finding($"{App}Deep.plist", Rules.UnreadablePropertyList)]);
        Expect("an XML property list whose reading and checking visit more nodes than the scan does is unreadable",
            Files(("Many.plist", Xml($"<array>{string.Concat(Enumerable.Repeat("<true/>", 700_000))}</array>"))),
            [new Finding($"{App}Many.plist", Rules.UnreadablePropertyList)]);

        // A text that takes longer than the limit to match. For these cases the limit is a tenth of
        // a second, far above the steps of about 16 ms in which Windows' clock moves, so that no
        // short text times out by chance, and a text of 16 MiB, which takes most of a second,
        // shows the handling.
        var limit = Rules.MatchTimeout;
        Rules.MatchTimeout = TimeSpan.FromMilliseconds(100);
        try
        {
            var text = new string('a', 16 << 20);
            Expect("a text that takes too long to match makes a binary property list unreadable",
                Files(("Long.plist", RawPlist(0, Ascii(text)))),
                [new Finding($"{App}Long.plist", Rules.UnreadablePropertyList)]);
            Expect("a text that takes too long to match makes the entitlements unreadable",
                Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements($"<key>com.paneport.self-test</key><string>{text}</string>"))))),
                [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
            Expect("a text that takes too long to match makes the DER entitlements unreadable",
                Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(("com.paneport.self-test", text)))))),
                [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        }
        finally
        {
            Rules.MatchTimeout = limit;
        }

        ExpectUnreadable("an entry of more than 256 MiB makes its package unreadable", Huge());
        // .NET stops a deflated entry at its declared size, but reads a stored one to its
        // compressed size, which can run past it.
        ExpectUnreadable("a stored entry whose data runs past its declared size makes its package unreadable",
            DeclaredSize(Files(CompressionLevel.NoCompression), 16));
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

    // A package that the scan refuses as a whole, which ScanPackages reports before it goes on.
    private static void ExpectUnreadable(string name, byte[] package)
    {
        try
        {
            Scanner.Scan(new MemoryStream(package));
            s_failures++;
            Console.WriteLine($"FAIL {name}: the scan read the package.");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Console.WriteLine($"ok   {name}");
        }
        catch (Exception exception)
        {
            s_failures++;
            Console.WriteLine($"FAIL {name}: the scan threw {exception.GetType().Name}.");
        }
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
    private static byte[] Files(params (string Name, byte[]? Bytes)[] changes) => Files(CompressionLevel.Optimal, changes);

    // The same, as a package whose entries have the given compression.
    private static byte[] Files(CompressionLevel compression, params (string Name, byte[]? Bytes)[] changes)
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

        return Package(compression, [.. files.Select(file => (App + file.Key, file.Value))]);
    }

    // A package of the given entries, by their full names.
    private static byte[] Package(params (string Name, byte[] Bytes)[] entries) => Package(CompressionLevel.Optimal, entries);

    private static byte[] Package(CompressionLevel compression, params (string Name, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                using var entry = archive.CreateEntry(name, compression).Open();
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

        return Concat(
        [
            Little(0xFEEDFACF, 0x0100000C, 0, 2, (uint)superBlobs.Length, (uint)(superBlobs.Length * Command), 0, 0),
            Little([.. commands]),
            .. superBlobs,
        ]);
    }

    // The given parts end to end, each copied once, so that the large texts of the self-test
    // take little memory to build.
    private static byte[] Concat(IReadOnlyList<byte[]> parts)
    {
        var bytes = new byte[parts.Sum(part => part.Length)];
        var offset = 0;
        foreach (var part in parts)
        {
            part.CopyTo(bytes, offset);
            offset += part.Length;
        }

        return bytes;
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

        return Concat([Big(0xFADE0CC0, (uint)offset, (uint)blobs.Length), Big([.. index]), .. blobs.Select(blob => blob.Blob)]);
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

    // Entitlements in the DER form that codesign writes beside the XML form.
    private static byte[] DerEntitlements(params (string Key, object Value)[] entries) => Blob(0xFADE7172, DerOf(entries));

    // Launch constraints, which are DER too.
    private static byte[] LaunchConstraint(params (string Key, object Value)[] entries) => Blob(0xFADE8181, DerOf(entries));

    // The DER that codesign writes for a dictionary: a version and the dictionary, whose values are
    // Booleans, integers, texts, arrays and dictionaries.
    private static byte[] DerOf((string Key, object Value)[] entries) => Tlv(0x70, Tlv(0x02, [1]), DerDictionary(entries));

    private static byte[] DerDictionary((string Key, object Value)[] entries) =>
        Tlv(0xB0, [.. entries.Select(entry => Tlv(0x30, Tlv(0x0C, Encoding.UTF8.GetBytes(entry.Key)), DerValue(entry.Value)))]);

    private static byte[] DerValue(object value) => value switch
    {
        bool flag => Tlv(0x01, [flag ? (byte)0xFF : (byte)0]),
        int number => Tlv(0x02, [(byte)number]),
        string text => Tlv(0x0C, Encoding.UTF8.GetBytes(text)),
        ValueTuple<string, object>[] entries => DerDictionary(entries),
        object[] items => Tlv(0x30, [.. items.Select(DerValue)]),
        _ => throw new ArgumentException("The self-test writes no such value.", nameof(value)),
    };

    // An element of DER: its tag, its length and its content, the given parts in turn.
    private static byte[] Tlv(byte tag, params byte[][] parts)
    {
        var size = parts.Sum(part => part.Length);
        byte[] length = size switch
        {
            < 0x80 => [(byte)size],
            < 0x100 => [0x81, (byte)size],
            < 0x10000 => [0x82, (byte)(size >> 8), (byte)size],
            _ => [0x84, .. Big((uint)size)],
        };
        return Concat([[tag], length, .. parts]);
    }

    // An array nested the given number of levels, with a Boolean at its bottom.
    private static object Nested(int depth) => depth == 0 ? true : new object[] { Nested(depth - 1) };

    // A package whose one entry holds 256 MiB and one byte of zeros, which compress to little.
    private static byte[] Huge()
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = archive.CreateEntry($"{App}Huge.bin", CompressionLevel.Fastest).Open();
            var zeros = new byte[1 << 20];
            for (var left = Scanner.MaxEntryBytes + 1; left > 0; left -= zeros.Length)
            {
                entry.Write(zeros, 0, (int)Math.Min(left, zeros.Length));
            }
        }

        return stream.ToArray();
    }

    // A thin 64-bit Mach-O file whose two LC_CODE_SIGNATURE commands name one super blob.
    private static byte[] TwiceNamed(byte[] superBlob)
    {
        const uint Start = 32 + (2 * 16);
        return
        [
            .. Little(0xFEEDFACF, 0x0100000C, 0, 2, 2, 2 * 16, 0, 0),
            .. Little(0x1D, 16, Start, (uint)superBlob.Length),
            .. Little(0x1D, 16, Start, (uint)superBlob.Length),
            .. superBlob,
        ];
    }

    // A super blob whose given number of index entries all name one blob, under one slot.
    private static byte[] OneBlobNamed(uint slot, byte[] blob, int entries)
    {
        var header = (uint)(12 + (entries * 8));
        return
        [
            .. Big(0xFADE0CC0, header + (uint)blob.Length, (uint)entries),
            .. Enumerable.Range(0, entries).SelectMany(_ => Big(slot, header)),
            .. blob,
        ];
    }

    // A blob of a code signature: its magic, its length, which a given value replaces, and its
    // content.
    private static byte[] Blob(uint magic, byte[] content, uint? length = null) =>
        Concat([Big(magic, length ?? (uint)(8 + content.Length)), content]);

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
    // the next, and a Boolean last, after the given number of zero bytes. Read without sharing, it
    // would be 2^depth objects.
    private static byte[] SharedReferencePlist(int depth, int padding = 0) =>
        RawPlist(padding, [.. Enumerable.Range(1, depth).Select(next => new byte[] { 0xA2, (byte)next, (byte)next }), [0x09]]);

    // A bplist00 file of the given objects, the first of them the top, after the given number of
    // zero bytes, with offsets of four bytes and one-byte references.
    private static byte[] RawPlist(int padding, params byte[][] objects)
    {
        var file = new List<byte>(40 + padding + objects.Sum(item => item.Length) + (objects.Length * 4));
        file.AddRange("bplist00"u8.ToArray());
        file.AddRange(new byte[padding]);
        var offsets = new List<uint>();
        foreach (var item in objects)
        {
            offsets.Add((uint)file.Count);
            file.AddRange(item);
        }

        var table = file.Count;
        file.AddRange(Big([.. offsets]));
        var trailer = new byte[32];
        trailer[6] = 4;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)objects.Length);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        file.AddRange(trailer);
        return [.. file];
    }

    // An array object of the given number of one-byte references, all to one object.
    private static byte[] ArrayOf(int count, byte reference) => [.. Marker(0xA0, count), .. Enumerable.Repeat(reference, count)];

    // An ASCII text object.
    private static byte[] Ascii(string text) => Concat([Marker(0x50, text.Length), Encoding.ASCII.GetBytes(text)]);

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

    // An object's marker: its type with a length below 15, or with 15 and the length as an integer
    // object of one, two or four bytes.
    private static byte[] Marker(int type, int length) => length switch
    {
        < 15 => [(byte)(type | length)],
        < 0x100 => [(byte)(type | 0xF), 0x10, (byte)length],
        < 0x10000 => [(byte)(type | 0xF), 0x11, (byte)(length >> 8), (byte)length],
        _ => [(byte)(type | 0xF), 0x12, .. Big((uint)length)],
    };

    // The package with the uncompressed size that its central directory gives the app's Info.plist
    // replaced. A header of the central directory is the signature PK\1\2, ..., the uncompressed
    // size at byte 24, the name's length at byte 28 and the name at byte 46.
    private static byte[] DeclaredSize(byte[] package, uint size)
    {
        var name = Encoding.ASCII.GetBytes($"{App}Info.plist");
        for (var offset = 0; offset + 46 + name.Length <= package.Length; offset++)
        {
            if (BinaryPrimitives.ReadUInt32LittleEndian(package.AsSpan(offset)) == 0x02014B50
                && BinaryPrimitives.ReadUInt16LittleEndian(package.AsSpan(offset + 28)) == name.Length
                && package.AsSpan(offset + 46).StartsWith(name))
            {
                BinaryPrimitives.WriteUInt32LittleEndian(package.AsSpan(offset + 24), size);
                return package;
            }
        }

        throw new InvalidOperationException("The package holds no such entry.");
    }
}
