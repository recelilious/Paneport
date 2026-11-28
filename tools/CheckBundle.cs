// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Checks a built app bundle against eng/bundle-layout.json (C-0009 section 4, S4.4.8) and reports
// what each part of the bundle weighs. The checks, each of which names the file and the rule that
// it breaks and never a value that it found:
//
// - every required framework and PE module is in place, and none is missing or in another folder:
//   a framework only in Frameworks/, a PE module only in its folder under wine/, a Mach-O file
//   only at the root of the bundle (its executable) or under Frameworks/;
// - every Mach-O file, thin or fat, is arm64 and built for iOS, and each PE module has the machine
//   that its folder expects, or that of the test programs;
// - nothing that C-0009 section 4 excludes (C-0003 section 8) is in the bundle, by the name
//   patterns of the layout file;
// - the test programs are only in the device test flavor, and there each is listed in
//   test-programs/SHA256SUMS with its hash;
// - the template prefix has no absolute host path, no host name, no user folder or user name but
//   the template's own, and no link that leaves it or maps the host's root.
//
//   dotnet run tools/CheckBundle.cs -- --app <app> --flavor main|test [--layout <file>] [--summary <file>]
//   dotnet run tools/CheckBundle.cs -- --self-test
//
// The size report is a Markdown table that goes to the standard output and is appended to the file
// of --summary, which the ios job names as $GITHUB_STEP_SUMMARY. Exit codes: 0 clean, 1 findings,
// 2 usage, or a layout or bundle that cannot be read. docs/design/runtime-ios.md section 9
// describes the rules, and tools/README.md the layout file.

#:property PublishAot=false

using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return Cli.Run(args);
}
catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
    or JsonException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

// What the command line asks for.
internal static class Cli
{
    public const string DefaultLayout = "eng/bundle-layout.json";

    public static int Run(string[] args)
    {
        if (args is ["--self-test"])
        {
            return SelfTest.Run();
        }

        string? app = null;
        string? flavorName = null;
        var layoutPath = DefaultLayout;
        string? summary = null;
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length)
            {
                return Usage();
            }

            switch (args[index])
            {
                case "--app":
                    app = args[index + 1];
                    break;
                case "--flavor":
                    flavorName = args[index + 1];
                    break;
                case "--layout":
                    layoutPath = args[index + 1];
                    break;
                case "--summary":
                    summary = args[index + 1];
                    break;
                default:
                    return Usage();
            }
        }

        var flavor = flavorName switch
        {
            "main" => Flavor.Main,
            "test" => Flavor.Test,
            _ => (Flavor?)null,
        };
        if (app is null || flavor is null)
        {
            return Usage();
        }

        if (!Directory.Exists(app))
        {
            Console.Error.WriteLine($"error: {app} is not a folder.");
            return 2;
        }

        var layout = Layout.Parse(File.ReadAllText(layoutPath), layoutPath);
        var report = Checker.Check(app, flavor.Value, layout);
        return Report(app, flavor.Value, report, summary);
    }

    private static int Usage()
    {
        Console.Error.WriteLine(
            "usage: dotnet run tools/CheckBundle.cs -- --app <app> --flavor main|test [--layout <file>] [--summary <file>]"
            + " | --self-test");
        return 2;
    }

    private static int Report(string app, Flavor flavor, BundleReport report, string? summary)
    {
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        foreach (var finding in report.Findings)
        {
            Console.Error.WriteLine($"{finding.File}: {finding.Rule}: {finding.Text}");
            if (annotate)
            {
                Console.WriteLine($"::error title=Bundle layout::{finding.File}: {finding.Rule}");
            }
        }

        var table = report.Markdown(Path.GetFileName(Path.TrimEndingDirectorySeparator(app)), flavor);
        Console.WriteLine(table);
        if (summary is not null)
        {
            File.AppendAllText(summary, table + "\n");
        }

        if (report.Findings.Count == 0)
        {
            Console.WriteLine($"Bundle layout: {app} ({FlavorName(flavor)}) is as eng/bundle-layout.json requires.");
            return 0;
        }

        Console.Error.WriteLine($"Bundle layout: {report.Findings.Count} finding(s) in {app} ({FlavorName(flavor)}).");
        return 1;
    }

    public static string FlavorName(Flavor flavor) => flavor == Flavor.Main ? "main" : "test";
}

internal enum Flavor
{
    Main,
    Test,
}

// A finding: the path of the file or folder in the bundle (or of the one that is missing), the
// rule that it breaks and what is wrong, without any value that the file holds.
internal sealed record Finding(string File, string Rule, string Text);

internal static class Rules
{
    public const string MissingFramework = "missing-framework";
    public const string MissingFolder = "missing-folder";
    public const string MissingModule = "missing-module";
    public const string FrameworkLocation = "framework-location";
    public const string MachOLocation = "macho-location";
    public const string MachOUnreadable = "macho-unreadable";
    public const string MachOArchitecture = "macho-architecture";
    public const string MachOPlatform = "macho-platform";
    public const string PeLocation = "pe-location";
    public const string PeMachine = "pe-machine";
    public const string Excluded = "excluded-content";
    public const string TestProgramInMain = "test-program-in-main";
    public const string TestProgramList = "test-program-list";
    public const string TestProgramContent = "test-program-content";
    public const string TemplateHostPath = "template-host-path";
    public const string TemplateHostName = "template-host-name";
    public const string TemplateUser = "template-user";
    public const string TemplateLink = "template-link";
    public const string TemplateUnscanned = "template-unscanned";
}

// One part of the size report.
internal sealed class Component(string name)
{
    public string Name { get; } = name;

    public int Files { get; set; }

    public long Bytes { get; set; }
}

internal sealed class BundleReport(List<Finding> findings, List<Component> components)
{
    public IReadOnlyList<Finding> Findings => findings;

    public IReadOnlyList<Component> Components => components;

    // The size report as a Markdown table, a row for each component that holds a file.
    public string Markdown(string app, Flavor flavor)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"### Bundle size: {app} ({Cli.FlavorName(flavor)})\n\n");
        text.Append("| Component | Files | Bytes | MiB |\n|---|---:|---:|---:|\n");
        foreach (var component in components.Where(component => component.Files > 0))
        {
            text.Append(CultureInfo.InvariantCulture,
                $"| {component.Name} | {component.Files} | {component.Bytes} | {component.Bytes / 1048576.0:F2} |\n");
        }

        var files = components.Sum(component => component.Files);
        var bytes = components.Sum(component => component.Bytes);
        text.Append(CultureInfo.InvariantCulture, $"| **Total** | {files} | {bytes} | {bytes / 1048576.0:F2} |\n");
        return text.ToString();
    }
}

// eng/bundle-layout.json: what a built app must hold. tools/README.md describes the keys.
internal sealed class Layout
{
    public required IReadOnlyList<FrameworkRule> Frameworks { get; init; }

    public required IReadOnlyList<PeFolderRule> PeFolders { get; init; }

    public required TestProgramRule TestPrograms { get; init; }

    public required PrefixRule Prefix { get; init; }

    public required IReadOnlyList<ExcludedRule> Excluded { get; init; }

