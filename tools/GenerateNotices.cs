#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Generates the third-party notices of an app bundle (S9.1.1; C-0003 sections 3 to 6, C-0009
// sections 4 and 6): THIRD_PARTY_NOTICES.md, the full text of each component's license and
// notices, and the index that the app's open-source licenses screen reads. It writes them into
// licenses/ at the root of the bundle, before the bundle is signed, and with --out into a build
// output folder too, never into git. It lists every file of the bundle and attributes each one to
// Paneport or to components of the register in C-0003 section 6 through
// eng/release/bundle-components.json. The main executable maps to the components that the mapping
// names for it and to the component of every package in the app's lock file, through
// eng/license-map.json, because full AOT compiles every package into it. The register and the map
// are read by the files in tools/Shared/, which tools/CheckLicenses.cs includes too.
//
//   dotnet run tools/GenerateNotices.cs -- --mode ios|release --project <app.csproj> --app <bundle.app>
//       [--out <folder>]                                generate the notices of a built app
//   dotnet run tools/GenerateNotices.cs -- --self-test  check every rule on generated fixtures
//
// Both modes fail on a component that the register lacks or marks Under review, Forbidden or Tool;
// on a component without a license text; on a package whose declared license its register row
// does not name; on an Apache-2.0 NOTICE file left out; on an FTL component without its credit;
// and on an LGPL component other than SteamKit2 in the main executable (C-0003 section 3). A file
// that the mapping does not attribute is a warning in ios mode, the ios job's, and a failure in
// release mode. After a failure nothing is written. Findings name the file, component or package,
// never a license text. Exit codes: 0 the notices are written, 1 a failure, 2 usage or an input
// that cannot be read. docs/design/release-engineering.md describes the rules and the outputs.

#:include Shared/Register.cs
#:include Shared/LicenseMap.cs
#:property PublishAot=false

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

try
{
    if (args is ["--self-test"])
    {
        return SelfTest.Run();
    }

    return Options.Parse(args) is { } options ? Cli.Run(options) : Cli.Usage();
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal enum Mode
{
    // The ios job: an unattributed file is a warning.
    Ios,

    // The release job: an unattributed file fails too.
    Release,
}

internal sealed record Options(Mode Mode, string Project, string App, string? Out)
{
    public static Options? Parse(IReadOnlyList<string> args)
    {
        Mode? mode = null;
        string? project = null;
        string? app = null;
        string? output = null;
        for (var index = 0; index < args.Count; index += 2)
        {
            if (index + 1 >= args.Count || args[index + 1].Length == 0)
            {
                return null;
            }

            var value = args[index + 1];
            switch (args[index])
            {
                case "--mode" when mode is null:
                    mode = value switch
                    {
                        "ios" => Mode.Ios,
                        "release" => Mode.Release,
                        _ => null,
                    };
                    if (mode is null)
                    {
                        return null;
                    }

                    break;
                case "--project" when project is null:
                    project = value;
                    break;
                case "--app" when app is null:
                    app = value;
                    break;
                case "--out" when output is null:
                    output = value;
                    break;
                default:
                    return null;
            }
        }

        return mode is { } chosen && project is not null && app is not null
            ? new Options(chosen, project, app, output)
            : null;
    }
}

internal static class Cli
{
    public static int Usage()
    {
        Console.Error.WriteLine(
            "usage: dotnet run tools/GenerateNotices.cs -- --mode ios|release --project <app.csproj> --app <bundle.app> "
                + "[--out <folder>] | --self-test");
        return 2;
    }

    public static int Run(Options options)
    {
        var root = Directory.GetCurrentDirectory();
        if (!File.Exists(Path.Combine(root, Register.FilePath)))
        {
            throw new InvalidDataException($"run the tool from the repository root, where {Register.FilePath} is.");
        }

        var configuration = Configuration.Load(root);
        var app = Path.GetFullPath(options.App);
        if (!Directory.Exists(app))
        {
            throw new InvalidDataException($"{options.App} is not an app bundle.");
        }

        Output.EnsureBuildOutput(root, app, "--app");
        var output = options.Out is null ? null : Path.GetFullPath(options.Out);
        if (output is not null)
        {
            Output.EnsureBuildOutput(root, output, "--out");
        }

        var bundle = Bundle.Read(app);
        var build = BuildQuery.Run(root, options.Project);
        var result = Notices.Generate(bundle, build, configuration, options.Mode);
        Report.Print(bundle, build, configuration, options.Mode, result, Console.Out);
        if (result.Outputs is not { } outputs)
        {
            return 1;
        }

        Output.Write(Path.Combine(app, Notices.Folder), outputs);
        var written = $"{bundle.Name}/{Notices.Folder}/";
        if (output is not null)
        {
            Output.Write(output, outputs);
            written += $" and {Path.GetRelativePath(root, output).Replace('\\', '/')}/";
        }

        Console.WriteLine(
            $"Wrote {Notices.NoticesFile}, {Notices.IndexFile} and {outputs.Texts.Count} texts into {written}.");
        return 0;
    }
}

// The repository's inputs: the register of C-0003 section 6, eng/license-map.json, the attribution
// mapping, and the MIT license text that the repository holds for its own code.
internal sealed record Configuration(
    string Root,
    IReadOnlyList<RegisterRow> Register,
    LicenseMap Map,
    Mapping Mapping,
    string MitText)
{
    public static Configuration Load(string root) => new(
        root,
        global::Register.Parse(Read(root, global::Register.FilePath)),
        LicenseMap.Parse(Read(root, LicenseMap.FilePath)),
        Mapping.Parse(Read(root, Mapping.FilePath)),
        MitTemplate.Validate(Read(root, MitTemplate.FilePath)));

    private static string Read(string root, string file)
    {
        var path = Path.Combine(root, file);
        return File.Exists(path) ? File.ReadAllText(path) : throw new InvalidDataException($"{file} does not exist.");
    }
}

// The MIT license of LICENSES/MIT.txt, the standard text, whose copyright line the tool replaces with
// the one that a package declares when the package declares MIT and ships no license file.
internal static partial class MitTemplate
{
    public const string FilePath = "LICENSES/MIT.txt";

    public static string Validate(string text)
    {
        var normalized = TextFile.Normalize(text);
        return CopyrightLine().IsMatch(normalized) && normalized.Contains("Permission is hereby granted", StringComparison.Ordinal)
            ? normalized
            : throw new InvalidDataException($"{FilePath} is not the MIT license with a copyright line.");
    }

    public static string Apply(string template, string copyright) =>
        CopyrightLine().Replace(template, copyright.Replace("$", "$$", StringComparison.Ordinal), 1);

    [GeneratedRegex(@"^Copyright\b.*$", RegexOptions.Multiline)]
    private static partial Regex CopyrightLine();
}

// A package of the app's lock file.
internal sealed record LockedPackage(string Id, string Version);

// A runtime pack that the build resolved: the framework that it serves, its package ID and version,
// and its folder.
internal sealed record RuntimePack(string Framework, string Id, string Version, string Folder);

// What the app's build used: its lock file, NuGet's package folder, the packages of the lock file
// and the runtime packs.
internal sealed record BuildInputs(
    string LockFile,
    string PackageRoot,
    IReadOnlyList<LockedPackage> Packages,
    IReadOnlyList<RuntimePack> RuntimePacks)
{
    public string PackageFolder(LockedPackage package) =>
        Path.Combine(PackageRoot, package.Id.ToLowerInvariant(), package.Version.ToLowerInvariant());

    public LockedPackage? Package(string id) =>
        Packages.FirstOrDefault(package => string.Equals(package.Id, id, StringComparison.OrdinalIgnoreCase));
}

// Asks MSBuild for the runtime packs and NuGet's package folder of the app's project, the ones that
// its build used, and reads the project's lock file.
internal static class BuildQuery
{
    public static BuildInputs Run(string root, string project)
    {
        var path = Path.GetFullPath(project);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{project} does not exist.");
        }

        var json = Dotnet(
            root,
            "msbuild",
            path,
            "-nologo",
            "-nodeReuse:false",
            "-t:ResolveFrameworkReferences",
            "-getItem:ResolvedRuntimePack",
            "-getProperty:NuGetPackageRoot",
            "-p:Configuration=Release");
        var (packageRoot, packs) = ParseQuery(json);
        var lockFile = LockFiles.Find(path);
        return new BuildInputs(
            Path.GetRelativePath(root, lockFile).Replace('\\', '/'),
            packageRoot,
            LockFiles.Packages(File.ReadAllText(lockFile)),
            packs);
    }

    // The JSON that MSBuild prints for -getProperty and -getItem. A runtime pack without a folder was
    // not resolved and is left out; two packs for one framework would make the texts ambiguous.
    public static (string PackageRoot, List<RuntimePack> Packs) ParseQuery(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var packageRoot = root.TryGetProperty("Properties", out var properties)
                && properties.TryGetProperty("NuGetPackageRoot", out var value)
                && value.GetString() is { Length: > 0 } text
                    ? text
                    : throw new InvalidDataException("MSBuild reported no NuGetPackageRoot.");
            var packs = new List<RuntimePack>();
            if (root.TryGetProperty("Items", out var items)
                && items.TryGetProperty("ResolvedRuntimePack", out var resolved))
            {
                foreach (var item in resolved.EnumerateArray())
                {
                    if (Metadata(item, "PackageDirectory") is not { } folder)
                    {
                        continue;
                    }

                    var pack = new RuntimePack(
                        Metadata(item, "FrameworkName") ?? throw new InvalidDataException("a runtime pack has no FrameworkName."),
                        Metadata(item, "NuGetPackageId") ?? throw new InvalidDataException("a runtime pack has no NuGetPackageId."),
                        Metadata(item, "NuGetPackageVersion")
                            ?? throw new InvalidDataException("a runtime pack has no NuGetPackageVersion."),
                        folder);
                    if (packs.Any(other => other.Framework == pack.Framework))
                    {
                        throw new InvalidDataException($"MSBuild resolved two runtime packs for {pack.Framework}.");
                    }

                    packs.Add(pack);
                }
            }

            return (packageRoot, packs);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"MSBuild's answer is not the JSON this tool reads: {exception.Message}");
        }
    }

    private static string? Metadata(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 } text
            ? text
            : null;

    private static string Dotnet(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en";
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidDataException("dotnet could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"dotnet could not be started: {exception.Message}");
        }

        using (process)
        {
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode == 0)
            {
                return output;
            }

            var tail = string.Join('\n', (output + error.Result).Split('\n').TakeLast(20));
            throw new InvalidDataException($"dotnet {arguments[0]} failed with exit code {process.ExitCode}:\n{tail}");
        }
    }
}

internal static class LockFiles
{
    // NuGet uses packages.<project>.lock.json in place of packages.lock.json when it exists.
    public static string Find(string project)
    {
        var folder = Path.GetDirectoryName(project) ?? ".";
        var named = Path.Combine(folder, $"packages.{Path.GetFileNameWithoutExtension(project)}.lock.json");
        if (File.Exists(named))
        {
            return named;
        }

        var plain = Path.Combine(folder, "packages.lock.json");
        return File.Exists(plain)
            ? plain
            : throw new InvalidDataException($"{Path.GetFileName(project)} has no lock file beside it.");
    }

