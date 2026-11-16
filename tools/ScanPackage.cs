// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Scans app packages for what a build step may leave behind that names an Apple account
// (C-0009 section 6, S1.3.9): a provisioning profile, and a property list or entitlement that
// names a team identifier or an application-identifier prefix. It lists the entitlements of
// every code signature it finds. M9.1 extends it for releases and keeps its name.
//
//   dotnet run tools/ScanPackage.cs -- <package.ipa>...   scan packages
//   dotnet run tools/ScanPackage.cs -- --self-test        check every rule on generated packages
//
// A finding names the package, the file inside it and the rule, never the value, because CI
// logs are as public as the repository. Exit codes: 0 clean, 1 findings, 2 usage or a package
// that cannot be read. docs/design/ios-app-head.md section 11 describes the rules.

#:property PublishAot=false

using System.Buffers.Binary;
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
    public static int ScanPackages(IReadOnlyList<string> paths)
    {
        var total = 0;
        foreach (var path in paths)
        {
            var report = Scan(File.OpenRead(path));
            foreach (var entitlement in report.Entitlements)
            {
                Console.WriteLine($"{Path.GetFileName(path)}: {entitlement}");
            }

            foreach (var finding in report.Findings)
            {
                Console.WriteLine($"{Path.GetFileName(path)}: {finding.Entry}: {finding.Rule}");
                if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
                {
                    Console.WriteLine($"::error title=Package scan::{Path.GetFileName(path)}: {finding.Entry}: {finding.Rule}");
                }
            }

            total += report.Findings.Count;
            Console.WriteLine($"{Path.GetFileName(path)}: {report.Findings.Count} findings.");
        }

        return total == 0 ? 0 : 1;
    }

    // Scans one package, a zip archive such as an .ipa.
    public static PackageReport Scan(Stream package)
    {
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        var findings = new List<Finding>();
        var entitlements = new List<string>();
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
                ScanSignature(name, bytes, findings, entitlements);
            }
            else if (Plist.IsPropertyList(name, bytes))
            {
                try
                {
                    Check(name, Plist.Parse(bytes), findings);
                }
                catch (InvalidDataException)
                {
                    findings.Add(new Finding(name, Rules.UnreadablePropertyList));
                }
            }
        }

        return new PackageReport([.. findings.Distinct()], entitlements);
    }

    private static void ScanSignature(string name, byte[] bytes, List<Finding> findings, List<string> entitlements)
    {
        byte[]? xml;
        try
        {
            xml = MachO.ReadEntitlements(bytes);
        }
        catch (InvalidDataException)
        {
            findings.Add(new Finding(name, Rules.UnreadableSignature));
            return;
        }

        if (xml is null)
        {
            return;
        }

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

            Check($"{name} (code signature)", tree, findings);
        }
        catch (InvalidDataException)
        {
            findings.Add(new Finding(name, Rules.UnreadableSignature));
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

// The code signature of a Mach-O file, as Apple's open-source mach-o/loader.h, mach-o/fat.h and
// the code signing blobs describe it: LC_CODE_SIGNATURE points at a super blob, whose index names
// the entitlements blob, an XML property list. Mach-O headers are little-endian on arm64; fat
// headers and signature blobs are big-endian.
internal static class MachO
{
    private const uint Magic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint CodeSignatureCommand = 0x1D;
    private const uint SuperBlobMagic = 0xFADE0CC0;
    private const uint EntitlementsMagic = 0xFADE7171;
    private const uint EntitlementsSlot = 5;

    public static bool IsMachO(byte[] bytes) =>
        bytes.Length >= 32 && (BinaryPrimitives.ReadUInt32LittleEndian(bytes) == Magic64 || BinaryPrimitives.ReadUInt32BigEndian(bytes) == FatMagic);

    // The entitlements of the first slice that has a signature, or null when no slice is signed.
    public static byte[]? ReadEntitlements(byte[] bytes)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(bytes) == FatMagic)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4));
            for (var index = 0; index < count && index < 16; index++)
            {
                var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, 8 + (index * 20) + 8, 4));
                if (ReadThin(bytes, offset) is { } xml)
                {
                    return xml;
                }
            }

            return null;
        }

        return ReadThin(bytes, 0);
    }

    private static byte[]? ReadThin(byte[] bytes, int start)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start, 4)) != Magic64)
        {
            throw new InvalidDataException("not a 64-bit Mach-O file");
        }

        var commands = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start + 16, 4));
        var offset = start + 32;
        for (var index = 0; index < commands; index++)
        {
            var command = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, 4));
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 4, 4));
            if (size < 8)
            {
                throw new InvalidDataException("a load command is too short");
            }

            if (command == CodeSignatureCommand)
            {
                var dataOffset = start + (int)BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 8, 4));
                return ReadBlob(bytes, dataOffset);
            }

            offset += size;
        }

        return null;
    }

    private static byte[]? ReadBlob(byte[] bytes, int superBlob)
    {
        if (BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob, 4)) != SuperBlobMagic)
        {
            throw new InvalidDataException("the code signature is not a super blob");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 8, 4));
        for (var index = 0; index < count && index < 64; index++)
        {
            var slot = BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 12 + (index * 8), 4));
            var blob = superBlob + (int)BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, superBlob + 16 + (index * 8), 4));
            if (slot != EntitlementsSlot)
            {
                continue;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, blob, 4)) != EntitlementsMagic)
            {
                throw new InvalidDataException("the entitlements blob has another magic");
            }

            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(Slice(bytes, blob + 4, 4));
            return Slice(bytes, blob + 8, length - 8).ToArray();
        }

        return null;
    }

    private static ReadOnlySpan<byte> Slice(byte[] bytes, int offset, int length) =>
        offset < 0 || length < 0 || offset + length > bytes.Length
            ? throw new InvalidDataException("a structure lies outside the file")
            : bytes.AsSpan(offset, length);
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

            var trailer = bytes.AsSpan(bytes.Length - 32);
            var offsetSize = trailer[6];
            var referenceSize = trailer[7];
            var count = checked((int)BinaryPrimitives.ReadUInt64BigEndian(trailer[8..]));
            var top = checked((int)BinaryPrimitives.ReadUInt64BigEndian(trailer[16..]));
            var table = checked((int)BinaryPrimitives.ReadUInt64BigEndian(trailer[24..]));
            if (offsetSize is < 1 or > 8 || referenceSize is < 1 or > 8 || count < 1 || top >= count
                || table < 8 || table + ((long)count * offsetSize) > bytes.Length - 32)
            {
                throw new InvalidDataException("the binary property list's trailer is invalid");
            }

            var offsets = new int[count];
            for (var index = 0; index < count; index++)
            {
                offsets[index] = checked((int)Number(bytes.AsSpan(table + (index * offsetSize), offsetSize)));
            }

            return new Reader(bytes, offsets, referenceSize).Read(top, 0);
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
            public object Read(int reference, int depth)
            {
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

                var size = 1 << (marker & 0x0F);
                return (checked((int)Number(Span(offset + 2, size))), offset + 2 + size);
            }

            private int Reference(int offset) => checked((int)Number(Span(offset, referenceSize)));

            private ReadOnlySpan<byte> Span(int offset, int length) =>
                offset < 0 || length < 0 || (long)offset + length > bytes.Length
                    ? throw new InvalidDataException("the binary property list is malformed")
                    : bytes.AsSpan(offset, length);
        }
    }
}