    public static Layout Parse(string json, string source)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || Number(root, "version", source) != 1)
        {
            throw new InvalidDataException($"{source}: the layout is not an object of version 1.");
        }

        var frameworks = Items(root, "frameworks", source)
            .Select(item => new FrameworkRule(Text(item, "name", source), Flag(item, "required", source)))
            .ToList();
        var peFolders = Items(root, "peFolders", source)
            .Select(item => new PeFolderRule(
                Text(item, "path", source),
                Texts(item, "machines", source).Select(machine => Machine(machine, source)).ToList(),
                Flag(item, "required", source),
                Texts(item, "requiredModules", source)))
            .ToList();

        var programs = Member(root, "testPrograms", source);
        var testPrograms = new TestProgramRule(
            Text(programs, "folder", source),
            Text(programs, "sums", source),
            Texts(programs, "machines", source).Select(machine => Machine(machine, source)).ToList(),
            Texts(programs, "names", source));

        var prefix = Member(root, "prefixTemplate", source);
        var computerName = prefix.TryGetProperty("computerName", out var computer) && computer.ValueKind == JsonValueKind.String
            ? computer.GetString()
            : null;
        var prefixRule = new PrefixRule(
            Text(prefix, "path", source),
            Flag(prefix, "required", source),
            Texts(prefix, "allowedUserFolders", source),
            computerName,
            Texts(prefix, "hostPaths", source),
            Texts(prefix, "hostNamePatterns", source),
            Texts(prefix, "forbiddenStrings", source));
        _ = Text(prefix, "userName", source);

        var excluded = Items(root, "excluded", source)
            .Select(item => new ExcludedRule(
                Text(item, "pattern", source),
                Text(item, "reason", source),
                item.TryGetProperty("unlessWineBuiltin", out var unless) && unless.ValueKind == JsonValueKind.True))
            .ToList();

        foreach (var pattern in prefixRule.HostNamePatterns)
        {
            try
            {
                _ = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
            }
            catch (ArgumentException)
            {
                throw new InvalidDataException($"{source}: prefixTemplate.hostNamePatterns holds a pattern that is not a regular expression.");
            }
        }

        return new Layout
        {
            Frameworks = frameworks,
            PeFolders = peFolders,
            TestPrograms = testPrograms,
            Prefix = prefixRule,
            Excluded = excluded,
        };
    }

    private static JsonElement Member(JsonElement element, string name, string source) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"{source}: the key {name} is missing.");

    private static string Text(JsonElement element, string name, string source)
    {
        var value = Member(element, name, source);
        return value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : throw new InvalidDataException($"{source}: the key {name} is not a non-empty string.");
    }

    private static int Number(JsonElement element, string name, string source)
    {
        var value = Member(element, name, source);
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{source}: the key {name} is not a whole number.");
    }

    private static bool Flag(JsonElement element, string name, string source)
    {
        var value = Member(element, name, source);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidDataException($"{source}: the key {name} is not true or false."),
        };
    }

    private static List<JsonElement> Items(JsonElement element, string name, string source)
    {
        var value = Member(element, name, source);
        return value.ValueKind == JsonValueKind.Array
            ? [.. value.EnumerateArray()]
            : throw new InvalidDataException($"{source}: the key {name} is not a list.");
    }

    private static List<string> Texts(JsonElement element, string name, string source) =>
        [.. Items(element, name, source).Select(item => item.ValueKind == JsonValueKind.String
            ? item.GetString()!
            : throw new InvalidDataException($"{source}: the list {name} holds a value that is not a string."))];

    private static ushort Machine(string text, string source) =>
        text.StartsWith("0x", StringComparison.Ordinal)
        && ushort.TryParse(text.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var machine)
            ? machine
            : throw new InvalidDataException($"{source}: a machine is not a hexadecimal number such as 0xAA64.");
}

internal sealed record FrameworkRule(string Name, bool Required);

internal sealed record PeFolderRule(string Path, IReadOnlyList<ushort> Machines, bool Required, IReadOnlyList<string> RequiredModules);

internal sealed record TestProgramRule(string Folder, string Sums, IReadOnlyList<ushort> Machines, IReadOnlyList<string> Names);

internal sealed record PrefixRule(
    string Path,
    bool Required,
    IReadOnlyList<string> AllowedUserFolders,
    string? ComputerName,
    IReadOnlyList<string> HostPaths,
    IReadOnlyList<string> HostNamePatterns,
    IReadOnlyList<string> ForbiddenStrings);

internal sealed record ExcludedRule(string Pattern, string Reason, bool UnlessWineBuiltin);

// One file, folder or link of the bundle, by its path relative to the app, with forward slashes.
internal sealed record Entry(string Path, string FullPath, bool IsDirectory, bool IsLink, string? LinkTarget, long Length)
{
    public string Name => Path[(Path.LastIndexOf('/') + 1)..];

    public string Parent => Path.Contains('/', StringComparison.Ordinal) ? Path[..Path.LastIndexOf('/')] : string.Empty;
}

internal static class Glob
{
    // A pattern without a slash matches the name of any file or folder, one with a slash matches the
    // whole path; * does not cross a slash and ** does. Matching ignores case.
    public static bool Matches(string pattern, string path, string name)
    {
        return ToRegex(pattern).IsMatch(pattern.Contains('/', StringComparison.Ordinal) ? path : name);
    }

    private static Regex ToRegex(string pattern)
    {
        var text = new StringBuilder("^");
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                index++;
                if (index + 1 < pattern.Length && pattern[index + 1] == '/')
                {
                    index++;
                    text.Append("(?:.*/)?");
                }
                else
                {
                    text.Append(".*");
                }
            }
            else if (character == '*')
            {
                text.Append("[^/]*");
            }
            else if (character == '?')
            {
                text.Append("[^/]");
            }
            else
            {
                text.Append(Regex.Escape(character.ToString()));
            }
        }

        text.Append('$');
        return new Regex(text.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}

// The headers of Mach-O and PE files, read as far as the checks need.
internal static class Images
{
    public const uint CpuArm64 = 0x0100000C;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint FatMagic64 = 0xCAFEBABF;
    private const int MaxLoadCommands = 4096;

    public sealed record PeInfo(ushort Machine, bool Managed);

    // Whether the file starts like a Mach-O file, thin or fat. A fat header shares its magic number
    // with Java class files, whose next field is a version of 45 or more, which no fat file has.
    public static bool IsMachO(ReadOnlySpan<byte> head)
    {
        if (head.Length < 8)
        {
            return false;
        }

        var little = BinaryPrimitives.ReadUInt32LittleEndian(head);
        if (little is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE)
        {
            return true;
        }

        var big = BinaryPrimitives.ReadUInt32BigEndian(head);
        return big is FatMagic or FatMagic64 && BinaryPrimitives.ReadUInt32BigEndian(head[4..]) is >= 1 and <= 30;
    }

    // The problems of a Mach-O file: an architecture that is not arm64, a platform that is not iOS.
    // Each is a rule and a text; an empty list means the file is arm64 for iOS, thin or fat.
    public static List<(string Rule, string Text)> CheckMachO(FileStream stream)
    {
        var problems = new List<(string, string)>();
        var head = ReadAt(stream, 0, 8);
        if (head.Length >= 8 && BinaryPrimitives.ReadUInt32BigEndian(head) is FatMagic or FatMagic64)
        {
            CheckFat(stream, problems);
        }
        else
        {
            CheckThin(stream, 0, problems, string.Empty);
        }

        return problems;
    }

    private static void CheckFat(FileStream stream, List<(string, string)> problems)
    {
        var header = ReadAt(stream, 0, 8);
        var is64 = BinaryPrimitives.ReadUInt32BigEndian(header) == FatMagic64;
        var count = (int)BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        var size = is64 ? 32 : 20;
        var table = ReadAt(stream, 8, count * size);
        if (table.Length < count * size)
        {
            problems.Add((Rules.MachOUnreadable, "the fat header is cut short"));
            return;
        }

        for (var slice = 0; slice < count; slice++)
        {
            var entry = table.AsSpan(slice * size, size);
            var cpu = BinaryPrimitives.ReadUInt32BigEndian(entry);
            var offset = is64
                ? (long)BinaryPrimitives.ReadUInt64BigEndian(entry[8..])
                : BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            var label = $"slice {slice + 1}";
            if (cpu != CpuArm64)
            {
                problems.Add((Rules.MachOArchitecture, $"{label} is {CpuName(cpu)}, not arm64"));
                continue;
            }

            CheckThin(stream, offset, problems, label + " ");
        }

        if (count == 0)
        {
            problems.Add((Rules.MachOArchitecture, "the fat file holds no slice"));
        }
    }