    // Every package of every target and runtime, direct, transitive and centrally pinned, without
    // the solution's own projects.
    public static List<LockedPackage> Packages(string json)
    {
        var packages = new List<LockedPackage>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("dependencies", out var targets)
                || targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("the lock file has no \"dependencies\" object.");
            }

            foreach (var target in targets.EnumerateObject())
            {
                foreach (var entry in target.Value.EnumerateObject())
                {
                    var type = entry.Value.TryGetProperty("type", out var kind) ? kind.GetString() : null;
                    if (type == "Project")
                    {
                        continue;
                    }

                    var version = entry.Value.TryGetProperty("resolved", out var resolved) ? resolved.GetString() : null;
                    if (string.IsNullOrEmpty(version))
                    {
                        throw new InvalidDataException($"the lock file gives {entry.Name} no resolved version.");
                    }

                    if (!packages.Any(package => string.Equals(package.Id, entry.Name, StringComparison.OrdinalIgnoreCase)
                        && package.Version == version))
                    {
                        packages.Add(new LockedPackage(entry.Name, version));
                    }
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            throw new InvalidDataException($"the lock file is not of the shape this tool reads: {exception.Message}");
        }

        return [.. packages.OrderBy(package => package.Id, StringComparer.OrdinalIgnoreCase)];
    }
}

internal enum SourceKind
{
    // A file of a package of the lock file, from NuGet's package folder.
    PackageFile,

    // The standard text of the license that a package declares by its expression, with the
    // copyright line of its description; the tool builds it for MIT only.
    PackageLicense,

    // A file of a runtime pack that the build resolved, named by the framework it serves.
    RuntimePackFile,

    // A file of this repository, such as a license file of a fork under external/.
    RepositoryFile,
}

internal sealed record TextSource(SourceKind Kind, string Name, string Value);

internal sealed record ComponentTexts(IReadOnlyList<TextSource> Texts, string? Credit);

internal sealed record FileRule(string Pattern, IReadOnlyList<string> Components, Regex Matcher);

// eng/release/bundle-components.json: the reviewed mapping from the files of an app bundle to
// Paneport or to components of the register, and where each component's license texts come from.
internal sealed record Mapping(
    IReadOnlyList<string> MainExecutable,
    IReadOnlyList<FileRule> Files,
    IReadOnlyDictionary<string, ComponentTexts> Components)
{
    public const string FilePath = "eng/release/bundle-components.json";

    // Paneport's own code, which the register does not list and the notices leave out.
    public const string Paneport = "Paneport";

    public static Mapping Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = Object(document.RootElement, "the mapping", ["mainExecutable", "files", "components"], []);
            var main = Object(root["mainExecutable"], "mainExecutable", ["components", "note"], []);
            Note(main["note"], "mainExecutable");
            var files = new List<FileRule>();
            if (root["files"].ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("\"files\" must be an array of rules.");
            }

            foreach (var element in root["files"].EnumerateArray())
            {
                var rule = Object(element, "a rule of \"files\"", ["path", "components", "note"], []);
                var pattern = Text(rule["path"], "a rule's \"path\"");
                Note(rule["note"], pattern);
                files.Add(new FileRule(pattern, Names(rule["components"], pattern), Glob.ToRegex(pattern)));
            }

            if (root["components"].ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("\"components\" must be an object.");
            }

            var components = new Dictionary<string, ComponentTexts>(StringComparer.Ordinal);
            foreach (var property in root["components"].EnumerateObject())
            {
                if (property.Name == Paneport || property.Name.Length == 0)
                {
                    throw new InvalidDataException($"\"components\" cannot hold \"{property.Name}\".");
                }

                var entry = Object(property.Value, property.Name, ["texts"], ["credit", "note"]);
                if (entry["texts"].ValueKind != JsonValueKind.Array || entry["texts"].GetArrayLength() == 0)
                {
                    throw new InvalidDataException($"{property.Name}: \"texts\" must list at least one text.");
                }

                var texts = entry["texts"].EnumerateArray().Select(text => Source(text, property.Name)).ToList();
                var credit = entry.TryGetValue("credit", out var value) ? Text(value, $"{property.Name}: \"credit\"") : null;
                if (entry.TryGetValue("note", out var note))
                {
                    Note(note, property.Name);
                }

                if (!components.TryAdd(property.Name, new ComponentTexts(texts, credit)))
                {
                    throw new InvalidDataException($"\"components\" lists {property.Name} twice.");
                }
            }

            return new Mapping(Names(main["components"], "mainExecutable"), files, components);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }

    private static TextSource Source(JsonElement element, string component)
    {
        var keys = element.ValueKind == JsonValueKind.Object
            ? element.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray()
            : [];
        var where = $"a text of {component}";
        return keys switch
        {
            ["file", "package"] => new TextSource(
                SourceKind.PackageFile,
                Text(element.GetProperty("package"), where),
                RelativePath(element.GetProperty("file"), where)),
            ["license", "package"] => new TextSource(
                SourceKind.PackageLicense,
                Text(element.GetProperty("package"), where),
                Text(element.GetProperty("license"), where)),
            ["file", "runtimePack"] => new TextSource(
                SourceKind.RuntimePackFile,
                Text(element.GetProperty("runtimePack"), where),
                RelativePath(element.GetProperty("file"), where)),
            ["repository"] => new TextSource(
                SourceKind.RepositoryFile,
                string.Empty,
                RelativePath(element.GetProperty("repository"), where)),
            _ => throw new InvalidDataException(
                $"{where} must name a package with a file or a license, a runtime pack with a file, or a repository file."),
        };
    }

    private static Dictionary<string, JsonElement> Object(
        JsonElement element,
        string where,
        string[] required,
        string[] optional)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where} must be an object.");
        }

        var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!required.Contains(property.Name) && !optional.Contains(property.Name))
            {
                throw new InvalidDataException($"{where} has a key \"{property.Name}\" that the tool does not know.");
            }

            if (!properties.TryAdd(property.Name, property.Value))
            {
                throw new InvalidDataException($"{where} gives \"{property.Name}\" twice.");
            }
        }

        var missing = required.FirstOrDefault(key => !properties.ContainsKey(key));
        return missing is null ? properties : throw new InvalidDataException($"{where} has no \"{missing}\".");
    }

    private static List<string> Names(JsonElement element, string where)
    {
        var names = element.ValueKind == JsonValueKind.Array
            ? element.EnumerateArray().Select(name => Text(name, $"{where}: a component")).ToList()
            : throw new InvalidDataException($"{where}: \"components\" must be an array.");
        return names.Count > 0 && names.Distinct(StringComparer.Ordinal).Count() == names.Count
            ? names
            : throw new InvalidDataException($"{where}: \"components\" must name components, each once.");
    }

    private static void Note(JsonElement element, string where) => Text(element, $"{where}: \"note\"");

    private static string Text(JsonElement element, string where) =>
        element.ValueKind == JsonValueKind.String && element.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new InvalidDataException($"{where} must be a text that is not empty.");

    // A path below a package, a runtime pack or the repository: relative, with forward slashes, and
    // never leaving its folder.
    private static string RelativePath(JsonElement element, string where)
    {
        var path = Text(element, where);
        return path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal) || path.Contains(':', StringComparison.Ordinal)
            || path.Split('/').Any(part => part is "" or "." or "..")
            ? throw new InvalidDataException($"{where}: {path} must be a relative path that stays below its folder.")
            : path;
    }
}

// The paths of the mapping's rules: "*" matches within one part of a path, "?" one character of it,
// and "**", as a whole part, any number of parts, so that "Frameworks/x.framework/**" is everything
// inside that framework and "**/x" an x in any folder.
internal static class Glob
{
    public static Regex ToRegex(string pattern)
    {
        var parts = pattern.Split('/');
        if (pattern.StartsWith('/') || pattern.Contains('\\', StringComparison.Ordinal)
            || parts.Any(part => part is "" or "." or ".." || (part.Contains("**", StringComparison.Ordinal) && part != "**")))
        {
            throw new InvalidDataException(
                $"the path {pattern} must be relative to the bundle, and \"**\" must be a whole part of it.");
        }

        var builder = new StringBuilder("^");
        for (var index = 0; index < parts.Length; index++)
        {
            var last = index == parts.Length - 1;
            if (parts[index] == "**")
            {
                builder.Append(last ? ".+" : "(?:[^/]+/)*");
                continue;
            }

            foreach (var character in parts[index])
            {
                builder.Append(character switch
                {
                    '*' => "[^/]*",
                    '?' => "[^/]",
                    _ => Regex.Escape(character.ToString()),
                });
            }

            if (!last)
            {
                builder.Append('/');
            }
        }

        return new Regex(builder.Append('$').ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }
}

// An app bundle: its folder and name, every file in it except the notices that this tool writes,
// and the keys of its Info.plist that the notices use.
internal sealed record Bundle(
    string Folder,
    string Name,
    IReadOnlyList<string> Files,
    string Executable,
    string Title,
    string? ShortVersion,
    string? BuildNumber)
{
    public static Bundle Read(string folder)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
        var info = Path.Combine(folder, "Info.plist");
        if (!File.Exists(info))
        {
            throw new InvalidDataException($"{name} has no Info.plist.");
        }

        Dictionary<string, string> values;
        try
        {
            values = PropertyList.TopLevelStrings(File.ReadAllBytes(info));
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{name}/Info.plist cannot be read: {exception.Message}");
        }

        var executable = values.GetValueOrDefault("CFBundleExecutable");
        if (string.IsNullOrEmpty(executable) || executable.Contains('/', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name}/Info.plist names no executable in CFBundleExecutable.");
        }

        var title = values.GetValueOrDefault("CFBundleDisplayName") ?? values.GetValueOrDefault("CFBundleName")
            ?? Path.GetFileNameWithoutExtension(name);
        var files = new List<string>();
        Walk(new DirectoryInfo(folder), string.Empty, files);
        files.Sort(StringComparer.Ordinal);
        return new Bundle(
            folder,
            name,
            files,
            executable,
            title,
            values.GetValueOrDefault("CFBundleShortVersionString"),
            values.GetValueOrDefault("CFBundleVersion"));
    }

    // Every file, hidden ones too. A link is listed as a file and never followed.
    private static void Walk(DirectoryInfo directory, string prefix, List<string> files)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        foreach (var entry in directory.EnumerateFileSystemInfos("*", options))
        {
            if (prefix.Length == 0 && entry.Name == Notices.Folder && entry is DirectoryInfo)
            {
                continue;
            }

            if (entry is DirectoryInfo child && entry.LinkTarget is null)
            {
                Walk(child, $"{prefix}{entry.Name}/", files);
            }
            else
            {
                files.Add(prefix + entry.Name);
            }
        }
    }
}

