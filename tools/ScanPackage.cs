#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Scans app packages for what C-0009 section 6 lets no release hold, and fails if it finds any
// (S1.3.9, S9.1.3). The ios job runs it on both flavors of the app, and the release job on the
// release's .ipa and its notes, each with the allow-list eng/release/scan-allowlist.json.
//
// - What an Apple account leaves: a provisioning profile (provisioning-profile), and a team
//   identifier or an application-identifier prefix in a property list, XML or binary, or in a code
//   signature (team-identifier, app-identifier-prefix). Of each code signature in every 64-bit
//   Mach-O file, thin or in a fat file, it reads the code directories (slots 0 and 0x1000 to
//   0x1004), the entitlements in XML (slot 5) and in DER (slot 7), the launch constraints (slots 8
//   to 11), which are DER too, and the CMS blob (slot 0x10000). It does not read the requirements
//   (slot 2), slots 0x10001 and 0x10002 or any other, nor the code directory's identifier and
//   hashes. A Mach-O file of a kind that it does not parse fails as unreadable. It lists the
//   entitlements of every code signature it finds, and fails a package that holds no app.
// - Personal data: the content rules of tools/Shared/PersonalData.cs, which tools/ScanPersonalData.cs
//   uses too, that C-0009 section 6 names: udid, ecid, serial-number, team-identifier,
//   app-identifier-prefix, email and local-path. It reads every entry: a text file line by line, a
//   property list as its keys, its string values and each key with its value, and any other file,
//   such as a Mach-O file, an assembly or an image, as the strings in it, runs of at least 4
//   printable characters in ASCII or UTF-8 and in UTF-16LE, cut into windows of 64 Ki characters. A
//   device model identifier, such as iPhone18,1 in a compat/ snapshot, matches no rule. A hit shows
//   its line, with the nearest "## " heading above it in a Markdown file, the key above it in a
//   property list, or its byte offset in a binary file. A file whose matching would take more than
//   a few times its size, or longer than a pattern's time limit, fails as unreadable-content.
// - Signatures: every code directory carries the ad-hoc flag and not the linker's
//   (signature-not-ad-hoc, signature-linker-signed). With --entitlements, a Mach-O file without a
//   code signature fails (unsigned), and the app's executable must carry exactly the declared
//   entitlements in its XML and in its DER form, and every other Mach-O file none
//   (entitlements-differ).
// - Debug symbols: a STABS entry in a symbol table, or a __DWARF segment (debug-symbols).
// - The exclusions of C-0009 section 4, by name (excluded-file): Valve's client, Microsoft's
//   runtimes and their installers, unless Wine built the file, Wine Mono, Wine Gecko, any .msi file
//   and Apple's Metal Shader Converter.
// - Build paths: a file that holds a --build-path, in UTF-8 or in UTF-16LE (build-path).
//
//   dotnet run tools/ScanPackage.cs -- [<option>...] <package.ipa>...  scan packages
//   dotnet run tools/ScanPackage.cs -- --self-test                     check every rule on generated packages
//
//   --allow-list <file>            excuse a content hit by its file and the SHA-256 of its text
//   --strict-allow-list            fail on an entry that excused nothing in a file that was scanned
//   --entitlements <plist>         the entitlements that the app's executable must carry
//   --extended-virtual-addressing  declare com.apple.developer.kernel.extended-virtual-addressing too
//   --build-path <text>            a folder of the build, such as $GITHUB_WORKSPACE; repeatable
//   --text <file>                  a text file outside the packages to scan too; repeatable
//
// A finding names the package, the file inside it and the rule, and for a content rule the SHA-256
// of the matched text and where it is, never the value, because CI logs are as public as the
// repository; a name in the file's path that a content rule fires on is left out, with what lies
// below it. Only a content hit can be allowed, in one file and with one hash. Exit codes: 0 clean,
// 1 findings, or a stale entry with --strict-allow-list, 2 usage, an allow-list or entitlements file
// that cannot be read, or a package that cannot be read. docs/design/ios-app-head.md section 11 and
// docs/design/release-engineering.md section 9.4 describe the rules.

#:include Shared/PersonalData.cs
#:include Shared/PropertyList.cs
#:property PublishAot=false

using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return args is ["--self-test"] ? SelfTest.Run() : Cli.Run(args, File.OpenRead, Console.Out, Console.Error);
}
catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

// A finding: the file inside the package and the rule it breaks. A content finding also holds the
// SHA-256 of the matched text, in lowercase hex, where in the file it is, and the path by which an
// entry of the allow-list names the file; some other rules add a detail that holds no value, such
// as the keys of the entitlements that differ.
internal sealed record Finding(string Entry, string Rule)
{
    public string? Sha256 { get; init; }

    public string? Where { get; init; }

    public string? AllowPath { get; init; }

    public string? Detail { get; init; }
}

// What one package holds: its findings, the entitlements of each code signature in it, the number
// of its entries, the size of its largest, and the files whose content it scanned, by the paths
// that the allow-list names them by.
internal sealed record PackageReport(
    IReadOnlyList<Finding> Findings,
    IReadOnlyList<string> Entitlements,
    int Entries,
    long Largest,
    IReadOnlySet<string> Scanned);

internal static class Rules
{
    public const string ProvisioningProfile = "provisioning-profile";
    public const string TeamIdentifier = "team-identifier";
    public const string AppIdentifierPrefix = "app-identifier-prefix";
    public const string UnreadableSignature = "unreadable-signature";
    public const string UnreadablePropertyList = "unreadable-property-list";
    public const string NoApp = "no-app";
    public const string SignatureNotAdHoc = "signature-not-ad-hoc";
    public const string SignatureLinkerSigned = "signature-linker-signed";
    public const string Unsigned = "unsigned";
    public const string EntitlementsDiffer = "entitlements-differ";
    public const string DebugSymbols = "debug-symbols";
    public const string ExcludedFile = "excluded-file";
    public const string BuildPath = "build-path";
    public const string UnreadableContent = "unreadable-content";

    // The content rules of tools/Shared/PersonalData.cs that C-0009 section 6 names. They are the
    // only rules that the allow-list can excuse.
    public static readonly string[] ContentIds =
        ["udid", "ecid", "serial-number", TeamIdentifier, AppIdentifierPrefix, "email", "local-path"];

    public static readonly ContentRule[] Content =
        [.. ContentIds.Select(id => PersonalData.Rules.Single(rule => rule.Id == id))];

    // The Info.plist of an app at the top of its bundle, in the package's Payload folder.
    public static readonly Regex AppInfo = new(@"^Payload/[^/]+\.app/Info\.plist$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    // The folder of an app in the package's Payload folder, which the allow-list leaves out of a path.
    public static readonly Regex AppFolder =
        new(@"^Payload/[^/]+\.app/", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

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

    // Scans the packages in turn without options.
    internal static int ScanPackages(
        IReadOnlyList<string> paths, Func<string, Stream> open, TextWriter output, TextWriter error) =>
        ScanAll(new Settings { Packages = paths }, open, output, error);

    // Scans the packages and then the texts in turn. One that cannot be read is reported and the
    // scan goes on with the next, so that one run shows every package; the result is then 2,
    // otherwise 1 for findings that the allow-list does not excuse, or for a stale entry of it with
    // --strict-allow-list, and 0 for none. An entry is stale when its file was scanned in this run
    // and it excused nothing in any package or text of the run.
    internal static int ScanAll(Settings settings, Func<string, Stream> open, TextWriter output, TextWriter error)
    {
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var total = 0;
        var unreadable = 0;
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        var used = new HashSet<AllowEntry>();
        void print(string line)
        {
            output.WriteLine(line);
            if (annotate)
            {
                output.WriteLine(Output.Annotation(line));
            }
        }

        void summarize(string name, int count, int excused)
        {
            output.WriteLine($"{name}: {count} findings.");
            if (excused != 0)
            {
                output.WriteLine($"{name}: {excused} hits excused by {settings.AllowList.Path}.");
            }
        }

        foreach (var path in settings.Packages)
        {
            var package = Path.GetFileName(path);
            PackageReport report;
            try
            {
                using var stream = open(path);
                report = Scan(stream, settings.Options);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                unreadable++;
                error.WriteLine($"{package}: error: {exception.Message}");
                if (annotate)
                {
                    output.WriteLine(Output.Annotation($"{package}: cannot be read"));
                }

                continue;
            }

            foreach (var entitlement in report.Entitlements)
            {
                output.WriteLine($"{package}: {entitlement}");
            }

            var (kept, excused) = settings.AllowList.Apply(report.Findings, used);
            foreach (var finding in kept)
            {
                print(Output.Line(package, finding));
            }

            scanned.UnionWith(report.Scanned);
            total += kept.Count;
            output.WriteLine($"{package}: {report.Entries} entries, the largest of {report.Largest} bytes.");
            summarize(package, kept.Count, excused);
        }

        foreach (var path in settings.Texts)
        {
            var name = Path.GetFileName(path);
            byte[] bytes;
            try
            {
                using var stream = open(path);
                using var copy = new MemoryStream();
                stream.CopyTo(copy);
                bytes = copy.ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                unreadable++;
                error.WriteLine($"{name}: error: {exception.Message}");
                if (annotate)
                {
                    output.WriteLine(Output.Annotation($"{name}: cannot be read"));
                }

                continue;
            }

            var (kept, excused) = settings.AllowList.Apply(ScanText(name, bytes, settings.Options), used);
            foreach (var finding in kept)
            {
                print(Output.Line(null, finding));
            }

            scanned.Add(name);
            total += kept.Count;
            summarize(name, kept.Count, excused);
        }

        var stale = settings.AllowList.Stale(used, scanned);
        foreach (var entry in stale)
        {
            var line = $"{settings.AllowList.Path}: the entry for {entry.File}, {entry.Rule}, sha256 {entry.Sha256} "
                + "excused nothing in this run.";
            if (settings.Strict)
            {
                print(line);
            }
            else
            {
                output.WriteLine(line);
            }
        }

        return unreadable != 0 ? 2 : total != 0 || (settings.Strict && stale.Count != 0) ? 1 : 0;
    }

    // Scans one package, a zip archive such as an .ipa. A package needs an app: an Info.plist in
    // Payload/<name>.app/ whose CFBundleExecutable names a Mach-O file beside it, so that a
    // packaging mistake cannot pass for a clean package. Every entry but a provisioning profile,
    // which fails by its name alone, goes through the exclusions, the build paths and the content
    // rules, and every Mach-O file through the checks of its signatures and its symbols. The
    // entitlements are compared at the end, once the app's executable is known.
    public static PackageReport Scan(Stream package, ScanOptions? options = null)
    {
        options ??= new ScanOptions();
        using var archive = new ZipArchive(package, ZipArchiveMode.Read);
        var findings = new List<Finding>();
        var entitlements = new List<string>();
        var machOFiles = new Dictionary<string, MachOFile>(StringComparer.Ordinal);
        var executables = new List<string>();
        var scanned = new HashSet<string>(StringComparer.Ordinal);
        var buildPaths = new BuildPaths(options.BuildPaths);
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
            var allowPath = AllowPath(name);
            if (Exclusions.Reason(name, bytes) is { } reason)
            {
                findings.Add(new Finding(name, Rules.ExcludedFile) { Detail = reason });
            }

            findings.AddRange(buildPaths.Find(name, bytes));
            var content = new ContentScan(name, allowPath, bytes.Length);
            IReadOnlyList<(long Start, long End)> signatures = [];
            if (MachO.IsMachO(bytes))
            {
                var file = ScanSignature(name, bytes, findings, entitlements, content);
                machOFiles.Add(name, file);
                signatures = file.Signatures;
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

                    content.Tree(tree, name);
                }
                catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
                {
                    findings.Add(new Finding(name, Rules.UnreadablePropertyList));
                }
            }

            content.Bytes(bytes, signatures, IsMarkdown(name));
            findings.AddRange(content.Findings);
            scanned.Add(allowPath);
        }

        if (!executables.Any(machOFiles.ContainsKey))
        {
            findings.Add(new Finding("Payload/", Rules.NoApp));
        }

        if (options.Entitlements is { } declared)
        {
            findings.AddRange(CheckEntitlements(machOFiles, executables, declared));
        }

        return new PackageReport([.. findings.Distinct()], [.. entitlements.Distinct()], entries, largest, scanned);
    }

    // Scans a text file outside the packages, such as the composed release notes, with the content
    // rules and the build paths. Its findings, and the allow-list, name it by its file name.
    public static List<Finding> ScanText(string name, byte[] bytes, ScanOptions options)
    {
        var findings = new BuildPaths(options.BuildPaths).Find(name, bytes).ToList();
        var content = new ContentScan(name, name, bytes.Length);
        content.Bytes(bytes, [], IsMarkdown(name));
        findings.AddRange(content.Findings);
        return findings;
    }