    private static void CheckThin(FileStream stream, long offset, List<(string, string)> problems, string label)
    {
        var header = ReadAt(stream, offset, 32);
        if (header.Length < 28)
        {
            problems.Add((Rules.MachOUnreadable, $"{label}header is cut short"));
            return;
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (magic != 0xFEEDFACF)
        {
            var cpuBig = magic is 0xCEFAEDFE or 0xCFFAEDFE
                ? BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4))
                : BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
            problems.Add((Rules.MachOArchitecture, $"{label}is {CpuName(cpuBig)}, not arm64"));
            return;
        }

        var cpu = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
        if (cpu != CpuArm64)
        {
            problems.Add((Rules.MachOArchitecture, $"{label}is {CpuName(cpu)}, not arm64"));
            return;
        }

        var commands = (int)BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        var commandBytes = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20)), 1 << 20);
        var data = ReadAt(stream, offset + 32, commandBytes);
        string? platform = null;
        var position = 0;
        for (var command = 0; command < Math.Min(commands, MaxLoadCommands); command++)
        {
            if (position + 8 > data.Length)
            {
                problems.Add((Rules.MachOUnreadable, $"{label}load commands are cut short"));
                return;
            }

            var kind = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position));
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 4));
            if (length < 8 || position + length > data.Length)
            {
                problems.Add((Rules.MachOUnreadable, $"{label}has a load command of a wrong size"));
                return;
            }

            switch (kind)
            {
                case 0x32 when length >= 12:
                    platform = PlatformName(BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(position + 8)));
                    break;
                case 0x25:
                    platform = "iOS";
                    break;
                case 0x24:
                    platform = "macOS";
                    break;
                case 0x2F:
                    platform = "tvOS";
                    break;
                case 0x30:
                    platform = "watchOS";
                    break;
            }

            position += length;
        }

        if (platform is null)
        {
            problems.Add((Rules.MachOPlatform, $"{label}names no platform, so it is not built for iOS"));
        }
        else if (platform != "iOS")
        {
            problems.Add((Rules.MachOPlatform, $"{label}is built for {platform}, not iOS"));
        }
    }

    private static string CpuName(uint cpu) => cpu switch
    {
        0x01000007 => "x86_64",
        0x00000007 => "i386",
        0x0100000C => "arm64",
        0x0200000C => "arm64_32",
        0x0000000C => "arm",
        _ => "of another architecture",
    };

    private static string PlatformName(uint platform) => platform switch
    {
        1 => "macOS",
        2 => "iOS",
        3 => "tvOS",
        4 => "watchOS",
        5 => "bridgeOS",
        6 => "Mac Catalyst",
        7 => "the iOS simulator",
        8 => "the tvOS simulator",
        9 => "the watchOS simulator",
        10 => "DriverKit",
        11 => "visionOS",
        12 => "the visionOS simulator",
        _ => "another platform",
    };

    // The machine of a PE image, and whether it has a CLR header, which makes it a .NET assembly.
    // Null when the file is not a PE image.
    public static PeInfo? ReadPe(FileStream stream)
    {
        var dos = ReadAt(stream, 0, 64);
        if (dos.Length < 64 || dos[0] != (byte)'M' || dos[1] != (byte)'Z')
        {
            return null;
        }

        var start = BinaryPrimitives.ReadUInt32LittleEndian(dos.AsSpan(0x3C));
        if (start < 64 || start > stream.Length - 24)
        {
            return null;
        }

        var nt = ReadAt(stream, start, 24 + 240);
        if (nt.Length < 24 || nt[0] != (byte)'P' || nt[1] != (byte)'E' || nt[2] != 0 || nt[3] != 0)
        {
            return null;
        }

        var machine = BinaryPrimitives.ReadUInt16LittleEndian(nt.AsSpan(4));
        var optional = nt.AsSpan(24);
        var managed = false;
        if (optional.Length >= 2)
        {
            var magic = BinaryPrimitives.ReadUInt16LittleEndian(optional);
            var directories = magic == 0x20B ? 108 : 92;
            var clr = directories + 4 + (14 * 8);
            if (optional.Length >= clr + 8 && BinaryPrimitives.ReadUInt32LittleEndian(optional[directories..]) > 14)
            {
                managed = BinaryPrimitives.ReadUInt32LittleEndian(optional[clr..]) != 0;
            }
        }

        return new PeInfo(machine, managed);
    }

    // Wine marks its own PE modules with a signature in the DOS header's stub, which the modules
    // that Microsoft built do not have.
    public static bool IsWineBuiltin(FileStream stream)
    {
        var head = ReadAt(stream, 0x40, 64);
        var text = Encoding.ASCII.GetString(head);
        return text.Contains("Wine builtin DLL", StringComparison.Ordinal)
            || text.Contains("Wine placeholder DLL", StringComparison.Ordinal);
    }

    public static byte[] ReadAt(FileStream stream, long offset, int count)
    {
        if (offset < 0 || offset >= stream.Length || count <= 0)
        {
            return [];
        }

        var buffer = new byte[(int)Math.Min(count, stream.Length - offset)];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}

internal static class Checker
{
    // The longest file of the template that is scanned for host data. The template holds registry
    // files of a few hundred kilobytes; a larger file is reported, not skipped.
    private const long MaxScanBytes = 64L * 1024 * 1024;

    private const string Frameworks = "Frameworks";

    public static BundleReport Check(string app, Flavor flavor, Layout layout)
    {
        var findings = new List<Finding>();
        var entries = Enumerate(app);
        var byPath = entries.ToDictionary(entry => entry.Path, StringComparer.OrdinalIgnoreCase);

        CheckExcluded(entries, layout, findings);
        CheckFrameworks(entries, byPath, layout, findings);
        CheckImages(entries, flavor, layout, findings);
        CheckPeFolders(entries, byPath, layout, findings);
        CheckTestPrograms(entries, byPath, flavor, layout, findings);
        CheckTemplate(entries, byPath, layout, findings);

        var components = Sizes(entries, layout);
        return new BundleReport(findings, components);
    }

    // Every file, folder and link below the app. Links are listed and not followed.
    private static List<Entry> Enumerate(string app)
    {
        var entries = new List<Entry>();
        var root = new DirectoryInfo(app);
        Walk(root, string.Empty, entries);
        return entries;
    }

    private static void Walk(DirectoryInfo folder, string prefix, List<Entry> entries)
    {
        foreach (var info in folder.EnumerateFileSystemInfos().OrderBy(info => info.Name, StringComparer.Ordinal))
        {
            var path = prefix.Length == 0 ? info.Name : prefix + "/" + info.Name;
            var target = info.LinkTarget;
            if (target is not null)
            {
                entries.Add(new Entry(path, info.FullName, false, true, target, 0));
            }
            else if (info is DirectoryInfo directory)
            {
                entries.Add(new Entry(path, info.FullName, true, false, null, 0));
                Walk(directory, path, entries);
            }
            else
            {
                entries.Add(new Entry(path, info.FullName, false, false, null, ((FileInfo)info).Length));
            }
        }
    }

    private static void CheckExcluded(List<Entry> entries, Layout layout, List<Finding> findings)
    {
        var flagged = new List<string>();
        foreach (var entry in entries)
        {
            if (flagged.Any(folder => entry.Path.StartsWith(folder + "/", StringComparison.Ordinal)))
            {
                continue;
            }

            foreach (var rule in layout.Excluded)
            {
                if (!Glob.Matches(rule.Pattern, entry.Path, entry.Name))
                {
                    continue;
                }

                if (rule.UnlessWineBuiltin)
                {
                    if (entry.IsDirectory || entry.IsLink)
                    {
                        continue;
                    }

                    using var stream = File.OpenRead(entry.FullPath);
                    if (Images.IsWineBuiltin(stream))
                    {
                        continue;
                    }
                }

                findings.Add(new Finding(entry.Path, Rules.Excluded, $"C-0009 section 4 excludes {rule.Reason}"));
                if (entry.IsDirectory)
                {
                    flagged.Add(entry.Path);
                }

                break;
            }
        }
    }