// The string values at the top of a property list, XML or binary: the build writes the app's
// Info.plist in Apple's binary format.
internal static class PropertyList
{
    public static Dictionary<string, string> TopLevelStrings(byte[] bytes) =>
        bytes.AsSpan().StartsWith("bplist00"u8) ? BinaryPropertyList.TopLevelStrings(bytes) : Xml(bytes);

    private static Dictionary<string, string> Xml(byte[] bytes)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        XDocument document;
        try
        {
            using var reader = XmlReader.Create(new MemoryStream(bytes), settings);
            document = XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"it is not a property list: {exception.Message}");
        }

        var dictionary = document.Root is { Name.LocalName: "plist" } root ? root.Element("dict") : null;
        if (dictionary is null)
        {
            throw new InvalidDataException("it holds no dictionary.");
        }

        var elements = dictionary.Elements().ToList();
        if (elements.Count % 2 != 0)
        {
            throw new InvalidDataException("its dictionary has a key without a value.");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < elements.Count; index += 2)
        {
            if (elements[index].Name.LocalName != "key")
            {
                throw new InvalidDataException("its dictionary has a value without a key.");
            }

            if (elements[index + 1].Name.LocalName == "string" && !values.TryAdd(elements[index].Value, elements[index + 1].Value))
            {
                throw new InvalidDataException("its dictionary gives a key twice.");
            }
        }

        return values;
    }
}

// Apple's binary property list, read only as far as the string values of a dictionary at the top.
// Every offset, reference and length is checked against the file before it is used.
internal sealed class BinaryPropertyList
{
    private const int TrailerSize = 32;

    private readonly byte[] _bytes;
    private readonly int _offsetSize;
    private readonly int _referenceSize;
    private readonly ulong _count;
    private readonly long _table;
    private readonly long _end;

    private BinaryPropertyList(byte[] bytes, int offsetSize, int referenceSize, ulong count, long table)
    {
        _bytes = bytes;
        _offsetSize = offsetSize;
        _referenceSize = referenceSize;
        _count = count;
        _table = table;
        _end = bytes.Length - TrailerSize;
    }

    public static Dictionary<string, string> TopLevelStrings(byte[] bytes)
    {
        if (bytes.Length < 8 + 1 + TrailerSize)
        {
            throw Unreadable("it is too short");
        }

        var trailer = bytes.AsSpan(bytes.Length - TrailerSize);
        int offsetSize = trailer[6];
        int referenceSize = trailer[7];
        var count = BinaryPrimitives.ReadUInt64BigEndian(trailer[8..]);
        var top = BinaryPrimitives.ReadUInt64BigEndian(trailer[16..]);
        var table = BinaryPrimitives.ReadUInt64BigEndian(trailer[24..]);
        var end = (ulong)(bytes.Length - TrailerSize);
        if (offsetSize is < 1 or > 8 || referenceSize is < 1 or > 8 || count == 0 || top >= count || table < 9
            || table > end || (end - table) / (ulong)offsetSize < count)
        {
            throw Unreadable("its trailer does not fit the file");
        }

        return new BinaryPropertyList(bytes, offsetSize, referenceSize, count, (long)table).ReadTop(top);
    }

    private static InvalidDataException Unreadable(string reason) =>
        new($"it is not a binary property list that this tool reads: {reason}.");

    private Dictionary<string, string> ReadTop(ulong top)
    {
        var offset = ObjectOffset(top);
        var marker = _bytes[offset];
        if (marker >> 4 != 0xD)
        {
            throw Unreadable("its top object is not a dictionary");
        }

        var (entries, position) = Length(marker, offset + 1);
        if (position > _table || entries > (ulong)(_table - position) / (ulong)(2 * _referenceSize))
        {
            throw Unreadable("a dictionary runs past its object");
        }

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (ulong index = 0; index < entries; index++)
        {
            var key = ReadString(Read(position + ((long)index * _referenceSize), _referenceSize))
                ?? throw Unreadable("a key is not a string");
            var value = ReadString(Read(position + ((long)(entries + index) * _referenceSize), _referenceSize));
            if (value is not null && !values.TryAdd(key, value))
            {
                throw Unreadable("a key is given twice");
            }
        }

        return values;
    }

    private long ObjectOffset(ulong index)
    {
        if (index >= _count)
        {
            throw Unreadable("an object reference is out of range");
        }

        var offset = Read(_table + ((long)index * _offsetSize), _offsetSize);
        return offset >= 8 && offset < (ulong)_table ? (long)offset : throw Unreadable("an object offset is out of range");
    }

    private ulong Read(long position, int size)
    {
        if (position < 8 || position > _end - size)
        {
            throw Unreadable("a number runs past the file");
        }

        ulong value = 0;
        for (var index = 0; index < size; index++)
        {
            value = (value << 8) | _bytes[position + index];
        }

        return value;
    }

    // The length in a marker's low four bits, or in the integer that follows it when they are all set.
    private (ulong Length, long Next) Length(byte marker, long position)
    {
        if ((marker & 0xF) != 0xF)
        {
            return ((ulong)(marker & 0xF), position);
        }

        if (position >= _table || _bytes[position] >> 4 != 0x1 || (_bytes[position] & 0xF) > 3)
        {
            throw Unreadable("a length is not an integer");
        }

        var size = 1 << (_bytes[position] & 0xF);
        return (Read(position + 1, size), position + 1 + size);
    }

    // An ASCII or UTF-16 string, or null for an object of another type.
    private string? ReadString(ulong index)
    {
        var offset = ObjectOffset(index);
        var type = _bytes[offset] >> 4;
        if (type is not (0x5 or 0x6))
        {
            return null;
        }

        var (length, start) = Length(_bytes[offset], offset + 1);
        var width = type == 0x5 ? 1UL : 2UL;
        if (start > _table || length > (ulong)(_table - start) / width)
        {
            throw Unreadable("a string runs past its object");
        }

        var span = _bytes.AsSpan((int)start, (int)(length * width));
        return type == 0x5 ? Encoding.ASCII.GetString(span) : Encoding.BigEndianUnicode.GetString(span);
    }
}

// A package's description, its .nuspec in NuGet's package folder: its declared license and copyright.
internal sealed record PackageDescription(string? LicenseType, string? License, string? Copyright)
{
    public static PackageDescription? Read(string folder, string id)
    {
        var path = Path.Combine(folder, $"{id.ToLowerInvariant()}.nuspec");
        if (!File.Exists(path))
        {
            return null;
        }

        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(path, settings);
            var metadata = XDocument.Load(reader).Root?.Elements().FirstOrDefault(element => element.Name.LocalName == "metadata")
                ?? throw new InvalidDataException($"the description of {id} has no metadata.");
            var license = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "license");
            var copyright = metadata.Elements().FirstOrDefault(element => element.Name.LocalName == "copyright")?.Value.Trim();
            return new PackageDescription(
                license?.Attribute("type")?.Value,
                license?.Value.Trim(),
                string.IsNullOrEmpty(copyright) ? null : copyright);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"the description of {id} cannot be read: {exception.Message}");
        }
    }
}

// The licenses that an SPDX expression names, and whether a register row's License text names one.
internal static partial class Spdx
{
    public static List<string> Licenses(string expression)
    {
        var licenses = new List<string>();
        var tokens = Separators().Split(expression).Where(token => token.Length > 0).ToList();
        for (var index = 0; index < tokens.Count; index++)
        {
            switch (tokens[index].ToUpperInvariant())
            {
                case "AND" or "OR":
                    break;
                case "WITH":
                    index++;
                    break;
                default:
                    licenses.Add(tokens[index]);
                    break;
            }
        }

        return licenses;
    }

    // As a whole name: "MIT" counts in "MIT. Its packages" but not in "MIT-Modern-Variant".
    public static bool Mentions(string text, string license) =>
        Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9.+-]){Regex.Escape(license)}(?![A-Za-z0-9+-]|\.[A-Za-z0-9])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    [GeneratedRegex(@"[\s()]+")]
    private static partial Regex Separators();
}

internal static class TextFile
{
    // A license or notices file as text: UTF-8, or UTF-16 with a byte-order mark, with Windows line
    // endings made Unix ones and one line break at its end. Null for a missing file.
    public static string? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        string text;
        try
        {
            using var reader = new StreamReader(
                path,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not UTF-8 text.");
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} is not text.");
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException($"{Path.GetFileName(path)} is empty.")
            : Normalize(text);
    }

    public static string Normalize(string text)
    {
        var unix = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        return unix + "\n";
    }
}

internal sealed record Attribution(string File, IReadOnlyList<string> Components);

internal sealed record TextBlock(string Title, string Text);

internal sealed record ComponentNotice(
    string Name,
    RegisterRow Row,
    IReadOnlyList<string> Sources,
    string? Credit,
    IReadOnlyList<TextBlock> Texts,
    int Files);

internal sealed record NoticeOutputs(string Notices, string Index, IReadOnlyList<(string Path, string Content)> Texts);

internal sealed record Result(
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<Attribution> Coverage,
    IReadOnlyList<string> MainComponents,
    IReadOnlyList<ComponentNotice> Components,
    IReadOnlyList<string> UnusedRules,
    NoticeOutputs? Outputs);

internal static partial class Notices
{
    public const string Folder = "licenses";
    public const string NoticesFile = "THIRD_PARTY_NOTICES.md";
    public const string IndexFile = "index.json";
    public const string TextsFolder = "texts";
    public const int Format = 1;

    // C-0003 section 3: SteamKit2 is the one LGPL component that .NET AOT compiles into the executable.
    public const string StaticLgplComponent = "SteamKit2";

    // The order of the components in all three outputs.
    public static readonly Comparer<string> NameOrder = Comparer<string>.Create((left, right) =>
    {
        var order = string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        return order != 0 ? order : string.CompareOrdinal(left, right);
    });

    public static Result Generate(Bundle bundle, BuildInputs build, Configuration configuration, Mode mode)
    {
        var app = bundle.Name;
        var failures = new List<string>();
        var warnings = new List<string>();

        // The main executable: what the mapping names for it, and every package of the lock file.
        var main = new List<string>(configuration.Mapping.MainExecutable);
        foreach (var package in build.Packages)
        {
            var component = configuration.Map.ComponentFor(Kind.Package, package.Id);
            if (component is null)
            {
                failures.Add($"{app}: the package {package.Id} of {build.LockFile} has no row in {LicenseMap.FilePath}.");
            }
            else if (!main.Contains(component))
            {
                main.Add(component);
            }
        }

        if (!bundle.Files.Contains(bundle.Executable))
        {
            failures.Add($"{app}: Info.plist names the executable {bundle.Executable}, which the bundle lacks.");
        }

        var coverage = new List<Attribution>();
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in bundle.Files)
        {
            if (file == bundle.Executable)
            {
                coverage.Add(new Attribution(file, main));
                continue;
            }

            var rule = configuration.Mapping.Files.FirstOrDefault(rule => rule.Matcher.IsMatch(file));
            if (rule is null)
            {
                coverage.Add(new Attribution(file, []));
                (mode == Mode.Release ? failures : warnings).Add(
                    $"{app}: {file} is attributed to no component. Map it in {Mapping.FilePath}.");
                continue;
            }

            used.Add(rule.Pattern);
            coverage.Add(new Attribution(file, rule.Components));
        }