    // The path by which the allow-list names an entry: the path below the app's folder, without
    // Payload/<name>.app/, so that one entry serves both flavors and the release, or the entry's
    // whole name outside an app.
    public static string AllowPath(string entry) =>
        Rules.AppFolder.Match(entry) is { Success: true } app ? entry[app.Length..] : entry;

    private static bool IsMarkdown(string name) => name.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    // The signatures and symbols of a Mach-O file. A file whose signature cannot be read fails as
    // unreadable-signature, and the checks of its entitlements then skip it.
    private static MachOFile ScanSignature(
        string name, byte[] bytes, List<Finding> findings, List<string> entitlements, ContentScan content)
    {
        var signature = new Signature(bytes.Length);
        var file = new MachOFile(signature.Ranges);
        try
        {
            MachO.ReadSignatures(bytes, signature);
        }
        catch (InvalidDataException)
        {
            findings.Add(new Finding(name, Rules.UnreadableSignature));
            file.Unreadable = true;
            return file;
        }

        var signed = $"{name} (code signature)";

        // A code directory names a team, and the CMS blob holds a certificate, only when an Apple
        // identity signed the file; an ad-hoc signature has neither.
        if (signature.NamesTeam)
        {
            findings.Add(new Finding(signed, Rules.TeamIdentifier));
        }

        // codesign sets the ad-hoc flag in every code directory of an ad-hoc signature; the
        // signature that the linker writes for arm64 code carries the linker's flag beside it.
        if (signature.CodeDirectoryFlags.Any(flags => (flags & MachO.AdHocFlag) == 0))
        {
            findings.Add(new Finding(signed, Rules.SignatureNotAdHoc));
        }

        if (signature.CodeDirectoryFlags.Any(flags => (flags & MachO.LinkerSignedFlag) != 0))
        {
            findings.Add(new Finding(signed, Rules.SignatureLinkerSigned));
        }

        // Release device builds are stripped (C-0009 section 6): the linker's debug map leaves STABS
        // entries in the symbol table, and a dSYM file holds its DWARF in a segment of its own.
        var symbols = new List<string>();
        if (signature.Stabs)
        {
            symbols.Add("STABS entries in its symbol table");
        }

        if (signature.Dwarf)
        {
            symbols.Add("a __DWARF segment");
        }

        if (symbols.Count != 0)
        {
            findings.Add(new Finding(name, Rules.DebugSymbols) { Detail = string.Join(" and ", symbols) });
        }

        file.Unsigned = signature.UnsignedSlices != 0;

        // The DER of the entitlements and of the launch constraints is read as the XML form is, so
        // that each of its texts gets the same checks.
        foreach (var der in signature.Der)
        {
            try
            {
                var budget = new Budget();
                var tree = Der.Parse(der, budget);
                Check(signed, tree, findings, budget);
                if (signature.DerEntitlements.Contains(der))
                {
                    // The DER holds a version and then the dictionary.
                    var dictionary = tree is List<object> { Count: 2 } parts ? parts[1] : tree;
                    file.Der.Add(dictionary is Dictionary<string, object> ? dictionary : tree);
                }

                content.Tree(tree, signed);
            }
            catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
            {
                findings.Add(new Finding(name, Rules.UnreadableSignature));
                file.Unreadable = true;
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
                        // Only a Boolean value is printed; any other value could name the account,
                        // and so could a key that a content rule fires on.
                        var shown = value is bool flag ? (flag ? "true" : "false") : "(not shown)";
                        entitlements.Add($"{Output.Shown(name)}: entitlement {Entitlements.Key(key)} = {shown}");
                    }
                }

                Check(signed, tree, findings, budget);
                file.Xml.Add(tree);
                content.Tree(tree, signed);
            }
            catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
            {
                findings.Add(new Finding(name, Rules.UnreadableSignature));
                file.Unreadable = true;
            }
        }

        return file;
    }

    // With --entitlements: a Mach-O file with a slice that has no code directory fails as
    // unsigned, the app's executable must carry exactly the declared entitlements, in its XML and
    // in its DER form, and every other Mach-O file none. A file whose signature cannot be read has
    // failed already, and is skipped.
    private static IEnumerable<Finding> CheckEntitlements(
        Dictionary<string, MachOFile> files, List<string> executables, Dictionary<string, object> declared)
    {
        var readable = files.Where(pair => !pair.Value.Unreadable).OrderBy(pair => pair.Key, StringComparer.Ordinal);
        foreach (var (name, file) in readable)
        {
            if (file.Unsigned)
            {
                yield return new Finding(name, Rules.Unsigned);
                continue;
            }

            var expected = executables.Contains(name) ? declared : Entitlements.None;
            if (Entitlements.Differences(expected, file.Xml, file.Der) is { } detail)
            {
                yield return new Finding($"{name} (code signature)", Rules.EntitlementsDiffer) { Detail = detail };
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

// What one run scans, and with which options and allow-list.
internal sealed class Settings
{
    public IReadOnlyList<string> Packages { get; init; } = [];

    public IReadOnlyList<string> Texts { get; init; } = [];

    public AllowList AllowList { get; init; } = AllowList.Empty;

    public bool Strict { get; init; }

    public ScanOptions Options { get; init; } = new();
}

// The checks that options add to a scan: the entitlements that the app's executable must carry,
// which also make a Mach-O file without a code signature fail, and the build paths that no file
// may hold.
internal sealed class ScanOptions
{
    public Dictionary<string, object>? Entitlements { get; init; }

    public IReadOnlyList<string> BuildPaths { get; init; } = [];
}

// What the scan keeps of a Mach-O file for the checks that need the whole package: the byte ranges
// of its code signatures, whether a part of them could not be read, whether a slice has no code
// directory, and its entitlements in XML and in DER.
internal sealed class MachOFile(IReadOnlyList<(long Start, long End)> signatures)
{
    public IReadOnlyList<(long Start, long End)> Signatures { get; } = signatures;

    public bool Unreadable { get; set; }

    public bool Unsigned { get; set; }

    public List<object> Xml { get; } = [];

    public List<object> Der { get; } = [];
}

// The content rules over one file: its text line by line, its property list trees, or the strings
// in it, each hit kept once per file, rule and hash, where it was first found. A line goes to the
// rules in windows of at most 64 Ki characters that overlap by 256, so that no pattern meets an
// unbounded text, and the characters that one file may send to them are bounded by its size, so
// that no file, however its lines or keys are built, takes more matching than a few times its
// length. A file that would take more, or whose matching takes longer than a pattern's time limit,
// fails as unreadable-content, beside the hits found until then.
internal sealed class ContentScan(string entry, string allowPath, long length)
{
    public const int Window = 64 * 1024;
    public const int Overlap = 256;

    // The line of a key and its value keeps the end of the key, which holds a label, and the start
    // of the value, which holds what a label names.
    private const int PairPart = 256;

    private readonly Dictionary<(string Entry, string Rule, string Sha256), Finding> _hits = [];
    private long _left = Math.Max(1L << 20, 8 * length);
    private bool _unreadable;

    public IEnumerable<Finding> Findings =>
        _unreadable ? [.. _hits.Values, new Finding(entry, Rules.UnreadableContent)] : _hits.Values;

    // A file's text, or the strings in it. A Mach-O file, a binary property list and a PE file are
    // binary, whatever their first bytes look like; any other file is text when it decodes as the
    // repository scan decodes text. A string inside a code signature is reported as the signature's.
    public void Bytes(byte[] bytes, IReadOnlyList<(long Start, long End)> signatures, bool markdown) =>
        Guard(() =>
        {
            var binary = MachO.IsMachO(bytes)
                || bytes.AsSpan().StartsWith("bplist"u8)
                || bytes.AsSpan().StartsWith("MZ"u8);
            if (!binary && PersonalData.Decode(bytes) is { } text)
            {
                Text(text, markdown);
                return;
            }

            var signed = $"{entry} (code signature)";
            foreach (var (offset, run) in Strings.Of(bytes))
            {
                if (Match(run) is { } hits)
                {
                    var inSignature = signatures.Any(range => offset >= range.Start && offset < range.End);
                    Record(inSignature ? signed : entry, hits, $"byte {offset}");
                }
            }
        });

    // A property list, or the DER of a signature: each key and each text value as a line, and each
    // key with its text value, or with each text of its array, as one more line, so that a labeled
    // rule sees both. A text or container that the tree shares is read once, and the walk spends a
    // budget of its own, as the reader does. A hit names the key above it, unless a rule fires on
    // that key too.
    public void Tree(object tree, string shown) =>
        Guard(() =>
        {
            var budget = new Budget();
            var walked = new HashSet<object>(ReferenceEqualityComparer.Instance);
            var fired = new Dictionary<object, bool>(ReferenceEqualityComparer.Instance);
            bool scan(string text, string where)
            {
                if (!fired.TryGetValue(text, out var hit))
                {
                    var hits = Match(text);
                    Record(shown, hits, where);
                    fired[text] = hit = hits is not null;
                }

                return hit;
            }

            void walk(object node, string? under)
            {
                budget.Spend();
                switch (node)
                {
                    case string text:
                        scan(text, under ?? "in a value");
                        break;
                    case List<object> list when walked.Add(list):
                        foreach (var item in list)
                        {
                            walk(item, under);
                        }

                        break;
                    case Dictionary<string, object> dictionary when walked.Add(dictionary):
                        foreach (var (key, value) in dictionary)
                        {
                            budget.Spend();
                            var label = scan(key, under is null ? "in a key" : $"in a key {under}")
                                ? "under a key that is not shown"
                                : $"under the key {Clip(key)}";
                            foreach (var text in Texts(value))
                            {
                                budget.Spend();
                                Record(shown, Match(Pair(key, text)), label);
                            }

                            walk(value, label);
                        }

                        break;
                }
            }

            walk(tree, null);
        });

    private static IEnumerable<string> Texts(object value) => value switch
    {
        string text => [text],
        List<object> list => list.OfType<string>(),
        _ => [],
    };

    private static string Pair(string key, string value) =>
        $"{(key.Length > PairPart ? key[^PairPart..] : key)}: {(value.Length > PairPart ? value[..PairPart] : value)}";

    // A key or a heading as a finding shows it: at most 80 characters, and a control character as ?.
    private static string Clip(string text)
    {
        var shown = text.Length > 80 ? $"{text[..80]}..." : text;
        return new([.. shown.Select(letter => char.IsControl(letter) ? '?' : letter)]);
    }

    // The lines of a text, as tools/ScanPersonalData.cs reads them, and in a Markdown file the
    // nearest "## " heading above each, unless a rule fires on that heading.
    private void Text(string text, bool markdown)
    {
        string? heading = null;
        string? below = null;
        var current = 0;
        foreach (var (number, line) in PersonalData.Lines(text))
        {
            if (number != current)
            {
                current = number;
                heading = below ?? heading;
                below = markdown && line.StartsWith("## ", StringComparison.Ordinal) ? line.TrimEnd() : null;
            }

            if (Match(line) is { } hits)
            {
                var where = heading is null
                    ? $"line {number}"
                    : Rules.Content.Any(rule => rule.FiresOn(heading, entry))
                        ? $"line {number}, below a heading that is not shown"
                        : $"line {number}, below \"{Clip(heading)}\"";
                Record(entry, hits, where);
            }
        }
    }

    // The hits of the rules in one line, window by window, or null for none.
    private List<(string Rule, string Value)>? Match(string line)
    {
        List<(string Rule, string Value)>? hits = null;
        for (var start = 0; ; start += Window - Overlap)
        {
            var size = Math.Min(Window, line.Length - start);
            _left -= size;
            if (_left < 0)
            {
                throw new InvalidDataException("the file needs more matching than its size allows");
            }

            var window = size == line.Length ? line : line.Substring(start, size);
            foreach (var rule in Rules.Content)
            {
                foreach (var value in rule.MatchesOn(window, entry))
                {
                    (hits ??= []).Add((rule.Id, value));
                }
            }

            if (start + size >= line.Length)
            {
                return hits;
            }
        }
    }

    private void Record(string shown, List<(string Rule, string Value)>? hits, string where)
    {
        foreach (var (rule, value) in hits ?? [])
        {
            var sha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            var hit = new Finding(shown, rule) { Sha256 = sha256, Where = where, AllowPath = allowPath };
            _hits.TryAdd((shown, rule, sha256), hit);
        }
    }

    private void Guard(Action scan)
    {
        try
        {
            scan();
        }
        catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
        {
            _unreadable = true;
        }
    }
}

// The strings in a binary file, as the strings tool finds them: runs of at least 4 printable
// characters in ASCII or UTF-8, and in UTF-16LE, each with its byte offset. In UTF-16LE only the
// code units of Latin-1 count, as the strings tool reads 16-bit characters, so that the two bytes
// between two literals of a managed assembly end a run, and code read as UTF-16 starts none. A run
// longer than a window comes in windows that overlap, each with its own offset.
internal static class Strings
{
    public const int MinimumRun = 4;

    // A window of UTF-8 ends at the start of a character, and the next one starts up to 4 bytes for
    // each character of the overlap before that end.
    private const int Utf8Overlap = ContentScan.Overlap * 4;

    public static IEnumerable<(long Offset, string Text)> Of(byte[] bytes) =>
        Utf8(bytes).Concat(Utf16(bytes, 0)).Concat(Utf16(bytes, 1));

    private static IEnumerable<(long Offset, string Text)> Utf8(byte[] bytes)
    {
        var start = 0;
        var characters = 0;
        for (var index = 0; index <= bytes.Length;)
        {
            var size = index < bytes.Length ? Printable(bytes, index) : 0;
            if (size != 0)
            {
                start = characters == 0 ? index : start;
                characters++;
                index += size;
                continue;
            }

            if (characters >= MinimumRun)
            {
                foreach (var window in Utf8Windows(bytes, start, index))
                {
                    yield return window;
                }
            }

            characters = 0;
            index++;
        }
    }

    private static IEnumerable<(long Offset, string Text)> Utf8Windows(byte[] bytes, int start, int end)
    {
        for (var from = start; ;)
        {
            var to = Math.Min(end, from + ContentScan.Window);
            while (to < end && IsContinuation(bytes[to]))
            {
                to--;
            }

            yield return (from, Encoding.UTF8.GetString(bytes, from, to - from));
            if (to >= end)
            {
                yield break;
            }

            from = to - Utf8Overlap;
            while (IsContinuation(bytes[from]))
            {
                from--;
            }
        }
    }

    private static IEnumerable<(long Offset, string Text)> Utf16(byte[] bytes, int alignment)
    {
        var start = alignment;
        var characters = 0;
        for (var index = alignment; ; index += 2)
        {
            if (index + 1 < bytes.Length
                && bytes[index + 1] == 0
                && bytes[index] is 0x09 or (>= 0x20 and < 0x7F) or >= 0xA0)
            {
                start = characters == 0 ? index : start;
                characters++;
                continue;
            }

            for (var first = 0; characters >= MinimumRun; first += ContentScan.Window - ContentScan.Overlap)
            {
                var count = Math.Min(ContentScan.Window, characters - first);
                yield return (start + (2L * first), Encoding.Unicode.GetString(bytes, start + (2 * first), 2 * count));
                if (first + count >= characters)
                {
                    break;
                }
            }

            characters = 0;
            if (index + 1 >= bytes.Length)
            {
                yield break;
            }
        }
    }

    // The length in bytes of the printable character at the index, or 0: a tab, printable ASCII, or a
    // valid UTF-8 sequence of a character that is neither a control or format character, nor a
    // separator of lines or paragraphs, nor a private or unassigned one.
    private static int Printable(byte[] bytes, int index)
    {
        if (bytes[index] is 0x09 or (>= 0x20 and < 0x7F))
        {
            return 1;
        }

        var sequence = bytes.AsSpan(index, Math.Min(4, bytes.Length - index));
        return bytes[index] is >= 0xC2 and <= 0xF4
            && Rune.DecodeFromUtf8(sequence, out var rune, out var size) == OperationStatus.Done
            && Rune.GetUnicodeCategory(rune) is not (UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned)
                ? size
                : 0;
    }

    private static bool IsContinuation(byte value) => (value & 0xC0) == 0x80;
}

// The files that C-0009 section 4 keeps out of the bundle and the release (C-0003 sections 5 and 8),
// by name and without regard to case: Valve's client, Microsoft's runtimes and their installers,
// Wine Mono and Wine Gecko, any .msi file, and Apple's Metal Shader Converter, which is Under
// review. A PE file named like one of Microsoft's runtimes passes when it carries Wine's builtin
// marker, the text "Wine builtin DLL" right after the 64-byte DOS header, which winebuild --builtin
// writes into Wine's own modules of those names.
internal static class Exclusions
{
    private static readonly string[] s_valve =
    [
        "steamclient", "steam_api", "libsteam_api", "tier0_s", "vstdlib_s", "gameoverlayrenderer", "gameoverlayui",
        "steamservice", "steamerrorreporter", "steamwebhelper", "crashhandler",
    ];

    private static readonly string[] s_valveNames = ["steam.exe", "steamui.dll"];

    private static readonly string[] s_installers = ["vc_redist", "vcredist", "dxsetup", "dxwebsetup"];

    // A * stands for any text.
    private static readonly string[] s_runtimes =
    [
        "vcruntime*.dll", "msvcp*.dll", "msvcr*.dll", "ucrtbase*.dll", "concrt*.dll", "vccorlib*.dll", "mfc*.dll",
        "vcomp*.dll", "api-ms-win-*.dll", "d3dx9_*.dll", "d3dx10*.dll", "d3dx11*.dll", "d3dcompiler_*.dll",
        "d3dcsx*.dll", "xinput*.dll", "xaudio2_*.dll", "x3daudio*.dll", "xapofx*.dll", "dxcompiler.dll", "dxil.dll",
    ];

    private static readonly string[] s_wine = ["wine-mono", "wine_mono", "wine-gecko", "wine_gecko"];

    private static readonly string[] s_metal = ["libmetalirconverter", "metalirconverter"];

    // Why an entry is excluded, or null when it is not.
    public static string? Reason(string entry, byte[] bytes)
    {
        var parts = entry.Split('/');
        var name = parts[^1];
        if (StartsWithAny(name, s_valve) || s_valveNames.Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return "a part of Valve's client";
        }

        if (StartsWithAny(name, s_installers))
        {
            return "an installer of Microsoft's runtimes";
        }

        if (s_runtimes.Any(pattern => IsLike(name, pattern))
            && bytes.AsSpan().StartsWith("MZ"u8)
            && !IsWineBuiltin(bytes))
        {
            return "one of Microsoft's runtimes, without Wine's builtin marker";
        }

        if (parts.Any(part => StartsWithAny(part, s_wine)))
        {
            return "Wine Mono or Wine Gecko";
        }

        if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
        {
            return "an installer package";
        }

        return StartsWithAny(name, s_metal)
            ? "Apple's Metal Shader Converter, which C-0003 section 5 keeps Under review"
            : null;
    }

    private static bool StartsWithAny(string name, string[] prefixes) =>
        prefixes.Any(prefix => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    private static bool IsLike(string name, string pattern)
    {
        var star = pattern.IndexOf('*', StringComparison.Ordinal);
        return star < 0
            ? name.Equals(pattern, StringComparison.OrdinalIgnoreCase)
            : name.Length >= pattern.Length - 1
                && name.StartsWith(pattern[..star], StringComparison.OrdinalIgnoreCase)
                && name.EndsWith(pattern[(star + 1)..], StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWineBuiltin(byte[] bytes) =>
        bytes.Length >= 0x50 && bytes.AsSpan(0x40, 16).SequenceEqual("Wine builtin DLL"u8);
}

// The build paths that no file may hold, in UTF-8 or in UTF-16LE, such as the job's checkout
// folder: a path of our own build is fixed in the build, so no allow-list entry can excuse it. The
// finding shows the path, which is the job's own folder, and where it first is, but nothing else.
internal sealed class BuildPaths(IReadOnlyList<string> paths)
{
    private readonly (string Path, byte[] Utf8, byte[] Utf16)[] _paths =
        [.. paths.Select(path => (path, Encoding.UTF8.GetBytes(path), Encoding.Unicode.GetBytes(path)))];

    public IEnumerable<Finding> Find(string entry, byte[] bytes)
    {
        foreach (var (path, utf8, utf16) in _paths)
        {
            var at = IndexOf(bytes, utf8);
            var encoding = "UTF-8";
            if (at < 0)
            {
                at = IndexOf(bytes, utf16);
                encoding = "UTF-16LE";
            }

            if (at >= 0)
            {
                yield return new Finding(entry, Rules.BuildPath)
                {
                    Detail = $"holds the build path {path} in {encoding}, at byte {at}",
                };
            }
        }
    }

    private static int IndexOf(byte[] bytes, byte[] text) => bytes.AsSpan().IndexOf(text);
}

// Compares the entitlements of a Mach-O file with the declared ones: the same keys with the same
// values. A difference names keys and Boolean values only, as the entitlements that the scan lists
// do: a key names an entitlement, but any other value could name an account. A key that a content
// rule fires on is not shown either.
internal static class Entitlements
{
    public const string ExtendedVirtualAddressing = "com.apple.developer.kernel.extended-virtual-addressing";

    public static Dictionary<string, object> None { get; } = new(StringComparer.Ordinal);

    // What differs between the declared entitlements and each XML and each DER form of them that a
    // file carries, or null when every form holds exactly the declared ones. A file that must carry
    // entitlements needs both forms; one that must carry none may lack either.
    public static string? Differences(
        Dictionary<string, object> declared, IReadOnlyList<object> xml, IReadOnlyList<object> der)
    {
        var parts = new List<string>();
        Compare("XML", declared, xml, parts);
        Compare("DER", declared, der, parts);
        return parts.Count == 0 ? null : string.Join("; ", parts.Distinct());
    }

    private static void Compare(
        string form, Dictionary<string, object> declared, IReadOnlyList<object> trees, List<string> parts)
    {
        IReadOnlyList<object> carried = trees.Count == 0 ? [None] : trees;
        foreach (var tree in carried)
        {
            if (tree is not Dictionary<string, object> actual)
            {
                parts.Add($"the {form} entitlements are not a dictionary");
                continue;
            }

            var extra = actual.Keys.Where(key => !declared.ContainsKey(key)).ToList();
            if (extra.Count != 0)
            {
                parts.Add($"the {form} entitlements hold {Keys(extra)} too");
            }

            var missing = declared.Keys.Where(key => !actual.ContainsKey(key)).ToList();
            if (missing.Count != 0)
            {
                parts.Add($"the {form} entitlements lack {Keys(missing)}");
            }

            foreach (var key in declared.Keys.Where(actual.ContainsKey).Order(StringComparer.Ordinal))
            {
                if (!Same(actual[key], declared[key]))
                {
                    parts.Add(actual[key] is bool value && declared[key] is bool wanted
                        ? $"the {form} entitlements set {Key(key)} to {Text(value)}, not {Text(wanted)}"
                        : $"the {form} entitlements give {Key(key)} another value than the declared one");
                }
            }
        }
    }

    private static bool Same(object actual, object declared) => (actual, declared) switch
    {
        (bool left, bool right) => left == right,
        (string left, string right) => left == right,
        (List<object> left, List<object> right) =>
            left.Count == right.Count && left.Zip(right).All(pair => Same(pair.First, pair.Second)),
        (Dictionary<string, object> left, Dictionary<string, object> right) =>
            left.Count == right.Count
                && left.All(pair => right.TryGetValue(pair.Key, out var value) && Same(pair.Value, value)),
        _ => false,
    };

    private static string Keys(IEnumerable<string> keys) =>
        string.Join(", ", keys.Order(StringComparer.Ordinal).Select(Key));

    public static string Key(string key) =>
        Rules.Content.Any(rule => rule.FiresOn(key, string.Empty)) ? "a key that is not shown" : key;

    private static string Text(bool value) => value ? "true" : "false";
}

internal sealed record AllowEntry(string File, string Rule, string Sha256, string Reason);

// eng/release/scan-allowlist.json: each entry excuses one content hit, by the file that holds it,
// its rule and the SHA-256 of the matched text, and says why. Nothing is allowed by pattern alone,
// and nothing of a structural rule. The list is refused for an unknown key, a missing or empty
// field, a hash that is not 64 lowercase hex digits, a rule that is not a content rule, and an entry
// given twice.
internal sealed class AllowList
{
    public static readonly AllowList Empty = new([], null);

    private static readonly string[] s_top = ["format", "entries"];
    private static readonly string[] s_fields = ["file", "rule", "sha256", "reason"];

    private readonly Dictionary<(string File, string Rule, string Sha256), AllowEntry> _entries;

    private AllowList(IEnumerable<AllowEntry> entries, string? path)
    {
        _entries = entries.ToDictionary(entry => (entry.File, entry.Rule, entry.Sha256));
        Path = path;
    }

    public string? Path { get; }

    public static AllowList Parse(string json, string path)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw Refused(path, "the list must be an object with \"format\" and \"entries\"");
            }

            var top = Properties(document.RootElement, path, "the list", s_top);
            if (top["format"] is not { ValueKind: JsonValueKind.Number } format
                || !format.TryGetInt32(out var version)
                || version != 1)
            {
                throw Refused(path, "\"format\" must be 1");
            }

            if (top["entries"].ValueKind != JsonValueKind.Array)
            {
                throw Refused(path, "\"entries\" must be an array");
            }

            var entries = new List<AllowEntry>();
            var number = 0;
            foreach (var element in top["entries"].EnumerateArray())
            {
                var what = $"entry {++number}";
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw Refused(path, $"{what} is not an object");
                }

                var fields = Properties(element, path, what, s_fields);
                string text(string field) =>
                    fields[field] is { ValueKind: JsonValueKind.String } value
                        && value.GetString() is { } content
                        && content.Trim().Length != 0
                        ? content
                        : throw Refused(path, $"{what} needs a non-empty text as \"{field}\"");
                var entry = new AllowEntry(text("file"), text("rule"), text("sha256"), text("reason"));
                if (!Rules.ContentIds.Contains(entry.Rule))
                {
                    throw Refused(path, $"{what} names \"{entry.Rule}\", which is not a content rule");
                }

                if (entry.Sha256.Length != 64
                    || !entry.Sha256.All(digit => digit is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
                {
                    throw Refused(path, $"{what} needs 64 lowercase hex digits as \"sha256\"");
                }

                if (entries.Any(other => other with { Reason = entry.Reason } == entry))
                {
                    throw Refused(path, $"{what} is given twice");
                }

                entries.Add(entry);
            }

            return new AllowList(entries, path);
        }
    }

    // Splits the findings into those that no entry excuses and the number that one excuses, and
    // adds each entry that excused one to the used ones. Only a content hit, which has a hash, can
    // be excused, and only in its own file.
    public (List<Finding> Kept, int Excused) Apply(IEnumerable<Finding> findings, HashSet<AllowEntry> used)
    {
        var kept = new List<Finding>();
        var excused = 0;
        foreach (var finding in findings)
        {
            if (finding is { Sha256: { } sha256, AllowPath: { } file }
                && _entries.TryGetValue((file, finding.Rule, sha256), out var entry))
            {
                used.Add(entry);
                excused++;
            }
            else
            {
                kept.Add(finding);
            }
        }

        return (kept, excused);
    }

    // The entries that excused nothing although their file was scanned. An entry whose file was not
    // scanned at all, such as one for the notes in the ios job, is not judged.
    public List<AllowEntry> Stale(HashSet<AllowEntry> used, IReadOnlySet<string> scanned) =>
        [.. _entries.Values.Where(entry => scanned.Contains(entry.File) && !used.Contains(entry))];

    // The properties of an object, which must be exactly the given ones, each given once.
    private static Dictionary<string, JsonElement> Properties(
        JsonElement element, string path, string what, string[] names)
    {
        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Contains(property.Name, StringComparer.Ordinal))
            {
                throw Refused(path, $"{what} has the unknown key \"{property.Name}\"");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw Refused(path, $"{what} gives \"{property.Name}\" twice");
            }
        }

        return names.FirstOrDefault(name => !properties.ContainsKey(name)) is { } missing
            ? throw Refused(path, $"{what} has no \"{missing}\"")
            : properties;
    }

    private static InvalidDataException Refused(string path, string reason) => new($"{path}: {reason}.");
}

// How a finding is printed: never with its value, and without any name in its path that a content
// rule fires on, and what lies below that name, as tools/ScanPersonalData.cs leaves such names out.
internal static class Output
{
    public const string NameNotShown = "<name not shown>";

    public static string Line(string? package, Finding finding)
    {
        var tail = finding switch
        {
            { Sha256: { } sha256 } => $": sha256 {sha256}, {finding.Where}",
            { Detail: { } detail } => $": {detail}",
            _ => string.Empty,
        };
        var line = $"{Shown(finding.Entry)}: {finding.Rule}{tail}";
        return package is null ? line : $"{package}: {line}";
    }

    // A GitHub Actions annotation, its data escaped as GitHub asks: %, CR and LF as %25, %0D and %0A.
    public static string Annotation(string line) =>
        "::error title=Package scan::" + line.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    public static string Shown(string entry)
    {
        var parts = entry.Split('/');
        for (var index = 0; index < parts.Length; index++)
        {
            if (Rules.Content.Any(rule => rule.FiresOn(parts[index], entry)))
            {
                return string.Concat(parts[..index].Select(part => $"{part}/")) + NameNotShown;
            }
        }

        return entry;
    }
}

// The command line: the options, the allow-list and the declared entitlements, then the scan. A
// wrong argument, and an allow-list or entitlements file that cannot be read, end the run with 2
// before anything is scanned.
internal static class Cli
{
    private const string Usage =
        "usage: dotnet run tools/ScanPackage.cs -- [--allow-list <file> [--strict-allow-list]] "
            + "[--entitlements <plist> [--extended-virtual-addressing]] [--build-path <text>]... [--text <file>]... "
            + "<package.ipa>... | --self-test";

    public static int Run(IReadOnlyList<string> args, Func<string, Stream> open, TextWriter output, TextWriter error)
    {
        string? allowList = null;
        string? entitlements = null;
        var strict = false;
        var extended = false;
        var buildPaths = new List<string>();
        var texts = new List<string>();
        var packages = new List<string>();
        for (var index = 0; index < args.Count; index++)
        {
            var argument = args[index];
            var value = string.Empty;
            if (argument is "--allow-list" or "--entitlements" or "--build-path" or "--text")
            {
                if (index + 1 >= args.Count
                    || args[index + 1].StartsWith("--", StringComparison.Ordinal)
                    || args[index + 1].Trim().Length == 0)
                {
                    return Refuse(error);
                }

                value = args[++index];
            }

            switch (argument)
            {
                case "--allow-list" when allowList is null:
                    allowList = value;
                    break;
                case "--entitlements" when entitlements is null:
                    entitlements = value;
                    break;
                case "--build-path":
                    buildPaths.Add(value);
                    break;
                case "--text":
                    texts.Add(value);
                    break;
                case "--strict-allow-list" when !strict:
                    strict = true;
                    break;
                case "--extended-virtual-addressing" when !extended:
                    extended = true;
                    break;
                case var other when other.StartsWith("--", StringComparison.Ordinal):
                    return Refuse(error);
                default:
                    packages.Add(argument);
                    break;
            }
        }

        if ((packages.Count == 0 && texts.Count == 0)
            || (strict && allowList is null)
            || (extended && entitlements is null))
        {
            return Refuse(error);
        }

        var list = AllowList.Empty;
        Dictionary<string, object>? declared = null;
        try
        {
            if (allowList is not null)
            {
                list = AllowList.Parse(Encoding.UTF8.GetString(ReadAll(open, allowList)), allowList);
            }

            if (entitlements is not null)
            {
                declared = Declared(entitlements, ReadAll(open, entitlements));
                if (extended)
                {
                    declared[Entitlements.ExtendedVirtualAddressing] = true;
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine($"error: {exception.Message}");
            return 2;
        }

        var settings = new Settings
        {
            Packages = packages,
            Texts = texts,
            AllowList = list,
            Strict = strict,
            Options = new ScanOptions { Entitlements = declared, BuildPaths = buildPaths },
        };
        return Scanner.ScanAll(settings, open, output, error);
    }

    private static int Refuse(TextWriter error)
    {
        error.WriteLine(Usage);
        return 2;
    }

    private static byte[] ReadAll(Func<string, Stream> open, string path)
    {
        using var stream = open(path);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    // The entitlements that Entitlements.plist declares: a dictionary at the top of a property list.
    private static Dictionary<string, object> Declared(string path, byte[] bytes)
    {
        try
        {
            return Plist.Parse(bytes, new Budget()) is Dictionary<string, object> declared
                ? declared
                : throw new InvalidDataException("it holds no dictionary at its top");
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{path} cannot be read as entitlements: {exception.Message}", exception);
        }
    }
}

// What the scan reads of a Mach-O file. Of its code signatures: every entitlements blob in XML,
// every blob in DER (the entitlements and the launch constraints) and which of them are the DER
// entitlements, the flags of every code directory, whether a code directory or a CMS blob names a
// team, the byte range of each signature, and the number of slices without a code directory. Of its
// load commands: whether a symbol table holds a STABS entry and whether a segment is __DWARF. Each
// super blob is read once, and the blobs copied from a file may hold no more bytes together than
// the file, which a real signature, whose blobs do not overlap, never reaches.
internal sealed class Signature(long fileLength)
{
    private long _copied;

    public List<byte[]> Entitlements { get; } = [];

    public List<byte[]> Der { get; } = [];

    public List<byte[]> DerEntitlements { get; } = [];

    public HashSet<long> SuperBlobs { get; } = [];

    public List<uint> CodeDirectoryFlags { get; } = [];

    public List<(long Start, long End)> Ranges { get; } = [];

    public bool NamesTeam { get; set; }

    public int UnsignedSlices { get; set; }

    public bool Stabs { get; set; }

    public bool Dwarf { get; set; }

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
// directory's identifier and hashes are not read; of a code directory only its flags (byte 12),
// among them CS_ADHOC (0x2) and CS_LINKER_SIGNED (0x20000). Each slice needs a code directory to be
// signed. The symbol table of LC_SYMTAB holds entries of 16 bytes (nlist_64), whose type byte has a
// bit of N_STAB (0xE0) set for a debugging entry, and an LC_SEGMENT_64 names its segment in the 16
// bytes after its size. A 32-bit or big-endian Mach-O file and a fat file with 64-bit offsets are
// not parsed, and so are unreadable, as are more slices or blobs than a real file holds and a
// symbol table outside the file. Mach-O headers are little-endian on arm64; fat headers and
// signature blobs are big-endian. Offsets are long, so that no sum of the file's numbers overflows.
internal static class MachO
{
    public const uint AdHocFlag = 0x2;
    public const uint LinkerSignedFlag = 0x20000;

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
    private const uint SymbolTableCommand = 0x2;
    private const uint Segment64Command = 0x19;
    private const uint SymbolTableCommandSize = 24;
    private const uint Segment64CommandSize = 72;
    private const int SymbolSize = 16;
    private const int SymbolTypeField = 4;
    private const byte StabMask = 0xE0;
    private const int CodeDirectoryFlagsField = 12;
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

    // Reads the code signatures and the symbols of every slice into the signature, which keeps what
    // it read before a part that it cannot read.
    public static void ReadSignatures(byte[] bytes, Signature signature)
    {
        var magic = BinaryPrimitives.ReadUInt32BigEndian(bytes);
        if (magic == FatMagic64)
        {
            throw new InvalidDataException("a fat file with 64-bit offsets, which this reader does not parse");
        }

        if (magic != FatMagic)
        {
            ReadThin(bytes, 0, signature);
            return;
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
    }

    private static void ReadThin(byte[] bytes, long start, Signature signature)
    {
        if (BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start, 4)) != Magic64)
        {
            throw new InvalidDataException("not a 64-bit little-endian Mach-O file");
        }

        // Every LC_CODE_SIGNATURE is read, although a valid file holds one, and the super blob of
        // each once, even when several commands name it.
        var codeDirectories = signature.CodeDirectoryFlags.Count;
        var commands = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, start + 16, 4));
        var offset = start + 32;
        for (var index = 0L; index < commands; index++)
        {
            var command = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset, 4));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 4, 4));
            if (size < 8
                || (command == SymbolTableCommand && size < SymbolTableCommandSize)
                || (command == Segment64Command && size < Segment64CommandSize))
            {
                throw new InvalidDataException("a load command is too short");
            }

            if (command == CodeSignatureCommand)
            {
                var superBlob = start + BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 8, 4));
                var end = superBlob + BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 12, 4));
                signature.Ranges.Add((superBlob, Math.Min(end, bytes.Length)));
                if (signature.SuperBlobs.Add(superBlob))
                {
                    ReadSuperBlob(bytes, superBlob, signature);
                }
            }
            else if (command == SymbolTableCommand)
            {
                var symbols = start + BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 8, 4));
                var count = BinaryPrimitives.ReadUInt32LittleEndian(Slice(bytes, offset + 12, 4));
                var table = Slice(bytes, symbols, count * (long)SymbolSize);
                for (var entry = SymbolTypeField; entry < table.Length && !signature.Stabs; entry += SymbolSize)
                {
                    signature.Stabs = (table[entry] & StabMask) != 0;
                }
            }
            else if (command == Segment64Command)
            {
                signature.Dwarf |= Slice(bytes, offset + 8, 16).SequenceEqual("__DWARF\0\0\0\0\0\0\0\0\0"u8);
            }

            offset += size;
        }

        if (signature.CodeDirectoryFlags.Count == codeDirectories)
        {
            signature.UnsignedSlices++;
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
                    var der = signature.Copy(Content(bytes, blob, DerEntitlementsMagic));
                    signature.Der.Add(der);
                    signature.DerEntitlements.Add(der);
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
                    var flags = Slice(bytes, blob + CodeDirectoryFlagsField, 4);
                    signature.CodeDirectoryFlags.Add(BinaryPrimitives.ReadUInt32BigEndian(flags));
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
// gives, so that its texts get the same checks: an integer of up to 8 bytes becomes its decimal
// text, as the XML form gives an integer, so that the entitlements of both forms compare, and a
// longer one and data become an empty text. Any other tag, such as another type of string or a
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
            0x02 => Integer(content),
            0x04 => string.Empty,
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

    // A signed big-endian integer of 1 to 8 bytes as its decimal text, and any other as empty.
    private static string Integer(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length is 0 or > 8)
        {
            return string.Empty;
        }

        long value = (sbyte)bytes[0];
        foreach (var part in bytes[1..])
        {
            value = (value << 8) | part;
        }

        return value.ToString(CultureInfo.InvariantCulture);
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

// Checks every rule on packages that it builds in memory: a clean package, whose signatures are ad
// hoc, passes; a planted provisioning profile, team identifier or application-identifier prefix
// each fails and names its file, in every part of a code signature that the scan reads; a package
// without an app fails; a Mach-O file, property list or blob of DER that is malformed, hostile or
// of a kind the scan does not parse is reported, never crashed on; an entry larger than the scan
// reads, or with data that does not match its size, makes its package unreadable; and the scan
// goes on after a package that it cannot read. Then the rest of C-0009 section 6: a clean package
// with notices, a binary Info.plist, a compat/ snapshot and exactly the declared entitlements
// passes; each planted UDID, ECID, serial number, team identifier, e-mail address and local path
// fails in a text, in a binary property list, in ASCII and in UTF-16LE inside a binary and in a
// --text file, without its value in any report line; each signature, entitlements, symbol,
// exclusion and build-path rule fails on its fixture and passes on its counterpart; the allow-list
// excuses a hit only in its file and with its hash, refuses each malformed list and reports a stale
// entry; and on macOS, real signatures that clang and codesign make. The planted values are made up
// and exist only in memory, or in a temporary folder on macOS.
internal static class SelfTest
{
    private const string App = "Payload/Paneport.app/";
    private const string Team = "AB12CD34EF";
    private const string BundleId = "io.github.recelilious.paneport";
    private const string AppIdentifier = $"<key>application-identifier</key><string>{Team}.{BundleId}</string>";
    private const string IncreasedMemoryLimit = "com.apple.developer.kernel.increased-memory-limit";
    private const string Serial = "X1Y2Z3W4V5U6";
    private const string Email = "tester@example.com";
    private const string OtherEmail = "other@example.com";
    private const string LocalPath = "/Users/fixture/work/Paneport/obj/Release/host.o";
    private const string BuildPath = "/opt/build/ws-7f3a/Paneport";
    private const string Host = "Frameworks/paneport_host.framework/paneport_host";
    private const string ExtendedVirtualAddressing = global::Entitlements.ExtendedVirtualAddressing;
    private const string RealApp = "Payload/Probe.app/";
    private const int AotHeader = 24;
    private const int AssemblyHeader = 256;

    private static readonly List<string> s_skipped = [];

    // A UDID and an ECID, whose hex digits are built at run time, as tools/ScanPersonalData.cs builds
    // its own, so that no secret scanner mistakes this file for a leak.
    private static readonly string s_udid = $"0000FFFF-{Hex(16, 1)}";
    private static readonly string s_ecid = $"0x{Hex(14, 2)}";

    // The values that no report line may show; the e-mail addresses stand for the matched text of
    // the others too.
    private static readonly string[] s_planted = [Team, s_udid, s_ecid, Serial, Email, OtherEmail, "/Users/fixture"];

    private static int s_failures;
    private static int s_passed;

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
        // The strings of the signature, which the content rules read, still show the prefix that
        // the unreadable list would hide; so do those of the binary property list further down.
        Expect("a key given twice makes the XML entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(
                $"{AppIdentifier}<key>application-identifier</key><string>{BundleId}</string>"))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature),
                new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
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
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature),
                new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
        Expect("DER nested 100 levels deep makes the entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(("com.paneport.self-test", Nested(100))))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a DER length that runs past its blob makes the entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, Blob(0xFADE7172, [0x70, 0x84, 0x7F, 0xFF, 0xFF, 0xFF, 0x02, 0x01, 0x01]))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature)]);
        Expect("a key given twice makes the DER entitlements unreadable",
            Files(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements(
                ("application-identifier", $"{Team}.{BundleId}"), ("application-identifier", BundleId)))))),
            [new Finding($"{App}Paneport.iOS", Rules.UnreadableSignature),
                new Finding($"{App}Paneport.iOS (code signature)", Rules.AppIdentifierPrefix)]);
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
            [new Finding($"{App}Twice.plist", Rules.UnreadablePropertyList),
                new Finding($"{App}Twice.plist", Rules.AppIdentifierPrefix)]);
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
        CheckContent();
        CheckSignatures();
        CheckEntitlements();
        CheckSymbols();
        CheckExclusions();
        CheckBuildPaths();
        CheckRuns();
        CheckRealSignatures();

        var skipped = s_skipped.Count == 0
            ? string.Empty
            : $", {s_skipped.Count} skipped ({string.Join("; ", s_skipped.Distinct())})";
        Console.WriteLine(s_failures == 0
            ? $"Self-test passed: {s_passed} checks as expected{skipped}."
            : $"Self-test failed: {s_failures} of {s_passed + s_failures} checks{skipped}.");
        return s_failures == 0 ? 0 : 1;
    }

    // A package whose findings, by file and rule, are exactly the expected ones, and none of whose
    // report lines shows a planted value.
    private static void Expect(string name, byte[] package, Finding[] expected, ScanOptions? options = null)
    {
        PackageReport report;
        try
        {
            report = Scanner.Scan(new MemoryStream(package), options);
        }
        catch (Exception exception)
        {
            Fail(name, $"the scan threw {exception.GetType().Name}");
            return;
        }

        var found = Pairs(report.Findings);
        var wanted = Pairs(expected);
        var leaks = Leaks(report);
        if (!found.SequenceEqual(wanted) || leaks.Count != 0)
        {
            var leak = leaks.Count != 0 ? ", and a report line shows the value" : string.Empty;
            Fail(name, $"found [{string.Join(", ", found)}], expected [{string.Join(", ", wanted)}]{leak}");
            return;
        }

        Pass(name);
    }

    private static void ExpectEntitlement(string name, byte[] package, string line)
    {
        var report = Scanner.Scan(new MemoryStream(package));
        if (!report.Entitlements.Contains(line))
        {
            Fail(name, $"[{string.Join("; ", report.Entitlements)}]");
            return;
        }

        Pass(name);
    }

    // A package that the scan refuses as a whole, which ScanPackages reports before it goes on.
    private static void ExpectUnreadable(string name, byte[] package)
    {
        try
        {
            Scanner.Scan(new MemoryStream(package));
            Fail(name, "the scan read the package");
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            Pass(name);
        }
        catch (Exception exception)
        {
            Fail(name, $"the scan threw {exception.GetType().Name}");
        }
    }

    private static List<(string Entry, string Rule)> Pairs(IEnumerable<Finding> findings) =>
    [
        .. findings.Select(finding => (finding.Entry, finding.Rule)).Distinct()
            .OrderBy(pair => pair.Entry, StringComparer.Ordinal).ThenBy(pair => pair.Rule, StringComparer.Ordinal),
    ];

    // The report lines of a package, as a run prints them, that show a planted value.
    private static List<string> Leaks(PackageReport report) =>
    [
        .. report.Entitlements.Concat(report.Findings.Select(finding => Output.Line("fixture.ipa", finding)))
            .Where(line => s_planted.Any(value => line.Contains(value, StringComparison.Ordinal))),
    ];

    private static void Pass(string name)
    {
        s_passed++;
        Console.WriteLine($"ok   {name}");
    }

    private static void Fail(string name, string detail)
    {
        s_failures++;
        Console.WriteLine($"FAIL {name}: {detail}.");
    }

    // A check that cannot run on this system: it counts neither as passed nor as failed, and the
    // summary names its reason, as tools/GenerateNotices.cs reports such a check.
    private static void Skip(string name, string reason)
    {
        s_skipped.Add(reason);
        Console.WriteLine($"skip {name} ({reason})");
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
            Fail(name, $"the scan threw {exception.GetType().Name}");
            return;
        }

        if (result != 2 || !error.ToString().StartsWith("broken.ipa: error: ", StringComparison.Ordinal)
            || !output.ToString().Contains("flagged.ipa: 1 findings.", StringComparison.Ordinal))
        {
            Fail(name, $"exit {result}, output [{output}], errors [{error}]");
            return;
        }

        Pass(name);
    }

    // The content rules of C-0009 section 6 in each kind of file that a package holds.
    private static void CheckContent()
    {
        Expect(
            "a clean package with notices, a binary Info.plist, a compat/ snapshot and exactly the declared "
                + "entitlements passes",
            CleanPackage(),
            [],
            new ScanOptions { Entitlements = Declared() });
        foreach (var (rule, line, key, value) in Planted())
        {
            Expect($"{rule} in a text fails", Files(("licenses/NOTES.md", Notes(line))),
                [new Finding($"{App}licenses/NOTES.md", rule)]);
            Expect($"{rule} in a binary property list fails",
                Files(("Settings.bundle/Root.plist", BinaryPlist(new Dictionary<string, object> { [key] = value }))),
                [new Finding($"{App}Settings.bundle/Root.plist", rule)]);
            Expect($"{rule} in ASCII inside a binary fails",
                Files(("Paneport.Core.aotdata.arm64", AotData(Encoding.UTF8.GetBytes(line)))),
                [new Finding($"{App}Paneport.Core.aotdata.arm64", rule)]);
            Expect($"{rule} in UTF-16LE inside an assembly fails", Files(("Paneport.Core.dll", ManagedAssembly(line))),
                [new Finding($"{App}Paneport.Core.dll", rule)]);
            ExpectRun($"{rule} in a --text file fails", ["--text", "notes.md"], new() { ["notes.md"] = Notes(line) }, 1,
                [$"notes.md: {rule}: sha256 "], []);
        }

        // A chip identifier that a property list stores as an integer, as device records do, is
        // read as its decimal text.
        Expect("an ECID stored as an integer in a binary property list fails",
            Files(("Settings.bundle/Root.plist", BinaryPlist(new Dictionary<string, object> { ["UniqueChipID"] = 0x0001_2A3B_4C5D_6E7FL }))),
            [new Finding($"{App}Settings.bundle/Root.plist", "ecid")]);

        var notes = $"{App}licenses/NOTES.md";
        var data = $"{App}Paneport.Core.aotdata.arm64";
        var settings = $"{App}Settings.bundle/Root.plist";
        ExpectHit(
            "a hit in a Markdown file names its line and the heading above it, and the SHA-256 of its text",
            Files(("licenses/NOTES.md", Notes($"Contact: {Email}"))),
            notes,
            "email",
            Email,
            "line 5, below \"## Planted\"");
        ExpectHit(
            "a hit below a heading that a rule fires on does not show the heading",
            Files(("licenses/NOTES.md", Text($"# Notes\n\n## Contact {OtherEmail}\n\nBuilt in {LocalPath}\n"))),
            notes,
            "local-path",
            "/Users/fixture",
            "line 5, below a heading that is not shown");
        ExpectHit(
            "a hit in ASCII inside a binary names its byte offset",
            Files(("Paneport.Core.aotdata.arm64", AotData(Encoding.UTF8.GetBytes($"Contact: {Email}")))),
            data,
            "email",
            Email,
            $"byte {AotHeader}");
        ExpectHit(
            "a hit in UTF-16LE inside an assembly names its byte offset",
            Files(("Paneport.Core.dll", ManagedAssembly($"Contact: {Email}"))),
            $"{App}Paneport.Core.dll",
            "email",
            Email,
            $"byte {AssemblyHeader}");
        ExpectHit(
            "a hit in a property list names the key above it",
            Files(("Settings.bundle/Root.plist", BinaryPlist(new Dictionary<string, object> { ["Contact"] = Email }))),
            settings,
            "email",
            Email,
            "under the key Contact");
        var keyed = Files(
            ("Settings.bundle/Root.plist", BinaryPlist(new Dictionary<string, object> { [Email] = LocalPath })));
        Expect("a key that holds an e-mail address fails without being shown", keyed,
            [new Finding(settings, "email"), new Finding(settings, "local-path")]);
        ExpectHit("a value below such a key does not name it", keyed, settings, "local-path", "/Users/fixture",
            "under a key that is not shown");
        Expect("a local path after the end of the executable fails as the executable's",
            Files(("Paneport.iOS", [.. MachO(Entitlements(string.Empty)), 0, .. Encoding.UTF8.GetBytes(LocalPath), 0])),
            [new Finding($"{App}Paneport.iOS", "local-path")]);
        Expect("an e-mail address in the entitlements fails as the signature's",
            Files(("Paneport.iOS", MachO(Entitlements($"<key>com.example.contact</key><string>{Email}</string>")))),
            [new Finding($"{App}Paneport.iOS (code signature)", "email")]);
        var across = new string('~', ContentScan.Window - 8) + Email + new string('~', 4000);
        Expect("an e-mail address across the end of a window of a long string is found",
            Files(("Paneport.Core.aotdata.arm64", AotData(Encoding.ASCII.GetBytes(across)))),
            [new Finding(data, "email")]);

        // A key of 60,000 characters with an array of 20 values below it: the lines of the key with
        // each value hold more characters than the scan matches for a file of this size.
        var values = string.Concat(Enumerable.Repeat("<string>x</string>\n", 20));
        Expect("a text that needs more matching than its size allows fails as unreadable-content",
            Files(("Notes.txt", Text($"<key>{new string('~', 60_000)}</key>\n<array>\n{values}</array>\n"))),
            [new Finding($"{App}Notes.txt", Rules.UnreadableContent)]);

        var named = Scanner.Scan(new MemoryStream(Files(($"logs/{Email}.txt", Text($"Contact: {Email}\n")))));
        var lines = named.Findings.Select(finding => Output.Line("fixture.ipa", finding)).ToList();
        var hidden = $"fixture.ipa: {App}logs/{Output.NameNotShown}: email: sha256 ";
        Check("a file named after an e-mail address is reported without its name",
            lines.Count == 1 && lines[0].StartsWith(hidden, StringComparison.Ordinal) && Leaks(named).Count == 0,
            string.Join("; ", lines));
        Check("an annotation escapes %, CR and LF",
            Output.Annotation("a%b\r\nc") == "::error title=Package scan::a%25b%0D%0Ac", Output.Annotation("a%b\r\nc"));
    }

    // The flags of the code directories.
    private static void CheckSignatures()
    {
        var signed = $"{App}Paneport.iOS (code signature)";
        Expect("a code directory without the ad-hoc flag fails",
            Files(("Paneport.iOS", Signed((0, CodeDirectory(flags: 0)), (5, XmlEntitlements(string.Empty))))),
            [new Finding(signed, Rules.SignatureNotAdHoc)]);
        Expect("an alternate code directory without the ad-hoc flag fails",
            Files(("Paneport.iOS",
                Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty)), (0x1000, CodeDirectory(flags: 0))))),
            [new Finding(signed, Rules.SignatureNotAdHoc)]);
        const uint LinkerSigned = global::MachO.AdHocFlag | global::MachO.LinkerSignedFlag;
        Expect("a code directory that the linker signed fails",
            Files(("Paneport.iOS",
                Signed((0, CodeDirectory(flags: LinkerSigned)), (5, XmlEntitlements(string.Empty))))),
            [new Finding(signed, Rules.SignatureLinkerSigned)]);
        Expect("a framework without the ad-hoc flag fails",
            Files((Host, Signed((0, CodeDirectory(flags: 0))))),
            [new Finding($"{App}{Host} (code signature)", Rules.SignatureNotAdHoc)]);
    }

    // The entitlements of --entitlements, and a Mach-O file without a signature.
    private static void CheckEntitlements()
    {
        var options = new ScanOptions { Entitlements = Declared() };
        var main = $"{App}Paneport.iOS (code signature)";
        const string Limit = IncreasedMemoryLimit;
        ExpectDetail(
            "an extra entitlement fails and names its key in both forms",
            SignedFiles(("Paneport.iOS",
                Entitled(Entitlements("<key>get-task-allow</key><true/>"), (Limit, true), ("get-task-allow", true)))),
            options,
            main,
            ["the XML entitlements hold get-task-allow too", "the DER entitlements hold get-task-allow too"],
            []);
        ExpectDetail(
            "a missing entitlement fails and names its key in both forms",
            SignedFiles(("Paneport.iOS", Entitled("<dict/>"))),
            options,
            main,
            [$"the XML entitlements lack {Limit}", $"the DER entitlements lack {Limit}"],
            []);
        ExpectDetail(
            "a different Boolean value fails and shows both values",
            SignedFiles(("Paneport.iOS", Entitled($"<dict><key>{Limit}</key><false/></dict>", (Limit, false)))),
            options,
            main,
            [$"the XML entitlements set {Limit} to false, not true", $"the DER entitlements set {Limit} to false"],
            []);
        ExpectDetail(
            "a value of another type fails without being shown",
            SignedFiles(("Paneport.iOS",
                Entitled($"<dict><key>{Limit}</key><string>yes</string></dict>", (Limit, "yes")))),
            options,
            main,
            [$"the XML entitlements give {Limit} another value", $"the DER entitlements give {Limit} another value"],
            ["yes"]);
        ExpectDetail(
            "DER entitlements that differ from correct XML ones fail",
            SignedFiles(("Paneport.iOS",
                Entitled(Entitlements(string.Empty), (Limit, true), ("com.apple.security.app-sandbox", true)))),
            options,
            main,
            ["the DER entitlements hold com.apple.security.app-sandbox too"],
            ["the XML entitlements"]);
        ExpectDetail(
            "an executable without DER entitlements fails",
            SignedFiles(("Paneport.iOS", Signed((0, CodeDirectory()), (5, XmlEntitlements(string.Empty))))),
            options,
            main,
            [$"the DER entitlements lack {Limit}"],
            ["the XML entitlements"]);
        ExpectDetail(
            "an executable without XML entitlements fails",
            SignedFiles(("Paneport.iOS", Signed((0, CodeDirectory()), (7, DerEntitlements((Limit, true)))))),
            options,
            main,
            [$"the XML entitlements lack {Limit}"],
            ["the DER entitlements"]);
        ExpectDetail(
            "entitlements on a framework fail",
            Files((Host, MachO(Entitlements(string.Empty)))),
            options,
            $"{App}{Host} (code signature)",
            [$"the XML entitlements hold {Limit} too", $"the DER entitlements hold {Limit} too"],
            []);
        var extended = new ScanOptions { Entitlements = Declared(extended: true) };
        ExpectDetail(
            "--extended-virtual-addressing requires that entitlement",
            SignedFiles(),
            extended,
            main,
            [
                $"the XML entitlements lack {ExtendedVirtualAddressing}",
                $"the DER entitlements lack {ExtendedVirtualAddressing}",
            ],
            []);
        var both = Entitlements($"<key>{ExtendedVirtualAddressing}</key><true/>");
        Expect("--extended-virtual-addressing passes an executable that carries both entitlements",
            SignedFiles(("Paneport.iOS", Entitled(both, (Limit, true), (ExtendedVirtualAddressing, true)))),
            [],
            extended);
        Expect("with --entitlements an unsigned framework fails", Files(),
            [new Finding($"{App}{Host}", Rules.Unsigned)], options);
        Expect("with --entitlements an unsigned executable fails", SignedFiles(("Paneport.iOS", MachO(null))),
            [new Finding($"{App}Paneport.iOS", Rules.Unsigned)], options);
        Expect("with --entitlements a fat file with an unsigned slice fails",
            SignedFiles(("Paneport.iOS", Fat(MachO(Entitlements(string.Empty)), MachO(null)))),
            [new Finding($"{App}Paneport.iOS", Rules.Unsigned)], options);
        Expect("without --entitlements neither the entitlements nor a missing signature are checked",
            Files(("Paneport.iOS", Entitled("<dict/>"))), []);
        Expect("an entitlement whose key holds an e-mail address fails without the key",
            SignedFiles(("Paneport.iOS", Entitled(Entitlements($"<key>{Email}</key><true/>"), (Limit, true)))),
            [new Finding(main, "email"), new Finding(main, Rules.EntitlementsDiffer)], options);
    }

    // Debug symbols: STABS entries and a __DWARF segment.
    private static void CheckSymbols()
    {
        var executable = $"{App}Paneport.iOS";
        Expect("a STABS entry in the symbol table fails", Files(("Paneport.iOS", WithSymbols([0x0F, 0x64]))),
            [new Finding(executable, Rules.DebugSymbols)]);
        Expect("a symbol table without STABS entries passes",
            Files(("Paneport.iOS", WithSymbols([0x0F, 0x01, 0x1E]))), []);
        Expect("a __DWARF segment fails", Files(("Paneport.iOS", WithSymbols([], "__DWARF"))),
            [new Finding(executable, Rules.DebugSymbols)]);
        Expect("a __TEXT segment passes", Files(("Paneport.iOS", WithSymbols([], "__TEXT"))), []);
        Expect("a STABS entry in a framework fails", Files((Host, WithSymbols([0x24]))),
            [new Finding($"{App}{Host}", Rules.DebugSymbols)]);
        Expect("a symbol table that runs past the file is unreadable",
            Files(("Paneport.iOS", WithSymbols([0x0F], symbols: 0x7FFFFFF0))),
            [new Finding(executable, Rules.UnreadableSignature)]);
    }

    // The exclusions of C-0009 section 4, by name.
    private static void CheckExclusions()
    {
        (string Path, byte[] Bytes)[] excluded =
        [
            ("wine/x86_64-windows/steamclient64.dll", Pe(marker: false)),
            ("wine/x86_64-windows/SteamClient64.DLL", Pe(marker: true)),
            ("wine/i386-windows/steam_api.dll", Pe(marker: false)),
            ("Frameworks/libsteam_api.dylib", [1, 2, 3, 4]),
            ("wine/i386-windows/tier0_s.dll", Pe(marker: false)),
            ("wine/i386-windows/Steam.exe", Pe(marker: false)),
            ("wine/x86_64-windows/vcruntime140.dll", Pe(marker: false)),
            ("wine/x86_64-windows/D3DCompiler_47.dll", Pe(marker: false)),
            ("wine/x86_64-windows/api-ms-win-crt-runtime-l1-1-0.dll", Pe(marker: false)),
            ("wine/x86_64-windows/xinput1_3.dll", Pe(marker: false)),
            ("redist/VC_redist.x64.exe", [1, 2, 3, 4]),
            ("wine/mono/wine-mono-9.4.0/lib/mono.dll", [1, 2, 3, 4]),
            ("gecko/wine_gecko/xul.dll", [1, 2, 3, 4]),
            ("share/setup.MSI", [1, 2, 3, 4]),
            ("Frameworks/libmetalirconverter.dylib", [1, 2, 3, 4]),
            ("Frameworks/MetalIRConverter.framework/MetalIRConverter", [1, 2, 3, 4]),
        ];
        foreach (var (path, bytes) in excluded)
        {
            Expect($"{path} fails as excluded", Files((path, bytes)),
                [new Finding($"{App}{path}", Rules.ExcludedFile)]);
        }

        Expect("Microsoft runtime names that Wine built, with its builtin marker, pass",
            Files(
                ("wine/x86_64-windows/vcruntime140.dll", Pe(marker: true)),
                ("wine/x86_64-windows/msvcp140.dll", Pe(marker: true)),
                ("wine/x86_64-windows/xinput1_3.dll", Pe(marker: true)),
                ("wine/i386-windows/d3dx9_43.dll", Pe(marker: true))),
            []);
        Expect("a file named like a runtime that is no PE file passes",
            Files(("wine/x86_64-windows/msvcp140.dll", Text("not a module"))), []);
        Expect("names that hold such names after their start pass",
            Files(
                ("Paneport.Steam.dll", Pe(marker: false)),
                ("wine/x86_64-windows/kernel32.dll", Pe(marker: false)),
                ("licenses/my_steam_api_notes.txt", Text("notes")),
                ("licenses/wine-notes.txt", Text("notes"))),
            []);
    }

    // The build paths of --build-path.
    private static void CheckBuildPaths()
    {
        var options = new ScanOptions { BuildPaths = [BuildPath] };
        var source = $"{BuildPath}/src/Paneport.Core/Steam.cs";
        ExpectDetail(
            "a build path in UTF-8 fails and names the path",
            Files(("Paneport.Core.aotdata.arm64", AotData(Encoding.UTF8.GetBytes(source)))),
            options,
            $"{App}Paneport.Core.aotdata.arm64",
            [$"holds the build path {BuildPath} in UTF-8, at byte {AotHeader}"],
            ["Steam.cs"],
            Rules.BuildPath);
        ExpectDetail(
            "a build path in UTF-16LE fails and names the path",
            Files(("Paneport.Core.dll", ManagedAssembly(source))),
            options,
            $"{App}Paneport.Core.dll",
            [$"holds the build path {BuildPath} in UTF-16LE, at byte {AssemblyHeader}"],
            ["Steam.cs"],
            Rules.BuildPath);
        Expect("a build path in a text fails", Files(("licenses/NOTES.md", Notes($"Built in {BuildPath}."))),
            [new Finding($"{App}licenses/NOTES.md", Rules.BuildPath)], options);
        Expect("another path passes",
            Files(("Paneport.Core.aotdata.arm64", AotData("/opt/build/other/Paneport/src"u8))), [], options);
    }

    // Whole runs, as the jobs call the tool: the options, --text, the allow-list and its refusals.
    private static void CheckRuns()
    {
        const string Notices = "licenses/THIRD_PARTY_NOTICES.md";
        var allowed = Files((Notices, Notes($"Contact: {Email}")));
        var clean = Files((Notices, Notes("Copyright (c) The Example Authors.")));
        var list = ListOf(EntryOf(Notices, "email", Sha(Email)));
        ExpectRun("the scan without options takes packages as before", ["a.ipa"], new() { ["a.ipa"] = Files() }, 0,
            ["a.ipa: 0 findings."], []);
        ExpectRun("an allowed hit passes", ["--allow-list", "list.json", "a.ipa"],
            new() { ["list.json"] = list, ["a.ipa"] = allowed }, 0,
            ["a.ipa: 0 findings.", "a.ipa: 1 hits excused by list.json."], [": email:", "excused nothing"]);
        ExpectRun("the same text in another file fails", ["--allow-list", "list.json", "a.ipa"],
            new()
            {
                ["list.json"] = list,
                ["a.ipa"] = Files(
                    (Notices, Notes($"Contact: {Email}")), ("licenses/OTHER.md", Notes($"Contact: {Email}"))),
            },
            1, [$"a.ipa: {App}licenses/OTHER.md: email: sha256 {Sha(Email)}"], [$"{App}{Notices}: email"]);
        ExpectRun("another text in the allowed file fails", ["--allow-list", "list.json", "a.ipa"],
            new() { ["list.json"] = list, ["a.ipa"] = Files((Notices, Notes($"Contact: {Email}, {OtherEmail}"))) }, 1,
            [$"a.ipa: {App}{Notices}: email: sha256 {Sha(OtherEmail)}"],
            [$"{App}{Notices}: email: sha256 {Sha(Email)}"]);
        ExpectRun("a stale entry is printed, and passes without --strict-allow-list",
            ["--allow-list", "list.json", "a.ipa"],
            new() { ["list.json"] = list, ["a.ipa"] = clean }, 0,
            [$"list.json: the entry for {Notices}, email, sha256 {Sha(Email)} excused nothing"], []);
        ExpectRun("a stale entry fails with --strict-allow-list",
            ["--allow-list", "list.json", "--strict-allow-list", "a.ipa"],
            new() { ["list.json"] = list, ["a.ipa"] = clean }, 1, ["excused nothing"], []);
        ExpectRun("an entry whose file was not scanned is not judged",
            ["--allow-list", "list.json", "--strict-allow-list", "a.ipa"],
            new() { ["list.json"] = ListOf(EntryOf("licenses/MISSING.md", "email", Sha(Email))), ["a.ipa"] = clean }, 0,
            [], ["excused nothing"]);
        ExpectRun("an entry that one of two packages uses is not stale",
            ["--allow-list", "list.json", "--strict-allow-list", "main.ipa", "test.ipa"],
            new() { ["list.json"] = list, ["main.ipa"] = allowed, ["test.ipa"] = clean }, 0,
            ["main.ipa: 1 hits excused"], ["excused nothing"]);
        ExpectRun("a hit in a --text file names the file, its line and its heading", ["--text", "notes.md"],
            new() { ["notes.md"] = Notes($"Contact: {Email}") }, 1,
            [$"notes.md: email: sha256 {Sha(Email)}, line 5, below \"## Planted\""], []);
        ExpectRun("an allowed hit in a --text file passes", ["--allow-list", "list.json", "--text", "notes.md"],
            new()
            {
                ["list.json"] = ListOf(EntryOf("notes.md", "email", Sha(Email))),
                ["notes.md"] = Notes($"Contact: {Email}"),
            },
            0, ["notes.md: 1 hits excused"], []);
        ExpectRun("a build path in a --text file fails", ["--build-path", BuildPath, "--text", "notes.md"],
            new() { ["notes.md"] = Notes($"Built in {BuildPath}.") }, 1,
            [$"notes.md: build-path: holds the build path {BuildPath} in UTF-8"], []);
        ExpectRun("a --text file that cannot be read makes the run exit with 2", ["--text", "missing.md"], new(), 2,
            [], []);
        ExpectRun(
            "a build path is never excused, while the local path in it is",
            ["--allow-list", "list.json", "--build-path", "/Users/fixture/work/Paneport", "a.ipa"],
            new()
            {
                ["list.json"] = ListOf(EntryOf("Paneport.Core.aotdata.arm64", "local-path", Sha("/Users/fixture"))),
                ["a.ipa"] = Files(("Paneport.Core.aotdata.arm64", AotData(Encoding.UTF8.GetBytes(LocalPath)))),
            },
            1,
            ["build-path: holds the build path /Users/fixture/work/Paneport in UTF-8", "a.ipa: 1 hits excused"],
            [": local-path:", "obj/Release"],
            leaks: false);
        var entitlements = Xml(Entitlements(string.Empty));
        ExpectRun("--entitlements reads Entitlements.plist and a clean package passes",
            ["--entitlements", "Entitlements.plist", "a.ipa"],
            new() { ["Entitlements.plist"] = entitlements, ["a.ipa"] = SignedFiles() }, 0, ["a.ipa: 0 findings."], []);
        ExpectRun("--extended-virtual-addressing adds that entitlement to the declared ones",
            ["--entitlements", "Entitlements.plist", "--extended-virtual-addressing", "a.ipa"],
            new() { ["Entitlements.plist"] = entitlements, ["a.ipa"] = SignedFiles() }, 1,
            [$"entitlements-differ: the XML entitlements lack {ExtendedVirtualAddressing}"], []);

        var sha256 = Sha(Email);
        string[] malformed =
        [
            "{",
            "[]",
            """{ "format": 1, "entries": [], "note": "x" }""",
            """{ "entries": [] }""",
            """{ "format": 2, "entries": [] }""",
            """{ "format": "1", "entries": [] }""",
            """{ "format": 1 }""",
            """{ "format": 1, "entries": {} }""",
            """{ "format": 1, "entries": [ 1 ] }""",
            """{ "format": 1, "format": 1, "entries": [] }""",
            ListText($$"""{ "file": "a.md", "rule": "email", "sha256": "{{sha256}}", "reason": "T.", "note": "x" }"""),
            ListText($$"""{ "file": "a.md", "rule": "email", "sha256": "{{sha256}}" }"""),
            ListText($$"""{ "rule": "email", "sha256": "{{sha256}}", "reason": "Test." }"""),
            ListText($$"""{ "file": "a.md", "rule": "email", "sha256": "{{sha256}}", "reason": " " }"""),
            ListText($$"""{ "file": "", "rule": "email", "sha256": "{{sha256}}", "reason": "Test." }"""),
            ListText($$"""{ "file": 1, "rule": "email", "sha256": "{{sha256}}", "reason": "Test." }"""),
            ListText($$"""{ "file": "a", "file": "b", "rule": "email", "sha256": "{{sha256}}", "reason": "T." }"""),
            ListText(EntryOf("a.md", "email", sha256.ToUpperInvariant())),
            ListText(EntryOf("a.md", "email", sha256[..63])),
            ListText(EntryOf("a.md", "email", $"{sha256[..63]}g")),
            ListText(EntryOf("a.md", Rules.ProvisioningProfile, sha256)),
            ListText(EntryOf("a.md", Rules.BuildPath, sha256)),
            ListText(EntryOf("a.md", Rules.SignatureNotAdHoc, sha256)),
            ListText(EntryOf("a.md", Rules.ExcludedFile, sha256)),
            ListText(EntryOf("a.md", Rules.DebugSymbols, sha256)),
            ListText(EntryOf("a.md", "steam-id", sha256)),
            ListText(EntryOf("a.md", "no-such-rule", sha256)),
            ListText(EntryOf("a.md", "email", sha256), EntryOf("a.md", "email", sha256)),
        ];
        foreach (var json in malformed)
        {
            ExpectRun($"the allow-list {json} is refused", ["--allow-list", "list.json", "a.ipa"],
                new() { ["list.json"] = Text(json), ["a.ipa"] = Files() }, 2, [], ["a.ipa:"]);
        }

        ExpectRun("an allow-list that does not exist makes the run exit with 2",
            ["--allow-list", "missing.json", "a.ipa"], new() { ["a.ipa"] = Files() }, 2, [], ["a.ipa:"]);
        ExpectRun("an entitlements file that is not a property list makes the run exit with 2",
            ["--entitlements", "Entitlements.plist", "a.ipa"],
            new() { ["Entitlements.plist"] = Text("<dict>"), ["a.ipa"] = Files() }, 2, [], ["a.ipa:"]);
        ExpectRun("an entitlements file without a dictionary makes the run exit with 2",
            ["--entitlements", "Entitlements.plist", "a.ipa"],
            new() { ["Entitlements.plist"] = Xml("<array/>"), ["a.ipa"] = Files() }, 2, [], ["a.ipa:"]);
        string[][] usages =
        [
            [],
            ["--strict", "a.ipa"],
            ["--strict-allow-list", "a.ipa"],
            ["--extended-virtual-addressing", "a.ipa"],
            ["--allow-list", "list.json", "--allow-list", "list.json", "a.ipa"],
            ["--text"],
            ["--text", "--strict-allow-list", "a.ipa"],
            ["--build-path", " ", "a.ipa"],
            ["--self-test", "a.ipa"],
        ];
        foreach (var arguments in usages)
        {
            ExpectRun($"the arguments [{string.Join(' ', arguments)}] are refused", arguments,
                new() { ["list.json"] = ListOf(), ["a.ipa"] = Files() }, 2, [], ["a.ipa:"]);
        }
    }

    // Real signatures, made on macOS: clang links a small program, whose linker signature fails;
    // codesign signs it ad hoc with the declared entitlements, which passes, and a copy without
    // entitlements, which passes as a framework; a copy without a signature fails with
    // --entitlements; and a program linked with debug information fails, as does its dSYM file.
    private static void CheckRealSignatures()
    {
        string[] names =
        [
            "a real linker signature fails as the linker's",
            "a real ad-hoc signature with exactly the declared entitlements passes, beside a framework without any",
            "a real file without a signature fails with --entitlements",
            "a real program linked with debug information fails",
            "a real dSYM file fails",
        ];
        if (!OperatingSystem.IsMacOS())
        {
            foreach (var name in names)
            {
                Skip(name, "clang and codesign run only on macOS");
            }

            return;
        }

        var folder = Directory.CreateTempSubdirectory("scan-package-");
        try
        {
            string file(string name) => Path.Combine(folder.FullName, name);
            byte[] read(string name) => File.ReadAllBytes(file(name));
            File.WriteAllText(file("probe.c"), "int main(void) { return 0; }\n");
            File.WriteAllBytes(file("Entitlements.plist"), Xml(Entitlements(string.Empty)));
            if (!Tool("xcrun", "clang", "-arch", "arm64", "-o", file("Probe"), file("probe.c"))
                || !Tool("xcrun", "clang", "-arch", "arm64", "-g", "-o", file("Debug"), file("probe.c")))
            {
                foreach (var name in names)
                {
                    Fail(name, "clang could not link the program");
                }

                return;
            }

            ExpectSome(names[0], Real(read("Probe")), null, $"{RealApp}Probe (code signature)",
                Rules.SignatureLinkerSigned);
            File.Copy(file("Probe"), file("Framework"));
            File.Copy(file("Probe"), file("Unsigned"));
            string[] sign =
                ["--force", "--sign", "-", "--entitlements", file("Entitlements.plist"), "--generate-entitlement-der"];
            if (!Tool("codesign", [.. sign, file("Probe")])
                || !Tool("codesign", "--force", "--sign", "-", file("Framework"))
                || !Tool("codesign", "--remove-signature", file("Unsigned"))
                || !Tool("xcrun", "dsymutil", file("Debug"), "-o", file("Debug.dSYM")))
            {
                foreach (var name in names[1..])
                {
                    Fail(name, "codesign or dsymutil failed");
                }

                return;
            }

            const string Framework = "Frameworks/Probe.framework/Probe";
            const string Dwarf = "Probe.dSYM/Contents/Resources/DWARF/Probe";
            var options = new ScanOptions { Entitlements = Declared() };
            Expect(names[1], Real(read("Probe"), (Framework, read("Framework"))), [], options);
            Expect(names[2], Real(read("Probe"), (Framework, read("Unsigned"))),
                [new Finding($"{RealApp}{Framework}", Rules.Unsigned)], options);
            ExpectSome(names[3], Real(read("Debug")), null, $"{RealApp}Probe", Rules.DebugSymbols);
            ExpectSome(names[4], Real(read("Probe"), (Dwarf, read("Debug.dSYM/Contents/Resources/DWARF/Debug"))), null,
                $"{RealApp}{Dwarf}", Rules.DebugSymbols);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }

    // A hit of the rule in the entry, with the SHA-256 of the given text and the given place.
    private static void ExpectHit(string name, byte[] package, string entry, string rule, string text, string where)
    {
        var report = Scanner.Scan(new MemoryStream(package));
        var hit = report.Findings.FirstOrDefault(finding => finding.Entry == entry && finding.Rule == rule);
        Check(name, hit is not null && hit.Sha256 == Sha(text) && hit.Where == where && Leaks(report).Count == 0,
            hit is null ? "no such hit" : $"sha256 {hit.Sha256}, {hit.Where}");
    }

    // Exactly one finding, of the rule in the entry, whose detail shows each given text and none of
    // the others.
    private static void ExpectDetail(
        string name,
        byte[] package,
        ScanOptions options,
        string entry,
        string[] shows,
        string[] lacks,
        string rule = Rules.EntitlementsDiffer)
    {
        var report = Scanner.Scan(new MemoryStream(package), options);
        var pairs = Pairs(report.Findings);
        var detail = report.Findings.FirstOrDefault(finding => finding.Entry == entry && finding.Rule == rule)?.Detail
            ?? string.Empty;
        Check(name,
            pairs.SequenceEqual([(entry, rule)])
                && shows.All(text => detail.Contains(text, StringComparison.Ordinal))
                && !lacks.Any(text => detail.Contains(text, StringComparison.Ordinal))
                && Leaks(report).Count == 0,
            $"found [{string.Join(", ", pairs)}], detail [{detail}]");
    }

    // A finding of the rule in the entry among any others.
    private static void ExpectSome(string name, byte[] package, ScanOptions? options, string entry, string rule)
    {
        var report = Scanner.Scan(new MemoryStream(package), options);
        Check(name, report.Findings.Any(finding => finding.Entry == entry && finding.Rule == rule),
            $"found [{string.Join(", ", Pairs(report.Findings))}]");
    }

    // A run of the command line over files in memory: its exit code, and the texts that its output
    // shows and lacks. Unless told otherwise, no line of it may show a planted value.
    private static void ExpectRun(
        string name,
        string[] arguments,
        Dictionary<string, byte[]> files,
        int exit,
        string[] shows,
        string[] lacks,
        bool leaks = true)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture);
        using var error = new StringWriter(CultureInfo.InvariantCulture);
        int result;
        try
        {
            result = Cli.Run(
                arguments,
                path => files.TryGetValue(path, out var bytes)
                    ? new MemoryStream(bytes)
                    : throw new FileNotFoundException($"{path} does not exist"),
                output,
                error);
        }
        catch (Exception exception)
        {
            Fail(name, $"the run threw {exception.GetType().Name}");
            return;
        }

        var text = output.ToString();
        var shown = $"{text}{error}";
        Check(name,
            result == exit
                && shows.All(part => text.Contains(part, StringComparison.Ordinal))
                && !lacks.Any(part => text.Contains(part, StringComparison.Ordinal))
                && (!leaks || !s_planted.Any(value => shown.Contains(value, StringComparison.Ordinal))),
            $"exit {result}, output [{text.Trim()}], errors [{error.ToString().Trim()}]");
    }

    private static void Check(string name, bool passed, string detail)
    {
        if (passed)
        {
            Pass(name);
        }
        else
        {
            Fail(name, detail);
        }
    }

    // Runs a tool of the system, such as clang or codesign, and tells whether it succeeded.
    private static bool Tool(string program, params string[] arguments)
    {
        var start = new ProcessStartInfo(program)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return false;
            }

            var errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            _ = errors.Result;
            return process.ExitCode == 0;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    // Each planted value with its rule, a line that holds it with its label, and a key and value
    // of a property list that hold it.
    private static (string Rule, string Line, string Key, string Value)[] Planted() =>
    [
        ("udid", $"Device: {s_udid}", "Device", s_udid),
        ("ecid", $"ECID: {s_ecid}", "UniqueChipID", s_ecid),
        ("serial-number", $"Serial Number: {Serial}", "SerialNumber", Serial),
        (Rules.TeamIdentifier, $"Team ID: {Team}", "TeamIdentifier", Team),
        ("email", $"Contact: {Email}", "Contact", Email),
        ("local-path", $"Built in {LocalPath}", "Path", LocalPath),
    ];

    private static Dictionary<string, object> Declared(bool extended = false)
    {
        var declared = new Dictionary<string, object>(StringComparer.Ordinal) { [IncreasedMemoryLimit] = true };
        if (extended)
        {
            declared[ExtendedVirtualAddressing] = true;
        }

        return declared;
    }

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // An entry of the allow-list, as JSON.
    private static string EntryOf(string file, string rule, string sha256) =>
        $$"""{ "file": "{{file}}", "rule": "{{rule}}", "sha256": "{{sha256}}", "reason": "Synthetic." }""";

    // An allow-list of the given entries, as JSON.
    private static string ListText(params string[] entries) =>
        $"{{ \"format\": 1, \"entries\": [ {string.Join(", ", entries)} ] }}";

    // The same, as the bytes of a file.
    private static byte[] ListOf(params string[] entries) => Text(ListText(entries));

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

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);

    // A Markdown file whose fifth line, below the heading "## Planted", is the given one.
    private static byte[] Notes(string line) => Text($"# Notes\n\n## Planted\n\n{line}\n");

    // Ahead-of-time data that holds the given text as a string after a binary header of 24 bytes.
    private static byte[] AotData(ReadOnlySpan<byte> text) =>
        [0x4D, 0x41, 0x4F, 0x54, 1, 0, 0, 0, 0xFF, 0xFE, 0, 0x10, .. new byte[AotHeader - 12], .. text, 0, 0xFF, 0, 1];

    // A managed assembly that holds the given text in UTF-16LE, as its literals are, after a PE
    // header of 256 bytes.
    private static byte[] ManagedAssembly(string text) =>
        [.. "MZ"u8, .. new byte[AssemblyHeader - 2], .. Encoding.Unicode.GetBytes(text), 0, 0];

    // A PE module, with or without the marker that winebuild --builtin writes after the DOS header.
    private static byte[] Pe(bool marker)
    {
        var bytes = new byte[0x80];
        "MZ"u8.CopyTo(bytes);
        if (marker)
        {
            "Wine builtin DLL"u8.CopyTo(bytes.AsSpan(0x40));
        }

        return bytes;
    }

    // A clean app with what a real bundle holds besides: the notices, a binary Info.plist, a compat/
    // snapshot with a device model identifier, a commit hash and a SHA-256 value, an assembly and
    // ahead-of-time data with harmless strings, and every Mach-O file signed ad hoc, the executable
    // with exactly the declared entitlements.
    private static byte[] CleanPackage() => SignedFiles(
        ("Info.plist", BinaryPlist(new Dictionary<string, object>
        {
            ["CFBundleIdentifier"] = BundleId,
            ["CFBundleDisplayName"] = "Paneport",
            ["CFBundleExecutable"] = "Paneport.iOS",
            ["CFBundleShortVersionString"] = "0.1.0",
            ["UIRequiredDeviceCapabilities"] = new List<object> { "arm64" },
        })),
        ("licenses/THIRD_PARTY_NOTICES.md",
            Text("# Third-party notices\n\n## Avalonia\n\nCopyright (c) The Avalonia Project.\n\n"
                + "Permission is hereby granted, free of charge, to any person obtaining a copy of this software.\n")),
        ("compat/steam/440.json",
            Text($"{{ \"appId\": 440, \"device\": \"iPhone18,1\", \"commit\": \"{Hex(40, 3)}\", "
                + $"\"exeSha256\": [\"{Hex(64, 4)}\"] }}\n")),
        ("Paneport.Core.dll", ManagedAssembly("https://example.com/home/tester/page")),
        ("Paneport.Core.aotdata.arm64",
            AotData("System.Private.CoreLib io.github.recelilious.paneport.host 1.2.3.4"u8)));

    // The files of a clean app whose framework is signed ad hoc too, without entitlements, as the
    // jobs sign it, with the given files added, replaced or removed.
    private static byte[] SignedFiles(params (string Name, byte[]? Bytes)[] changes) =>
        Files(
        [
            (Host, Signed((0, CodeDirectory()), (2, Requirements()), (0x1000, CodeDirectory()), (0x10000, Cms()))),
            .. changes,
        ]);

    // An executable signed ad hoc with the given XML dictionary of entitlements and the given DER ones.
    private static byte[] Entitled(string dictionary, params (string Key, object Value)[] der) =>
        Signed(
            (0, CodeDirectory()),
            (2, Requirements()),
            (5, Blob(0xFADE7171, Xml(dictionary))),
            (7, DerEntitlements(der)),
            (0x1000, CodeDirectory()),
            (0x10000, Cms()));

    // The CMS blob of an ad-hoc signature: its header alone.
    private static byte[] Cms() => Blob(0xFADE0B01, []);

    // A thin 64-bit Mach-O file signed ad hoc, whose LC_SYMTAB holds nlist_64 entries of the given
    // types, after the load commands or at the given offset, and, with a segment name, with an
    // LC_SEGMENT_64 of that segment.
    private static byte[] WithSymbols(byte[] types, string? segment = null, uint? symbols = null)
    {
        const int Header = 32;
        const int SymbolTableCommand = 24;
        const int SegmentCommand = 72;
        const int SignatureCommand = 16;
        var superBlob = SuperBlob(AdHoc(Entitlements(string.Empty)));
        var commands = SymbolTableCommand + (segment is null ? 0 : SegmentCommand) + SignatureCommand;
        var table = Header + commands;
        var strings = table + (types.Length * 16);
        var signature = strings + 4;
        var entries = new byte[types.Length * 16];
        for (var index = 0; index < types.Length; index++)
        {
            entries[(index * 16) + 4] = types[index];
        }

        var segmentName = Encoding.ASCII.GetBytes((segment ?? string.Empty).PadRight(16, '\0'));
        byte[] segmentCommand = segment is null
            ? []
            : [.. Little(0x19, SegmentCommand), .. segmentName, .. new byte[48]];
        return Concat(
        [
            Little(0xFEEDFACF, 0x0100000C, 0, 2, segment is null ? 2U : 3U, (uint)commands, 0, 0),
            Little(0x2, SymbolTableCommand, symbols ?? (uint)table, (uint)types.Length, (uint)strings, 4),
            segmentCommand,
            Little(0x1D, SignatureCommand, (uint)signature, (uint)superBlob.Length),
            entries,
            new byte[4],
            superBlob,
        ]);
    }

    // A package of a real app: an Info.plist that names Probe as the executable, the executable and
    // the given other files.
    private static byte[] Real(byte[] executable, params (string Name, byte[] Bytes)[] others) =>
        Package(
        [
            ($"{RealApp}Info.plist", Xml("<dict><key>CFBundleExecutable</key><string>Probe</string></dict>")),
            ($"{RealApp}Probe", executable),
            .. others.Select(other => ($"{RealApp}{other.Name}", other.Bytes)),
        ]);

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

    // A code directory of the given version and flags: without a team, or naming the team after its
    // fields, at byte 88, with that offset at byte 48. The flags are the ad-hoc flag alone unless
    // given, also with a team, so that each rule shows by itself.
    private static byte[] CodeDirectory(
        string? team = null, uint version = 0x20400, uint flags = global::MachO.AdHocFlag)
    {
        const int Fields = 88;
        byte[] teamBytes = team is null ? [] : [.. Encoding.ASCII.GetBytes(team), 0];
        var directory = new byte[Fields + teamBytes.Length];
        Big(0xFADE0C02, (uint)directory.Length, version, flags).CopyTo(directory, 0);
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
    // lists, ASCII texts, Booleans and integers of eight bytes.
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
                case long number:
                    var integer = new byte[9];
                    integer[0] = 0x13;
                    BinaryPrimitives.WriteInt64BigEndian(integer.AsSpan(1), number);
                    objects.Add(integer);
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

    // Made-up hex digits, the same for a seed in every run.
    private static string Hex(int length, int seed)
    {
        var digits = new StringBuilder(length);
        var state = (uint)seed;
        for (var index = 0; index < length; index++)
        {
            state = (state * 1664525) + 1013904223;
            digits.Append("0123456789abcdef"[(int)(state >> 24) % 16]);
        }

        return digits.ToString();
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