// Checks every rule on packages that it builds in memory: a clean package passes, and a planted
// provisioning profile, team identifier or application-identifier prefix each fails and names
// its file. The planted values are made up and exist only in memory.
internal static class SelfTest
{
    private const string App = "Payload/Paneport.app/";
    private const string Team = "AB12CD34EF";
    private const string BundleId = "io.github.recelilious.paneport";

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

        Console.WriteLine(s_failures == 0 ? "Self-test passed." : $"Self-test failed: {s_failures} cases.");
        return s_failures == 0 ? 0 : 1;
    }

    private static void Expect(string name, byte[] package, Finding[] expected)
    {
        var report = Scanner.Scan(new MemoryStream(package));
        var found = report.Findings.OrderBy(finding => finding.Entry, StringComparer.Ordinal).ToList();
        var wanted = expected.OrderBy(finding => finding.Entry, StringComparer.Ordinal).ToList();
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

    // The files of a clean app, with the given files added or replaced, as a package.
    private static byte[] Files(params (string Name, byte[] Bytes)[] changes)
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
            files[name] = bytes;
        }

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in files)
            {
                using var entry = archive.CreateEntry(App + name).Open();
                entry.Write(bytes);
            }
        }

        return stream.ToArray();
    }

    private static string InfoBody(string extra) =>
        $"<dict><key>CFBundleIdentifier</key><string>{BundleId}</string><key>CFBundleDisplayName</key><string>Paneport</string>{extra}</dict>";

    private static string Entitlements(string extra) =>
        $"<dict><key>com.apple.developer.kernel.increased-memory-limit</key><true/>{extra}</dict>";

    private static byte[] Xml(string body) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + $"<plist version=\"1.0\">{body}</plist>\n");

    // A 64-bit Mach-O header with one LC_CODE_SIGNATURE command whose super blob holds the
    // entitlements, or with no load command for an unsigned binary.
    private static byte[] MachO(string? entitlementsBody)
    {
        var bytes = new List<byte>();
        void little(uint value)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, value);
            bytes.AddRange(buffer);
        }

        void big(uint value)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            bytes.AddRange(buffer);
        }

        little(0xFEEDFACF);
        little(0x0100000C);
        little(0);
        little(2);
        little(entitlementsBody is null ? 0U : 1U);
        little(entitlementsBody is null ? 0U : 16U);
        little(0);
        little(0);
        if (entitlementsBody is null)
        {
            return [.. bytes];
        }

        var xml = Xml(entitlementsBody);
        const uint SignatureOffset = 64;
        var blobLength = (uint)(8 + xml.Length);
        little(0x1D);
        little(16);
        little(SignatureOffset);
        little(20 + blobLength);
        while (bytes.Count < SignatureOffset)
        {
            bytes.Add(0);
        }

        big(0xFADE0CC0);
        big(20 + blobLength);
        big(1);
        big(5);
        big(20);
        big(0xFADE7171);
        big(blobLength);
        bytes.AddRange(xml);
        return [.. bytes];
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