        var names = coverage.SelectMany(attribution => attribution.Components)
            .Where(name => name != Mapping.Paneport)
            .Distinct(StringComparer.Ordinal)
            .Order(NameOrder)
            .ToList();
        var components = new List<ComponentNotice>();
        foreach (var name in names)
        {
            var row = configuration.Register.FirstOrDefault(row => row.Component == name);
            if (row is null)
            {
                failures.Add($"{app}: {name} is not in the register of C-0003 section 6. Register it before it ships.");
                continue;
            }

            if (Refusal(row) is { } refusal)
            {
                failures.Add($"{app}: {name} {refusal}");
                continue;
            }

            var count = coverage.Count(attribution => attribution.Components.Contains(name));
            if (Notice(app, name, row, count, build, configuration, failures) is { } notice)
            {
                components.Add(notice);
            }
        }

        foreach (var name in main.Where(name => name != StaticLgplComponent))
        {
            if (configuration.Register.FirstOrDefault(row => row.Component == name) is { } row
                && row.License.Contains("LGPL", StringComparison.Ordinal))
            {
                failures.Add(
                    $"{app}: {name}, under {row.License}, is compiled into the main executable {bundle.Executable}. "
                        + $"C-0003 section 3 allows that only for {StaticLgplComponent}; ship it as a library that the "
                        + "user can replace.");
            }
        }

        CheckDeclaredLicenses(app, build, configuration, failures);
        var unused = configuration.Mapping.Files.Select(rule => rule.Pattern).Where(pattern => !used.Contains(pattern)).ToList();
        var outputs = failures.Count == 0 ? Render(bundle, components) : null;
        return new Result(failures, warnings, coverage, main, components, unused, outputs);
    }

    private static string? Refusal(RegisterRow row) => row.Category switch
    {
        "Under review" => "is Under review in the register, and nothing under review ships until its review is recorded "
            + "(C-0003 section 5, C-0009 section 4).",
        "Forbidden" => "is Forbidden in the register and never enters the app package (C-0003 section 5).",
        "Tool" => "is a build tool in the register, and a Tool row covers nothing that ships (C-0003 section 5).",
        _ => null,
    };

    // The component's texts, sources and credit, or null after a failure.
    private static ComponentNotice? Notice(
        string app,
        string name,
        RegisterRow row,
        int files,
        BuildInputs build,
        Configuration configuration,
        List<string> failures)
    {
        var sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in build.Packages.Where(package => configuration.Map.ComponentFor(Kind.Package, package.Id) == name))
        {
            sources.Add($"{package.Id} {package.Version}");
        }

        if (!configuration.Mapping.Components.TryGetValue(name, out var entry))
        {
            failures.Add($"{app}: missing license text of {name}: {Mapping.FilePath} names no texts for it.");
            return null;
        }

        var before = failures.Count;
        var texts = new List<TextBlock>();
        foreach (var source in entry.Texts)
        {
            if (Text(app, name, source, build, configuration, failures, sources) is { } text)
            {
                texts.Add(text);
            }
        }

        if (Spdx.Mentions(row.License, "Apache-2.0"))
        {
            CheckNoticeFiles(app, name, entry, build, configuration, failures);
        }

        if (Spdx.Mentions(row.License, "FTL") && entry.Credit is null)
        {
            failures.Add(
                $"{app}: {name} is under the FTL, which asks for a credit to FreeType in the documentation (C-0003 "
                    + $"section 5). Give it a credit in {Mapping.FilePath}.");
        }

        return failures.Count == before ? new ComponentNotice(name, row, [.. sources], entry.Credit, texts, files) : null;
    }

    private static TextBlock? Text(
        string app,
        string name,
        TextSource source,
        BuildInputs build,
        Configuration configuration,
        List<string> failures,
        SortedSet<string> sources)
    {
        var missing = $"{app}: missing license text of {name}:";
        try
        {
            switch (source.Kind)
            {
                case SourceKind.PackageFile or SourceKind.PackageLicense:
                    if (build.Package(source.Name) is not { } package)
                    {
                        failures.Add($"{missing} the package {source.Name} is not in {build.LockFile}.");
                        return null;
                    }

                    var label = $"{package.Id} {package.Version}";
                    sources.Add(label);
                    var folder = build.PackageFolder(package);
                    if (source.Kind == SourceKind.PackageFile)
                    {
                        return TextFile.Read(Path.Combine(folder, source.Value)) is { } text
                            ? new TextBlock($"{source.Value} of the package {label}", text)
                            : Fail(failures, $"{missing} the package {label} has no {source.Value}.");
                    }

                    return License(missing, source, package, folder, configuration, failures);
                case SourceKind.RuntimePackFile:
                    if (build.RuntimePacks.FirstOrDefault(pack => pack.Framework == source.Name) is not { } pack)
                    {
                        failures.Add($"{missing} the build resolved no runtime pack for {source.Name}.");
                        return null;
                    }

                    sources.Add($"{pack.Id} {pack.Version}");
                    return TextFile.Read(Path.Combine(pack.Folder, source.Value)) is { } packText
                        ? new TextBlock($"{source.Value} of the runtime pack {pack.Id} {pack.Version}", packText)
                        : Fail(failures, $"{missing} the runtime pack {pack.Id} {pack.Version} has no {source.Value}.");
                default:
                    sources.Add(source.Value);
                    return TextFile.Read(Path.Combine(configuration.Root, source.Value)) is { } fileText
                        ? new TextBlock($"{source.Value} in Paneport's source tree", fileText)
                        : Fail(failures, $"{missing} {source.Value} does not exist in the repository.");
            }
        }
        catch (InvalidDataException exception)
        {
            return Fail(failures, $"{missing} {exception.Message}");
        }
    }

    // The standard text of the license that a package declares by its expression, with the
    // copyright line of its description: built for MIT only, from LICENSES/MIT.txt.
    private static TextBlock? License(
        string missing,
        TextSource source,
        LockedPackage package,
        string folder,
        Configuration configuration,
        List<string> failures)
    {
        var label = $"{package.Id} {package.Version}";
        if (source.Value != "MIT")
        {
            return Fail(
                failures,
                $"{missing} the tool builds the text of MIT only, not of {source.Value}; name a file of {label} instead.");
        }

        if (PackageDescription.Read(folder, package.Id) is not { } description)
        {
            return Fail(failures, $"{missing} the package {label} has no description in NuGet's package folder.");
        }

        if (description.LicenseType != "expression" || description.License != "MIT")
        {
            return Fail(failures, $"{missing} the package {label} does not declare the expression MIT.");
        }

        if (description.Copyright is not { } copyright || !CopyrightNotice().IsMatch(copyright))
        {
            return Fail(failures, $"{missing} the package {label} declares no copyright notice.");
        }

        return new TextBlock(
            $"MIT license, with the copyright line of the package {label}, which declares MIT and ships no license file",
            MitTemplate.Apply(configuration.MitText, copyright));
    }

    private static TextBlock? Fail(List<string> failures, string message)
    {
        failures.Add(message);
        return null;
    }

    // Apache-2.0 asks to keep the NOTICE file (C-0003 section 5): every NOTICE file at the top of a
    // package of the component, or of a package or runtime pack that its texts come from, is one of
    // its texts.
    private static void CheckNoticeFiles(
        string app,
        string name,
        ComponentTexts entry,
        BuildInputs build,
        Configuration configuration,
        List<string> failures)
    {
        var folders = new List<(string Label, string Folder, SourceKind Kind, string Key)>();
        foreach (var package in build.Packages)
        {
            if (configuration.Map.ComponentFor(Kind.Package, package.Id) == name
                || entry.Texts.Any(text => (text.Kind is SourceKind.PackageFile or SourceKind.PackageLicense)
                    && string.Equals(text.Name, package.Id, StringComparison.OrdinalIgnoreCase)))
            {
                var label = $"the package {package.Id} {package.Version}";
                folders.Add((label, build.PackageFolder(package), SourceKind.PackageFile, package.Id));
            }
        }

        foreach (var pack in build.RuntimePacks)
        {
            if (entry.Texts.Any(text => text.Kind == SourceKind.RuntimePackFile && text.Name == pack.Framework))
            {
                var label = $"the runtime pack {pack.Id} {pack.Version}";
                folders.Add((label, pack.Folder, SourceKind.RuntimePackFile, pack.Framework));
            }
        }

        foreach (var (label, folder, kind, key) in folders.Where(candidate => Directory.Exists(candidate.Folder)))
        {
            var files = Directory.EnumerateFiles(folder).Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (NoticeFileName().IsMatch(file)
                    && !entry.Texts.Any(text => text.Kind == kind && string.Equals(text.Name, key, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(text.Value, file, StringComparison.OrdinalIgnoreCase)))
                {
                    failures.Add(
                        $"{app}: {name} leaves out the NOTICE file {file} of {label}, which Apache-2.0 asks to keep "
                            + $"(C-0003 section 5). Add it to the component's texts in {Mapping.FilePath}.");
                }
            }
        }
    }

    // Every license that a package declares by its expression is one that the register row of its
    // component names; otherwise the register needs a change first (C-0003 section 6).
    private static void CheckDeclaredLicenses(string app, BuildInputs build, Configuration configuration, List<string> failures)
    {
        foreach (var package in build.Packages)
        {
            if (configuration.Map.ComponentFor(Kind.Package, package.Id) is not { } component
                || configuration.Register.FirstOrDefault(row => row.Component == component) is not { } row)
            {
                continue;
            }

            var label = $"{package.Id} {package.Version}";
            if (PackageDescription.Read(build.PackageFolder(package), package.Id) is not { } description)
            {
                failures.Add($"{app}: the package {label} is not in NuGet's package folder. Restore the app first.");
                continue;
            }

            if (description is { LicenseType: "expression", License: { } expression })
            {
                foreach (var license in Spdx.Licenses(expression).Where(license => !Spdx.Mentions(row.License, license)))
                {
                    failures.Add(
                        $"{app}: the package {label} declares {license}, which the register row {component} does not "
                            + "name. The register needs a change first (C-0003 section 6).");
                }
            }
        }
    }

    private static NoticeOutputs Render(Bundle bundle, List<ComponentNotice> components)
    {
        var width = Math.Max(2, components.Count.ToString(CultureInfo.InvariantCulture).Length);
        var paths = components
            .Select((component, index) =>
                $"{TextsFolder}/{(index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')}-{Slug(component.Name)}.txt")
            .ToList();
        var texts = components.Select((component, index) => (paths[index], PlainText(component))).ToList();
        return new NoticeOutputs(Markdown(bundle, components, paths), Index(components, paths), texts);
    }

    private static string PlainText(ComponentNotice component)
    {
        var builder = new StringBuilder();
        builder.Append(component.Name).Append("\n\n");
        builder.Append("License: ").Append(component.Row.License).Append('\n');
        builder.Append("Sources: ").Append(string.Join(", ", component.Sources)).Append('\n');
        if (component.Credit is { } credit)
        {
            builder.Append("Credit: ").Append(credit).Append('\n');
        }

        foreach (var text in component.Texts)
        {
            builder.Append("\n===== ").Append(text.Title).Append(" =====\n\n").Append(text.Text);
        }

        return builder.ToString();
    }

    private static string Markdown(Bundle bundle, List<ComponentNotice> components, List<string> paths)
    {
        var version = bundle.ShortVersion is { } shortVersion
            ? $" {shortVersion}{(bundle.BuildNumber is { } build ? $" ({build})" : string.Empty)}"
            : string.Empty;
        var builder = new StringBuilder();
        builder.Append("# Third-party notices\n\n");
        builder.Append(Inline(bundle.Title)).Append(version).Append(" contains the third-party components below. ");
        builder.Append("Paneport's own code is under the MIT license. Each component keeps its own license, so the app as ");
        builder.Append("a whole is not under the MIT license.\n\n");
        builder.Append("| # | Component | License |\n|---|---|---|\n");
        for (var index = 0; index < components.Count; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"| {index + 1} | {Cell(components[index].Name)} | ");
            builder.Append(Cell(components[index].Row.License)).Append(" |\n");
        }

        builder.Append("\nEach section below gives a component's sources and the full text of its license and notices. ");
        builder.Append(CultureInfo.InvariantCulture, $"The folder `{TextsFolder}` beside this file holds the same texts, ");
        builder.Append(CultureInfo.InvariantCulture, $"one file per component, and `{IndexFile}` lists them in this order ");
        builder.Append("for the app's licenses screen.\n");
        for (var index = 0; index < components.Count; index++)
        {
            var component = components[index];
            builder.Append(CultureInfo.InvariantCulture, $"\n## {index + 1}. {Inline(component.Name)}\n\n");
            builder.Append("- License: ").Append(Inline(component.Row.License)).Append('\n');
            builder.Append("- Sources: ").Append(Inline(string.Join(", ", component.Sources))).Append('\n');
            if (component.Credit is { } credit)
            {
                builder.Append("- Credit: ").Append(Inline(credit)).Append('\n');
            }

            builder.Append(CultureInfo.InvariantCulture, $"- Text: [{paths[index]}]({paths[index]})\n");
            foreach (var text in component.Texts)
            {
                var fence = new string('`', Math.Max(3, LongestBacktickRun(text.Text) + 1));
                builder.Append("\n### ").Append(Inline(text.Title)).Append("\n\n");
                builder.Append(fence).Append("text\n").Append(text.Text).Append(fence).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string Index(List<ComponentNotice> components, List<string> paths)
    {
        using var stream = new MemoryStream();
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using (var writer = new Utf8JsonWriter(stream, options))
        {
            writer.WriteStartObject();
            writer.WriteNumber("format", Format);
            writer.WriteStartArray("components");
            for (var index = 0; index < components.Count; index++)
            {
                var component = components[index];
                writer.WriteStartObject();
                writer.WriteString("name", component.Name);
                writer.WriteString("license", component.Row.License);
                writer.WriteString("category", component.Row.Category);
                writer.WriteStartArray("sources");
                foreach (var source in component.Sources)
                {
                    writer.WriteStringValue(source);
                }

                writer.WriteEndArray();
                if (component.Credit is { } credit)
                {
                    writer.WriteString("credit", credit);
                }

                writer.WriteString("text", paths[index]);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
    }

    public static string Slug(string name)
    {
        var slug = NonAlphanumeric().Replace(name.ToLowerInvariant(), "-").Trim('-');
        return slug.Length == 0 ? "component" : slug;
    }

    private static string Cell(string text) => Inline(text).Replace("|", "\\|", StringComparison.Ordinal);

    private static string Inline(string text) => text.ReplaceLineEndings(" ");

    private static int LongestBacktickRun(string text)
    {
        int longest = 0, run = 0;
        foreach (var character in text)
        {
            run = character == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return longest;
    }

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumeric();

    [GeneratedRegex(@"^NOTICE(?:\..+)?$", RegexOptions.IgnoreCase)]
    private static partial Regex NoticeFileName();

    // A copyright notice says so: "Copyright", "(c)" or the copyright sign.
    [GeneratedRegex("copyright|\\(c\\)|\\u00A9", RegexOptions.IgnoreCase)]
    private static partial Regex CopyrightNotice();
}

internal static class Output
{
    // The notices go into build output, never into git: a folder outside the repository, or inside
    // it under build/ or below a bin/ or obj/ folder, which C-0004 section 3 reserves for build
    // output and .gitignore ignores.
    public static void EnsureBuildOutput(string root, string path, string option)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts[0] == "build" || parts.Any(part => part is "bin" or "obj"))
        {
            return;
        }

        throw new InvalidDataException(
            $"{option} {relative.Replace('\\', '/')} is in the repository outside build/, bin/ and obj/; the notices go "
                + "into build output, never into git.");
    }

    // Writes the notices into a folder, replacing the tool's own earlier output there and nothing else.
    public static void Write(string folder, NoticeOutputs outputs)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any())
        {
            if (!IsOwn(folder))
            {
                throw new IOException(
                    $"{folder} holds files that this tool did not write, and it replaces only its own notices.");
            }

            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(Path.Combine(folder, Notices.TextsFolder));
        Save(Path.Combine(folder, Notices.NoticesFile), outputs.Notices);
        Save(Path.Combine(folder, Notices.IndexFile), outputs.Index);
        foreach (var (path, content) in outputs.Texts)
        {
            Save(Path.Combine(folder, path), content);
        }
    }

    private static bool IsOwn(string folder)
    {
        var index = Path.Combine(folder, Notices.IndexFile);
        if (!File.Exists(index))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(index));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Number
                && document.RootElement.TryGetProperty("components", out var components)
                && components.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Save(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
}

internal static class Report
{
    public static void Print(Bundle bundle, BuildInputs build, Configuration configuration, Mode mode, Result result, TextWriter output)
    {
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var app = bundle.Name;
        output.WriteLine($"Third-party notices of {app}, in {(mode == Mode.Ios ? "ios" : "release")} mode");
        output.WriteLine(
            $"Register: {configuration.Register.Count} rows. Mapping: {configuration.Mapping.Files.Count} file rules and "
                + $"the texts of {configuration.Mapping.Components.Count} components. {build.LockFile}: "
                + $"{build.Packages.Count} packages.");
        foreach (var pack in build.RuntimePacks)
        {
            output.WriteLine($"Runtime pack for {pack.Framework}: {pack.Id} {pack.Version}");
        }

        output.WriteLine($"Coverage of {result.Coverage.Count} files:");
        foreach (var attribution in result.Coverage)
        {
            var components = attribution.Components.Count == 0 ? "(no component)" : string.Join(", ", attribution.Components);
            var role = attribution.File == bundle.Executable ? " (main executable)" : string.Empty;
            output.WriteLine($"  {attribution.File}{role}: {components}");
        }

        if (result.UnusedRules.Count > 0)
        {
            output.WriteLine($"Rules that match no file of {app}: {string.Join(", ", result.UnusedRules)}");
        }

        output.WriteLine($"Components in the notices: {result.Components.Count}");
        for (var index = 0; index < result.Components.Count; index++)
        {
            var component = result.Components[index];
            output.WriteLine(
                $"  {index + 1}. {component.Name} ({component.Row.Category}): {component.Files} files, "
                    + $"{component.Texts.Count} texts, from {string.Join(", ", component.Sources)}");
        }

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"warning: {warning}");
            if (annotate)
            {
                output.WriteLine($"::warning title=Notices::{warning}");
            }
        }

        foreach (var failure in result.Failures)
        {
            output.WriteLine($"error: {failure}");
            if (annotate)
            {
                output.WriteLine($"::error title=Notices::{failure}");
            }
        }

        output.WriteLine($"{app}: {result.Failures.Count} failures, {result.Warnings.Count} warnings.");
    }
}