    private static void CheckFrameworks(List<Entry> entries, Dictionary<string, Entry> byPath, Layout layout,
        List<Finding> findings)
    {
        foreach (var entry in entries.Where(entry => entry.IsDirectory
            && entry.Name.EndsWith(".framework", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Parent, Frameworks, StringComparison.Ordinal)
            && !entry.Parent.EndsWith(".framework", StringComparison.OrdinalIgnoreCase)))
        {
            findings.Add(new Finding(entry.Path, Rules.FrameworkLocation, "a framework belongs in Frameworks/"));
        }

        foreach (var rule in layout.Frameworks.Where(rule => rule.Required))
        {
            var folder = $"{Frameworks}/{rule.Name}";
            var executable = $"{folder}/{rule.Name[..rule.Name.LastIndexOf('.')]}";
            if (!byPath.TryGetValue(folder, out var found) || !found.IsDirectory)
            {
                findings.Add(new Finding(folder, Rules.MissingFramework, "the framework is not in the bundle"));
            }
            else if (!byPath.TryGetValue(executable, out var binary) || binary.IsDirectory)
            {
                findings.Add(new Finding(executable, Rules.MissingFramework, "the framework has no executable"));
            }
        }
    }

    // The architecture and platform of every Mach-O file and the machine of every PE image, and
    // where each may be.
    private static void CheckImages(List<Entry> entries, Flavor flavor, Layout layout,
        List<Finding> findings)
    {
        var template = layout.Prefix.Path;
        foreach (var entry in entries.Where(entry => !entry.IsDirectory && !entry.IsLink && entry.Length >= 8))
        {
            using var stream = File.OpenRead(entry.FullPath);
            var head = Images.ReadAt(stream, 0, 8);
            if (Images.IsMachO(head))
            {
                CheckMachO(entry, stream, findings);
                continue;
            }

            if (Images.ReadPe(stream) is not { } info)
            {
                continue;
            }

            if (entry.Path.StartsWith(template + "/", StringComparison.Ordinal))
            {
                continue;
            }

            if (layout.TestPrograms.Names.Contains(entry.Name, StringComparer.OrdinalIgnoreCase) && flavor == Flavor.Main)
            {
                findings.Add(new Finding(entry.Path, Rules.TestProgramInMain,
                    "a test program of tests/win32 is in the main app, which holds none"));
                continue;
            }

            if (info.Managed)
            {
                continue;
            }

            var folder = layout.PeFolders.FirstOrDefault(rule => entry.Path.StartsWith(rule.Path + "/", StringComparison.Ordinal));
            if (folder is not null)
            {
                if (!folder.Machines.Contains(info.Machine))
                {
                    findings.Add(new Finding(entry.Path, Rules.PeMachine,
                        $"the machine of the module is not one that {folder.Path}/ takes"));
                }
            }
            else if (entry.Path.StartsWith(layout.TestPrograms.Folder + "/", StringComparison.Ordinal))
            {
                if (flavor == Flavor.Test && !layout.TestPrograms.Machines.Contains(info.Machine))
                {
                    findings.Add(new Finding(entry.Path, Rules.PeMachine, "a test program is not an x86-64 image"));
                }
            }
            else
            {
                findings.Add(new Finding(entry.Path, Rules.PeLocation,
                    "a PE module belongs in its folder under wine/, a Windows program of ours in test-programs/"));
            }
        }
    }

    private static void CheckMachO(Entry entry, FileStream stream, List<Finding> findings)
    {
        var allowed = entry.Parent.Length == 0
            || entry.Path.StartsWith(Frameworks + "/", StringComparison.Ordinal);
        if (!allowed)
        {
            findings.Add(new Finding(entry.Path, Rules.MachOLocation,
                "a Mach-O file belongs in Frameworks/, or at the root of the bundle as its executable"));
        }

        foreach (var (rule, text) in Images.CheckMachO(stream))
        {
            findings.Add(new Finding(entry.Path, rule, text));
        }
    }

    private static void CheckPeFolders(List<Entry> entries, Dictionary<string, Entry> byPath, Layout layout,
        List<Finding> findings)
    {
        foreach (var rule in layout.PeFolders)
        {
            var exists = byPath.TryGetValue(rule.Path, out var folder) && folder.IsDirectory;
            if (!exists)
            {
                if (rule.Required)
                {
                    findings.Add(new Finding(rule.Path, Rules.MissingFolder, "the folder of PE modules is not in the bundle"));
                }

                continue;
            }

            foreach (var module in rule.RequiredModules)
            {
                var found = entries.Any(entry => !entry.IsDirectory
                    && string.Equals(entry.Path, $"{rule.Path}/{module}", StringComparison.OrdinalIgnoreCase));
                if (!found)
                {
                    findings.Add(new Finding($"{rule.Path}/{module}", Rules.MissingModule, "the PE module is not in the bundle"));
                }
            }
        }
    }