internal sealed class Tally
{
    public int Total { get; private set; }

    public int Failed { get; private set; }

    public void Expect(bool passed, string description)
    {
        Total++;
        Failed += passed ? 0 : 1;
        Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {description}");
    }
}

// Writes the binary property lists of the self-test: a dictionary of strings at the top, as the
// build writes Info.plist, with a string beyond ASCII in UTF-16.
internal static class BinaryPropertyListWriter
{
    public static byte[] Write(IReadOnlyList<(string Key, string Value)> entries)
    {
        var count = entries.Count;
        var dictionary = new List<byte> { (byte)(0xD0 | Math.Min(count, 0xF)) };
        if (count >= 0xF)
        {
            dictionary.AddRange([0x10, (byte)count]);
        }

        for (var index = 0; index < 2 * count; index++)
        {
            dictionary.Add((byte)(index + 1));
        }

        var objects = new List<byte[]> { dictionary.ToArray() };
        objects.AddRange(entries.Select(entry => Encode(entry.Key)));
        objects.AddRange(entries.Select(entry => Encode(entry.Value)));
        using var stream = new MemoryStream();
        stream.Write("bplist00"u8);
        var offsets = new List<int>();
        foreach (var item in objects)
        {
            offsets.Add((int)stream.Position);
            stream.Write(item);
        }

        var table = stream.Position;
        foreach (var offset in offsets)
        {
            stream.Write([(byte)(offset >> 8), (byte)offset]);
        }

        var trailer = new byte[32];
        trailer[6] = 2;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)objects.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        stream.Write(trailer);
        return stream.ToArray();
    }

    private static byte[] Encode(string text)
    {
        var ascii = text.All(char.IsAscii);
        var bytes = ascii ? Encoding.ASCII.GetBytes(text) : Encoding.BigEndianUnicode.GetBytes(text);
        var head = new List<byte> { (byte)((ascii ? 0x50 : 0x60) | Math.Min(text.Length, 0xF)) };
        if (text.Length >= 0xF)
        {
            head.AddRange([0x10, (byte)text.Length]);
        }

        return [.. head, .. bytes];
    }
}

// A generated bundle with its packages, runtime packs and repository, in a folder of its own. Each
// case starts from the fully registered fixture and changes one thing.
internal sealed class Fixture
{
    public const string Register =
        """
        # Sample licensing contract

        ## 6. Component register

        | Component | Purpose | License | Category |
        |---|---|---|---|
        | Sample.Runtime | A runtime | MIT | Allowed |
        | Sample.Platform | Platform bindings | MIT | Allowed |
        | Sample.Ui | A user interface library | MIT | Allowed |
        | Sample.Graphics | A graphics library | MIT. Its native package lists further components in its notices. | Allowed |
        | Sample.Apache | A protocol library | Apache-2.0 | Allowed |
        | Sample.Fonts | Font rendering | FTL or GPL-2.0 | Allowed, under FTL |
        | SteamKit2 | A network protocol | LGPL-2.1-only | Conditional |
        | Sample.Lgpl | A library the user can replace | LGPL-2.1-or-later | Conditional |
        | Sample.Review | A component | Proprietary | Under review |
        | Sample.Forbidden | A library | GPL-3.0 | Forbidden |
        | Sample.Tool | A build tool | MIT | Tool |
        """;

    public const string Map =
        """
        {
          "packages": {
            "Sample.Ui": "Sample.Ui",
            "Sample.Ui.Extra": "Sample.Ui",
            "Sample.Graphics": "Sample.Graphics",
            "Sample.Graphics.Native": "Sample.Graphics",
            "Sample.Apache": "Sample.Apache",
            "SteamKit2": "SteamKit2",
            "Sample.Fonts.Native": "Sample.Fonts",
            "Sample.Lgpl.Static": "Sample.Lgpl"
          }
        }
        """;

    public const string Mapping =
        """
        {
          "mainExecutable": {
            "components": ["Paneport", "Sample.Runtime", "Sample.Platform"],
            "note": "The executable."
          },
          "files": [
            { "path": "Info.plist", "components": ["Paneport"], "note": "The build writes it." },
            { "path": "PkgInfo", "components": ["Paneport"], "note": "The build writes it." },
            { "path": "Sample.App.*", "components": ["Paneport"], "note": "Our assembly." },
            { "path": "Ui.*", "components": ["Sample.Ui"], "note": "The library." },
            { "path": "Graphics.dll", "components": ["Sample.Graphics"], "note": "Its assembly." },
            { "path": "Frameworks/libgraphics.framework/**", "components": ["Sample.Graphics"], "note": "Its library." },
            { "path": "Apache.*", "components": ["Sample.Apache"], "note": "Its assembly." },
            { "path": "SteamKit2.*", "components": ["SteamKit2"], "note": "Its assembly." },
            { "path": "System.*", "components": ["Sample.Runtime"], "note": "The runtime's assemblies." },
            { "path": "data/**", "components": ["Sample.Runtime"], "note": "The runtime's data." },
            { "path": "Platform.dll", "components": ["Sample.Platform"], "note": "The bindings." },
            { "path": "Frameworks/libsamplelgpl.framework/**", "components": ["Sample.Lgpl"], "note": "A library." },
            { "path": "Frameworks/libfonts.framework/**", "components": ["Sample.Fonts"], "note": "A library." },
            { "path": "Unused/**", "components": ["Paneport"], "note": "A rule that matches nothing." }
          ],
          "components": {
            "Sample.Ui": { "texts": [{ "package": "Sample.Ui", "license": "MIT" }] },
            "Sample.Graphics": {
              "texts": [
                { "package": "Sample.Graphics", "file": "LICENSE.txt" },
                { "package": "Sample.Graphics.Native", "file": "THIRD-PARTY-NOTICES.txt" }
              ]
            },
            "Sample.Apache": {
              "texts": [
                { "package": "Sample.Apache", "file": "LICENSE.txt" },
                { "package": "Sample.Apache", "file": "NOTICE.txt" }
              ]
            },
            "SteamKit2": { "texts": [{ "package": "SteamKit2", "file": "license.txt" }] },
            "Sample.Fonts": {
              "texts": [{ "package": "Sample.Fonts.Native", "file": "LICENSE.TXT" }],
              "credit": "Portions of this software are copyright (c) 2026 The Sample Font Project. All rights reserved.",
              "note": "The FTL asks for this credit."
            },
            "Sample.Runtime": {
              "texts": [
                { "runtimePack": "Sample.Runtime.App", "file": "LICENSE.TXT" },
                { "runtimePack": "Sample.Runtime.App", "file": "THIRD-PARTY-NOTICES.TXT" }
              ]
            },
            "Sample.Platform": { "texts": [{ "runtimePack": "Sample.Platform", "file": "LICENSE" }] },
            "Sample.Lgpl": { "texts": [{ "repository": "external/Sample-lgpl/COPYING.LIB" }] }
          }
        }
        """;

    public const string Mit =
        """
        MIT License

        Copyright (c) 2026 The Sample Template Holder

        Permission is hereby granted, free of charge, to any person obtaining a copy of this software.

        THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND.
        """;

    public const string Credit = "Portions of this software are copyright (c) 2026 The Sample Font Project. All rights reserved.";

    private static readonly string[] s_appFiles =
    [
        "Sample",
        "Sample.App.dll",
        "PkgInfo",
        "Ui.Core.dll",
        "Ui.Core.aotdata.arm64",
        "Graphics.dll",
        "Frameworks/libgraphics.framework/libgraphics",
        "Frameworks/libgraphics.framework/Info.plist",
        "Apache.Protocol.dll",
        "SteamKit2.dll",
        "System.Runtime.dll",
        "data/icudt.dat",
        "Platform.dll",
        "Frameworks/libsamplelgpl.framework/libsamplelgpl",
        "Frameworks/libfonts.framework/libfonts",
    ];

    private Fixture(string root)
    {
        Root = root;
    }

    public string Root { get; }

    public string Repository => Path.Combine(Root, "repo");

    public string App => Path.Combine(Repository, "src", "Sample", "bin", "Sample.app");

    public string Out => Path.Combine(Repository, "build", "notices");

    public string Packages => Path.Combine(Root, "packages");

    public string MappingText { get; set; } = Mapping.ReplaceLineEndings("\n");

    public List<LockedPackage> Locked { get; } =
    [
        new("Sample.Ui", "1.0.0"),
        new("Sample.Ui.Extra", "1.0.0"),
        new("Sample.Graphics", "2.0.0"),
        new("Sample.Graphics.Native", "2.0.0"),
        new("Sample.Apache", "3.0.0"),
        new("SteamKit2", "4.0.0"),
        new("Sample.Fonts.Native", "5.0.0"),
    ];

    public List<RuntimePack> Packs { get; } = [];

    public static Fixture Create(string parent, string name)
    {
        var fixture = new Fixture(Path.Combine(parent, name));
        fixture.Packs.Add(new RuntimePack("Sample.Runtime.App", "Sample.Runtime.Pack", "6.0.0", fixture.Pack("Sample.Runtime.Pack", "6.0.0")));
        fixture.Packs.Add(new RuntimePack("Sample.Platform", "Sample.Platform.Pack", "7.0.0", fixture.Pack("Sample.Platform.Pack", "7.0.0")));
        fixture.Package("Sample.Ui", "1.0.0", "expression", "MIT", "Copyright 2020 Sample UI Authors");
        fixture.Package("Sample.Ui.Extra", "1.0.0", "expression", "MIT", "Copyright 2020 Sample UI Authors");
        fixture.Package("Sample.Graphics", "2.0.0", "file", "LICENSE.txt", null, ("LICENSE.txt", "Sample graphics license text."));
        fixture.Package(
            "Sample.Graphics.Native",
            "2.0.0",
            "expression",
            "MIT",
            "Copyright 2021 Sample Graphics Authors",
            ("THIRD-PARTY-NOTICES.txt", "Sample graphics third-party notices."));
        fixture.Package(
            "Sample.Apache",
            "3.0.0",
            "expression",
            "Apache-2.0",
            null,
            ("LICENSE.txt", "Sample Apache license text."),
            ("NOTICE.txt", "Sample Apache NOTICE text."));
        fixture.Package("SteamKit2", "4.0.0", "expression", "LGPL-2.1-only", null, ("license.txt", "Sample LGPL license text."));
        fixture.Package("Sample.Fonts.Native", "5.0.0", "expression", "FTL", null, ("LICENSE.TXT", "Sample font license text."));
        WriteFile(Path.Combine(fixture.Packs[0].Folder, "LICENSE.TXT"), "Sample runtime license text.\r\nSecond line.\r\n");
        WriteFile(Path.Combine(fixture.Packs[0].Folder, "THIRD-PARTY-NOTICES.TXT"), "Sample runtime notices.");
        WriteFile(Path.Combine(fixture.Packs[1].Folder, "LICENSE"), "Sample platform license text.");
        WriteFile(Path.Combine(fixture.Repository, "external", "Sample-lgpl", "COPYING.LIB"), "Sample LGPL library text.");
        foreach (var file in s_appFiles)
        {
            WriteFile(Path.Combine(fixture.App, file), $"sample content of {file}");
        }

        fixture.WriteInfo("Sample", binary: true);
        return fixture;
    }

    public static string DisplayName => "Sampl" + (char)0x00E9 + " App";

    public void WriteInfo(string executable, bool binary)
    {
        (string Key, string Value)[] entries =
        [
            ("CFBundleExecutable", executable),
            ("CFBundleDisplayName", DisplayName),
            ("CFBundleShortVersionString", "1.2.3"),
            ("CFBundleVersion", "45"),
            ("CFBundleIdentifier", "org.example.sample"),
        ];
        var path = Path.Combine(App, "Info.plist");
        if (binary)
        {
            File.WriteAllBytes(path, BinaryPropertyListWriter.Write(entries));
            return;
        }

        var xml = new StringBuilder("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\">\n<dict>\n");
        foreach (var (key, value) in entries)
        {
            xml.Append(CultureInfo.InvariantCulture, $"  <key>{key}</key>\n  <string>{value}</string>\n");
        }

        xml.Append("  <key>UIDeviceFamily</key>\n  <array><integer>1</integer></array>\n</dict>\n</plist>\n");
        File.WriteAllText(path, xml.ToString());
    }