    private static void CheckTestPrograms(List<Entry> entries, Dictionary<string, Entry> byPath, Flavor flavor,
        Layout layout, List<Finding> findings)
    {
        var folder = layout.TestPrograms.Folder;
        var exists = byPath.TryGetValue(folder, out var found) && found.IsDirectory;
        if (flavor == Flavor.Main)
        {
            if (exists)
            {
                findings.Add(new Finding(folder, Rules.TestProgramInMain, "the main app holds no test programs"));
            }

            return;
        }

        if (!exists)
        {
            findings.Add(new Finding(folder, Rules.MissingFolder, "the device test flavor holds its test programs here"));
            return;
        }

        var files = entries.Where(entry => entry.Parent == folder).ToList();
        var sumsPath = $"{folder}/{layout.TestPrograms.Sums}";
        var sums = files.FirstOrDefault(entry => entry.Name == layout.TestPrograms.Sums && !entry.IsDirectory);
        if (sums is null)
        {
            findings.Add(new Finding(sumsPath, Rules.TestProgramList, "the list of SHA-256 hashes is missing"));
            return;
        }

        var listed = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var line in File.ReadAllText(sums.FullPath).Split('\n'))
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            var match = Regex.Match(line, "^([0-9a-f]{64})  ([^/\\\\ ][^/\\\\]*)$", RegexOptions.CultureInvariant,
                TimeSpan.FromSeconds(1));
            if (!match.Success)
            {
                findings.Add(new Finding(sumsPath, Rules.TestProgramList,
                    $"line {lineNumber} is not a lowercase SHA-256, two spaces and a file name"));
            }
            else if (!listed.TryAdd(match.Groups[2].Value, match.Groups[1].Value))
            {
                findings.Add(new Finding(sumsPath, Rules.TestProgramList, $"line {lineNumber} lists a file a second time"));
            }
        }

        foreach (var file in files.Where(file => file != sums))
        {
            if (file.IsDirectory || file.IsLink || !file.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(file.Path, Rules.TestProgramContent, "test-programs/ holds only .exe files and the list"));
                continue;
            }

            if (!listed.TryGetValue(file.Name, out var expected))
            {
                findings.Add(new Finding(file.Path, Rules.TestProgramList, "the file is not in the list of hashes"));
            }
            else if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file.FullPath))), expected,
                StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(file.Path, Rules.TestProgramList, "the SHA-256 hash differs from the list"));
            }
        }

        foreach (var name in listed.Keys.Where(name => !files.Any(file => file.Name == name)))
        {
            findings.Add(new Finding($"{folder}/{name}", Rules.TestProgramList, "the list names a file that is not in the bundle"));
        }
    }

    private static void CheckTemplate(List<Entry> entries, Dictionary<string, Entry> byPath, Layout layout,
        List<Finding> findings)
    {
        var rule = layout.Prefix;
        if (!byPath.TryGetValue(rule.Path, out var root) || !root.IsDirectory)
        {
            if (rule.Required)
            {
                findings.Add(new Finding(rule.Path, Rules.MissingFolder, "the template prefix is not in the bundle"));
            }

            return;
        }

        var hostPaths = rule.HostPaths
            .Select(path => (Path: path, Regex: new Regex("(?<![A-Za-z0-9_])" + Regex.Escape(path),
                RegexOptions.CultureInvariant, TimeSpan.FromSeconds(2))))
            .ToList();
        var hostNames = rule.HostNamePatterns
            .Select(pattern => new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2)))
            .ToList();
        var users = new Regex(@"[A-Za-z]:(?:\\\\|\\|/)users(?:\\\\|\\|/)([^\\/""\r\n\t]+)",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        var computer = new Regex(@"""(?:Active)?ComputerName""=""([^""]*)""",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromSeconds(2));
        var userFolder = new Regex(@"^[^/]+/users/([^/]+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1));

        foreach (var entry in entries.Where(entry => entry.Path.StartsWith(rule.Path + "/", StringComparison.Ordinal)))
        {
            var inside = entry.Path[(rule.Path.Length + 1)..];
            if (entry.IsLink)
            {
                CheckLink(entry, inside, root, findings);
                continue;
            }

            if (entry.IsDirectory)
            {
                if (userFolder.Match(inside) is { Success: true } folder
                    && !rule.AllowedUserFolders.Contains(folder.Groups[1].Value, StringComparer.OrdinalIgnoreCase))
                {
                    findings.Add(new Finding(entry.Path, Rules.TemplateUser, "a user folder other than the template's"));
                }

                continue;
            }

            if (string.Equals(entry.Name, "z:", StringComparison.OrdinalIgnoreCase)
                && inside.EndsWith("dosdevices/z:", StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(new Finding(entry.Path, Rules.TemplateLink, "the z: drive maps the host's root"));
            }

            ScanFile(entry, rule, hostPaths, hostNames, users, computer, findings);
        }
    }

    private static void CheckLink(Entry entry, string inside, Entry root, List<Finding> findings)
    {
        if (string.Equals(entry.Name, "z:", StringComparison.OrdinalIgnoreCase)
            && inside.EndsWith("dosdevices/z:", StringComparison.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(entry.Path, Rules.TemplateLink, "the z: drive maps the host's root"));
            return;
        }

        var target = entry.LinkTarget ?? string.Empty;
        var absolute = target.StartsWith('/') || target.StartsWith('\\') || (target.Length >= 2 && target[1] == ':');
        if (!absolute)
        {
            var parent = Path.GetDirectoryName(entry.FullPath) ?? string.Empty;
            var resolved = Path.GetFullPath(Path.Combine(parent, target));
            var rootFull = Path.GetFullPath(root.FullPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            absolute = !resolved.StartsWith(rootFull, StringComparison.Ordinal);
        }

        if (absolute)
        {
            findings.Add(new Finding(entry.Path, Rules.TemplateLink, "a link that leaves the template"));
        }
    }

    // Reads a file of the template as UTF-8 and as UTF-16LE text, and reports each rule once. Images
    // are not text: a placeholder module that Wine writes is skipped, which is its own path check.
    private static void ScanFile(Entry entry, PrefixRule rule, List<(string Path, Regex Regex)> hostPaths,
        List<Regex> hostNames, Regex users, Regex computer, List<Finding> findings)
    {
        if (entry.Length > MaxScanBytes)
        {
            findings.Add(new Finding(entry.Path, Rules.TemplateUnscanned, "the file is too large to scan"));
            return;
        }

        var bytes = File.ReadAllBytes(entry.FullPath);
        if (Images.IsMachO(bytes) || (bytes.Length > 1 && bytes[0] == (byte)'M' && bytes[1] == (byte)'Z'))
        {
            return;
        }

        var views = new[] { Encoding.Latin1.GetString(bytes), Encoding.Unicode.GetString(bytes) };
        var reported = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in views)
        {
            if (hostPaths.Any(item => item.Regex.IsMatch(text)) && reported.Add(Rules.TemplateHostPath))
            {
                findings.Add(new Finding(entry.Path, Rules.TemplateHostPath, "the file holds an absolute path of the host"));
            }

            if ((hostNames.Any(pattern => pattern.IsMatch(text))
                    || rule.ForbiddenStrings.Any(word => text.Contains(word, StringComparison.OrdinalIgnoreCase))
                    || (rule.ComputerName is not null && computer.Matches(text)
                        .Any(match => !string.Equals(match.Groups[1].Value, rule.ComputerName, StringComparison.OrdinalIgnoreCase))))
                && reported.Add(Rules.TemplateHostName))
            {
                findings.Add(new Finding(entry.Path, Rules.TemplateHostName, "the file holds the name of a host"));
            }

            if (users.Matches(text).Any(match => !rule.AllowedUserFolders.Contains(match.Groups[1].Value,
                    StringComparer.OrdinalIgnoreCase))
                && reported.Add(Rules.TemplateUser))
            {
                findings.Add(new Finding(entry.Path, Rules.TemplateUser, "the file names a user folder other than the template's"));
            }
        }
    }

    // The size report's components: each framework, each folder under wine/, the test programs, the
    // main executable, the .NET assemblies at the root, licenses/, and everything else.
    private static List<Component> Sizes(List<Entry> entries, Layout layout)
    {
        var components = new Dictionary<string, Component>(StringComparer.Ordinal);
        var order = new List<Component>();
        Component componentOf(string name)
        {
            if (!components.TryGetValue(name, out var component))
            {
                component = new Component(name);
                components[name] = component;
                order.Add(component);
            }

            return component;
        }

        _ = componentOf("main executable");
        _ = componentOf(".NET assemblies");
        foreach (var entry in entries.Where(entry => !entry.IsDirectory))
        {
            string name;
            var parts = entry.Path.Split('/');
            if (parts.Length >= 3 && parts[0] == Frameworks)
            {
                name = $"{Frameworks}/{parts[1]}";
            }
            else if (parts.Length >= 1 && entry.Path.StartsWith(layout.Prefix.Path + "/", StringComparison.Ordinal))
            {
                name = layout.Prefix.Path;
            }
            else if (parts.Length >= 3 && parts[0] == "wine")
            {
                name = $"wine/{parts[1]}";
            }
            else if (parts[0] == layout.TestPrograms.Folder && parts.Length >= 2)
            {
                name = layout.TestPrograms.Folder;
            }
            else if (parts[0] == "licenses" && parts.Length >= 2)
            {
                name = "licenses";
            }
            else if (parts.Length == 1)
            {
                name = IsAssembly(entry.Name) ? ".NET assemblies" : IsExecutable(entry) ? "main executable" : "everything else";
            }
            else
            {
                name = "everything else";
            }

            var component = componentOf(name);
            component.Files++;
            component.Bytes += entry.Length;
        }

        _ = componentOf("everything else");
        return order;
    }

    private static bool IsAssembly(string name) =>
        name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || name.Contains(".aotdata", StringComparison.Ordinal);

    // A file at the root that starts as a Mach-O file.
    private static bool IsExecutable(Entry entry)
    {
        if (entry.IsLink || entry.Length < 8)
        {
            return false;
        }

        using var stream = File.OpenRead(entry.FullPath);
        return Images.IsMachO(Images.ReadAt(stream, 0, 8));
    }
}

// The self-test builds its bundles in a temporary folder from headers that it writes itself, so
// that no binary is committed. Every case names what it expects as pairs of a file and a rule.
internal static class SelfTest
{
    private const string Secret = "Zorblax";

    // The two backslashes with which a registry file writes one.
    private const string Doubled = "\\\\";

    private const string StrictLayout = """
        {
          "version": 1,
          "frameworks": [{ "name": "paneport_host.framework", "required": true }],
          "peFolders": [
            { "path": "wine/aarch64-windows", "machines": ["0xAA64"], "required": true, "requiredModules": ["ntdll.dll"] },
            { "path": "wine/arm64ec-windows", "machines": ["0x8664", "0xAA64"], "required": true, "requiredModules": ["ntdll.dll"] },
            { "path": "wine/i386-windows", "machines": ["0x014C"], "required": true, "requiredModules": ["ntdll.dll"] }
          ],
          "testPrograms": {
            "folder": "test-programs", "sums": "SHA256SUMS", "machines": ["0x8664"],
            "names": ["console.exe", "seh.exe"]
          },
          "prefixTemplate": {
            "path": "wine/prefix-template", "required": true, "userName": "paneport",
            "allowedUserFolders": ["paneport", "Public"], "computerName": "PANEPORT",
            "hostPaths": ["/Users/", "/home/", "/private/var/", "/tmp/"],
            "hostNamePatterns": ["[A-Za-z0-9][A-Za-z0-9-]*[.]local\\b"],
            "forbiddenStrings": ["Zorblax-host"]
          },
          "excluded": [
            { "pattern": "wine-mono*", "reason": "Wine Mono" },
            { "pattern": "*.msi", "reason": "an installer package" },
            { "pattern": "steamclient*", "reason": "Valve's client" },
            { "pattern": "vcruntime*", "unlessWineBuiltin": true, "reason": "a Microsoft runtime" },
            { "pattern": "**/share/wine/gecko/**", "reason": "Wine Gecko" }
          ]
        }
        """;

    private const string HostFramework = "Frameworks/paneport_host.framework/paneport_host";

    private static int s_failures;

    private static int s_skipped;

    public static int Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "checkbundle-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Cases(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, true);
            }
            catch (IOException)
            {
                Console.Error.WriteLine("warning: the temporary folder was not removed.");
            }
        }

        if (s_failures > 0)
        {
            Console.Error.WriteLine($"Self-test failed: {s_failures} case(s).");
            return 1;
        }

        Console.WriteLine(s_skipped > 0 ? $"Self-test passed ({s_skipped} skipped)." : "Self-test passed.");
        return 0;
    }

    private static void Cases(string root)
    {
        var strict = Layout.Parse(StrictLayout, "the self-test layout");
        var next = 0;

        void addCase(string name, Flavor flavor, Action<string>? change, params (string File, string Rule)[] expected)
        {
            var app = Path.Combine(root, $"case{next++}", "Paneport.app");
            Directory.CreateDirectory(app);
            Write(app, flavor, false);
            change?.Invoke(app);
            Expect(name, app, flavor, strict, expected);
        }

        // Folders and frameworks.
        addCase("a clean main app passes", Flavor.Main, null);
        addCase("a clean device test flavor app passes", Flavor.Test, null);
        addCase("a missing framework fails", Flavor.Main,
            app => Directory.Delete(P(app, "Frameworks/paneport_host.framework"), true),
            ("Frameworks/paneport_host.framework", Rules.MissingFramework));
        addCase("a framework without its executable fails", Flavor.Main,
            app => File.Delete(P(app, HostFramework)),
            (HostFramework, Rules.MissingFramework));
        addCase("a framework outside Frameworks fails", Flavor.Main,
            app => Put(app, "Plugins/Other.framework/Other", MachO(Images.CpuArm64, BuildVersion(2))),
            ("Plugins/Other.framework", Rules.FrameworkLocation),
            ("Plugins/Other.framework/Other", Rules.MachOLocation));
        addCase("a missing required PE module fails", Flavor.Main,
            app => File.Delete(P(app, "wine/aarch64-windows/ntdll.dll")),
            ("wine/aarch64-windows/ntdll.dll", Rules.MissingModule));
        addCase("a missing PE folder fails", Flavor.Main,
            app => Directory.Delete(P(app, "wine/i386-windows"), true),
            ("wine/i386-windows", Rules.MissingFolder));

        // PE modules.
        addCase("a PE module outside its folders fails", Flavor.Main,
            app => Put(app, "wine/kernel32.dll", Pe(0xAA64, wine: true)),
            ("wine/kernel32.dll", Rules.PeLocation));
        addCase("a PE module in the bundle's root fails", Flavor.Main,
            app => Put(app, "d3d11.dll", Pe(0xAA64, wine: true)),
            ("d3d11.dll", Rules.PeLocation));
        addCase("a PE module of another machine fails", Flavor.Main,
            app => Put(app, "wine/aarch64-windows/kernel32.dll", Pe(0x014C, wine: true)),
            ("wine/aarch64-windows/kernel32.dll", Rules.PeMachine));
        addCase("an ARM64X image in the ARM64EC folder passes", Flavor.Main,
            app => Put(app, "wine/arm64ec-windows/kernelbase.dll", Pe(0xAA64, wine: true)));
        addCase("a .NET assembly outside the PE folders passes", Flavor.Main,
            app => Put(app, "Paneport.Core.dll", Pe(0x014C, managed: true)));

        // Mach-O files.
        addCase("a macOS Mach-O file fails and names the file", Flavor.Main,
            app => Put(app, HostFramework, MachO(Images.CpuArm64, BuildVersion(1))),
            (HostFramework, Rules.MachOPlatform));
        addCase("an iOS simulator Mach-O file fails", Flavor.Main,
            app => Put(app, "Paneport.iOS", MachO(Images.CpuArm64, BuildVersion(7))),
            ("Paneport.iOS", Rules.MachOPlatform));
        addCase("a thin x86_64 Mach-O file fails", Flavor.Main,
            app => Put(app, HostFramework, MachO(0x01000007, BuildVersion(2))),
            (HostFramework, Rules.MachOArchitecture));
        addCase("a fat file with an x86_64 slice fails", Flavor.Main,
            app => Put(app, HostFramework,
                Fat((Images.CpuArm64, MachO(Images.CpuArm64, BuildVersion(2))), (0x01000007, MachO(0x01000007, BuildVersion(2))))),
            (HostFramework, Rules.MachOArchitecture));
        addCase("a fat file with an arm64 slice for macOS fails", Flavor.Main,
            app => Put(app, HostFramework, Fat((Images.CpuArm64, MachO(Images.CpuArm64, BuildVersion(1))))),
            (HostFramework, Rules.MachOPlatform));
        addCase("a fat file with one arm64 slice for iOS passes", Flavor.Main,
            app => Put(app, HostFramework, Fat((Images.CpuArm64, MachO(Images.CpuArm64, BuildVersion(2))))));
        addCase("the older iOS minimum-version command passes", Flavor.Main,
            app => Put(app, HostFramework, MachO(Images.CpuArm64, VersionMin(0x25))));
        addCase("the macOS minimum-version command fails", Flavor.Main,
            app => Put(app, HostFramework, MachO(Images.CpuArm64, VersionMin(0x24))),
            (HostFramework, Rules.MachOPlatform));
        addCase("a Mach-O file with no platform command fails", Flavor.Main,
            app => Put(app, HostFramework, MachO(Images.CpuArm64)),
            (HostFramework, Rules.MachOPlatform));
        addCase("a Mach-O file outside the allowed folders fails", Flavor.Main,
            app => Put(app, "Resources/tool", MachO(Images.CpuArm64, BuildVersion(2))),
            ("Resources/tool", Rules.MachOLocation));
        addCase("a Mach-O file with cut-short load commands fails", Flavor.Main,
            app => Put(app, HostFramework, MachO(Images.CpuArm64, BuildVersion(2))[..40]),
            (HostFramework, Rules.MachOUnreadable));

        // What C-0009 section 4 excludes.
        addCase("a Wine Mono installer fails", Flavor.Main,
            app => Put(app, "Resources/wine-mono-9.0.0-x86.msi", [1, 2, 3, 4, 5, 6, 7, 8]),
            ("Resources/wine-mono-9.0.0-x86.msi", Rules.Excluded));
        addCase("a Valve client library fails", Flavor.Main,
            app => Put(app, "wine/aarch64-windows/steamclient64.dll", Pe(0xAA64)),
            ("wine/aarch64-windows/steamclient64.dll", Rules.Excluded));
        addCase("a Microsoft runtime fails, and Wine's own build of the name passes", Flavor.Main,
            app => Put(app, "wine/arm64ec-windows/vcruntime140.dll", Pe(0x8664)),
            ("wine/arm64ec-windows/vcruntime140.dll", Rules.Excluded));
        addCase("a folder of Wine Gecko fails", Flavor.Main,
            app =>
            {
                Put(app, "Resources/share/wine/gecko/a.cab", [1, 2, 3, 4, 5, 6, 7, 8]);
                Put(app, "Resources/share/wine/gecko/b.cab", [1, 2, 3, 4, 5, 6, 7, 8]);
            },
            ("Resources/share/wine/gecko/a.cab", Rules.Excluded),
            ("Resources/share/wine/gecko/b.cab", Rules.Excluded));

        // The test programs.
        addCase("a test programs folder in the main app fails", Flavor.Main,
            app => PutTestPrograms(app, ("console.exe", Pe(0x8664))),
            ("test-programs", Rules.TestProgramInMain),
            ("test-programs/console.exe", Rules.TestProgramInMain));
        addCase("a loose test program in the main app fails", Flavor.Main,
            app => Put(app, "seh.exe", Pe(0x8664)),
            ("seh.exe", Rules.TestProgramInMain));
        addCase("a device test flavor without its test programs fails", Flavor.Test,
            app => Directory.Delete(P(app, "test-programs"), true),
            ("test-programs", Rules.MissingFolder));
        addCase("a missing list of hashes fails", Flavor.Test,
            app => File.Delete(P(app, "test-programs/SHA256SUMS")),
            ("test-programs/SHA256SUMS", Rules.TestProgramList));
        addCase("a changed test program fails", Flavor.Test,
            app => File.AppendAllText(P(app, "test-programs/console.exe"), "x"),
            ("test-programs/console.exe", Rules.TestProgramList));
        addCase("a test program that the list lacks fails", Flavor.Test,
            app => Put(app, "test-programs/extra.exe", Pe(0x8664)),
            ("test-programs/extra.exe", Rules.TestProgramList));
        addCase("a listed test program that is missing fails", Flavor.Test,
            app => File.Delete(P(app, "test-programs/console.exe")),
            ("test-programs/console.exe", Rules.TestProgramList));
        addCase("a malformed line in the list fails", Flavor.Test,
            app => File.AppendAllText(P(app, "test-programs/SHA256SUMS"), "not a hash\n"),
            ("test-programs/SHA256SUMS", Rules.TestProgramList));
        addCase("a file other than a program in test-programs fails", Flavor.Test,
            app => Put(app, "test-programs/console.map", [1, 2, 3, 4, 5, 6, 7, 8]),
            ("test-programs/console.map", Rules.TestProgramContent));
        addCase("a test program of another machine fails", Flavor.Test,
            app => PutTestPrograms(app, ("console.exe", Pe(0x014C)), ("checksum.exe", Pe(0x8664))),
            ("test-programs/console.exe", Rules.PeMachine));

        // The template prefix.
        addCase("a missing template prefix fails", Flavor.Main,
            app => Directory.Delete(P(app, "wine/prefix-template"), true),
            ("wine/prefix-template", Rules.MissingFolder));
        addCase("a planted absolute path in the template fails, and the finding holds no value", Flavor.Main,
            app => Text(app, "wine/prefix-template/user.reg", $"\"Path\"=\"/Users/{Secret}/Library\"\n"),
            ("wine/prefix-template/user.reg", Rules.TemplateHostPath));
        addCase("a host path in a UTF-16 file fails", Flavor.Main,
            app => Put(app, "wine/prefix-template/drive_c/settings.ini", Encoding.Unicode.GetBytes($"x=/home/{Secret}/a\n")),
            ("wine/prefix-template/drive_c/settings.ini", Rules.TemplateHostPath));
        addCase("a host name in the template fails", Flavor.Main,
            app => Text(app, "wine/prefix-template/system.reg", $"\"Name\"=\"{Secret}-mac.local\"\n"),
            ("wine/prefix-template/system.reg", Rules.TemplateHostName));
        addCase("a forbidden string in the template fails", Flavor.Main,
            app => Text(app, "wine/prefix-template/system.reg", "\"Name\"=\"zorblax-host\"\n"),
            ("wine/prefix-template/system.reg", Rules.TemplateHostName));
        addCase("another computer name in the registry fails", Flavor.Main,
            app => Text(app, "wine/prefix-template/system.reg", $"\"ComputerName\"=\"{Secret}\"\n"),
            ("wine/prefix-template/system.reg", Rules.TemplateHostName));
        addCase("another user name in the registry fails", Flavor.Main,
            app => Text(app, "wine/prefix-template/user.reg", $"\"Profile\"=\"C:{Doubled}users{Doubled}{Secret}{Doubled}Desktop\"\n"),
            ("wine/prefix-template/user.reg", Rules.TemplateUser));
        addCase("another user folder in the template fails", Flavor.Main,
            app => Put(app, $"wine/prefix-template/drive_c/users/{Secret}/Desktop/readme.txt", [1, 2, 3, 4, 5, 6, 7, 8]),
            ($"wine/prefix-template/drive_c/users/{Secret}", Rules.TemplateUser));
        addCase("a PE module inside the template is not scanned as text", Flavor.Main,
            app => Put(app, "wine/prefix-template/drive_c/windows/system32/stub.dll",
                [.. Pe(0xAA64, wine: true), .. "/Users/x/"u8.ToArray()]));

        // Links need a file system that makes them, and the colon of z: a name that it allows.
        LinkCase(root, strict, "a z: drive that maps the host's root fails", "wine/prefix-template/dosdevices/z:", "/");
        LinkCase(root, strict, "an absolute link in the template fails", "wine/prefix-template/drive_c/hostlink", "/tmp");
        LinkCase(root, strict, "a link out of the template fails", "wine/prefix-template/drive_c/outside", "../../../..");

        Today(root);
        Reports(root, strict);
        Layouts();
    }

    private static void LinkCase(string root, Layout layout, string name, string link, string target)
    {
        var app = Path.Combine(root, $"link{Guid.NewGuid():N}", "Paneport.app");
        Directory.CreateDirectory(app);
        Write(app, Flavor.Main, false);
        try
        {
            var path = P(app, link);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException
            or PlatformNotSupportedException or NotSupportedException)
        {
            s_skipped++;
            Console.WriteLine($"skip {name}: this file system cannot make the link");
            return;
        }

        Expect(name, app, Flavor.Main, layout, [(link, Rules.TemplateLink)]);
    }

    // The bundle of today: only the host framework, checked against the layout file of the repository.
    private static void Today(string root)
    {
        const string LayoutPath = Cli.DefaultLayout;
        if (!File.Exists(LayoutPath))
        {
            s_skipped++;
            Console.WriteLine($"skip the repository's layout: {LayoutPath} is not here");
            return;
        }

        var layout = Layout.Parse(File.ReadAllText(LayoutPath), LayoutPath);
        foreach (var flavor in new[] { Flavor.Main, Flavor.Test })
        {
            var app = Path.Combine(root, $"today{flavor}", "Paneport.app");
            Directory.CreateDirectory(app);
            Write(app, flavor, true);
            Expect($"today's {Cli.FlavorName(flavor)} app passes the layout file of the repository", app, flavor, layout, []);
        }

        var missing = Path.Combine(root, "todayMissing", "Paneport.app");
        Directory.CreateDirectory(missing);
        Write(missing, Flavor.Main, true);
        Directory.Delete(P(missing, "Frameworks/paneport_host.framework"), true);
        Expect("the layout file of the repository requires the host framework", missing, Flavor.Main, layout,
            [("Frameworks/paneport_host.framework", Rules.MissingFramework)]);
    }

    private static void Reports(string root, Layout layout)
    {
        var app = Path.Combine(root, "report", "Paneport.app");
        Directory.CreateDirectory(app);
        Write(app, Flavor.Test, false);
        var table = Checker.Check(app, Flavor.Test, layout).Markdown("Paneport.app", Flavor.Test);
        string[] rows =
        [
            "| Frameworks/paneport_host.framework | 1 |", "| wine/aarch64-windows | 2 |", "| wine/prefix-template | 5 |",
            "| test-programs | 3 |", "| main executable | 1 |", "| .NET assemblies | 1 |", "| licenses | 1 |", "| **Total** |",
        ];
        Check("the size report has a row for each part of the bundle",
            rows.All(row => table.Contains(row, StringComparison.Ordinal)), table);
    }

    private static void Layouts()
    {
        Check("a layout that is not of version 1 is refused", Throws(() => Layout.Parse("{\"version\":2}", "x.json")));
        Check("a layout without a key is refused", Throws(() => Layout.Parse("{\"version\":1}", "x.json")));
        Check("a machine that is not a hexadecimal number is refused",
            Throws(() => Layout.Parse(StrictLayout.Replace("0x014C", "i386", StringComparison.Ordinal), "x.json")));
        Check("a host name pattern that is not a regular expression is refused",
            Throws(() => Layout.Parse(StrictLayout.Replace("[.]local", "(", StringComparison.Ordinal), "x.json")));
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static void Expect(string name, string app, Flavor flavor, Layout layout, (string File, string Rule)[] expected)
    {
        var findings = Checker.Check(app, flavor, layout).Findings;
        var actual = findings.Select(finding => (finding.File, finding.Rule)).Order().ToList();
        var wanted = expected.Order().ToList();
        var leaked = findings.Any(finding => finding.Text.Contains(Secret, StringComparison.OrdinalIgnoreCase)
            || finding.Rule.Contains(Secret, StringComparison.OrdinalIgnoreCase));
        Check(name, actual.SequenceEqual(wanted) && !leaked,
            $"expected [{string.Join("; ", wanted)}], found [{string.Join("; ", actual)}]{(leaked ? ", and a value leaked" : string.Empty)}");
    }

    private static void Check(string name, bool passed, string detail = "")
    {
        if (passed)
        {
            Console.WriteLine($"ok   {name}");
            return;
        }

        s_failures++;
        Console.Error.WriteLine($"FAIL {name}: {detail}");
    }

    private static string P(string app, string relative) => Path.Combine(app, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void Put(string app, string relative, byte[] content)
    {
        var path = P(app, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static void Text(string app, string relative, string content) => Put(app, relative, Encoding.UTF8.GetBytes(content));

    private static void PutTestPrograms(string app, params (string Name, byte[] Content)[] programs)
    {
        var lines = new StringBuilder();
        foreach (var (name, content) in programs)
        {
            Put(app, $"test-programs/{name}", content);
            lines.Append(Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()).Append("  ").Append(name).Append('\n');
        }

        Text(app, "test-programs/SHA256SUMS", lines.ToString());
    }

    // A clean app: what the layout requires of it today, or with the runtime that later stages bring.
    private static void Write(string app, Flavor flavor, bool today)
    {
        Put(app, "Info.plist", "plist"u8.ToArray());
        Put(app, "Paneport.iOS", MachO(Images.CpuArm64, BuildVersion(2)));
        Put(app, "Paneport.iOS.dll", Pe(0x014C, managed: true));
        Put(app, HostFramework, MachO(Images.CpuArm64, BuildVersion(2)));
        Put(app, "licenses/THIRD_PARTY_NOTICES.md", "notices"u8.ToArray());
        if (!today)
        {
            Put(app, "wine/aarch64-windows/ntdll.dll", Pe(0xAA64, wine: true));
            Put(app, "wine/aarch64-windows/vcruntime140.dll", Pe(0xAA64, wine: true));
            Put(app, "wine/arm64ec-windows/ntdll.dll", Pe(0x8664, wine: true));
            Put(app, "wine/i386-windows/ntdll.dll", Pe(0x014C, wine: true));
            const string Template = "wine/prefix-template/";
            Text(app, Template + "system.reg", $"\"ComputerName\"=\"PANEPORT\"\n\"Path\"=\"C:{Doubled}users{Doubled}paneport\"\n");
            Put(app, Template + "user.reg", Encoding.Unicode.GetBytes($"\"Profile\"=\"C:{Doubled}users{Doubled}Public\"\n"));
            Text(app, Template + ".update-timestamp", "disable\n");
            Text(app, Template + "drive_c/users/paneport/Desktop/readme.txt", "hello\n");
            Text(app, Template + "drive_c/users/Public/readme.txt", "hello\n");
        }

        if (flavor == Flavor.Test)
        {
            PutTestPrograms(app, ("console.exe", Pe(0x8664)), ("checksum.exe", Pe(0x8664)));
        }
    }

    private static byte[] BuildVersion(uint platform)
    {
        var command = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(command, 0x32);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(4), 24);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(8), platform);
        return command;
    }

    private static byte[] VersionMin(uint kind)
    {
        var command = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(command, kind);
        BinaryPrimitives.WriteUInt32LittleEndian(command.AsSpan(4), 16);
        return command;
    }

    // A thin 64-bit little-endian Mach-O header with the given load commands.
    private static byte[] MachO(uint cpu, params byte[][] commands)
    {
        var body = commands.SelectMany(command => command).ToArray();
        var file = new byte[Math.Max(64, 32 + body.Length)];
        BinaryPrimitives.WriteUInt32LittleEndian(file, 0xFEEDFACF);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), cpu);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(12), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)commands.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(20), (uint)body.Length);
        body.CopyTo(file, 32);
        return file;
    }

    // A fat file: the slices at multiples of 16 KiB after a big-endian table.
    private static byte[] Fat(params (uint Cpu, byte[] Slice)[] slices)
    {
        const int Align = 0x4000;
        var file = new byte[(slices.Length + 1) * Align];
        BinaryPrimitives.WriteUInt32BigEndian(file, 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(file.AsSpan(4), (uint)slices.Length);
        for (var index = 0; index < slices.Length; index++)
        {
            var entry = file.AsSpan(8 + (index * 20), 20);
            BinaryPrimitives.WriteUInt32BigEndian(entry, slices[index].Cpu);
            BinaryPrimitives.WriteUInt32BigEndian(entry[8..], (uint)((index + 1) * Align));
            BinaryPrimitives.WriteUInt32BigEndian(entry[12..], (uint)slices[index].Slice.Length);
            BinaryPrimitives.WriteUInt32BigEndian(entry[16..], 14);
            slices[index].Slice.CopyTo(file, (index + 1) * Align);
        }

        return file;
    }

    // A PE image with a header and no sections: the machine, a CLR header for a .NET assembly, and the
    // signature that Wine writes into its own modules.
    private static byte[] Pe(ushort machine, bool managed = false, bool wine = false)
    {
        var file = new byte[0x200];
        file[0] = (byte)'M';
        file[1] = (byte)'Z';
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(0x3C), 0x80);
        if (wine)
        {
            Encoding.ASCII.GetBytes("Wine builtin DLL").CopyTo(file, 0x40);
        }

        var pe32 = machine == 0x014C;
        Encoding.ASCII.GetBytes("PE").CopyTo(file, 0x80);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x84), machine);
        BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(0x94), pe32 ? (ushort)224 : (ushort)240);
        var optional = file.AsSpan(0x98);
        BinaryPrimitives.WriteUInt16LittleEndian(optional, pe32 ? (ushort)0x10B : (ushort)0x20B);
        var directories = pe32 ? 92 : 108;
        BinaryPrimitives.WriteUInt32LittleEndian(optional[directories..], 16);
        if (managed)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(optional[(directories + 4 + (14 * 8))..], 0x2000);
        }

        return file;
    }
}