    public void Package(string id, string version, string licenseType, string license, string? copyright, params (string Name, string Text)[] files)
    {
        var folder = Path.Combine(Packages, id.ToLowerInvariant(), version);
        var copyrightElement = copyright is null ? string.Empty : $"\n    <copyright>{copyright}</copyright>";
        WriteFile(
            Path.Combine(folder, $"{id.ToLowerInvariant()}.nuspec"),
            $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
              <metadata>
                <id>{id}</id>
                <version>{version}</version>
                <license type="{licenseType}">{license}</license>{copyrightElement}
              </metadata>
            </package>
            """);
        foreach (var (name, text) in files)
        {
            WriteFile(Path.Combine(folder, name), text);
        }
    }

    public string PackageFile(string id, string version, string file) =>
        Path.Combine(Packages, id.ToLowerInvariant(), version, file);

    public static void WriteFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public Result Generate(Mode mode) => Notices.Generate(
        Bundle.Read(App),
        new BuildInputs("packages.lock.json", Packages, Locked, Packs),
        new Configuration(
            Repository,
            global::Register.Parse(Register),
            LicenseMap.Parse(Map),
            global::Mapping.Parse(MappingText),
            MitTemplate.Validate(Mit)),
        mode);

    // Every file under the fixture's folder, for the check that the tool writes nowhere else.
    public List<string> Snapshot() =>
        [.. Directory.EnumerateFiles(Root, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 })
            .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)];

    private string Pack(string id, string version) => Path.Combine(Root, "packs", id, version);
}

internal static partial class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("Self-test of tools/GenerateNotices.cs, with fixtures that it generates in a temporary folder.");
        var tally = new Tally();
        var root = Path.Combine(Path.GetTempPath(), $"GenerateNotices-{Guid.NewGuid():N}");
        try
        {
            Readers(tally);
            FullyRegistered(tally, root);
            Failures(tally, root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
    }

    private static void Readers(Tally tally)
    {
        Console.WriteLine("Readers:");
        (string, string)[] entries = [("CFBundleExecutable", "Sample"), ("CFBundleDisplayName", Fixture.DisplayName)];
        var binary = BinaryPropertyListWriter.Write(entries);
        var values = PropertyList.TopLevelStrings(binary);
        tally.Expect(
            values.GetValueOrDefault("CFBundleExecutable") == "Sample"
                && values.GetValueOrDefault("CFBundleDisplayName") == Fixture.DisplayName,
            "reads the ASCII and UTF-16 strings of a binary property list");
        tally.Expect(Rejects(() => PropertyList.TopLevelStrings(binary[..^10])), "rejects a truncated binary property list");
        var damaged = binary.ToArray();
        damaged[^1] = 0xFF;
        tally.Expect(Rejects(() => PropertyList.TopLevelStrings(damaged)), "rejects an offset table outside the file");
        var xml = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><plist><dict><key>CFBundleExecutable</key><string>Sample</string>"
                + "<key>UIDeviceFamily</key><array><integer>1</integer></array></dict></plist>");
        tally.Expect(
            PropertyList.TopLevelStrings(xml).SequenceEqual([new KeyValuePair<string, string>("CFBundleExecutable", "Sample")]),
            "reads the strings of an XML property list and skips other values");
        tally.Expect(
            Rejects(() => PropertyList.TopLevelStrings("<plist><dict><key>A</key></dict></plist>"u8.ToArray())),
            "rejects an XML dictionary with a key and no value");

        tally.Expect(
            Matches("Frameworks/x.framework/**", "Frameworks/x.framework/x", "Frameworks/x.framework/a/b")
                && MatchesNone("Frameworks/x.framework/**", "Frameworks/x.framework", "Frameworks/y.framework/x"),
            "\"dir/**\" matches everything below dir, and nothing else");
        tally.Expect(
            Matches("Avalonia.*", "Avalonia.Base.dll", "Avalonia.Base.aotdata.arm64")
                && MatchesNone("Avalonia.*", "a/Avalonia.dll", "Avalonia"),
            "\"*\" matches within one part of a path");
        tally.Expect(
            Matches("**/x.txt", "x.txt", "a/x.txt", "a/b/x.txt") && Matches("a/**/b", "a/b", "a/c/d/b")
                && Matches("icudt?.dat", "icudt1.dat"),
            "\"**/\" matches any number of folders, and \"?\" one character");
        string[] badPatterns = ["/abs", "a/../b", "a**", "a\\b", "a//b"];
        tally.Expect(
            badPatterns.All(pattern => Rejects(() => Glob.ToRegex(pattern))),
            "rejects absolute paths, \"..\", a partial \"**\", backslashes and empty parts");

        string[] broken =
        [
            Fixture.Mapping.Replace("\"mainExecutable\"", "\"main\"", StringComparison.Ordinal),
            Fixture.Mapping.Replace("\"note\": \"Our assembly.\"", "\"why\": \"Our assembly.\"", StringComparison.Ordinal),
            Fixture.Mapping.Replace("\"components\": [\"Sample.Ui\"]", "\"components\": []", StringComparison.Ordinal),
            Fixture.Mapping.Replace("\"file\": \"LICENSE.TXT\"", "\"file\": \"../LICENSE.TXT\"", StringComparison.Ordinal),
            Fixture.Mapping.Replace("{ \"package\": \"Sample.Ui\", \"license\": \"MIT\" }", "{ \"package\": \"Sample.Ui\" }", StringComparison.Ordinal),
            Fixture.Mapping.Replace("\"Sample.Lgpl\": {", "\"Paneport\": {", StringComparison.Ordinal),
            Fixture.Mapping.Replace("\"path\": \"Unused/**\"", "\"path\": \"Unused/**/../x\"", StringComparison.Ordinal),
            "[]",
        ];
        tally.Expect(
            broken.All(text => Rejects(() => Mapping.Parse(text))) && Mapping.Parse(Fixture.Mapping).Files.Count == 14,
            "reads the mapping, and rejects an unknown key, a rule without a note or components, a path that leaves its "
                + "folder, a text without its file or license, texts for Paneport, a bad pattern and a mapping that is no object");

        var query =
            """
            {
              "Properties": { "NuGetPackageRoot": "/packages/" },
              "Items": { "ResolvedRuntimePack": [
                { "Identity": "A.Runtime", "FrameworkName": "A.App", "NuGetPackageId": "A.Runtime", "NuGetPackageVersion": "1.0.0", "PackageDirectory": "/packs/a" },
                { "Identity": "B.Runtime", "FrameworkName": "B.App", "NuGetPackageId": "B.Runtime", "NuGetPackageVersion": "2.0.0" }
              ] }
            }
            """;
        var (packageRoot, packs) = BuildQuery.ParseQuery(query);
        tally.Expect(
            packageRoot == "/packages/" && packs.Count == 1 && packs[0] == new RuntimePack("A.App", "A.Runtime", "1.0.0", "/packs/a"),
            "reads NuGet's package folder and the resolved runtime packs from MSBuild's answer, without unresolved ones");
        tally.Expect(
            Rejects(() => BuildQuery.ParseQuery(query.Replace("\"B.App\"", "\"A.App\"", StringComparison.Ordinal)
                .Replace("\"2.0.0\" }", "\"2.0.0\", \"PackageDirectory\": \"/packs/b\" }", StringComparison.Ordinal)))
                && Rejects(() => BuildQuery.ParseQuery("{ \"Items\": {} }")),
            "rejects two runtime packs for one framework, and an answer without NuGet's package folder");
        var locked = LockFiles.Packages(
            """
            {
              "version": 2,
              "dependencies": {
                "net10.0-ios": {
                  "Sample.Direct": { "type": "Direct", "requested": "[1.0.0, )", "resolved": "1.0.0" },
                  "Sample.Transitive": { "type": "Transitive", "resolved": "2.0.0" },
                  "sample.project": { "type": "Project" }
                },
                "net10.0-ios/ios-arm64": { "Sample.Native": { "type": "Transitive", "resolved": "3.0.0" } }
              }
            }
            """);
        tally.Expect(
            locked.SequenceEqual([new("Sample.Direct", "1.0.0"), new("Sample.Native", "3.0.0"), new("Sample.Transitive", "2.0.0")]),
            "reads the packages of every target and runtime of a lock file, without the projects");
        tally.Expect(
            Spdx.Licenses("MIT OR (Apache-2.0 WITH LLVM-exception)").SequenceEqual(["MIT", "Apache-2.0"])
                && Spdx.Mentions("MIT. Its native packages", "MIT") && !Spdx.Mentions("MIT-Modern-Variant", "MIT")
                && Spdx.Mentions("FTL or GPL-2.0", "FTL") && !Spdx.Mentions("LGPL-2.1-only", "GPL-2.1-only"),
            "reads the licenses of an SPDX expression, and finds a license in a register row only as a whole name");
        tally.Expect(
            Rejects(() => MitTemplate.Validate("Permission is hereby granted")) && Rejects(() => MitTemplate.Validate("Copyright 2026")),
            "rejects an MIT text without its copyright line or its permission");
    }

    private static void FullyRegistered(Tally tally, string root)
    {
        Console.WriteLine("A fully registered bundle:");
        var fixture = Fixture.Create(root, "registered");
        var before = fixture.Snapshot();
        var result = fixture.Generate(Mode.Ios);
        tally.Expect(result.Failures.Count == 0 && result.Warnings.Count == 0 && result.Outputs is not null, "passes in ios mode");
        tally.Expect(fixture.Generate(Mode.Release).Failures.Count == 0, "passes in release mode");
        string[] expected =
            ["Sample.Apache", "Sample.Fonts", "Sample.Graphics", "Sample.Lgpl", "Sample.Platform", "Sample.Runtime", "Sample.Ui", "SteamKit2"];
        tally.Expect(result.Components.Select(component => component.Name).SequenceEqual(expected), "lists each component once, in name order");
        var main = result.Coverage.Single(attribution => attribution.File == "Sample").Components;
        tally.Expect(
            main.ToHashSet().SetEquals(["Paneport", "Sample.Runtime", "Sample.Platform", "Sample.Ui", "Sample.Graphics", "Sample.Apache", "SteamKit2", "Sample.Fonts"]),
            "maps the main executable to the mapping's components and to that of every package in the lock file, SteamKit2 "
                + "among them");
        tally.Expect(result.UnusedRules.SequenceEqual(["Unused/**"]), "reports a rule that matches no file");
        if (result.Outputs is not { } outputs)
        {
            return;
        }

        Output.Write(Path.Combine(fixture.App, Notices.Folder), outputs);
        Output.Write(fixture.Out, outputs);
        Output.Write(fixture.Out, outputs);
        var notices = Path.Combine(fixture.App, Notices.Folder);
        var after = fixture.Snapshot();
        var added = after.Except(before).ToList();
        tally.Expect(
            added.All(file => file.StartsWith("repo/src/Sample/bin/Sample.app/licenses/", StringComparison.Ordinal)
                || file.StartsWith("repo/build/notices/", StringComparison.Ordinal))
                && before.All(after.Contains) && added.Count == 2 * (2 + expected.Length),
            "writes only into licenses/ in the bundle and the output folder, and replaces its own earlier output there");

        var tableOrder = File.ReadAllLines(Path.Combine(notices, Notices.NoticesFile))
            .Select(line => TableRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(notices, Notices.IndexFile)));
        var indexOrder = index.RootElement.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("name").GetString()).ToList();
        var textFiles = Directory.EnumerateFiles(Path.Combine(notices, Notices.TextsFolder))
            .Select(path => Path.GetFileName(path)).Order(StringComparer.Ordinal).ToList();
        var textOrder = textFiles.Select(file => file[3..^4]).ToList();
        tally.Expect(
            tableOrder.SequenceEqual(expected) && indexOrder.SequenceEqual(expected)
                && textOrder.SequenceEqual(expected.Select(Notices.Slug))
                && textFiles.Select((file, position) => file.StartsWith($"{position + 1:00}-", StringComparison.Ordinal)).All(match => match),
            "THIRD_PARTY_NOTICES.md, the texts and the index list the same components in the same order");
        var indexTexts = index.RootElement.GetProperty("components").EnumerateArray()
            .Select(component => component.GetProperty("text").GetString()).ToList();
        tally.Expect(
            indexTexts.SequenceEqual(textFiles.Select(file => $"{Notices.TextsFolder}/{file}")),
            "the index names each component's text");

        var texts = textFiles.ToDictionary(
            file => file[3..^4],
            file => File.ReadAllText(Path.Combine(notices, Notices.TextsFolder, file)));
        var markdown = File.ReadAllText(Path.Combine(notices, Notices.NoticesFile));
        tally.Expect(
            texts["sample-ui"].Contains("Copyright 2020 Sample UI Authors", StringComparison.Ordinal)
                && texts["sample-ui"].Contains("Permission is hereby granted", StringComparison.Ordinal)
                && !texts["sample-ui"].Contains("Sample Template Holder", StringComparison.Ordinal),
            "builds the MIT text of a package without a license file from the MIT text and the package's copyright line");
        tally.Expect(
            texts["sample-graphics"].Contains("Sample graphics license text.", StringComparison.Ordinal)
                && texts["sample-graphics"].Contains("Sample graphics third-party notices.", StringComparison.Ordinal)
                && texts["sample-runtime"].Contains("Sample runtime license text.\nSecond line.\n", StringComparison.Ordinal)
                && texts["sample-runtime"].Contains("Sample runtime notices.", StringComparison.Ordinal)
                && texts["sample-lgpl"].Contains("Sample LGPL library text.", StringComparison.Ordinal),
            "takes the texts and third-party notices of packages, runtime packs and repository files, with Unix line endings");
        tally.Expect(
            texts["sample-apache"].Contains("Sample Apache NOTICE text.", StringComparison.Ordinal)
                && markdown.Contains("Sample Apache NOTICE text.", StringComparison.Ordinal),
            "keeps the Apache-2.0 NOTICE file");
        tally.Expect(
            texts["sample-fonts"].Contains(Fixture.Credit, StringComparison.Ordinal)
                && markdown.Contains(Fixture.Credit, StringComparison.Ordinal)
                && File.ReadAllText(Path.Combine(notices, Notices.IndexFile)).Contains(Fixture.Credit, StringComparison.Ordinal),
            "keeps the FreeType credit in all three outputs");
        tally.Expect(
            markdown.StartsWith("# Third-party notices\n\n" + Fixture.DisplayName + " 1.2.3 (45) contains", StringComparison.Ordinal)
                && markdown.Contains("Sample.Ui.Extra 1.0.0", StringComparison.Ordinal)
                && markdown.Contains("Sample.Runtime.Pack 6.0.0", StringComparison.Ordinal)
                && !markdown.Contains(fixture.Root, StringComparison.Ordinal) && !texts.Values.Any(text => text.Contains(fixture.Root, StringComparison.Ordinal)),
            "names the app, its version and each component's sources, and never a local path");

        var xmlFixture = Fixture.Create(root, "xml-info");
        xmlFixture.WriteInfo("Sample", binary: false);
        tally.Expect(xmlFixture.Generate(Mode.Release).Failures.Count == 0, "reads an XML Info.plist as well as a binary one");

        File.WriteAllText(Path.Combine(fixture.App, Notices.Folder, Notices.IndexFile), "{}");
        tally.Expect(
            Throws(() => Output.Write(Path.Combine(fixture.App, Notices.Folder), outputs))
                && File.Exists(Path.Combine(fixture.App, Notices.Folder, Notices.NoticesFile)),
            "refuses to replace a licenses/ folder that it did not write, and leaves it as it is");
        tally.Expect(
            Rejects(() => Output.EnsureBuildOutput(fixture.Repository, Path.Combine(fixture.Repository, "docs", "notices"), "--out"))
                && Rejects(() => Output.EnsureBuildOutput(fixture.Repository, fixture.Repository, "--out"))
                && !Rejects(() => Output.EnsureBuildOutput(fixture.Repository, fixture.Out, "--out"))
                && !Rejects(() => Output.EnsureBuildOutput(fixture.Repository, fixture.App, "--app"))
                && !Rejects(() => Output.EnsureBuildOutput(fixture.Repository, Path.Combine(fixture.Root, "elsewhere"), "--out")),
            "writes into build/, bin/, obj/ or outside the repository, never elsewhere in it");
    }

    private static void Failures(Tally tally, string root)
    {
        Console.WriteLine("Each failure names what fails:");
        var unattributed = Fixture.Create(root, "unattributed");
        Fixture.WriteFile(Path.Combine(unattributed.App, "Unknown.dll"), "unknown");
        Fixture.WriteFile(Path.Combine(unattributed.App, ".hidden"), "hidden");
        var ios = unattributed.Generate(Mode.Ios);
        var release = unattributed.Generate(Mode.Release);
        tally.Expect(
            ios.Failures.Count == 0 && ios.Outputs is not null && Names(ios.Warnings, "Unknown.dll") && Names(ios.Warnings, ".hidden"),
            "an unattributed file, hidden ones too, is a warning in ios mode, and the notices are still written");
        tally.Expect(
            release.Outputs is null && Names(release.Failures, "Unknown.dll") && Names(release.Failures, ".hidden"),
            "an unattributed file fails in release mode, and nothing is written");

        foreach (var (component, word, description) in new[]
        {
            ("Sample.Missing", "not in the register", "a component that the register lacks fails, named"),
            ("Sample.Review", "Under review", "a component that is Under review fails, named"),
            ("Sample.Forbidden", "Forbidden", "a component that is Forbidden fails, named"),
            ("Sample.Tool", "build tool", "a component whose row is a Tool row fails, named"),
        })
        {
            var fixture = Fixture.Create(root, component);
            fixture.MappingText = Fixture.Mapping.Replace(
                "{ \"path\": \"Platform.dll\", \"components\": [\"Sample.Platform\"]",
                $"{{ \"path\": \"Platform.dll\", \"components\": [\"{component}\"]",
                StringComparison.Ordinal);
            var result = fixture.Generate(Mode.Ios);
            tally.Expect(
                result.Outputs is null && result.Failures.Any(failure => failure.Contains(component, StringComparison.Ordinal)
                    && failure.Contains(word, StringComparison.Ordinal)),
                description);
        }

        Expect(tally, root, "no-texts", fixture => fixture.MappingText = fixture.MappingText.Replace(
            "\"Sample.Ui\": { \"texts\": [{ \"package\": \"Sample.Ui\", \"license\": \"MIT\" }] },",
            string.Empty,
            StringComparison.Ordinal), ["missing license text of Sample.Ui"], "a component without texts fails");
        Expect(tally, root, "no-file", fixture => File.Delete(fixture.PackageFile("Sample.Graphics.Native", "2.0.0", "THIRD-PARTY-NOTICES.txt")),
            ["missing license text of Sample.Graphics", "THIRD-PARTY-NOTICES.txt"], "a missing license file fails, named");
        Expect(tally, root, "no-copyright", fixture => fixture.Package("Sample.Ui", "1.0.0", "expression", "MIT", null),
            ["missing license text of Sample.Ui", "copyright"], "an MIT package without a copyright line fails");
        Expect(tally, root, "no-mit", fixture => fixture.Package("Sample.Ui", "1.0.0", "expression", "BSD-3-Clause", "Copyright 2020 Sample"),
            ["missing license text of Sample.Ui", "does not declare the expression MIT"],
            "an MIT text for a package that declares another license fails");
        Expect(tally, root, "no-pack", fixture => fixture.Packs.RemoveAt(1),
            ["missing license text of Sample.Platform", "no runtime pack for Sample.Platform"], "a runtime pack that the build lacks fails");
        Expect(tally, root, "not-locked", fixture => fixture.Locked.RemoveAll(package => package.Id == "SteamKit2"),
            ["missing license text of SteamKit2", "SteamKit2 is not in"], "a package that the lock file lacks fails");
        Expect(tally, root, "not-restored", fixture => Directory.Delete(Path.GetDirectoryName(fixture.PackageFile("Sample.Ui.Extra", "1.0.0", "x"))!, recursive: true),
            ["Sample.Ui.Extra 1.0.0 is not in NuGet's package folder"], "a package that is not restored fails");
        Expect(tally, root, "unmapped", fixture => fixture.Locked.Add(new LockedPackage("Sample.Unmapped", "1.0.0")),
            ["Sample.Unmapped", "no row in eng/license-map.json"], "a package of the lock file that the license map lacks fails");
        Expect(tally, root, "static-lgpl", fixture =>
            {
                fixture.Locked.Add(new LockedPackage("Sample.Lgpl.Static", "1.0.0"));
                fixture.Package("Sample.Lgpl.Static", "1.0.0", "expression", "LGPL-2.1-or-later", null);
            },
            ["Sample.Lgpl", "main executable Sample", "only for SteamKit2"], "an LGPL component other than SteamKit2 in the main executable fails");
        Expect(tally, root, "no-notice", fixture => fixture.MappingText = fixture.MappingText.Replace(
            ",\n        { \"package\": \"Sample.Apache\", \"file\": \"NOTICE.txt\" }",
            string.Empty,
            StringComparison.Ordinal), ["Sample.Apache leaves out the NOTICE file NOTICE.txt"], "an Apache-2.0 NOTICE file left out fails");
        Expect(tally, root, "no-credit", fixture => fixture.MappingText = fixture.MappingText.Replace(
            $"\"credit\": \"{Fixture.Credit}\",",
            string.Empty,
            StringComparison.Ordinal), ["Sample.Fonts is under the FTL"], "an FTL component without its credit fails");
        Expect(tally, root, "other-license", fixture => fixture.Package(
                "Sample.Graphics.Native", "2.0.0", "expression", "BSD-3-Clause", null, ("THIRD-PARTY-NOTICES.txt", "Notices.")),
            ["Sample.Graphics.Native 2.0.0 declares BSD-3-Clause", "register row Sample.Graphics"], "a package whose declared license its register row lacks fails");
        Expect(tally, root, "no-executable", fixture => fixture.WriteInfo("Missing", binary: true),
            ["executable Missing"], "an executable that the bundle lacks fails");
    }

    private static void Expect(Tally tally, string root, string name, Action<Fixture> change, string[] words, string description)
    {
        var fixture = Fixture.Create(root, name);
        change(fixture);
        var result = fixture.Generate(Mode.Ios);
        tally.Expect(
            result.Outputs is null
                && result.Failures.Any(failure => words.All(word => failure.Contains(word, StringComparison.Ordinal))),
            description);
    }

    private static bool Names(IEnumerable<string> messages, string file) =>
        messages.Any(message => message.Contains($": {file} is attributed to no component", StringComparison.Ordinal));

    private static bool Matches(string pattern, params string[] paths) => paths.All(Glob.ToRegex(pattern).IsMatch);

    private static bool MatchesNone(string pattern, params string[] paths) =>
        !paths.Any(Glob.ToRegex(pattern).IsMatch);

    private static bool Rejects(Action action)
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

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (IOException)
        {
            return true;
        }
    }

    [GeneratedRegex(@"^\| \d+ \| (.+?) \| ")]
    private static partial Regex TableRow();
}
