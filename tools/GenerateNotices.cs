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
// are read by the files in tools/Shared/, which tools/CheckLicenses.cs includes too. A text that no
// package ships comes from raw.githubusercontent.com at a full commit: the ios job fetches it into
// build/upstream/ and checks its SHA-256, and the tool, which never uses the network, checks the
// hash again when it reads the file.
//
//   dotnet run tools/GenerateNotices.cs -- --mode ios|release --project <app.csproj> --app <bundle.app>
//       [--out <folder>]                                    generate the notices of a built app
//   dotnet run tools/GenerateNotices.cs -- --list-upstream  print "<sha256> <url>" for each text to fetch
//   dotnet run tools/GenerateNotices.cs -- --self-test      check every rule on generated fixtures
//
// Both modes fail on a component that the register lacks or marks Under review, Forbidden or Tool;
// on a component without a license text, or without the full terms of LGPL-2.1, Apache-2.0 or
// Unicode-3.0 when its row or its packages name them; on a package whose declared license its
// register row does not name; on an Apache-2.0 NOTICE file left out; on an FTL component without
// its credit; and on an LGPL component other than SteamKit2 in the main executable (C-0003 section
// 3). A file that the mapping does not attribute, and a text that the mapping lists as having no
// source yet, are warnings in ios mode, the ios job's, and failures in release mode. After a
// failure nothing is written. Findings name the file, component or package, never a license text.
// Exit codes: 0 the notices are written, 1 a failure, 2 usage or an input that cannot be read.
// docs/design/release-engineering.md describes the rules and the outputs.

#:include Shared/Register.cs
#:include Shared/LicenseMap.cs
#:property PublishAot=false

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

var actions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
try
{
    var root = Directory.GetCurrentDirectory();
    if (args is ["--self-test"])
    {
        return SelfTest.Run();
    }

    if (args is ["--list-upstream"])
    {
        return Cli.ListUpstream(root, Console.Out);
    }

    return Options.Parse(args) is { } options
        ? Cli.Run(options, root, project => BuildQuery.Msbuild(root, project), Console.Out, actions)
        : Cli.Usage();
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    return Cli.Fail(Console.Error, actions, exception);
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
    // The error that ends a run, whose message may name a file of the bundle, so that under GitHub
    // Actions it is printed while workflow commands are stopped. Returns the exit code 2.
    public static int Fail(TextWriter error, bool actions, Exception exception)
    {
        Report.Untrusted(error, actions, [$"error: {exception.Message}"]);
        return 2;
    }

    public static int Usage()
    {
        Console.Error.WriteLine(
            "usage: dotnet run tools/GenerateNotices.cs -- --mode ios|release --project <app.csproj> "
                + "--app <bundle.app> [--out <folder>] | --list-upstream | --self-test");
        return 2;
    }

    // The query gives MSBuild's answer for the app's project; the self-test passes one of its own.
    public static int Run(Options options, string root, Func<string, string> query, TextWriter output, bool annotate)
    {
        if (!File.Exists(Path.Combine(root, Register.FilePath)))
        {
            throw new InvalidDataException($"run the tool from the repository root, where {Register.FilePath} is.");
        }

        var configuration = Configuration.Load(root);
        var app = Output.EnsureBuildOutput(root, options.App, "--app");
        if (!Directory.Exists(app))
        {
            throw new InvalidDataException($"{options.App} is not an app bundle.");
        }

        var notices = Path.Combine(app, Notices.Folder);
        var copy = options.Out is { } given ? Output.EnsureBuildOutput(root, given, "--out") : null;

        // Both destinations are checked before either is written.
        Output.Check(notices);
        if (copy is not null)
        {
            Output.Check(copy);
        }

        var bundle = Bundle.Read(app);
        var build = BuildQuery.Run(root, options.Project, query);
        var result = Notices.Generate(bundle, build, configuration, options.Mode);
        Report.Print(bundle, build, configuration, options.Mode, result, output, annotate);
        if (result.Outputs is not { } outputs)
        {
            return 1;
        }

        Output.Write(notices, outputs);
        var written = $"{bundle.Name}/{Notices.Folder}/";
        if (copy is not null)
        {
            Output.Write(copy, outputs);
            written += $" and {Path.GetRelativePath(root, copy).Replace('\\', '/')}/";
        }

        Report.Untrusted(
            output,
            annotate,
            [$"Wrote {Notices.NoticesFile}, {Notices.IndexFile} and {outputs.Texts.Count} texts into {written}."]);
        return 0;
    }

    // The texts that the ios job fetches before the notices step: one line "<sha256> <url>" each.
    public static int ListUpstream(string root, TextWriter output)
    {
        var path = Path.Combine(root, Mapping.FilePath);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"run the tool from the repository root, where {Mapping.FilePath} is.");
        }

        foreach (var (url, sha256) in Mapping.Parse(File.ReadAllText(path)).Upstream())
        {
            output.WriteLine($"{sha256} {url}");
        }

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
        return CopyrightLine().IsMatch(normalized)
            && normalized.Contains("Permission is hereby granted", StringComparison.Ordinal)
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
    public static BuildInputs Run(string root, string project, Func<string, string> query)
    {
        var path = Path.GetFullPath(project, root);
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{project} does not exist.");
        }

        var (packageRoot, packs) = ParseQuery(query(path));
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
                        Required(item, "FrameworkName"),
                        Required(item, "NuGetPackageId"),
                        Required(item, "NuGetPackageVersion"),
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

    private static string Required(JsonElement item, string name) =>
        Metadata(item, name) ?? throw new InvalidDataException($"a runtime pack has no {name}.");

    // MSBuild's answer for the project, from the repository root, where global.json picks the SDK.
    public static string Msbuild(string root, string project) => Dotnet(
        root,
        "msbuild",
        project,
        "-nologo",
        "-nodeReuse:false",
        "-t:ResolveFrameworkReferences",
        "-getItem:ResolvedRuntimePack",
        "-getProperty:NuGetPackageRoot",
        "-p:Configuration=Release");

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
    // NuGet uses packages.<project>.lock.json in place of packages.lock.json when it exists, with each
    // space of the project's name made an underscore.
    public static string Find(string project)
    {
        var folder = Path.GetDirectoryName(project) ?? ".";
        var name = Path.GetFileNameWithoutExtension(project).Replace(' ', '_');
        var named = Path.Combine(folder, $"packages.{name}.lock.json");
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
    // the solution's own projects. A package at two versions fails, because its texts would come
    // from one of them only.
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

                    var version = entry.Value.TryGetProperty("resolved", out var resolved)
                        ? resolved.GetString()
                        : null;
                    if (string.IsNullOrEmpty(version))
                    {
                        throw new InvalidDataException($"the lock file gives {entry.Name} no resolved version.");
                    }

                    var package = new LockedPackage(entry.Name, version);
                    var same = packages.FirstOrDefault(
                        other => string.Equals(other.Id, package.Id, StringComparison.OrdinalIgnoreCase));
                    if (same is null)
                    {
                        packages.Add(package);
                    }
                    else if (!string.Equals(same.Version, package.Version, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"the lock file gives {package.Id} two versions, {same.Version} and {package.Version}; the "
                                + "notices take a package's texts from one version, so the app must use one.");
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

    // A file that no package ships, at a full commit on raw.githubusercontent.com, which the ios job
    // fetches into build/upstream/ and checks by its SHA-256 before the notices step.
    Upstream,
}

// Lines First to Last of a file, counted from 1: a notice that lives only in a source file's header.
internal readonly record struct LineRange(int First, int Last);

// Name is the package, the runtime pack's framework, or the SHA-256 of an upstream file; Value is the
// file, the license, the repository path or the upstream URL. Terms marks an upstream file that only
// carries the full terms of a license, by the license's SPDX identifier.
internal sealed record TextSource(
    SourceKind Kind,
    string Name,
    string Value,
    LineRange? Lines = null,
    string? Terms = null);

// A text that a component needs and that no source provides yet, with the reason. A guarded one
// counts only while its assembly in the bundle defines its type (AssemblyTypes).
internal sealed record MissingText(string Text, string Note, TypeGuard? Guard = null);

internal sealed record ComponentTexts(
    IReadOnlyList<TextSource> Texts,
    string? Credit,
    IReadOnlyList<MissingText> Missing);

internal sealed record FileRule(string Pattern, IReadOnlyList<string> Components, Regex Matcher);

internal sealed record PathPattern(string Pattern, Regex Matcher);

// eng/release/bundle-components.json: the reviewed mapping from the files of an app bundle to
// Paneport or to components of the register, and where each component's license texts come from.
// MainExecutablePaths are the files that, like the main executable, hold what the build makes of
// the whole app, such as the ahead-of-time data of generic instances of every assembly.
internal sealed record Mapping(
    IReadOnlyList<string> MainExecutable,
    IReadOnlyList<PathPattern> MainExecutablePaths,
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
            var main = Object(root["mainExecutable"], "mainExecutable", ["components", "note"], ["paths"]);
            Note(main["note"], "mainExecutable");
            var mainPaths = new List<PathPattern>();
            if (main.TryGetValue("paths", out var paths))
            {
                if (paths.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidDataException("mainExecutable: \"paths\" must be an array of paths.");
                }

                foreach (var path in paths.EnumerateArray().Select(path => Text(path, "mainExecutable: a path")))
                {
                    mainPaths.Add(new PathPattern(path, Glob.ToRegex(path)));
                }
            }

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

                var entry = Object(property.Value, property.Name, ["texts"], ["credit", "missing", "note"]);
                if (entry["texts"].ValueKind != JsonValueKind.Array || entry["texts"].GetArrayLength() == 0)
                {
                    throw new InvalidDataException($"{property.Name}: \"texts\" must list at least one text.");
                }

                var texts = entry["texts"].EnumerateArray().Select(text => Source(text, property.Name)).ToList();
                var credit = entry.TryGetValue("credit", out var value)
                    ? Text(value, $"{property.Name}: \"credit\"")
                    : null;
                if (entry.TryGetValue("note", out var note))
                {
                    Note(note, property.Name);
                }

                var missing = entry.TryGetValue("missing", out var gaps) ? Missing(gaps, property.Name) : [];
                if (!components.TryAdd(property.Name, new ComponentTexts(texts, credit, missing)))
                {
                    throw new InvalidDataException($"\"components\" lists {property.Name} twice.");
                }
            }

            var mapping = new Mapping(Names(main["components"], "mainExecutable"), mainPaths, files, components);
            var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var source in mapping.Components.Values.SelectMany(entry => entry.Texts)
                .Where(source => source.Kind == SourceKind.Upstream))
            {
                if (hashes.TryGetValue(source.Value, out var other) && other != source.Name)
                {
                    throw new InvalidDataException($"{FilePath} gives {source.Value} two different SHA-256 hashes.");
                }

                hashes[source.Value] = source.Name;
            }

            return mapping;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }

    // Each upstream file once, by its URL, with its SHA-256.
    public List<(string Url, string Sha256)> Upstream() =>
    [
        .. Components.Values
            .SelectMany(entry => entry.Texts)
            .Where(source => source.Kind == SourceKind.Upstream)
            .Select(source => (source.Value, source.Name))
            .Distinct()
            .OrderBy(source => source.Value, StringComparer.Ordinal),
    ];

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
            ["sha256", "upstream"] or ["lines", "sha256", "upstream"] or ["sha256", "terms", "upstream"]
                or ["lines", "sha256", "terms", "upstream"] => UpstreamSource(element, where),
            _ when keys.Contains("terms", StringComparer.Ordinal) && !keys.Contains("upstream", StringComparer.Ordinal)
                => throw new InvalidDataException(
                $"{where}: only an upstream file takes \"terms\", the license whose full terms it carries."),
            _ => throw new InvalidDataException(
                $"{where} must name a package with a file or a license, a runtime pack with a file, a repository "
                    + "file, or an upstream file with its SHA-256."),
        };
    }

    private static TextSource UpstreamSource(JsonElement element, string where)
    {
        var url = Text(element.GetProperty("upstream"), where);
        UpstreamUrl.Parse(url, where);
        var sha256 = element.GetProperty("sha256") is { ValueKind: JsonValueKind.String } hash
            && hash.GetString() is { } text && UpstreamUrl.IsSha256(text)
                ? text
                : throw new InvalidDataException(
                    $"{where}: the SHA-256 of {url} must be 64 lowercase hexadecimal digits.");
        LineRange? lines = null;
        if (element.TryGetProperty("lines", out var range))
        {
            lines = range is { ValueKind: JsonValueKind.Array } && range.GetArrayLength() == 2
                && range[0].ValueKind == JsonValueKind.Number && range[1].ValueKind == JsonValueKind.Number
                && range[0].TryGetInt32(out var first) && range[1].TryGetInt32(out var last) && first >= 1
                && last >= first
                    ? new LineRange(first, last)
                    : throw new InvalidDataException(
                        $"{where}: \"lines\" of {url} must be [first, last], two line numbers from 1, the first not "
                            + "after the last.");
        }

        string? terms = null;
        if (element.TryGetProperty("terms", out var carried))
        {
            terms = carried is { ValueKind: JsonValueKind.String } && carried.GetString() is { } license
                && FullTerms.Passage(license) is not null
                    ? license
                    : throw new InvalidDataException(
                        $"{where}: \"terms\" of {url} must name a license whose full terms the tool recognizes: "
                            + $"{string.Join(", ", FullTerms.Known)}.");
        }

        return new TextSource(SourceKind.Upstream, sha256, url, lines, terms);
    }

    private static List<MissingText> Missing(JsonElement element, string component)
    {
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
        {
            throw new InvalidDataException($"{component}: \"missing\" must list at least one text.");
        }

        return
        [
            .. element.EnumerateArray().Select(gap =>
            {
                var fields = Object(gap, $"a missing text of {component}", ["note", "text"], ["whileDefined"]);
                return new MissingText(
                    Text(fields["text"], $"a missing text of {component}"),
                    Text(fields["note"], $"a missing text of {component}: \"note\""),
                    fields.TryGetValue("whileDefined", out var guard) ? TypeGuard.Parse(guard, component) : null);
            }),
        ];
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
        return path.StartsWith('/') || path.Contains('\\', StringComparison.Ordinal)
            || path.Contains(':', StringComparison.Ordinal) || path.Split('/').Any(part => part is "" or "." or "..")
                ? throw new InvalidDataException($"{where}: {path} must be a relative path below its folder.")
                : path;
    }
}

// A text that no package ships: a file on raw.githubusercontent.com at a full commit, with its
// SHA-256. The ios job fetches each one into build/upstream/<sha256> before the notices step and
// checks its hash; the tool never uses the network and checks the hash again when it reads the file.
internal static partial class UpstreamUrl
{
    public const string Prefix = "https://raw.githubusercontent.com/";
    public const string Folder = "build/upstream";
    public const string FetchStep = "Fetch the license texts that no package ships";

    // NEL, LS and PS, which end a line in C# besides CR and LF.
    private static readonly char[] s_lineTerminators = [(char)0x85, (char)0x2028, (char)0x2029];

    // The owner, repository, commit and path of an accepted URL: https on raw.githubusercontent.com
    // only, a full commit and no branch, tag or short hash, and no "..", "?", "#", "%" or backslash.
    public static (string Owner, string Repository, string Commit, string Path) Parse(string url, string where)
    {
        if (!url.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{where}: {url} must be an https URL on raw.githubusercontent.com.");
        }

        if (url.Contains("..", StringComparison.Ordinal) || url.IndexOfAny(['?', '#', '%', '\\']) >= 0)
        {
            throw new InvalidDataException(
                $"{where}: {url} must hold no \"..\", \"?\", \"#\", \"%\" or backslash.");
        }

        var parts = url[Prefix.Length..].Split('/');
        if (parts.Length < 4 || parts.Any(part => part == "." || !UrlPart().IsMatch(part)))
        {
            throw new InvalidDataException(
                $"{where}: {url} must name an owner, a repository, a commit and a path, each part of letters, digits "
                    + "and \"-._~+\".");
        }

        return FullCommit().IsMatch(parts[2])
            ? (parts[0], parts[1], parts[2], string.Join('/', parts[3..]))
            : throw new InvalidDataException(
                $"{where}: {url} must name a full commit of 40 lowercase hexadecimal digits, not a branch, a tag or "
                    + "a short hash.");
    }

    public static bool IsSha256(string text) => Sha256().IsMatch(text);

    // Lines First to Last of a text, which lie in the file's leading comment header: each line from
    // line 1 to Last is a comment or blank, so that no code ships as a notice. A range may start inside
    // a /* */ comment of the header, whose state the scan takes from line 1; a range that starts inside
    // a string or below code fails. A line inside a /* */ comment counts as a comment. A line that
    // holds NEL, LS or PS fails, because C# ends a line there too, so code could follow a "//" comment
    // on what reads here as one line; so does a line that ends in a backslash or holds the trigraph
    // ??/, which C splices with the next line, so that "*\" and "/" could close a comment. The text is
    // the lines without the blank or white-space lines at its end, and one line break; a range of
    // blank lines alone holds no notice.
    public static string Lines(string text, LineRange range, string name)
    {
        var lines = text[..^1].Split('\n');
        if (range.Last > lines.Length)
        {
            throw new InvalidDataException(
                $"the lines {range.First} to {range.Last} run past the end of {name}, which has {lines.Length} lines.");
        }

        const string Header = "a range lies within the leading comment header of a source file, so that no code "
            + "ships as a notice.";
        var inComment = false;
        for (var number = 1; number <= range.Last; number++)
        {
            var where = number < range.First
                ? $"before the range {range.First} to {range.Last}"
                : $"in the range {range.First} to {range.Last}";
            var raw = lines[number - 1];
            if (raw.IndexOfAny(s_lineTerminators) >= 0)
            {
                throw new InvalidDataException(
                    $"line {number} of {name}, {where}, holds NEL, LS or PS, which end a line in C#; {Header}");
            }

            if (raw.TrimEnd().EndsWith('\\') || raw.Contains("??/", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"line {number} of {name}, {where}, ends in a backslash or holds ??/, which C splices with the "
                        + $"next line; {Header}");
            }

            var line = raw.Trim();
            if (inComment)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                (line, inComment) = (line[(end + 2)..].TrimStart(), false);
            }

            if (line.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = line.IndexOf("*/", 2, StringComparison.Ordinal);
                (line, inComment) = end < 0 ? (string.Empty, true) : (line[(end + 2)..].TrimStart(), false);
            }

            if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"line {number} of {name}, {where}, is neither a comment nor blank; {Header}");
            }
        }

        // The last notice line keeps any white space at its end as upstream wrote it.
        var last = range.Last;
        while (last >= range.First && string.IsNullOrWhiteSpace(lines[last - 1]))
        {
            last--;
        }

        return last >= range.First
            ? string.Join('\n', lines[(range.First - 1)..last]) + "\n"
            : throw new InvalidDataException(
                $"the lines {range.First} to {range.Last} of {name} are blank, so they hold no notice.");
    }

    // \z, not $, which would also match before a final line break.
    [GeneratedRegex(@"\A[A-Za-z0-9._~+-]+\z")]
    private static partial Regex UrlPart();

    [GeneratedRegex(@"\A[0-9a-f]{40}\z")]
    private static partial Regex FullCommit();

    [GeneratedRegex(@"\A[0-9a-f]{64}\z")]
    private static partial Regex Sha256();
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
            || parts.Any(part => part is "" or "." or ".."
                || (part.Contains("**", StringComparison.Ordinal) && part != "**")))
        {
            throw new InvalidDataException(
                $"the path {pattern} must be relative to the bundle, and \"**\" must be a whole part of it.");
        }

        // \A and \z, not ^ and $, which would also match before a final line break, and "**" matches
        // line breaks too, since a file name may hold one.
        var builder = new StringBuilder(@"\A");
        for (var index = 0; index < parts.Length; index++)
        {
            var last = index == parts.Length - 1;
            if (parts[index] == "**")
            {
                builder.Append(last ? @"[\s\S]+" : "(?:[^/]+/)*");
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

        return new Regex(builder.Append(@"\z").ToString(), RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
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

            if (elements[index + 1].Name.LocalName == "string"
                && !values.TryAdd(elements[index].Value, elements[index + 1].Value))
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
        return offset >= 8 && offset < (ulong)_table
            ? (long)offset
            : throw Unreadable("an object offset is out of range");
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
// NuGet trims the license type and reads it without regard to case, and so does the tool.
internal sealed record PackageDescription(string? LicenseType, string? License, string? LicenseUrl, string? Copyright)
{
    public bool DeclaresExpression => string.Equals(LicenseType, "expression", StringComparison.OrdinalIgnoreCase);

    public bool DeclaresFile => string.Equals(LicenseType, "file", StringComparison.OrdinalIgnoreCase);

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
            var metadata = Child(XDocument.Load(reader).Root, "metadata")
                ?? throw new InvalidDataException($"the description of {id} has no metadata.");
            var license = Child(metadata, "license");
            var url = Child(metadata, "licenseUrl")?.Value.Trim();
            var copyright = Child(metadata, "copyright")?.Value.Trim();
            return new PackageDescription(
                license?.Attribute("type")?.Value.Trim(),
                license?.Value.Trim(),
                string.IsNullOrEmpty(url) ? null : url,
                string.IsNullOrEmpty(copyright) ? null : copyright);
        }
        catch (XmlException exception)
        {
            throw new InvalidDataException($"the description of {id} cannot be read: {exception.Message}");
        }
    }

    // A .nuspec puts its elements in a namespace of its schema's version, so they are found by local name.
    private static XElement? Child(XElement? parent, string name) =>
        parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name);
}

// The licenses that an SPDX expression names, and whether a register row's License text names one.
internal static partial class Spdx
{
    public static List<string> Licenses(string expression) => Named(Tokens(expression));

    // The alternatives of an expression, split at each OR outside parentheses, each with the licenses
    // that it names: "MIT OR LGPL-2.1-only" has two, and "(MIT OR LGPL-2.1-only) AND Zlib" one.
    public static List<List<string>> Alternatives(string expression)
    {
        var alternatives = new List<List<string>> { new() };
        var depth = 0;
        foreach (var token in Tokens(expression))
        {
            depth += token switch
            {
                "(" => 1,
                ")" => -1,
                _ => 0,
            };
            if (depth == 0 && token.Equals("OR", StringComparison.OrdinalIgnoreCase))
            {
                alternatives.Add([]);
            }
            else
            {
                alternatives[^1].Add(token);
            }
        }

        return [.. alternatives.Select(Named)];
    }

    public static bool IsLgpl(string license) => license.StartsWith("LGPL-", StringComparison.OrdinalIgnoreCase);

    // As a whole name: "MIT" counts in "MIT. Its packages" but not in "MIT-Modern-Variant".
    public static bool Mentions(string text, string license) =>
        Regex.IsMatch(
            text,
            $@"(?<![A-Za-z0-9.+-]){Regex.Escape(license)}(?![A-Za-z0-9+-]|\.[A-Za-z0-9])",
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(1));

    // The licenses of the expression that a register row's License text starts with, up to the first
    // period, comma, semicolon or colon that ends a phrase: "MIT" for "MIT. Its native library is
    // under LGPL-2.1", "Apache-2.0" for "Apache-2.0 WITH LLVM-exception; parts ...".
    public static List<string> Leading(string license) => Licenses(PhraseEnd().Split(license, 2)[0]);

    // The licenses among the tokens: neither an operator, a parenthesis, nor the exception after WITH.
    private static List<string> Named(List<string> tokens)
    {
        var licenses = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            switch (tokens[index].ToUpperInvariant())
            {
                case "AND" or "OR" or "(" or ")":
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

    private static List<string> Tokens(string expression) =>
        [.. Token().Matches(expression).Select(match => match.Value)];

    [GeneratedRegex(@"[()]|[^\s()]+")]
    private static partial Regex Token();

    [GeneratedRegex(@"[.,;:](?:\s|\z)")]
    private static partial Regex PhraseEnd();
}

// The licenses whose full terms a component's texts must hold when its register row or its packages
// name them, since a short notice that points to the license is not the license (C-0003 section 3).
// Each is recognized by a passage of its text, compared with runs of white space made one space.
internal static partial class FullTerms
{
    private static readonly (string License, string[] Identifiers, string Passage)[] s_terms =
    [
        (
            "Apache-2.0",
            ["Apache-2.0"],
            "TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION"),
        (
            "LGPL-2.1",
            ["LGPL-2.1", "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-2.1+"],
            "GNU LESSER GENERAL PUBLIC LICENSE TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION"),
        (
            "Unicode-3.0",
            ["Unicode-3.0"],
            "to any person obtaining a copy of data files and any associated documentation (the \"Data Files\") or "
                + "software and any associated documentation (the \"Software\") to deal in the Data Files or Software "
                + "without restriction"),
    ];

    // The identifiers whose full terms the rules recognize.
    public static IEnumerable<string> Known => s_terms.SelectMany(terms => terms.Identifiers);

    // The passage that marks the full terms of the license of this identifier, or null.
    public static string? Passage(string identifier) =>
        s_terms.FirstOrDefault(terms => terms.Identifiers.Contains(identifier, StringComparer.Ordinal)).Passage;

    // Each license among these whose full terms are needed, once, with the passage that marks them.
    public static List<(string License, string Passage)> Needed(IEnumerable<string> licenses) =>
    [
        .. s_terms
            .Where(terms => licenses.Any(
                license => terms.Identifiers.Contains(license, StringComparer.OrdinalIgnoreCase)))
            .Select(terms => (terms.License, terms.Passage)),
    ];

    public static bool Holds(string text, string passage) =>
        WhiteSpace().Replace(text, " ").Contains(passage, StringComparison.Ordinal);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();
}

internal static class TextFile
{
    // A license or notices file as text: UTF-8, or UTF-16 with a byte-order mark, with Windows line
    // endings made Unix ones and one line break at its end. Null for a missing file.
    public static string? Read(string path) =>
        File.Exists(path) ? Decode(File.ReadAllBytes(path), Path.GetFileName(path)) : null;

    // An empty text, one with a NUL character and one that is not valid UTF-8 are rejected.
    public static string Decode(byte[] bytes, string name)
    {
        string text;
        try
        {
            using var reader = new StreamReader(
                new MemoryStream(bytes),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true),
                detectEncodingFromByteOrderMarks: true);
            text = reader.ReadToEnd();
        }
        catch (DecoderFallbackException)
        {
            throw new InvalidDataException($"{name} is not UTF-8 text.");
        }

        if (text.Contains('\0', StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{name} is not text.");
        }

        return string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException($"{name} is empty.")
            : Normalize(text);
    }

    public static string Normalize(string text)
    {
        var unix = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimEnd('\n');
        return unix + "\n";
    }
}

internal sealed record Attribution(string File, IReadOnlyList<string> Components);

// What generating the notices of one app works with, which each component's steps share.
internal sealed record Context(
    string App,
    Mode Mode,
    BuildInputs Build,
    Configuration Configuration,
    List<string> Failures,
    List<string> Warnings)
{
    // The bundle's folder, where a guarded missing text reads its assembly.
    public string BundleFolder { get; init; } = string.Empty;
}

internal sealed record TextBlock(string Title, string Text);

internal sealed record ComponentNotice(
    string Name,
    RegisterRow Row,
    IReadOnlyList<string> Sources,
    string? Credit,
    IReadOnlyList<TextBlock> Texts,
    int Files);

internal sealed record NoticeOutputs(string Notices, string Index, IReadOnlyList<(string Path, string Content)> Texts);

// Unexpressed lists the packages that declare their license without an SPDX expression, for the report.
internal sealed record Result(
    IReadOnlyList<string> Failures,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<Attribution> Coverage,
    IReadOnlyList<string> MainComponents,
    IReadOnlyList<ComponentNotice> Components,
    IReadOnlyList<string> UnusedRules,
    IReadOnlyList<string> OtherLicenseFiles,
    IReadOnlyList<string> Unexpressed,
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
                failures.Add(
                    $"{app}: the package {package.Id} of {build.LockFile} has no row in {LicenseMap.FilePath}.");
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

            var mainPath = configuration.Mapping.MainExecutablePaths.FirstOrDefault(path => path.Matcher.IsMatch(file));
            if (mainPath is not null)
            {
                used.Add(mainPath.Pattern);
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

        var unexpressed = new List<string>();
        var declared = Declarations(app, build, configuration, failures, unexpressed);
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
            var licenses = Licenses(row, declared, name);
            var context = new Context(app, mode, build, configuration, failures, warnings)
            {
                BundleFolder = bundle.Folder,
            };
            if (Notice(context, name, row, licenses, count) is { } notice)
            {
                components.Add(notice);
            }
        }

        // Whether a component in the main executable is under the LGPL comes from the expression that its
        // register row starts with, which records the license chosen among alternatives, and from the
        // expressions that its packages declare, where an OR lets it out of the LGPL only through an
        // alternative of the licenses that the row starts with; a note further on in the row does not count.
        foreach (var name in main.Where(name => name != StaticLgplComponent))
        {
            if (configuration.Register.FirstOrDefault(row => row.Component == name) is { } row
                && Lgpl(row, declared.GetValueOrDefault(name) ?? []) is { } lgpl)
            {
                failures.Add(
                    $"{app}: {name}, under {lgpl}, is compiled into the main executable {bundle.Executable}. "
                        + $"C-0003 section 3 allows that only for {StaticLgplComponent}; ship it as a library that the "
                        + "user can replace.");
            }
        }

        var unused = configuration.Mapping.MainExecutablePaths
            .Select(path => path.Pattern)
            .Concat(configuration.Mapping.Files.Select(rule => rule.Pattern))
            .Where(pattern => !used.Contains(pattern))
            .ToList();
        var others = components
            .SelectMany(component => OtherLicenseFiles(
                component.Name,
                configuration.Mapping.Components[component.Name],
                build,
                configuration))
            .ToList();
        var outputs = failures.Count == 0 ? Render(bundle, components) : null;
        return new Result(failures, warnings, coverage, main, components, unused, others, unexpressed, outputs);
    }

    // The licenses of the expression that the row starts with, and every license that the expressions of
    // the component's packages name, alternatives included, which errs on the side of a notice too many.
    private static List<string> Licenses(RegisterRow row, Dictionary<string, List<string>> declared, string name) =>
        [.. Spdx.Leading(row.License), .. (declared.GetValueOrDefault(name) ?? []).SelectMany(Spdx.Licenses)];

    // The LGPL license that the row's leading expression names, or that a declared expression names
    // without a way out, or null. A way out is an alternative of the expression's top level that names
    // at least one license, and only licenses of the row's leading expression, which the register says
    // Paneport uses; it then names no LGPL license, since the row's arm has already returned any that
    // the leading expression names. Another alternative, under GPL-2.0, AGPL-3.0, MPL-2.0 or nothing,
    // leaves the LGPL as the only terms that can be used (C-0003 sections 3 and 5).
    internal static string? Lgpl(RegisterRow row, List<string> expressions)
    {
        var leading = Spdx.Leading(row.License);
        return leading.FirstOrDefault(Spdx.IsLgpl)
            ?? expressions
                .Select(Spdx.Alternatives)
                .Where(alternatives => !alternatives.Any(alternative => alternative.Count > 0
                    && alternative.All(license => leading.Contains(license, StringComparer.Ordinal))))
                .SelectMany(alternatives => alternatives.SelectMany(alternative => alternative))
                .FirstOrDefault(Spdx.IsLgpl);
    }

    private static string? Refusal(RegisterRow row) => row.Category switch
    {
        "Under review" => "is Under review in the register, and nothing under review ships until its review is "
            + "recorded (C-0003 section 5, C-0009 section 4).",
        "Forbidden" => "is Forbidden in the register and never enters the app package (C-0003 section 5).",
        "Tool" => "is a build tool in the register, and a Tool row covers nothing that ships (C-0003 section 5).",
        _ => null,
    };

    // The component's texts, sources and credit, or null after a failure.
    private static ComponentNotice? Notice(
        Context context,
        string name,
        RegisterRow row,
        List<string> licenses,
        int files)
    {
        var (app, mode, build, configuration, failures, warnings) = context;
        var sources = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in build.Packages)
        {
            if (configuration.Map.ComponentFor(Kind.Package, package.Id) == name)
            {
                sources.Add($"{package.Id} {package.Version}");
            }
        }

        if (!configuration.Mapping.Components.TryGetValue(name, out var entry))
        {
            failures.Add($"{app}: missing license text of {name}: {Mapping.FilePath} names no texts for it.");
            return null;
        }

        var before = failures.Count;
        // A text that two sources hold alike, such as the LICENSE of two packs of one SDK, is written
        // once, under a heading that names both.
        var texts = new List<TextBlock>();
        foreach (var source in entry.Texts)
        {
            if (Text(context, name, source, sources) is not { } text)
            {
                continue;
            }

            var same = texts.FindIndex(block => block.Text == text.Text);
            if (same < 0)
            {
                texts.Add(text);
            }
            else
            {
                texts[same] = texts[same] with { Title = $"{texts[same].Title}; the same text is {text.Title}" };
            }
        }

        if (failures.Count == before)
        {
            foreach (var (license, passage) in FullTerms.Needed(licenses))
            {
                if (!texts.Any(text => FullTerms.Holds(text.Text, passage)))
                {
                    failures.Add(
                        $"{app}: {name} is under {license}, but none of its texts holds the full terms of {license}, "
                            + $"and a notice alone is not the license (C-0003 section 3). Add its full text to the "
                            + $"component's texts in {Mapping.FilePath}.");
                }
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

        // A text that the component needs and that no source provides yet shows in every run. A guarded
        // one is needed only while its assembly in this bundle defines its type, and also when the
        // assembly is missing or cannot be read, so that the check fails safe.
        foreach (var gap in entry.Missing)
        {
            var why = gap.Guard is null ? string.Empty : AssemblyTypes.Needed(context.BundleFolder, gap.Guard);
            if (why is not null)
            {
                (mode == Mode.Release ? failures : warnings).Add(
                    $"{app}: {name} needs {gap.Text}, which no source provides yet ({Mapping.FilePath}: {gap.Note})"
                        + why);
            }
        }

        return failures.Count == before
            ? new ComponentNotice(name, row, [.. sources], entry.Credit, texts, files)
            : null;
    }

    private static TextBlock? Text(Context context, string name, TextSource source, SortedSet<string> sources)
    {
        var (app, _, build, configuration, failures, _) = context;
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

                    // A component's texts come from its own packages, so that a slip in the mapping cannot
                    // write another component's license under this one.
                    var owner = configuration.Map.ComponentFor(Kind.Package, package.Id);
                    if (owner != name)
                    {
                        return Fail(
                            failures,
                            $"{app}: a text of {name} names the package {package.Id}, which {LicenseMap.FilePath} "
                                + $"gives to {owner ?? "no component"}, not to {name}.");
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
                case SourceKind.Upstream:
                    return Upstream(missing, source, configuration, failures, sources);
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

    // A file that the ios job fetched into build/upstream/, named by its SHA-256, which is checked
    // again here: a missing or changed file fails in both modes.
    private static TextBlock? Upstream(
        string missing,
        TextSource source,
        Configuration configuration,
        List<string> failures,
        SortedSet<string> sources)
    {
        var (owner, repository, commit, file) = UpstreamUrl.Parse(source.Value, Mapping.FilePath);

        // A file that only carries a license's terms, such as another project's copy of Apache-2.0, is
        // the copy's origin and not a source of the component's code.
        if (source.Terms is null)
        {
            sources.Add($"{owner}/{repository} {commit}");
        }

        var fetch = $"The ios job's step \"{UpstreamUrl.FetchStep}\" fetches it and checks its SHA-256 before the "
            + "notices step.";
        var path = Path.Combine(configuration.Root, UpstreamUrl.Folder, source.Name);
        if (!File.Exists(path))
        {
            return Fail(failures, $"{missing} {source.Value} is not in {UpstreamUrl.Folder}/. {fetch}");
        }

        var bytes = File.ReadAllBytes(path);
        var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
        if (actual != source.Name)
        {
            return Fail(
                failures,
                $"{missing} {UpstreamUrl.Folder}/{source.Name} has the SHA-256 {actual}, not the one that "
                    + $"{Mapping.FilePath} gives for {source.Value}. {fetch}");
        }

        var name = $"{file} of {owner}/{repository} at commit {commit}";
        var text = TextFile.Decode(bytes, name);
        var block = source.Lines is { } lines
            ? new TextBlock($"lines {lines.First} to {lines.Last} of {name}", UpstreamUrl.Lines(text, lines, name))
            : new TextBlock(name, text);
        if (source.Terms is not { } terms)
        {
            return block;
        }

        // Titled by the license that it carries, so that a reader does not take it for that project's code.
        return FullTerms.Passage(terms) is { } passage && FullTerms.Holds(block.Text, passage)
            ? block with { Title = $"{terms} license, copied from {block.Title}" }
            : Fail(
                failures,
                $"{missing} {Mapping.FilePath} says that {source.Value} carries the full terms of {terms}, but it "
                    + $"does not hold them.");
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
                $"{missing} the tool builds the text of MIT only, not of {source.Value}; name a file of {label} "
                    + "instead.");
        }

        if (PackageDescription.Read(folder, package.Id) is not { } description)
        {
            return Fail(failures, $"{missing} the package {label} has no description in NuGet's package folder.");
        }

        if (!description.DeclaresExpression || description.License != "MIT")
        {
            return Fail(failures, $"{missing} the package {label} does not declare the expression MIT.");
        }

        if (description.Copyright is not { } copyright || !CopyrightNotice().IsMatch(copyright))
        {
            return Fail(failures, $"{missing} the package {label} declares no copyright notice.");
        }

        return new TextBlock(
            $"MIT license, with the copyright line of the package {label}, which declares MIT and ships no "
                + "license file",
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
        foreach (var (label, folder, kind, key) in SourceFolders(name, entry, build, configuration))
        {
            var files = Directory.EnumerateFiles(folder)
                .Select(path => Path.GetFileName(path))
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (NoticeFileName().IsMatch(file) && !AmongTexts(entry, kind, key, file))
                {
                    failures.Add(
                        $"{app}: {name} leaves out the NOTICE file {file} of {label}, which Apache-2.0 asks to keep "
                            + $"(C-0003 section 5). Add it to the component's texts in {Mapping.FilePath}.");
                }
            }
        }
    }

    // The license and notice files anywhere in the packages and runtime packs of a component that are
    // not among its texts. The report lists them for the coverage review, because a package can carry
    // notices of its own below its top, such as those of the native libraries inside it.
    private static IEnumerable<string> OtherLicenseFiles(
        string name,
        ComponentTexts entry,
        BuildInputs build,
        Configuration configuration)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        foreach (var (label, folder, kind, key) in SourceFolders(name, entry, build, configuration))
        {
            var files = Directory.EnumerateFiles(folder, "*", options)
                .Select(path => Path.GetRelativePath(folder, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal);
            foreach (var file in files)
            {
                if (LicenseFileName().IsMatch(file.Split('/')[^1]) && !AmongTexts(entry, kind, key, file))
                {
                    yield return $"{name}: {file} of {label}";
                }
            }
        }
    }

    // The folders that a component's texts may come from: each package of the lock file that maps to
    // it, and each package and runtime pack that its texts name.
    private static List<(string Label, string Folder, SourceKind Kind, string Key)> SourceFolders(
        string name,
        ComponentTexts entry,
        BuildInputs build,
        Configuration configuration)
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

        return [.. folders.Where(candidate => Directory.Exists(candidate.Folder))];
    }

    private static bool AmongTexts(ComponentTexts entry, SourceKind kind, string key, string file) =>
        entry.Texts.Any(text => text.Kind == kind
            && string.Equals(text.Name, key, StringComparison.OrdinalIgnoreCase)
            && string.Equals(text.Value, file, StringComparison.OrdinalIgnoreCase));

    // The expressions that each component's packages declare. Every license that one names is one that
    // the register row of the component names; otherwise the register needs a change first (C-0003
    // section 6). An expression that names no license, such as an empty one, "()" or "WITH X", fails, and
    // so does one with an alternative that names none, such as "MIT OR"; a package that declares its
    // license otherwise goes into the report.
    private static Dictionary<string, List<string>> Declarations(
        string app,
        BuildInputs build,
        Configuration configuration,
        List<string> failures,
        List<string> unexpressed)
    {
        var declared = new Dictionary<string, List<string>>(StringComparer.Ordinal);
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

            if (!description.DeclaresExpression)
            {
                if (!description.DeclaresFile)
                {
                    unexpressed.Add(description switch
                    {
                        { License: null, LicenseUrl: not null } => $"{label} declares its license only by licenseUrl",
                        { License: null } => $"{label} declares no license",
                        { LicenseType: null } => $"{label} declares a license without a type",
                        _ => $"{label} declares a license of the type \"{description.LicenseType}\"",
                    });
                }

                continue;
            }

            var expression = description.License ?? string.Empty;
            var licenses = Spdx.Licenses(expression);
            if (licenses.Count == 0)
            {
                failures.Add($"{app}: the package {label} declares a license expression that names no license.");
                continue;
            }

            // An alternative that names no license, as in "MIT OR" or "() OR LGPL-2.1-only", offers no terms.
            if (Spdx.Alternatives(expression).Any(alternative => alternative.Count == 0))
            {
                failures.Add(
                    $"{app}: the package {label} declares a license expression that names no license in one of its "
                        + "alternatives.");
                continue;
            }

            foreach (var license in licenses.Where(license => !Spdx.Mentions(row.License, license)))
            {
                failures.Add(
                    $"{app}: the package {label} declares {license}, which the register row {component} does not "
                        + "name. The register needs a change first (C-0003 section 6).");
            }

            if (!declared.TryGetValue(component, out var list))
            {
                declared[component] = list = [];
            }

            list.Add(expression);
        }

        return declared;
    }

    private static NoticeOutputs Render(Bundle bundle, List<ComponentNotice> components)
    {
        var width = Math.Max(2, components.Count.ToString(CultureInfo.InvariantCulture).Length);
        var paths = components
            .Select((component, index) => (index + 1).ToString(CultureInfo.InvariantCulture).PadLeft(width, '0')
                + $"-{Slug(component.Name)}.txt")
            .Select(name => $"{TextsFolder}/{name}")
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
        builder.Append("Paneport's own code is under the MIT license. Each component keeps its own license, so the ");
        builder.Append("app as a whole is not under the MIT license.\n\n");
        builder.Append("| # | Component | License |\n|---|---|---|\n");
        for (var index = 0; index < components.Count; index++)
        {
            builder.Append(CultureInfo.InvariantCulture, $"| {index + 1} | {Cell(components[index].Name)} | ");
            builder.Append(Cell(components[index].Row.License)).Append(" |\n");
        }

        builder.Append("\nEach section below gives a component's sources and the full text of its license and ");
        builder.Append(CultureInfo.InvariantCulture, $"notices. The folder `{TextsFolder}` beside this file holds ");
        builder.Append(CultureInfo.InvariantCulture, $"the same texts, one file per component, and `{IndexFile}` ");
        builder.Append("lists them in this order for the app's licenses screen.\n");
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

    [GeneratedRegex(
        @"licen[cs]e|notice|copying|copyright|third.?party|3rd.?party|authors|eula|patent|credit|acknowledg|legal",
        RegexOptions.IgnoreCase)]
    private static partial Regex LicenseFileName();

    // A copyright notice says so: "Copyright", "(c)" or the copyright sign.
    [GeneratedRegex("copyright|\\(c\\)|\\u00A9", RegexOptions.IgnoreCase)]
    private static partial Regex CopyrightNotice();
}

internal static partial class Output
{
    // The notices go into build output, never into git: a folder outside the repository, or inside
    // it under build/ or below a bin/ or obj/ folder, which C-0004 section 3 reserves for build
    // output and .gitignore ignores. A device or network path fails: on Windows any full path whose
    // root is not a drive, which covers \\?\, \\.\, \??\, \??\UNC\ and \\server\share alike, and any
    // with a ':' after its drive, which names a stream, as docs\A::$INDEX_ALLOCATION does the folder
    // docs\A, and docs::$INDEX_ALLOCATION the folder docs itself, or a path on another drive. Every
    // link among the existing folders of the path, and of the repository's, is followed to its final
    // target first, so that a link under build/ cannot lead into a tracked folder. Returns the full
    // path.
    public static string EnsureBuildOutput(string root, string path, string option)
    {
        var device = $"{option} {path} is a device or network path; the notices go into a local build output folder.";
        if (IsDeviceOrNetwork(path))
        {
            throw new InvalidDataException(device);
        }

        var full = Path.GetFullPath(path, root);
        if (IsDeviceOrNetwork(full)
            || (OperatingSystem.IsWindows() && (!DriveRoot().IsMatch(full) || full.IndexOf(':', 2) >= 0)))
        {
            throw new InvalidDataException(device);
        }

        var relative = Path.GetRelativePath(RealPath(root), RealPath(full));
        if (Path.IsPathRooted(relative) || relative == ".."
            || relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
        {
            return full;
        }

        var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (parts[0] == "build" || parts.Any(part => part is "bin" or "obj"))
        {
            return full;
        }

        throw new InvalidDataException(
            $"{option} {path} leads to {relative.Replace('\\', '/')} in the repository, outside build/, bin/ and obj/; "
                + "the notices go into build output, never into git.");
    }

    // The path with each link among its existing parts replaced by the link's final target: on macOS
    // and Linux part by part, as the kernel resolves it, and on Windows, whose junctions and links have
    // full targets, through .NET.
    public static string RealPath(string path) =>
        OperatingSystem.IsWindows()
            ? RealPath(path, 0)
            : PhysicalPath(Path.GetFullPath(path), part => new FileInfo(part).LinkTarget);

    // A full path of macOS or Linux resolved one part at a time, with the target of each link put in
    // place of the link: a ".." after a link leads to the parent of the link's target, as the kernel
    // has it, not to the folder that holds the link, as a lexical normalization such as
    // Path.GetFullPath would. The link reader returns a link's target, or null for anything else.
    public static string PhysicalPath(string path, Func<string, string?> linkTarget)
    {
        var pending = new Stack<string>(Parts(path).Reverse());
        var current = "/";
        var links = 0;
        while (pending.TryPop(out var part))
        {
            if (part == "..")
            {
                var slash = current.LastIndexOf('/');
                current = slash <= 0 ? "/" : current[..slash];
                continue;
            }

            var next = current == "/" ? $"/{part}" : $"{current}/{part}";
            if (linkTarget(next) is not { } target)
            {
                current = next;
                continue;
            }

            if (++links > 32)
            {
                throw new IOException($"{path} goes through too many links.");
            }

            foreach (var targetPart in Parts(target).Reverse())
            {
                pending.Push(targetPart);
            }

            if (target.StartsWith('/'))
            {
                current = "/";
            }
        }

        return current;
    }

    // A destination is missing, empty, or holds only the tool's own earlier output; it is never a link.
    public static void Check(string folder)
    {
        var info = new DirectoryInfo(folder);
        if (info.LinkTarget is not null)
        {
            throw new IOException(
                $"{folder} is a link, and the notices are written into a folder, never through a link.");
        }

        if (!info.Exists)
        {
            return;
        }

        if (Foreign(info) is { } foreign)
        {
            throw new IOException(
                $"{folder} holds {foreign}, which this tool did not write, and it replaces only its own notices.");
        }
    }

    // Writes the notices into a folder, replacing the tool's own earlier output there: it deletes only
    // THIRD_PARTY_NOTICES.md, index.json and texts/NN-<name>.txt, after Check has found nothing else.
    public static void Write(string folder, NoticeOutputs outputs)
    {
        Check(folder);
        var texts = Path.Combine(folder, Notices.TextsFolder);
        if (Directory.Exists(folder))
        {
            File.Delete(Path.Combine(folder, Notices.NoticesFile));
            File.Delete(Path.Combine(folder, Notices.IndexFile));
            if (Directory.Exists(texts))
            {
                var own = Directory.EnumerateFiles(texts).Where(path => TextName().IsMatch(Path.GetFileName(path)));
                foreach (var text in own.ToList())
                {
                    File.Delete(text);
                }

                Directory.Delete(texts);
            }
        }

        Directory.CreateDirectory(texts);
        Save(Path.Combine(folder, Notices.NoticesFile), outputs.Notices);
        Save(Path.Combine(folder, Notices.IndexFile), outputs.Index);
        foreach (var (path, content) in outputs.Texts)
        {
            Save(Path.Combine(folder, path), content);
        }
    }

    // A device path such as \\?\ or \\.\, or a network path such as \\server\share.
    private static bool IsDeviceOrNetwork(string path) =>
        path.Length >= 2 && (path[0] is '/' or '\\') && (path[1] is '/' or '\\');

    private static IEnumerable<string> Parts(string path) =>
        path.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(part => part != ".");

    private static string RealPath(string path, int depth)
    {
        if (depth > 32)
        {
            throw new IOException($"{path} goes through too many links.");
        }

        var full = Path.GetFullPath(path);
        var current = Path.GetPathRoot(full) ?? throw new IOException($"{path} has no root.");
        var parts = full[current.Length..].Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        // Path.Join, unlike Path.Combine, never starts over at a part that looks rooted, such as C:x.
        for (var index = 0; index < parts.Length; index++)
        {
            var next = Path.Join(current, parts[index]);
            var directory = new DirectoryInfo(next);
            FileSystemInfo entry = directory.Exists ? directory : new FileInfo(next);
            if (entry.LinkTarget is not null)
            {
                var target = entry.ResolveLinkTarget(returnFinalTarget: true)
                    ?? throw new IOException($"{next} is a link that cannot be followed.");
                next = RealPath(target.FullName, depth + 1);
            }
            else if (!entry.Exists)
            {
                return Path.Join([next, .. parts[(index + 1)..]]);
            }

            current = next;
        }

        return current;
    }

    // The first entry of a folder that is not the tool's own output, or null. A folder that is not
    // empty holds the tool's index of format 1, and besides it only the notices file and the texts.
    private static string? Foreign(DirectoryInfo folder)
    {
        var options = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        var entries = folder.EnumerateFileSystemInfos("*", options)
            .OrderBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();
        if (entries.Count == 0)
        {
            return null;
        }

        if (!IsOwnIndex(Path.Combine(folder.FullName, Notices.IndexFile)))
        {
            return $"no {Notices.IndexFile} of format {Notices.Format}";
        }

        foreach (var entry in entries)
        {
            var own = entry.LinkTarget is null && entry.Name switch
            {
                Notices.NoticesFile or Notices.IndexFile => entry is FileInfo,
                Notices.TextsFolder => entry is DirectoryInfo,
                _ => false,
            };
            if (!own)
            {
                return entry.Name;
            }
        }

        var texts = new DirectoryInfo(Path.Combine(folder.FullName, Notices.TextsFolder));
        var foreign = texts.Exists
            ? texts.EnumerateFileSystemInfos("*", options)
                .OrderBy(entry => entry.Name, StringComparer.Ordinal)
                .FirstOrDefault(entry => entry is not FileInfo || entry.LinkTarget is not null
                    || !TextName().IsMatch(entry.Name))
            : null;
        return foreign is null ? null : $"{Notices.TextsFolder}/{foreign.Name}";
    }

    private static bool IsOwnIndex(string index)
    {
        if (!File.Exists(index))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(index));
            var root = document.RootElement;
            return root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("format", out var format) && format.ValueKind == JsonValueKind.Number
                && format.TryGetInt32(out var number) && number == Notices.Format
                && root.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void Save(string path, string content) =>
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    // The name of a text that the tool writes: its number, of two digits or more, and a slug.
    [GeneratedRegex(@"\A[0-9]{2,}-[a-z0-9-]+\.txt\z")]
    private static partial Regex TextName();

    // A full path of Windows on a drive: X:\.
    [GeneratedRegex(@"\A[A-Za-z]:\\")]
    private static partial Regex DriveRoot();
}

// A guard of a missing text, "whileDefined" in eng/release/bundle-components.json: the text counts
// only while the assembly of that name at the root of the bundle defines the type, by its exact
// namespace and name, as a top-level type.
internal sealed record TypeGuard(string Assembly, string Namespace, string Name)
{
    public string FullName => $"{Namespace}.{Name}";

    public static TypeGuard Parse(JsonElement element, string component) =>
        AssemblyTypes.ParseGuard(element, component);
}

// Reads the type definitions of an assembly of the bundle with System.Reflection.Metadata, for the
// guards of missing texts (S9.1.1's mapping, M9.1).
internal static partial class AssemblyTypes
{
    // Null when the guarded text is not needed, because the bundle's assembly defines no such type;
    // otherwise why it is needed, as the end of the finding. A missing or unreadable assembly counts.
    public static string? Needed(string bundle, TypeGuard guard)
    {
        var path = Path.Combine(bundle, guard.Assembly);
        if (!File.Exists(path))
        {
            return $" It counts, because the bundle has no {guard.Assembly} to show whether {guard.FullName} ships.";
        }

        return Defines(path, guard) switch
        {
            true => $" It counts, because {guard.Assembly} defines {guard.FullName}.",
            false => null,
            null => $" It counts, because {guard.Assembly} cannot be read as an assembly.",
        };
    }

    // Whether the assembly defines the type as a top-level type; null when it cannot be read.
    public static bool? Defines(string path, TypeGuard guard)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var image = new System.Reflection.PortableExecutable.PEReader(stream);
            if (!image.HasMetadata)
            {
                return null;
            }

            var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(image);
            foreach (var handle in reader.TypeDefinitions)
            {
                var type = reader.GetTypeDefinition(handle);
                if (type.GetDeclaringType().IsNil
                    && reader.StringComparer.Equals(type.Namespace, guard.Namespace)
                    && reader.StringComparer.Equals(type.Name, guard.Name))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (
            exception is BadImageFormatException or IOException or UnauthorizedAccessException
                or InvalidOperationException)
        {
            return null;
        }
    }

    public static TypeGuard ParseGuard(JsonElement element, string component)
    {
        var where = $"a missing text of {component}: \"whileDefined\"";
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where} must be an object.");
        }

        string? assembly = null;
        string? type = null;
        foreach (var property in element.EnumerateObject())
        {
            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            if (property.Name == "assembly" && assembly is null)
            {
                assembly = value ?? string.Empty;
            }
            else if (property.Name == "type" && type is null)
            {
                type = value ?? string.Empty;
            }
            else
            {
                throw new InvalidDataException(
                    $"{where} has a key \"{property.Name}\" that the tool does not know, or gives it twice.");
            }
        }

        if (assembly is null || !DllName().IsMatch(assembly))
        {
            throw new InvalidDataException($"{where} must name in \"assembly\" a .dll at the root of the bundle.");
        }

        if (type is null || !TypeName().IsMatch(type))
        {
            throw new InvalidDataException($"{where} must name in \"type\" a type by its namespace and name.");
        }

        var dot = type.LastIndexOf('.');
        return new TypeGuard(assembly, type[..dot], type[(dot + 1)..]);
    }

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9_.-]*\.dll\z")]
    private static partial Regex DllName();

    [GeneratedRegex(@"\A[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)+\z")]
    private static partial Regex TypeName();
}

// The report of a run. Every line goes through Plain, and every annotation's data through Data, so
// that a file name with a line break cannot start a line of its own. Under GitHub Actions the plain
// lines, which name files of the bundle and of packages, are printed while workflow commands are
// stopped, so that no name runs a command without a line break either, such as one that starts with
// "::" or holds "##["; the annotations follow once commands resume.
internal static class Report
{
    public static void Print(
        Bundle bundle,
        BuildInputs build,
        Configuration configuration,
        Mode mode,
        Result result,
        TextWriter output,
        bool annotate)
    {
        var lines = new List<string>();
        void write(string text) => lines.Add(text);
        var app = bundle.Name;
        write($"Third-party notices of {app}, in {(mode == Mode.Ios ? "ios" : "release")} mode");
        write(
            $"Register: {configuration.Register.Count} rows. Mapping: {configuration.Mapping.Files.Count} file "
                + $"rules and the texts of {configuration.Mapping.Components.Count} components. {build.LockFile}: "
                + $"{build.Packages.Count} packages.");
        foreach (var pack in build.RuntimePacks)
        {
            write($"Runtime pack for {pack.Framework}: {pack.Id} {pack.Version}");
        }

        write($"Coverage of {result.Coverage.Count} files:");
        foreach (var attribution in result.Coverage)
        {
            var components = attribution.Components.Count == 0
                ? "(no component)"
                : string.Join(", ", attribution.Components);
            var role = attribution.File == bundle.Executable ? " (main executable)" : string.Empty;
            write($"  {attribution.File}{role}: {components}");
        }

        if (result.OtherLicenseFiles.Count > 0)
        {
            write("License and notice files in the sources that are not among the texts:");
            foreach (var file in result.OtherLicenseFiles)
            {
                write($"  {file}");
            }
        }

        if (result.Unexpressed.Count > 0)
        {
            write("Packages that declare their license without an SPDX expression:");
            foreach (var package in result.Unexpressed)
            {
                write($"  {package}");
            }
        }

        if (result.UnusedRules.Count > 0)
        {
            write($"Rules that match no file of {app}: {string.Join(", ", result.UnusedRules)}");
        }

        write($"Components in the notices: {result.Components.Count}");
        for (var index = 0; index < result.Components.Count; index++)
        {
            var component = result.Components[index];
            write(
                $"  {index + 1}. {component.Name} ({component.Row.Category}): {component.Files} files, "
                    + $"{component.Texts.Count} texts, from {string.Join(", ", component.Sources)}");
        }

        foreach (var warning in result.Warnings)
        {
            write($"warning: {warning}");
        }

        foreach (var failure in result.Failures)
        {
            write($"error: {failure}");
        }

        write($"{app}: {result.Failures.Count} failures, {result.Warnings.Count} warnings.");
        Untrusted(output, annotate, lines);
        if (!annotate)
        {
            return;
        }

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"::warning title=Notices::{Data(warning)}");
        }

        foreach (var failure in result.Failures)
        {
            output.WriteLine($"::error title=Notices::{Data(failure)}");
        }
    }

    // Lines that may name files of the bundle or of packages, each through Plain. Under GitHub Actions
    // they are printed between ::stop-commands::<token> and ::<token>::, with a new random token for
    // each run, which is how GitHub Actions prints text that must not run a command.
    public static void Untrusted(TextWriter output, bool actions, IEnumerable<string> lines)
    {
        var token = actions ? Guid.NewGuid().ToString("N") : null;
        if (token is not null)
        {
            output.WriteLine($"::stop-commands::{token}");
        }

        foreach (var line in lines)
        {
            output.WriteLine(Plain(line));
        }

        if (token is not null)
        {
            output.WriteLine($"::{token}::");
        }
    }

    // A line of the report, with CR and LF written as %0D and %0A.
    public static string Plain(string text) =>
        text.Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    // The data of an annotation, escaped as GitHub Actions asks: %, CR and LF as %25, %0D and %0A.
    public static string Data(string text) => Plain(text.Replace("%", "%25", StringComparison.Ordinal));
}

internal sealed class Tally(TextWriter output)
{
    private readonly List<string> _skipped = [];

    public Tally()
        : this(Console.Out)
    {
    }

    public int Total { get; private set; }

    public int Failed { get; private set; }

    public int Skipped => _skipped.Count;

    public void Expect(bool passed, string description)
    {
        Total++;
        Failed += passed ? 0 : 1;
        output.WriteLine($"  {(passed ? "ok  " : "FAIL")} {description}");
    }

    // A check that cannot run on this system: it counts neither as passed nor as failed, and the
    // summary names its reason.
    public void Skip(string description, string reason)
    {
        _skipped.Add(reason);
        output.WriteLine($"  skip {description} ({reason})");
    }

    public string Summary()
    {
        var skipped = _skipped.Count == 0
            ? string.Empty
            : $", {_skipped.Count} skipped ({string.Join("; ", _skipped)})";
        return Failed == 0
            ? $"Self-test passed: {Total} of {Total} checks as expected{skipped}."
            : $"Self-test FAILED: {Failed} of {Total} checks not as expected{skipped}.";
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

// Writes the smallest assembly that a metadata reader takes, with the given top-level types and,
// optionally, one type nested in one of them, for the guards of missing texts.
internal static class TinyAssembly
{
    public static byte[] Build(string name, IReadOnlyList<string> types, (string Outer, string Inner)? nested = null)
    {
        var metadata = new System.Reflection.Metadata.Ecma335.MetadataBuilder();
        metadata.AddModule(
            0,
            metadata.GetOrAddString($"{name}.dll"),
            metadata.GetOrAddGuid(Guid.Empty),
            default,
            default);
        metadata.AddAssembly(
            metadata.GetOrAddString(name),
            new Version(1, 0, 0, 0),
            default,
            default,
            0,
            System.Reflection.AssemblyHashAlgorithm.None);
        var fields = System.Reflection.Metadata.Ecma335.MetadataTokens.FieldDefinitionHandle(1);
        var methods = System.Reflection.Metadata.Ecma335.MetadataTokens.MethodDefinitionHandle(1);
        metadata.AddTypeDefinition(default, default, metadata.GetOrAddString("<Module>"), default, fields, methods);
        var defined = new Dictionary<string, System.Reflection.Metadata.TypeDefinitionHandle>(StringComparer.Ordinal);
        foreach (var type in types)
        {
            var dot = type.LastIndexOf('.');
            defined[type] = metadata.AddTypeDefinition(
                System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Class,
                metadata.GetOrAddString(dot < 0 ? string.Empty : type[..dot]),
                metadata.GetOrAddString(type[(dot + 1)..]),
                default,
                fields,
                methods);
        }

        // The nested type carries its outer type's namespace, which metadata allows, so that only the
        // nesting tells it apart from a top-level type of that namespace and name.
        if (nested is { } pair)
        {
            var inner = metadata.AddTypeDefinition(
                System.Reflection.TypeAttributes.NestedPublic | System.Reflection.TypeAttributes.Class,
                metadata.GetOrAddString(pair.Outer[..pair.Outer.LastIndexOf('.')]),
                metadata.GetOrAddString(pair.Inner),
                default,
                fields,
                methods);
            metadata.AddNestedType(inner, defined[pair.Outer]);
        }

        var image = new System.Reflection.PortableExecutable.ManagedPEBuilder(
            System.Reflection.PortableExecutable.PEHeaderBuilder.CreateLibraryHeader(),
            new System.Reflection.Metadata.Ecma335.MetadataRootBuilder(metadata),
            new System.Reflection.Metadata.BlobBuilder());
        var blob = new System.Reflection.Metadata.BlobBuilder();
        image.Serialize(blob);
        return blob.ToArray();
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
        | Sample.Graphics | A graphics library | MIT. Its native package lists more components. | Allowed |
        | Sample.Apache | A protocol library | Apache-2.0 | Allowed |
        | Sample.Fonts | Font rendering | FTL or GPL-2.0 | Allowed, under FTL |
        | SteamKit2 | A network protocol | LGPL-2.1-only | Conditional |
        | Sample.Lgpl | A library the user can replace | LGPL-2.1-or-later | Conditional |
        | Sample.Review | A component | Proprietary | Under review |
        | Sample.Forbidden | A library | GPL-3.0 | Forbidden |
        | Sample.Tool | A build tool | MIT | Tool |
        """;

    // The License text of Sample.Graphics' row, which some cases change.
    public const string GraphicsLicense = "MIT. Its native package lists more components.";

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

    public const string ApacheUrl =
        "https://raw.githubusercontent.com/sample/terms/0123456789abcdef0123456789abcdef01234567/LICENSE-2.0.txt";

    public const string HeaderUrl =
        "https://raw.githubusercontent.com/sample/ui/89abcdef0123456789abcdef0123456789abcdef/src/Header.cs";

    // The full terms of Apache-2.0 and LGPL-2.1 as the rules recognize them, each passage split over
    // lines as a license text may write it.
    public const string ApacheTerms =
        """
        Sample Apache License
        Version 2.0, January 2004

        TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND
        DISTRIBUTION

        Sample terms.
        """;

    public const string LgplTerms =
        """
        Sample LGPL library.

        GNU LESSER GENERAL PUBLIC LICENSE
           TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION

        Sample terms.
        """;

    public const string UnicodeUrl =
        "https://raw.githubusercontent.com/sample/data/fedcba9876543210fedcba9876543210fedcba98/LICENSE";

    // The permission of the Unicode License V3, and that of the older Unicode terms, which differ in it.
    public const string UnicodeTerms =
        """
        UNICODE LICENSE V3

        Copyright (c) 2026 Sample Data Holder

        Permission is hereby granted, free of charge, to any person obtaining a
        copy of data files and any associated documentation (the "Data Files") or
        software and any associated documentation (the "Software") to deal in the
        Data Files or Software without restriction.
        """;

    public const string OlderUnicodeTerms =
        """
        Permission is hereby granted, free of charge, to any person obtaining a copy of
        the Unicode data files and any associated documentation (the "Data Files") or
        Unicode software and any associated documentation (the "Software") to deal in
        the Data Files or Software without restriction.
        """;

    // A source file whose header holds a notice, of which the mapping takes lines 1 to 4.
    public const string Header =
        """
        // Copyright (c) Sample Header Holder.
        /* Licensed under the Sample License.
           Ported from a sample. */

        namespace Sample;

        internal static class Header;
        """;

    public const string Mapping =
        $$"""
        {
          "mainExecutable": {
            "components": ["Paneport", "Sample.Runtime", "Sample.Platform"],
            "paths": ["aot-instances.aotdata.*"],
            "note": "The executable."
          },
          "files": [
            { "path": "0-ui.dat", "components": ["Sample.Ui"], "note": "Its data, whose path sorts first." },
            { "path": "Info.plist", "components": ["Paneport"], "note": "The build writes it." },
            { "path": "PkgInfo", "components": ["Paneport"], "note": "The build writes it." },
            { "path": "Sample.App.*", "components": ["Paneport"], "note": "Our assembly." },
            { "path": "Ui.*", "components": ["Sample.Ui"], "note": "The library." },
            { "path": "Graphics.dll", "components": ["Sample.Graphics"], "note": "Its assembly." },
            { "path": "Frameworks/libgraphics.framework/**", "components": ["Sample.Graphics"], "note": "Native." },
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
            "Sample.Ui": {
              "texts": [
                { "package": "Sample.Ui", "license": "MIT" },
                { "upstream": "{{HeaderUrl}}", "sha256": "<header-sha256>", "lines": [1, 4] }
              ]
            },
            "Sample.Graphics": {
              "texts": [
                { "package": "Sample.Graphics", "file": "LICENSE.txt" },
                { "package": "Sample.Graphics.Native", "file": "THIRD-PARTY-NOTICES.txt" }
              ]
            },
            "Sample.Apache": {
              "texts": [
                { "package": "Sample.Apache", "file": "LICENSE.txt" },
                { "package": "Sample.Apache", "file": "NOTICE.txt" },
                { "upstream": "{{ApacheUrl}}", "sha256": "<apache-sha256>", "terms": "Apache-2.0" }
              ]
            },
            "SteamKit2": {
              "texts": [
                { "package": "SteamKit2", "file": "license.txt" },
                { "repository": "external/Sample-lgpl/COPYING.LIB" }
              ]
            },
            "Sample.Fonts": {
              "texts": [{ "package": "Sample.Fonts.Native", "file": "LICENSE.TXT" }],
              "credit": "Portions of this software are copyright (c) 2026 The Sample Font Project.",
              "note": "The FTL asks for this credit."
            },
            "Sample.Runtime": {
              "texts": [
                { "runtimePack": "Sample.Runtime.App", "file": "LICENSE.TXT" },
                { "runtimePack": "Sample.Runtime.App", "file": "THIRD-PARTY-NOTICES.TXT" }
              ]
            },
            "Sample.Platform": {
              "texts": [
                { "runtimePack": "Sample.Platform", "file": "LICENSE" },
                { "runtimePack": "Sample.Platform.Runtimes", "file": "LICENSE" }
              ]
            },
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

    public const string Credit = "Portions of this software are copyright (c) 2026 The Sample Font Project.";

    private static readonly string[] s_appFiles =
    [
        "Sample",
        "0-ui.dat",
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
        "aot-instances.aotdata.arm64",
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

    public string RegisterText { get; set; } = Register;

    public string MapText { get; set; } = Map;

    public string MappingText { get; set; } = Resolve(Mapping);

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

    public static string DisplayName => "Sampl" + (char)0x00E9 + " App";

    public static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    // A mapping with the SHA-256 of each upstream text in place.
    public static string Resolve(string mapping) =>
        mapping.ReplaceLineEndings("\n")
            .Replace("<apache-sha256>", Hash(ApacheTerms), StringComparison.Ordinal)
            .Replace("<header-sha256>", Hash(Header), StringComparison.Ordinal);

    public static Fixture Create(string parent, string name)
    {
        var fixture = new Fixture(Path.Combine(parent, name));
        fixture.AddPack("Sample.Runtime.App", "Sample.Runtime.Pack", "6.0.0");
        fixture.AddPack("Sample.Platform", "Sample.Platform.Pack", "7.0.0");
        fixture.AddPack("Sample.Platform.Runtimes", "Sample.Platform.Native", "7.0.0");
        fixture.Package(
            "Sample.Ui",
            "1.0.0",
            "expression",
            "MIT",
            "Copyright 2020 Sample UI Authors",
            ("native/THIRD-PARTY.md", "Notices of a library inside the package."));
        fixture.Package("Sample.Ui.Extra", "1.0.0", "expression", "MIT", "Copyright 2020 Sample UI Authors");
        fixture.Package(
            "Sample.Graphics",
            "2.0.0",
            "file",
            "LICENSE.txt",
            null,
            ("LICENSE.txt", "Sample graphics.\n```\nA run of three backticks.\n```\n"),
            ("ACKNOWLEDGEMENTS.md", "Thanks."),
            ("CREDITS", "Credits."),
            ("EULA.txt", "An agreement."),
            ("PATENTS", "A patent grant."),
            ("legal.txt", "Legal notes."));
        fixture.Package(
            "Sample.Graphics.Native",
            "2.0.0",
            "expression",
            "MIT",
            "Copyright 2021 Sample Graphics Authors");
        WriteFile(
            fixture.PackageFile("Sample.Graphics.Native", "2.0.0", "THIRD-PARTY-NOTICES.txt"),
            "Sample graphics third-party notices.",
            Encoding.Unicode);
        fixture.Package(
            "Sample.Apache",
            "3.0.0",
            "expression",
            "Apache-2.0",
            null,
            ("LICENSE.txt", "Sample Apache license header."),
            ("NOTICE.txt", "Sample Apache NOTICE text."));
        fixture.Package(
            "SteamKit2",
            "4.0.0",
            "expression",
            "LGPL-2.1-only",
            null,
            ("license.txt", "Sample LGPL notice."),
            ("3rd party.txt", "Sample third-party notes."),
            ("readme.txt", "Read me."));
        fixture.Package("Sample.Fonts.Native", "5.0.0", "expression", "FTL", null, ("LICENSE.TXT", "Sample font."));
        var runtime = fixture.Packs[0].Folder;
        WriteFile(
            Path.Combine(runtime, "LICENSE.TXT"),
            "Sample runtime license text.\r\nSecond line.\rThird line.\r\n");
        WriteFile(Path.Combine(runtime, "THIRD-PARTY-NOTICES.TXT"), "Sample runtime notices.");
        WriteFile(Path.Combine(fixture.Packs[1].Folder, "LICENSE"), "Sample platform license text.");
        WriteFile(Path.Combine(fixture.Packs[2].Folder, "LICENSE"), "Sample platform license text.\r\n");
        WriteFile(Path.Combine(fixture.Repository, "external", "Sample-lgpl", "COPYING.LIB"), LgplTerms);
        WriteFile(fixture.Upstream(ApacheTerms), ApacheTerms);
        WriteFile(fixture.Upstream(Header), Header);
        foreach (var file in s_appFiles)
        {
            WriteFile(Path.Combine(fixture.App, file), $"sample content of {file}");
        }

        fixture.WriteInfo("Sample", binary: true);
        return fixture;
    }

    // Where the ios job would have fetched an upstream text: build/upstream/<sha256>.
    public string Upstream(string text) => Path.Combine(Repository, "build", "upstream", Hash(text));

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

    public void Package(
        string id,
        string version,
        string licenseType,
        string license,
        string? copyright,
        params (string Name, string Text)[] files)
    {
        var copyrightElement = copyright is null ? string.Empty : $"\n    <copyright>{copyright}</copyright>";
        Description(id, version, $"<license type=\"{licenseType}\">{license}</license>{copyrightElement}");
        foreach (var (name, text) in files)
        {
            WriteFile(Path.Combine(PackageFolder(id, version), name), text);
        }
    }

    // A package description whose metadata holds these license elements.
    public void Description(string id, string version, string license) => WriteFile(
        Path.Combine(PackageFolder(id, version), $"{id.ToLowerInvariant()}.nuspec"),
        $"""
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>{id}</id>
            <version>{version}</version>
            {license}
          </metadata>
        </package>
        """);

    public string PackageFolder(string id, string version) => Path.Combine(Packages, id.ToLowerInvariant(), version);

    public string PackageFile(string id, string version, string file) => Path.Combine(PackageFolder(id, version), file);

    public static void WriteFile(string path, string content) => WriteFile(path, content, new UTF8Encoding(false));

    public static void WriteFile(string path, string content, Encoding encoding)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, encoding);
    }

    public BuildInputs Build() => new("packages.lock.json", Packages, Locked, Packs);

    public Configuration Load() => new(
        Repository,
        global::Register.Parse(RegisterText),
        LicenseMap.Parse(MapText),
        global::Mapping.Parse(MappingText),
        MitTemplate.Validate(Mit));

    public Result Generate(Mode mode) => Notices.Generate(Bundle.Read(App), Build(), Load(), mode);

    // The repository files that the command line reads, and the sample project with its lock file.
    public void WriteRepository()
    {
        WriteFile(Path.Combine(Repository, global::Register.FilePath), RegisterText);
        WriteFile(Path.Combine(Repository, LicenseMap.FilePath), MapText);
        WriteFile(Path.Combine(Repository, global::Mapping.FilePath), MappingText);
        WriteFile(Path.Combine(Repository, MitTemplate.FilePath), Mit);
        WriteFile(Path.Combine(Repository, "src", "Sample", "Sample.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var entries = Locked.Select(package =>
            $"      \"{package.Id}\": {{ \"type\": \"Transitive\", \"resolved\": \"{package.Version}\" }}");
        WriteFile(
            Path.Combine(Repository, "src", "Sample", "packages.lock.json"),
            $"{{\n  \"version\": 2,\n  \"dependencies\": {{\n    \"net10.0-ios\": {{\n{string.Join(",\n", entries)}\n"
                + "    }\n  }\n}\n");
    }

    // MSBuild's answer for the sample project: NuGet's package folder and the runtime packs.
    public string Query()
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteStartObject("Properties");
            writer.WriteString("NuGetPackageRoot", Packages + Path.DirectorySeparatorChar);
            writer.WriteEndObject();
            writer.WriteStartObject("Items");
            writer.WriteStartArray("ResolvedRuntimePack");
            foreach (var pack in Packs)
            {
                writer.WriteStartObject();
                writer.WriteString("FrameworkName", pack.Framework);
                writer.WriteString("NuGetPackageId", pack.Id);
                writer.WriteString("NuGetPackageVersion", pack.Version);
                writer.WriteString("PackageDirectory", pack.Folder);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // Every file under the fixture's folder, for the check that the tool writes nowhere else.
    public List<string> Snapshot()
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
        return
        [
            .. Directory.EnumerateFiles(Root, "*", options)
                .Select(path => Path.GetRelativePath(Root, path).Replace('\\', '/'))
                .Order(StringComparer.Ordinal),
        ];
    }

    // A link to a folder: a symbolic link, or on Windows without the right to make one, a junction,
    // which needs none. False when neither can be made.
    public static bool Link(string link, string target)
    {
        Directory.CreateDirectory(target);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (!OperatingSystem.IsWindows())
            {
                return false;
            }
        }

        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        if (process is null)
        {
            return false;
        }

        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0 && new DirectoryInfo(link).LinkTarget is not null;
    }

    // A symbolic link with a relative target, as written, to a folder that it makes first. False when
    // the link cannot be made.
    public static bool RelativeLink(string link, string target, string folder)
    {
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            Directory.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // A symbolic link to a file, which Windows makes only with a right or in developer mode.
    public static bool FileLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            File.CreateSymbolicLink(link, target);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void AddPack(string framework, string id, string version) =>
        Packs.Add(new RuntimePack(framework, id, version, Path.Combine(Root, "packs", id, version)));
}

internal static partial class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("Self-test of tools/GenerateNotices.cs, with fixtures generated in a temporary folder.");
        var tally = new Tally();
        var root = Path.Combine(Path.GetTempPath(), $"GenerateNotices-{Guid.NewGuid():N}");
        try
        {
            Readers(tally, root);
            UpstreamTexts(tally);
            FullyRegistered(tally, root);
            CaseOrder(tally, root);
            Outputs(tally, root);
            CommandLine(tally, root);
            Failures(tally, root);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                RemoveLinks(new DirectoryInfo(root));
                Directory.Delete(root, recursive: true);
            }
        }

        Console.WriteLine(tally.Summary());
        return tally.Failed == 0 ? 0 : 1;
    }

    // Removes each link to a folder by itself first: a recursive delete of a folder that holds a
    // junction fails on Windows, although it removes the junction.
    private static void RemoveLinks(DirectoryInfo folder)
    {
        foreach (var child in folder.EnumerateDirectories("*", new EnumerationOptions { AttributesToSkip = 0 }))
        {
            if (child.LinkTarget is not null)
            {
                child.Delete();
            }
            else
            {
                RemoveLinks(child);
            }
        }
    }

    private static void Readers(Tally tally, string root)
    {
        Console.WriteLine("Readers:");
        var counted = new Tally(TextWriter.Null);
        counted.Expect(true, "a check that runs");
        counted.Skip("a check that cannot run here", "a reason");
        tally.Expect(
            counted.Total == 1
                && counted.Summary() == "Self-test passed: 1 of 1 checks as expected, 1 skipped (a reason).",
            "a skipped check counts neither as passed nor as failed, and the summary gives its number and reason");
        (string, string)[] entries = [("CFBundleExecutable", "Sample"), ("CFBundleDisplayName", Fixture.DisplayName)];
        var binary = BinaryPropertyListWriter.Write(entries);
        var values = PropertyList.TopLevelStrings(binary);
        tally.Expect(
            values.GetValueOrDefault("CFBundleExecutable") == "Sample"
                && values.GetValueOrDefault("CFBundleDisplayName") == Fixture.DisplayName,
            "reads the ASCII and UTF-16 strings of a binary property list");
        tally.Expect(
            Rejects(() => PropertyList.TopLevelStrings(binary[..^10])),
            "rejects a truncated binary property list");
        var damaged = binary.ToArray();
        damaged[^1] = 0xFF;
        tally.Expect(Rejects(() => PropertyList.TopLevelStrings(damaged)), "rejects an offset table outside the file");
        var xml = Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\"?><plist><dict><key>CFBundleExecutable</key><string>Sample</string>"
                + "<key>UIDeviceFamily</key><array><integer>1</integer></array></dict></plist>");
        var xmlValues = PropertyList.TopLevelStrings(xml);
        tally.Expect(
            xmlValues.Count == 1 && xmlValues.GetValueOrDefault("CFBundleExecutable") == "Sample",
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
        tally.Expect(
            MatchesNone("Info.plist", "Info.plist\n") && MatchesNone("a/Info.plist", "a/Info.plist\n")
                && Matches("Frameworks/x.framework/**", "Frameworks/x.framework/a\nb"),
            "a pattern matches a whole name and never one with a line break after it, and \"**\" matches line breaks");
        string[] badPatterns = ["/abs", "a/../b", "a**", "a\\b", "a//b"];
        tally.Expect(
            badPatterns.All(pattern => Rejects(() => Glob.ToRegex(pattern))),
            "rejects absolute paths, \"..\", a partial \"**\", backslashes and empty parts");
        string[] names = ["b", "A", "a", "B"];
        tally.Expect(
            names.Order(Notices.NameOrder).SequenceEqual(["A", "a", "B", "b"]),
            "orders names without regard to case first, and then exactly");

        string[] broken =
        [
            EditMapping("\"mainExecutable\"", "\"main\""),
            EditMapping("\"note\": \"Our assembly.\"", "\"why\": \"Our assembly.\""),
            EditMapping("\"components\": [\"Sample.Ui\"]", "\"components\": []"),
            EditMapping("\"file\": \"LICENSE.TXT\"", "\"file\": \"../LICENSE.TXT\""),
            EditMapping("{ \"package\": \"Sample.Ui\", \"license\": \"MIT\" }", "{ \"package\": \"Sample.Ui\" }"),
            EditMapping("\"Sample.Lgpl\": {", "\"Paneport\": {"),
            EditMapping("\"path\": \"Unused/**\"", "\"path\": \"Unused/**/../x\""),
            EditMapping("\"paths\": [\"aot-instances.aotdata.*\"]", "\"paths\": \"aot-instances.aotdata.*\""),
            "[]",
        ];
        tally.Expect(
            broken.All(text => Rejects(() => Mapping.Parse(text)))
                && Mapping.Parse(Fixture.Resolve(Fixture.Mapping)) is { Files.Count: 15, MainExecutablePaths.Count: 1 },
            "reads the mapping, and rejects an unknown key, a rule without a note or components, a path that leaves "
                + "its folder, a text without its file or license, texts for Paneport, a bad pattern and a mapping "
                + "that is no object");

        var query =
            """
            {
              "Properties": { "NuGetPackageRoot": "/packages/" },
              "Items": { "ResolvedRuntimePack": [
                {
                  "Identity": "A.Runtime", "FrameworkName": "A.App", "NuGetPackageId": "A.Runtime",
                  "NuGetPackageVersion": "1.0.0", "PackageDirectory": "/packs/a"
                },
                {
                  "Identity": "B.Runtime", "FrameworkName": "B.App", "NuGetPackageId": "B.Runtime",
                  "NuGetPackageVersion": "2.0.0"
                }
              ] }
            }
            """;
        var (packageRoot, packs) = BuildQuery.ParseQuery(query);
        tally.Expect(
            packageRoot == "/packages/" && packs.Count == 1
                && packs[0] == new RuntimePack("A.App", "A.Runtime", "1.0.0", "/packs/a"),
            "reads NuGet's package folder and the resolved runtime packs from MSBuild's answer, without unresolved "
                + "ones");
        var twoPacks = query
            .Replace("\"B.App\"", "\"A.App\"", StringComparison.Ordinal)
            .Replace("\"2.0.0\"", "\"2.0.0\", \"PackageDirectory\": \"/packs/b\"", StringComparison.Ordinal);
        tally.Expect(
            Rejects(() => BuildQuery.ParseQuery(twoPacks)) && Rejects(() => BuildQuery.ParseQuery("{ \"Items\": {} }")),
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
                "net10.0-ios/ios-arm64": {
                  "Sample.Native": { "type": "Transitive", "resolved": "3.0.0" },
                  "sample.direct": { "type": "Transitive", "resolved": "1.0.0" }
                }
              }
            }
            """);
        LockedPackage[] lockedPackages =
            [new("Sample.Direct", "1.0.0"), new("Sample.Native", "3.0.0"), new("Sample.Transitive", "2.0.0")];
        tally.Expect(
            locked.SequenceEqual(lockedPackages),
            "reads the packages of every target and runtime of a lock file, each once, without the projects");
        var twoVersions =
            """
            {
              "version": 2,
              "dependencies": {
                "net10.0-ios": { "Sample.Twice": { "type": "Direct", "resolved": "1.0.0" } },
                "net10.0-ios/ios-arm64": { "sample.twice": { "type": "Transitive", "resolved": "2.0.0" } }
              }
            }
            """;
        tally.Expect(
            Rejects(() => LockFiles.Packages(twoVersions)),
            "rejects a lock file that gives one package two versions");
        var projects = Path.Combine(root, "projects");
        Fixture.WriteFile(Path.Combine(projects, "My App.csproj"), "<Project />");
        Fixture.WriteFile(Path.Combine(projects, "packages.lock.json"), "{}");
        Fixture.WriteFile(Path.Combine(projects, "packages.My_App.lock.json"), "{}");
        tally.Expect(
            Path.GetFileName(LockFiles.Find(Path.Combine(projects, "My App.csproj"))) == "packages.My_App.lock.json",
            "finds the lock file of a project with a space in its name as NuGet names it, with an underscore");

        tally.Expect(
            Spdx.Licenses("MIT OR (Apache-2.0 WITH LLVM-exception)").SequenceEqual(["MIT", "Apache-2.0"])
                && Spdx.Mentions("MIT. Its native packages", "MIT") && !Spdx.Mentions("MIT-Modern-Variant", "MIT")
                && Spdx.Mentions("FTL or GPL-2.0", "FTL") && !Spdx.Mentions("LGPL-2.1-only", "GPL-2.1-only"),
            "reads the licenses of an SPDX expression, and finds a license in a register row only as a whole name");
        tally.Expect(
            Spdx.Leading("MIT. Its native library is under LGPL-2.1-or-later.").SequenceEqual(["MIT"])
                && Spdx.Leading("Apache-2.0 WITH LLVM-exception; parts under MIT").SequenceEqual(["Apache-2.0"])
                && Spdx.Leading("LGPL-2.1, as its README says: more").SequenceEqual(["LGPL-2.1"])
                && Spdx.Leading("FTL or GPL-2.0").SequenceEqual(["FTL", "GPL-2.0"]),
            "reads the expression that a register row's License text starts with, up to the end of its phrase");
        static string alternatives(string expression) =>
            string.Join(" | ", Spdx.Alternatives(expression).Select(alternative => string.Join(' ', alternative)));
        tally.Expect(
            alternatives("MIT OR LGPL-2.1-only") == "MIT | LGPL-2.1-only"
                && alternatives("lgpl-2.1-only or (MIT AND Zlib)") == "lgpl-2.1-only | MIT Zlib"
                && alternatives("(MIT OR LGPL-2.1-only) AND Zlib") == "MIT LGPL-2.1-only Zlib",
            "splits an SPDX expression into its alternatives at each OR outside parentheses, in any case");
        var graphicsRow = global::Register.Parse(Fixture.Register).Single(row => row.Component == "Sample.Graphics");
        tally.Expect(
            Notices.Lgpl(graphicsRow, ["LGPL-2.1-only OR"]) == "LGPL-2.1-only"
                && Notices.Lgpl(graphicsRow, ["() OR LGPL-2.1-only"]) == "LGPL-2.1-only"
                && Notices.Lgpl(graphicsRow, ["MIT OR LGPL-2.1-only"]) is null,
            "an alternative that names no license is no way out of the LGPL, one of the row's licenses is");
        string[] declared = ["MIT", "LGPL-2.1-only", "Apache-2.0", "lgpl-2.1-or-later"];
        string[] otherLicenses = ["MIT", "LGPL-3.0-only", "GPL-2.0"];
        var needed = FullTerms.Needed(declared);
        tally.Expect(
            needed.Select(terms => terms.License).SequenceEqual(["Apache-2.0", "LGPL-2.1"])
                && FullTerms.Needed(otherLicenses).Count == 0
                && FullTerms.Holds(Fixture.ApacheTerms, needed[0].Passage)
                && FullTerms.Holds(Fixture.LgplTerms, needed[1].Passage)
                && !FullTerms.Holds("Licensed under the Apache License, Version 2.0.", needed[0].Passage),
            "knows which licenses need their full terms, and finds the terms across line breaks, not in a notice");
        tally.Expect(
            Rejects(() => MitTemplate.Validate("Permission is hereby granted"))
                && Rejects(() => MitTemplate.Validate("Copyright 2026")),
            "rejects an MIT text without its copyright line or its permission");
        var reshaped = Fixture.Register.Replace("| Purpose ", string.Empty, StringComparison.Ordinal);
        tally.Expect(
            Message(() => global::Register.Parse(reshaped)) is { } shape
                && shape.Contains("Update tools/Shared/Register.cs", StringComparison.Ordinal),
            "names tools/Shared/Register.cs, which both tools include, when the register's columns change");
        byte[] utf16 = [.. Encoding.Unicode.Preamble, .. Encoding.Unicode.GetBytes("Text.\r\n")];
        tally.Expect(
            TextFile.Decode(utf16, "a") == "Text.\n"
                && TextFile.Decode("A\rB\r\nC\n\n"u8.ToArray(), "a") == "A\nB\nC\n",
            "reads UTF-16 with a byte-order mark, and makes a lone CR and Windows line endings Unix ones");
        tally.Expect(
            Message(() => TextFile.Decode([], "x")) == "x is empty."
                && Message(() => TextFile.Decode([0x41, 0, 0x42], "x")) == "x is not text."
                && Message(() => TextFile.Decode([0x53, 0xE9, 0x0A], "x")) == "x is not UTF-8 text.",
            "rejects an empty text, one with a NUL character and one in Latin-1");
    }

    private static void UpstreamTexts(Tally tally)
    {
        Console.WriteLine("Texts that no package ships:");
        const string Commit = "0123456789abcdef0123456789abcdef01234567";
        var url = Fixture.ApacheUrl;
        bool refused(string to) => Rejects(() => Mapping.Parse(EditMapping(url, to)));
        tally.Expect(
            !refused(url) && refused(url.Replace("https://", "http://", StringComparison.Ordinal))
                && refused(url.Replace("raw.githubusercontent.com/", "example.com/", StringComparison.Ordinal))
                && refused(url.Replace(".com/", ".com.example.org/", StringComparison.Ordinal))
                && refused(url.Replace("githubusercontent.com/", "githubusercontent.org/", StringComparison.Ordinal)),
            "an upstream text comes over https from raw.githubusercontent.com only, not from a host of the same "
                + "length");
        tally.Expect(
            refused(url.Replace(Commit, "main", StringComparison.Ordinal))
                && refused(url.Replace(Commit, "v1.0.0", StringComparison.Ordinal))
                && refused(url.Replace(Commit, Commit[..12], StringComparison.Ordinal))
                && refused(url.Replace(Commit, Commit.ToUpperInvariant(), StringComparison.Ordinal)),
            "its URL names a full commit, never a branch, a tag or a short hash");
        tally.Expect(
            refused(url.Replace("/LICENSE", "/../LICENSE", StringComparison.Ordinal))
                && refused(url + "?raw=1") && refused(url + "#top")
                && refused(url.Replace("/LICENSE", "/%2E%2E/LICENSE", StringComparison.Ordinal))
                && refused(url.Replace("/LICENSE", "\\\\LICENSE", StringComparison.Ordinal)),
            "its URL holds no \"..\", \"?\", \"#\", \"%\" or backslash");
        tally.Expect(
            refused(url.Replace("/LICENSE", "/./LICENSE", StringComparison.Ordinal))
                && refused(url.Replace("/LICENSE-2.0.txt", string.Empty, StringComparison.Ordinal))
                && refused(url + "\\n"),
            "its URL has a path after the commit, no \".\" part and no line break at its end");

        var hash = Fixture.Hash(Fixture.ApacheTerms);
        bool refusedHash(string to) => Rejects(() => Mapping.Parse(EditMapping($", \"sha256\": \"{hash}\"", to)));
        tally.Expect(
            refusedHash(string.Empty) && refusedHash($", \"sha256\": \"{hash.ToUpperInvariant()}\"")
                && refusedHash($", \"sha256\": \"{hash[1..]}\"") && refusedHash(", \"sha256\": 1"),
            "it has a SHA-256 of 64 lowercase hexadecimal digits");
        bool refusedLines(string to) =>
            Rejects(() => Mapping.Parse(EditMapping("\"lines\": [1, 4]", $"\"lines\": {to}")));
        tally.Expect(
            !refusedLines("[2, 2]") && refusedLines("[0, 4]") && refusedLines("[4, 1]") && refusedLines("[1]")
                && refusedLines("[1, 2, 3]") && refusedLines("[\"1\", \"4\"]") && refusedLines("[1.5, 4]")
                && refusedLines("\"1-4\""),
            "a range of lines is [first, last], from line 1, the first not after the last");
        bool refusedTerms(string to) =>
            Rejects(() => Mapping.Parse(EditMapping("\"terms\": \"Apache-2.0\"", $"\"terms\": {to}")));
        tally.Expect(
            !refusedTerms("\"Unicode-3.0\"") && !refusedTerms("\"LGPL-2.1-only\"") && refusedTerms("\"MIT\"")
                && refusedTerms("\"apache-2.0\"") && refusedTerms("\"\"") && refusedTerms("1")
                && refusedTerms("null"),
            "the license whose terms a text carries is one whose full terms the rules recognize, by its identifier");
        const string GraphicsText = "{ \"package\": \"Sample.Graphics\", \"file\": \"LICENSE.txt\" }";
        const string LgplText = "{ \"repository\": \"external/Sample-lgpl/COPYING.LIB\" }";
        string? marked(string text) => Message(() => Mapping.Parse(
            EditMapping(text, text.Replace(" }", ", \"terms\": \"Apache-2.0\" }", StringComparison.Ordinal))));
        tally.Expect(
            marked(GraphicsText) is { } package && package.Contains("only an upstream file", StringComparison.Ordinal)
                && marked(LgplText) is { } file && file.Contains("only an upstream file", StringComparison.Ordinal),
            "only an upstream text is marked as carrying a license's terms");

        // An upstream text with "terms" that lacks its hash, misspells a key or names one twice gets the
        // message of the forms, which names the SHA-256, and not the refusal of the mark.
        const string Mark = "\"terms\": \"Apache-2.0\" }";
        bool namesForms(string from, string to) =>
            Message(() => Mapping.Parse(EditMapping(from, to))) is { } refused
                && refused.Contains("or an upstream file with its SHA-256", StringComparison.Ordinal);
        tally.Expect(
            namesForms($", \"sha256\": \"{hash}\", \"terms\"", ", \"terms\"")
                && namesForms(Mark, "\"terms\": \"Apache-2.0\", \"line\": [1, 2] }")
                && namesForms(Mark, "\"terms\": \"Apache-2.0\", \"terms\": \"Apache-2.0\" }"),
            "an upstream text with the mark but without its hash, with a misspelt key or a key twice names the forms");

        const string RuntimeText = "{ \"runtimePack\": \"Sample.Runtime.App\", \"file\": \"THIRD-PARTY-NOTICES.TXT\" }";
        string twice(string sha256) => EditMapping(
            RuntimeText,
            $"{RuntimeText},\n        {{ \"upstream\": \"{url}\", \"sha256\": \"{sha256}\" }}");
        (string, string)[] listed = [(url, hash), (Fixture.HeaderUrl, Fixture.Hash(Fixture.Header))];
        tally.Expect(
            Rejects(() => Mapping.Parse(twice(new string('0', 64))))
                && Mapping.Parse(twice(hash)).Upstream().SequenceEqual(listed),
            "rejects one URL with two hashes, and lists each upstream text once");

        const string Graphics = "\"Sample.Graphics\": {\n      \"texts\": [";
        string withMissing(string missing) =>
            EditMapping(Graphics, $"\"Sample.Graphics\": {{\n      \"missing\": {missing},\n      \"texts\": [");
        tally.Expect(
            Mapping.Parse(withMissing("[{ \"text\": \"its notices\", \"note\": \"None yet.\" }]"))
                .Components["Sample.Graphics"].Missing.Count == 1
                && Rejects(() => Mapping.Parse(withMissing("[]")))
                && Rejects(() => Mapping.Parse(withMissing("[{ \"text\": \"its notices\" }]"))),
            "lists a text that has no source with a note, and rejects an empty list and a text without a note");
        tally.Expect(
            UpstreamUrl.Lines("/*\n * A\n code in a comment;\n */\n// B\n\nint x;\n", new LineRange(1, 6), "f")
                == "/*\n * A\n code in a comment;\n */\n// B\n"
                && Rejects(() => UpstreamUrl.Lines("// A\n*/ x;\n", new LineRange(1, 2), "f"))
                && Rejects(() => UpstreamUrl.Lines("/* a */ int x;\n", new LineRange(1, 1), "f"))
                && Rejects(() => UpstreamUrl.Lines("/* a\n */ int x;\n", new LineRange(1, 2), "f")),
            "a range takes the lines of a /* */ comment, and refuses a line outside a comment and code after */");
        char[] terminators = [(char)0x85, (char)0x2028, (char)0x2029];
        string[] terminated = [.. terminators.Select(terminator => $"// a{terminator}int x;\n")];
        tally.Expect(
            terminated.All(text => Message(() => UpstreamUrl.Lines(text, new LineRange(1, 1), "f")) is { } refusal
                && refusal.Contains("holds NEL, LS or PS", StringComparison.Ordinal)),
            "a range refuses a // line that holds NEL, LS or PS, at which C# ends the comment");
        const string Blank = "// a\n\n\nint x;\n";
        tally.Expect(
            UpstreamUrl.Lines(Blank, new LineRange(1, 3), "f") == "// a\n"
                && Message(() => UpstreamUrl.Lines(Blank, new LineRange(2, 3), "f")) is { } blank
                && blank.Contains("are blank", StringComparison.Ordinal),
            "a range that ends in blank lines gives a text with one line break at its end, and blank lines alone fail");

        // The scan starts at line 1: a range may start inside a /* */ comment of the header, whose state
        // the scan takes from there, but a range that starts inside a string or below code fails, and so
        // does code after the comment's end.
        static string? refusal(string text, int first, int last) =>
            Message(() => UpstreamUrl.Lines(text, new LineRange(first, last), "f"));
        tally.Expect(
            refusal("/* a\n// b */ int x;\n", 2, 2) is not null
                && refusal("var s = @\"\n// not a comment\n\";\n", 2, 2) is not null
                && refusal("// a\n\nint x;\n// b\n", 4, 4) is not null
                && refusal("// a\n// b\n", 2, 2) is null
                && refusal("/*\n * a\n */\nint x;\n", 2, 3) is null,
            "a range lies within the file's leading comment header: it may start inside a comment of the header, "
                + "not inside a string or below code");
        tally.Expect(
            refusal("// a" + (char)0x2028 + "x\n// b\n", 2, 2) is { } terminatorBefore
                && terminatorBefore.Contains("before the range 2 to 2, holds NEL, LS or PS", StringComparison.Ordinal)
                && refusal("// a \\\n// b\n", 2, 2) is { } spliceBefore
                && spliceBefore.Contains("before the range 2 to 2, ends in a backslash", StringComparison.Ordinal)
                && refusal("// a ??/ b\n// c\n", 2, 2) is { } trigraphBefore
                && trigraphBefore.Contains("before the range 2 to 2, ends in a backslash", StringComparison.Ordinal),
            "a range refuses NEL, LS or PS, a backslash at the end of a line and ??/ on the lines above it too");
        tally.Expect(
            refusal("/* a *\\\n/ int x;\n", 1, 2) is { } splice
                && splice.Contains("ends in a backslash", StringComparison.Ordinal)
                && refusal("/* a *??/\n/ int x;\n", 1, 2) is { } trigraph
                && trigraph.Contains("holds ??/", StringComparison.Ordinal)
                && refusal("// a \\  \n// b\n", 1, 1) is not null,
            "a range refuses a line that ends in a backslash or holds the trigraph ??/, which C splices with the next");
        tally.Expect(
            UpstreamUrl.Lines("// a\n   \n\t\nint x;\n", new LineRange(1, 3), "f") == "// a\n"
                && UpstreamUrl.Lines("// a  \n\n", new LineRange(1, 2), "f") == "// a  \n",
            "a range leaves out the lines of white space at its end, and keeps the white space at the end of its last "
                + "notice line");
    }

    private static void FullyRegistered(Tally tally, string root)
    {
        Console.WriteLine("A fully registered bundle:");
        var fixture = Fixture.Create(root, "registered");
        var before = fixture.Snapshot();
        var result = fixture.Generate(Mode.Ios);
        tally.Expect(
            result.Failures.Count == 0 && result.Warnings.Count == 0 && result.Outputs is not null,
            "passes in ios mode");
        tally.Expect(fixture.Generate(Mode.Release).Failures.Count == 0, "passes in release mode");
        string[] expected =
        [
            "Sample.Apache",
            "Sample.Fonts",
            "Sample.Graphics",
            "Sample.Lgpl",
            "Sample.Platform",
            "Sample.Runtime",
            "Sample.Ui",
            "SteamKit2",
        ];
        tally.Expect(
            result.Components.Select(component => component.Name).SequenceEqual(expected),
            "lists each component once, in name order");
        string[] mainComponents =
        [
            "Paneport",
            "Sample.Runtime",
            "Sample.Platform",
            "Sample.Ui",
            "Sample.Graphics",
            "Sample.Apache",
            "SteamKit2",
            "Sample.Fonts",
        ];
        var main = result.Coverage.Single(attribution => attribution.File == "Sample").Components;
        tally.Expect(
            main.ToHashSet().SetEquals(mainComponents),
            "maps the main executable to the mapping's components and to that of every package in the lock file, "
                + "SteamKit2 among them");
        tally.Expect(
            result.Coverage.Single(attribution => attribution.File == "aot-instances.aotdata.arm64").Components
                .SequenceEqual(main),
            "maps a file of the main executable's paths, such as the data of generic instances, as the executable");
        tally.Expect(result.UnusedRules.SequenceEqual(["Unused/**"]), "reports a rule that matches no file");
        string[] others =
        [
            "Sample.Graphics: ACKNOWLEDGEMENTS.md of the package Sample.Graphics 2.0.0",
            "Sample.Graphics: CREDITS of the package Sample.Graphics 2.0.0",
            "Sample.Graphics: EULA.txt of the package Sample.Graphics 2.0.0",
            "Sample.Graphics: PATENTS of the package Sample.Graphics 2.0.0",
            "Sample.Graphics: legal.txt of the package Sample.Graphics 2.0.0",
            "Sample.Ui: native/THIRD-PARTY.md of the package Sample.Ui 1.0.0",
            "SteamKit2: 3rd party.txt of the package SteamKit2 4.0.0",
        ];
        tally.Expect(
            result.OtherLicenseFiles.SequenceEqual(others),
            "reports the license, notice, third-party, EULA, patent, credit, acknowledgement and legal files of a "
                + "component's packages that are not among its texts");
        string[] uiSources =
            ["Sample.Ui 1.0.0", "Sample.Ui.Extra 1.0.0", "sample/ui 89abcdef0123456789abcdef0123456789abcdef"];
        tally.Expect(
            result.Components.SingleOrDefault(component => component.Name == "Sample.Ui") is { } ui
                && ui.Sources.SequenceEqual(uiSources),
            "lists a component's sources sorted, and an upstream text by its repository and commit");
        tally.Expect(
            result.Components.SingleOrDefault(component => component.Name == "Sample.Apache") is { } apache
                && apache.Sources.SequenceEqual(["Sample.Apache 3.0.0"])
                && apache.Texts.Any(text => text.Title == "Apache-2.0 license, copied from LICENSE-2.0.txt of "
                    + "sample/terms at commit 0123456789abcdef0123456789abcdef01234567"),
            "titles a text that only carries a license's terms by that license, naming the file it is copied "
                + "from, and leaves that file's repository out of the sources");
        if (result.Outputs is not { } outputs)
        {
            return;
        }

        var notices = Path.Combine(fixture.App, Notices.Folder);
        Output.Write(notices, outputs);
        Output.Write(fixture.Out, outputs);
        Output.Write(fixture.Out, outputs);
        var after = fixture.Snapshot();
        var added = after.Except(before).ToList();
        tally.Expect(
            added.All(file => file.StartsWith("repo/src/Sample/bin/Sample.app/licenses/", StringComparison.Ordinal)
                || file.StartsWith("repo/build/notices/", StringComparison.Ordinal))
                && before.All(after.Contains) && added.Count == 2 * (2 + expected.Length),
            "writes only into licenses/ in the bundle and the output folder, and replaces its own earlier output "
                + "there");
        tally.Expect(
            Directory.EnumerateFiles(notices, "*", SearchOption.AllDirectories)
                .All(path => !File.ReadAllBytes(path).AsSpan().StartsWith(Encoding.UTF8.Preamble)),
            "writes every output without a byte-order mark");
        tally.Expect(
            fixture.Generate(Mode.Release).Failures.Count == 0,
            "a later run in release mode leaves out the notices that it wrote into the bundle");

        var markdown = File.ReadAllText(Path.Combine(notices, Notices.NoticesFile));
        var tableOrder = markdown.Split('\n')
            .Select(line => TableRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value)
            .ToList();
        var indexText = File.ReadAllText(Path.Combine(notices, Notices.IndexFile));
        using var index = JsonDocument.Parse(indexText);
        var indexComponents = index.RootElement.GetProperty("components").EnumerateArray().ToList();
        var indexOrder = indexComponents.Select(component => component.GetProperty("name").GetString()).ToList();
        var textFiles = Directory.EnumerateFiles(Path.Combine(notices, Notices.TextsFolder))
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .ToList();
        var textOrder = textFiles.Select(file => file[3..^4]).ToList();
        var numbered = textFiles.Select(
            (file, position) => file.StartsWith($"{position + 1:00}-", StringComparison.Ordinal));
        tally.Expect(
            tableOrder.SequenceEqual(expected) && indexOrder.SequenceEqual(expected)
                && textOrder.SequenceEqual(expected.Select(Notices.Slug)) && numbered.All(match => match),
            "THIRD_PARTY_NOTICES.md, the texts and the index list the same components in the same order");
        var paths = textFiles.Select(file => $"{Notices.TextsFolder}/{file}").ToList();
        var headings = markdown.Split('\n')
            .Select(line => SectionHeading().Match(line))
            .Where(match => match.Success)
            .Select(match => $"{match.Groups[1].Value}. {match.Groups[2].Value}");
        var links = markdown.Split('\n')
            .Select(line => TextLink().Match(line))
            .Where(match => match.Success)
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value));
        tally.Expect(
            headings.SequenceEqual(expected.Select((name, position) => $"{position + 1}. {name}"))
                && links.SequenceEqual(paths.Select(path => (path, path))),
            "each section of THIRD_PARTY_NOTICES.md has its number and component in that order, and links its text");
        var sectionTitles = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var section = string.Empty;
        foreach (var line in markdown.Split('\n'))
        {
            if (SectionHeading().Match(line) is { Success: true } heading)
            {
                section = heading.Groups[1].Value.PadLeft(2, '0');
                sectionTitles[section] = [];
            }
            else if (section.Length > 0 && line.StartsWith("### ", StringComparison.Ordinal))
            {
                sectionTitles[section].Add(line[4..]);
            }
        }

        tally.Expect(
            sectionTitles.Count == textFiles.Count
                && textFiles.All(file => sectionTitles.GetValueOrDefault(file[..2]) is { } titles
                    && File.ReadAllText(Path.Combine(notices, Notices.TextsFolder, file)).Split('\n')
                        .Select(line => TextTitle().Match(line))
                        .Where(match => match.Success)
                        .Select(match => match.Groups[1].Value)
                        .SequenceEqual(titles)),
            "each section of THIRD_PARTY_NOTICES.md holds, as ### headings, the titles of the texts of the file of its "
                + "number");

        // Each section's License and Sources lines against the index, and the body of each of its texts
        // against the same block of the text file of its number.
        var markdownLines = markdown.Split('\n');
        var sectionLines = new Dictionary<string, (string License, string Sources)>(StringComparer.Ordinal);
        var sectionBodies = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        section = string.Empty;
        for (var at = 0; at < markdownLines.Length; at++)
        {
            var current = markdownLines[at];
            if (SectionHeading().Match(current) is { Success: true } heading)
            {
                section = heading.Groups[1].Value.PadLeft(2, '0');
                sectionLines[section] = (string.Empty, string.Empty);
                sectionBodies[section] = [];
            }
            else if (section.Length > 0 && current.StartsWith("- License: ", StringComparison.Ordinal))
            {
                sectionLines[section] = sectionLines[section] with { License = current["- License: ".Length..] };
            }
            else if (section.Length > 0 && current.StartsWith("- Sources: ", StringComparison.Ordinal))
            {
                sectionLines[section] = sectionLines[section] with { Sources = current["- Sources: ".Length..] };
            }
            else if (section.Length > 0 && current.StartsWith("### ", StringComparison.Ordinal)
                && at + 2 < markdownLines.Length && markdownLines[at + 2].EndsWith("text", StringComparison.Ordinal))
            {
                var fence = markdownLines[at + 2][..^"text".Length];
                var end = Array.IndexOf(markdownLines, fence, at + 3);
                var body = end < 0 ? string.Empty : string.Join('\n', markdownLines[(at + 3)..end]) + "\n";
                sectionBodies[section].Add(body);
                at = end < 0 ? markdownLines.Length : end;
            }
        }

        // A text file's blocks: each starts two lines after its ===== heading and ends before the blank
        // line of the next heading, or at the end of the file.
        static List<string> bodies(string content)
        {
            var lines = content.Split('\n');
            var starts = Enumerable.Range(0, lines.Length).Where(at => TextTitle().IsMatch(lines[at])).ToList();
            var ends = starts.Skip(1).Select(start => start - 1).Append(lines.Length - 1).ToList();
            return [.. starts.Select((start, index) => string.Join('\n', lines[(start + 2)..ends[index]]) + "\n")];
        }

        static string sourcesOf(JsonElement component) =>
            string.Join(", ", component.GetProperty("sources").EnumerateArray().Select(source => source.GetString()));
        tally.Expect(
            sectionLines.Count == indexComponents.Count
                && indexComponents.Select((component, position) => (component, key: $"{position + 1:00}")).All(entry =>
                    sectionLines.GetValueOrDefault(entry.key) is var (license, sources)
                        && license == entry.component.GetProperty("license").GetString()
                        && sources == sourcesOf(entry.component)),
            "each section of THIRD_PARTY_NOTICES.md gives the License and Sources that the index gives its component");
        tally.Expect(
            textFiles.All(file => sectionBodies.GetValueOrDefault(file[..2]) is { } blocks
                && bodies(File.ReadAllText(Path.Combine(notices, Notices.TextsFolder, file))).SequenceEqual(blocks)),
            "each text of a section of THIRD_PARTY_NOTICES.md is the same as its block in the text file of its number");
        tally.Expect(
            indexComponents.Select(component => component.GetProperty("text").GetString()).SequenceEqual(paths),
            "the index names each component's text");
        var rows = global::Register.Parse(Fixture.Register);
        tally.Expect(
            indexComponents.All(component =>
                rows.Single(row => row.Component == component.GetProperty("name").GetString()) is var row
                    && component.GetProperty("license").GetString() == row.License
                    && component.GetProperty("category").GetString() == row.Category),
            "the index gives each component the License text and the category of its register row");

        var texts = textFiles.ToDictionary(
            file => file[3..^4],
            file => File.ReadAllText(Path.Combine(notices, Notices.TextsFolder, file)));
        tally.Expect(
            Holds(texts["sample-ui"], "Copyright 2020 Sample UI Authors", "Permission is hereby granted")
                && !texts["sample-ui"].Contains("Sample Template Holder", StringComparison.Ordinal),
            "builds the MIT text of a package without a license file from the MIT text and the package's copyright "
                + "line");
        tally.Expect(
            Holds(texts["sample-graphics"], "Sample graphics.", "Sample graphics third-party notices.")
                && Holds(texts["sample-runtime"], "Sample runtime license text.\nSecond line.\nThird line.\n")
                && Holds(texts["sample-runtime"], "Sample runtime notices.")
                && Holds(texts["sample-lgpl"], "Sample LGPL library."),
            "takes the texts and third-party notices of packages, runtime packs and repository files, UTF-16 too, "
                + "with Unix line endings in place of Windows ones and lone CRs");
        tally.Expect(
            Holds(texts["sample-apache"], "TERMS AND CONDITIONS FOR USE", "LICENSE-2.0.txt of sample/terms at commit")
                && Holds(texts["sample-ui"], "// Copyright (c) Sample Header Holder.\n/* Licensed", "a sample. */\n")
                && Holds(texts["sample-ui"], "lines 1 to 4 of src/Header.cs of sample/ui at commit 89abcdef")
                && !texts["sample-ui"].Contains("namespace Sample", StringComparison.Ordinal),
            "takes an upstream text from build/upstream/ by its SHA-256, and of a range only its lines");
        tally.Expect(
            texts["sample-ui"].EndsWith("a sample. */\n", StringComparison.Ordinal)
                && texts.Values.All(text => !text.EndsWith("\n\n", StringComparison.Ordinal)),
            "ends a range whose last line is blank, as every text, with one line break");
        tally.Expect(
            Holds(texts["sample-apache"], "Sample Apache NOTICE text.")
                && Holds(markdown, "Sample Apache NOTICE text."),
            "keeps the Apache-2.0 NOTICE file");
        var platform = texts["sample-platform"];
        tally.Expect(
            platform.Split("Sample platform license text.").Length == 2
                && Holds(platform, "Sample.Platform.Pack 7.0.0; the same text is LICENSE of the runtime pack")
                && Holds(platform, "Sample.Platform.Native 7.0.0 ====="),
            "writes a text that two sources hold alike once, under a heading that names both");
        tally.Expect(
            Holds(texts["sample-fonts"], Fixture.Credit) && Holds(markdown, Fixture.Credit)
                && Holds(indexText, Fixture.Credit),
            "keeps the FreeType credit in all three outputs");
        tally.Expect(
            Holds(markdown, "\n````text\nSample graphics.\n```\nA run of three backticks.\n```\n````\n"),
            "fences a text with more backticks than the longest run in it");
        tally.Expect(
            markdown.StartsWith(
                $"# Third-party notices\n\n{Fixture.DisplayName} 1.2.3 (45) contains",
                StringComparison.Ordinal)
                && Holds(markdown, "Sample.Ui.Extra 1.0.0", "Sample.Runtime.Pack 6.0.0")
                && !markdown.Contains(fixture.Root, StringComparison.Ordinal)
                && !texts.Values.Any(text => text.Contains(fixture.Root, StringComparison.Ordinal)),
            "names the app, its version and each component's sources, and never a local path");

        var xmlFixture = Fixture.Create(root, "xml-info");
        xmlFixture.WriteInfo("Sample", binary: false);
        tally.Expect(
            xmlFixture.Generate(Mode.Release).Failures.Count == 0,
            "reads an XML Info.plist as well as a binary one");

        File.WriteAllText(Path.Combine(notices, Notices.IndexFile), "{}");
        tally.Expect(
            Throws(() => Output.Write(notices, outputs)) && File.Exists(Path.Combine(notices, Notices.NoticesFile)),
            "refuses to replace a licenses/ folder that it did not write, and leaves it as it is");
        var repository = fixture.Repository;
        tally.Expect(
            Rejects(() => Output.EnsureBuildOutput(repository, Path.Combine(repository, "docs", "notices"), "--out"))
                && Rejects(() => Output.EnsureBuildOutput(repository, repository, "--out"))
                && !Rejects(() => Output.EnsureBuildOutput(repository, fixture.Out, "--out"))
                && !Rejects(() => Output.EnsureBuildOutput(repository, fixture.App, "--app"))
                && !Rejects(() => Output.EnsureBuildOutput(repository, Path.Combine(fixture.Root, "out"), "--out")),
            "writes into build/, bin/, obj/ or outside the repository, never elsewhere in it");
        tally.Expect(
            Rejects(() => Output.EnsureBuildOutput(repository, Path.Combine("DOCS", "notices"), "--out"))
                && (!(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                    || Rejects(() => Output.EnsureBuildOutput(
                        repository,
                        Path.Combine(repository.ToUpperInvariant(), "docs", "notices"),
                        "--out"))),
            "refuses a tracked folder named in another case, and on Windows and macOS also below the repository named "
                + "in another case");
    }

    // Both orders that release-engineering.md promises, through Generate: the components by name
    // without regard to case first, so that a name in lower case sorts among the others, and each
    // component's sources without regard to case.
    private static void CaseOrder(Tally tally, string root)
    {
        Console.WriteLine("Order without regard to case:");
        var fixture = Fixture.Create(root, "case-order");
        fixture.RegisterText = Fixture.Register + "\n| sample.lower | A library in lower case | MIT | Allowed |";
        fixture.MapText = Fixture.Map.Replace(
            "\"Sample.Lgpl.Static\": \"Sample.Lgpl\"",
            "\"Sample.Lgpl.Static\": \"Sample.Lgpl\",\n    \"alpha.Lower\": \"sample.lower\",\n"
                + "    \"Beta.Lower\": \"sample.lower\"",
            StringComparison.Ordinal);
        fixture.MappingText = EditMapping(
            "\"Sample.Lgpl\": {",
            "\"sample.lower\": { \"texts\": [{ \"package\": \"alpha.Lower\", \"license\": \"MIT\" }] },\n"
                + "    \"Sample.Lgpl\": {");
        foreach (var id in new[] { "alpha.Lower", "Beta.Lower" })
        {
            fixture.Locked.Add(new LockedPackage(id, "1.0.0"));
            fixture.Package(id, "1.0.0", "expression", "MIT", "Copyright 2026 Sample Lower Authors");
        }

        if (fixture.Generate(Mode.Release) is not { Outputs: { } outputs } result)
        {
            tally.Expect(false, "the fixture of the order checks generates its notices");
            return;
        }

        string[] expected =
        [
            "Sample.Apache",
            "Sample.Fonts",
            "Sample.Graphics",
            "Sample.Lgpl",
            "sample.lower",
            "Sample.Platform",
            "Sample.Runtime",
            "Sample.Ui",
            "SteamKit2",
        ];
        var folder = Path.Combine(fixture.Root, "notices");
        Output.Write(folder, outputs);
        var markdown = File.ReadAllText(Path.Combine(folder, Notices.NoticesFile));
        var table = markdown.Split('\n')
            .Select(line => TableRow().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[1].Value);
        var headings = markdown.Split('\n')
            .Select(line => SectionHeading().Match(line))
            .Where(match => match.Success)
            .Select(match => match.Groups[2].Value);
        using var index = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, Notices.IndexFile)));
        var entries = index.RootElement.GetProperty("components").EnumerateArray().ToList();
        var texts = Directory.EnumerateFiles(Path.Combine(folder, Notices.TextsFolder))
            .Select(path => Path.GetFileName(path))
            .Order(StringComparer.Ordinal)
            .Select(file => file[3..^4]);
        tally.Expect(
            result.Components.Select(component => component.Name).SequenceEqual(expected)
                && table.SequenceEqual(expected) && headings.SequenceEqual(expected)
                && entries.Select(entry => entry.GetProperty("name").GetString()).SequenceEqual(expected)
                && texts.SequenceEqual(expected.Select(Notices.Slug)),
            "puts a component whose name is in lower case in its place without regard to case, in the table, the "
                + "headings, the index and the texts");
        string[] sources = ["alpha.Lower 1.0.0", "Beta.Lower 1.0.0"];
        var lower = entries.Single(entry => entry.GetProperty("name").GetString() == "sample.lower");
        tally.Expect(
            lower.GetProperty("sources").EnumerateArray().Select(source => source.GetString()).SequenceEqual(sources)
                && Holds(markdown, "- Sources: alpha.Lower 1.0.0, Beta.Lower 1.0.0\n"),
            "sorts a component's sources without regard to case, in the index and in THIRD_PARTY_NOTICES.md");
    }

    private static void Outputs(Tally tally, string root)
    {
        Console.WriteLine("Output folders and links:");
        var fixture = Fixture.Create(root, "outputs");
        if (fixture.Generate(Mode.Ios).Outputs is not { } outputs)
        {
            tally.Expect(false, "the fixture of the output checks generates its notices");
            return;
        }

        // The tool's own output, all of it, after a write that it refused.
        bool intact(string folder) =>
            File.Exists(Path.Combine(folder, Notices.NoticesFile))
                && File.Exists(Path.Combine(folder, Notices.IndexFile))
                && outputs.Texts.All(text => File.Exists(Path.Combine(folder, text.Path)));
        bool keeps(string name, string file, bool hidden = false)
        {
            var folder = Path.Combine(fixture.Root, name);
            Output.Write(folder, outputs);
            var path = Path.Combine(folder, file);
            Fixture.WriteFile(path, "kept");
            if (hidden)
            {
                File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.Hidden);
            }

            return Throws(() => Output.Write(folder, outputs)) && File.Exists(path) && intact(folder);
        }

        tally.Expect(
            keeps("beside", "keep.txt") && keeps("below", Path.Combine("sub", "keep.txt"))
                && keeps("among", Path.Combine(Notices.TextsFolder, "notes.md")),
            "refuses a folder of its own output that holds another file too, beside, below it or among the texts, "
                + "and keeps that file and its own output");
        tally.Expect(
            keeps("hidden", ".keep", hidden: true)
                && keeps("hidden-text", Path.Combine(Notices.TextsFolder, ".keep"), hidden: true)
                && keeps("one-digit", Path.Combine(Notices.TextsFolder, "1-x.txt"))
                && keeps("upper-case", Path.Combine(Notices.TextsFolder, "01-X.txt"))
                && keeps("other-ending", Path.Combine(Notices.TextsFolder, "01-x.txt.bak"))
                && keeps("text-folder", Path.Combine(Notices.TextsFolder, "02-folder.txt", "keep.txt")),
            "refuses a hidden entry, also among the texts, and among the texts a name other than NN-<name>.txt and a "
                + "folder, and keeps them");

        // A link among the top-level entries in place of texts/, and a link among the texts to a folder:
        // the folders that they lead to keep their files.
        var victim = Path.Combine(fixture.Root, "victim");
        Fixture.WriteFile(Path.Combine(victim, "01-victim.txt"), "kept");
        var topLink = Path.Combine(fixture.Root, "top-link");
        Output.Write(topLink, outputs);
        Directory.Delete(Path.Combine(topLink, Notices.TextsFolder), recursive: true);
        var topLinked = Fixture.Link(Path.Combine(topLink, Notices.TextsFolder), victim);
        var textLink = Path.Combine(fixture.Root, "text-link");
        Output.Write(textLink, outputs);
        var inner = Path.Combine(fixture.Root, "inner");
        Fixture.WriteFile(Path.Combine(inner, "01-x.txt"), "kept");
        var textLinked = Fixture.Link(Path.Combine(textLink, Notices.TextsFolder, "99-folder.txt"), inner);
        tally.Expect(
            topLinked && textLinked
                && Throws(() => Output.Write(topLink, outputs)) && File.Exists(Path.Combine(victim, "01-victim.txt"))
                && File.Exists(Path.Combine(topLink, Notices.NoticesFile))
                && File.Exists(Path.Combine(topLink, Notices.IndexFile))
                && Throws(() => Output.Write(textLink, outputs)) && File.Exists(Path.Combine(inner, "01-x.txt"))
                && intact(textLink),
            "refuses a link among its own entries, also one among the texts, and keeps what they lead to");

        FileLinkAmongTexts(tally, Path.Combine(fixture.Root, "file-link"), outputs, intact, Fixture.FileLink);

        // Where a link cannot be made, either check is skipped on Windows and fails on macOS and Linux,
        // and never counts as passed: each runs again with a link maker that always fails.
        var forcedFile = new Tally(TextWriter.Null);
        var forcedFolder = Path.Combine(fixture.Root, "forced-file-link");
        FileLinkAmongTexts(forcedFile, forcedFolder, outputs, intact, (_, _) => false);
        var forcedRelative = new Tally(TextWriter.Null);
        RelativeLinks(forcedRelative, Fixture.Create(root, "forced-relative-links").Repository, (_, _, _) => false);
        tally.Expect(
            new[] { forcedFile, forcedRelative }.All(forced => OperatingSystem.IsWindows()
                ? forced.Total == 0 && forced.Skipped == 1
                : forced.Total == 1 && forced.Failed == 1),
            "a check whose link cannot be made is skipped on Windows and fails on macOS and Linux, never passed");
        // Only the index differs from the tool's own output: another format.
        var other = Path.Combine(fixture.Root, "other-format");
        Output.Write(other, outputs);
        File.WriteAllText(Path.Combine(other, Notices.IndexFile), "{ \"format\": 7, \"components\": [] }");
        var otherIndex = Path.Combine(other, Notices.IndexFile);
        tally.Expect(
            Throws(() => Output.Write(other, outputs)) && File.Exists(Path.Combine(other, Notices.NoticesFile))
                && File.ReadAllText(otherIndex).Contains("\"format\": 7", StringComparison.Ordinal) && intact(other),
            "refuses a folder whose index is of another format, and leaves it as it is");

        var fewer = Fixture.Create(root, "fewer");
        fewer.Locked.RemoveAll(package => package.Id == "Sample.Fonts.Native");
        File.Delete(Path.Combine(fewer.App, "Frameworks", "libfonts.framework", "libfonts"));
        var shorter = fewer.Generate(Mode.Ios).Outputs;
        var rewritten = Path.Combine(fixture.Root, "rewritten");
        Output.Write(rewritten, outputs);
        if (shorter is not null)
        {
            Output.Write(rewritten, shorter);
        }

        var left = Directory.EnumerateFiles(Path.Combine(rewritten, Notices.TextsFolder))
            .Select(path => $"{Notices.TextsFolder}/{Path.GetFileName(path)}")
            .Order(StringComparer.Ordinal);
        tally.Expect(
            shorter is { Texts.Count: 7 } && outputs.Texts.Count == 8
                && left.SequenceEqual(shorter.Texts.Select(text => text.Path).Order(StringComparer.Ordinal)),
            "a later run with one component fewer replaces the texts, and leaves no text of the earlier run behind");

        string[] devicePaths =
        [
            @"\\?\C:\repo\docs\notices",
            @"\\.\C:\repo\docs\notices",
            @"\??\C:\repo\docs\notices",
            @"\??\UNC\server\share\notices",
            @"\\?\UNC\server\share\notices",
            @"\\server\share\notices",
            "//server/share/x",
        ];
        tally.Expect(
            devicePaths.All(path => Rejects(() => Output.EnsureBuildOutput(fixture.Repository, path, "--out"))),
            "refuses device paths, \\\\?\\, \\\\.\\ and \\??\\ among them, and network paths");

        // On Windows a ':' after the drive names a stream: docs\A::$INDEX_ALLOCATION names, and creates,
        // the folder docs\A inside the tracked docs/, and docs::$INDEX_ALLOCATION the folder docs itself;
        // Path.Combine would also start over at a part such as C:x. On macOS and Linux a backslash is no
        // separator, so each of these paths is one name at the repository root, and the fourth starts with
        // the root-level name docs\new\; there the build-folder rule refuses them.
        string[] colonPaths =
        [
            @"docs\A::$INDEX_ALLOCATION",
            "docs::$INDEX_ALLOCATION",
            @"docs\Z:$I30:$INDEX_ALLOCATION",
            $@"docs\new\{fixture.Repository}\build\y",
            @"docs\new\C:x",
        ];
        bool refusedAsDevice(string path) =>
            Message(() => Output.EnsureBuildOutput(fixture.Repository, path, "--out")) is { } refusal
                && refusal.Contains(
                    OperatingSystem.IsWindows() ? "is a device or network path" : "in the repository, outside build/",
                    StringComparison.Ordinal);
        Directory.CreateDirectory(Path.Combine(fixture.Repository, "docs", "new"));
        var realRepository = Output.RealPath(fixture.Repository);
        bool keptUnder(params string[] parts) =>
            Output.RealPath(Path.Join([fixture.Repository, .. parts])) == Path.Join([realRepository, .. parts]);
        tally.Expect(
            colonPaths.All(refusedAsDevice)
                && (!OperatingSystem.IsWindows()
                    || (keptUnder("docs", "new", "C:x") && keptUnder("docs", "no", "C:x"))),
            "refuses on Windows a path with a ':' after its drive as a device path, and resolves a part such as C:x "
                + "below the folder that holds it; on macOS and Linux the build-folder rule refuses the five paths");

        // Links that the kernel of macOS and Linux follows part by part, from a made-up tree: build/a
        // leads to ../docs/x, and build/j to a/../y, which is docs/y and not build/y.
        var tree = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["/repo/build/a"] = "../docs/x",
            ["/repo/build/j"] = "a/../y",
            ["/repo/build/full"] = "/repo/docs",
            ["/repo/build/loop"] = "loop",
            ["/repo/build/k"] = "./../docs",
            ["/repo/build/s"] = "sub/",
            ["/repo/build/t"] = "s/../../docs",
        };
        string physical(string path) => Output.PhysicalPath(path, part => tree.GetValueOrDefault(part));
        tally.Expect(
            physical("/repo/build/j") == "/repo/docs/y" && physical("/repo/build/j/x") == "/repo/docs/y/x"
                && physical("/repo/build/full/n") == "/repo/docs/n"
                && physical("/repo/build/other") == "/repo/build/other"
                && physical("/repo/build/k/x") == "/repo/docs/x" && physical("/repo/build/t/x") == "/repo/docs/x"
                && physical("/repo/../../x") == "/x" && Throws(() => physical("/repo/build/loop")),
            "follows a link's target part by part, so that a .. after a link leads to the parent of its target, and "
                + "refuses a loop of links");

        // Links: on macOS and Linux symbolic links, on Windows junctions where symbolic links need a right.
        var linked = Fixture.Create(root, "linked");
        var outside = Path.Combine(linked.Root, "outside");
        Fixture.WriteFile(Path.Combine(outside, "inside.txt"), "behind a link");
        var build = Path.Combine(linked.Repository, "build");
        var made = Fixture.Link(Path.Combine(linked.App, "Linked"), outside)
            && Fixture.Link(Path.Combine(build, "into-docs"), Path.Combine(linked.Repository, "docs"))
            && Fixture.Link(Path.Combine(build, "out-link"), Path.Combine(build, "real"));
        var files = Bundle.Read(linked.App).Files;
        tally.Expect(
            made && files.Contains("Linked")
                && !files.Any(file => file.StartsWith("Linked/", StringComparison.Ordinal)),
            "lists a link in the bundle as a file, and never follows it");
        var throughLink = Path.Combine("build", "into-docs", "x");
        var plain = Path.Combine("build", "notices");
        tally.Expect(
            made
                && Rejects(() => Output.EnsureBuildOutput(linked.Repository, throughLink, "--out"))
                && !Rejects(() => Output.EnsureBuildOutput(linked.Repository, plain, "--out")),
            "follows a link under build/ to its target, and refuses one that leads into the repository outside build/");
        var bundleLink = Path.Combine(linked.App, Notices.Folder);
        tally.Expect(
            Fixture.Link(bundleLink, Path.Combine(build, "elsewhere")) && Throws(() => Output.Check(bundleLink))
                && Throws(() => Output.Write(Path.Combine(build, "out-link"), outputs)),
            "refuses a licenses/ folder in the bundle, or an output folder, that is a link");

        RelativeLinks(tally, Fixture.Create(root, "relative-links").Repository, Fixture.RelativeLink);
    }

    // A link among the texts to a file. Windows makes one only with a right that the self-test does not
    // ask for, so there the check is skipped when the link cannot be made; elsewhere it fails then.
    private static void FileLinkAmongTexts(
        Tally tally,
        string folder,
        NoticeOutputs outputs,
        Func<string, bool> intact,
        Func<string, string, bool> makeFileLink)
    {
        Output.Write(folder, outputs);
        var outsideText = folder + "-target.txt";
        Fixture.WriteFile(outsideText, "kept");
        const string Check = "refuses a link to a file among the texts, and keeps its target and its own output";
        if (makeFileLink(Path.Combine(folder, Notices.TextsFolder, "01-x.txt"), outsideText))
        {
            tally.Expect(
                Throws(() => Output.Write(folder, outputs)) && File.ReadAllText(outsideText) == "kept"
                    && intact(folder),
                Check);
        }
        else if (OperatingSystem.IsWindows())
        {
            tally.Skip(Check, "no file link without a right");
        }
        else
        {
            tally.Expect(false, $"{Check}: the link could not be made");
        }
    }

    // The made-up tree of the output checks on disk, where the kernel follows the links: on macOS and
    // Linux only, since Windows makes a relative link only with a right that the self-test does not ask for.
    private static void RelativeLinks(Tally tally, string relative, Func<string, string, string, bool> makeRelativeLink)
    {
        bool refusedThrough(params string[] parts) =>
            Message(() => Output.EnsureBuildOutput(relative, Path.Combine(parts), "--out")) is { } refusal
                && refusal.Contains("leads to docs/y", StringComparison.Ordinal);
        bool relativeLink(string name, string target, string folder) =>
            makeRelativeLink(Path.Combine(relative, "build", name), target, Path.Combine(relative, "docs", folder));
        const string Check =
            "on macOS and Linux, refuses build/j when it links to a/../y and build/a to ../docs/x, since that is "
                + "docs/y";
        if (OperatingSystem.IsWindows())
        {
            tally.Skip(Check, "no relative link without a right");
        }
        else
        {
            tally.Expect(
                relativeLink("a", "../docs/x", "x") && relativeLink("j", "a/../y", "y")
                    && refusedThrough("build", "j") && refusedThrough("build", "j", "x"),
                Check);
        }
    }

    private static void CommandLine(Tally tally, string root)
    {
        Console.WriteLine("The command line:");
        string[] good = ["--mode", "release", "--project", "a.csproj", "--app", "A.app", "--out", "build/x"];
        string[] unknownMode = ["--mode", "debug", "--project", "a.csproj", "--app", "A.app"];
        string[] noApp = ["--mode", "ios", "--project", "a.csproj"];
        string[] twice = [.. good, "--out", "build/y"];
        string[] noValue = ["--mode", "ios", "--project", "a.csproj", "--app"];
        tally.Expect(
            Options.Parse(good) == new Options(Mode.Release, "a.csproj", "A.app", "build/x")
                && Options.Parse(unknownMode) is null && Options.Parse(noApp) is null && Options.Parse(twice) is null
                && Options.Parse(noValue) is null,
            "reads its options, and rejects an unknown mode, a missing --app, an option given twice and one without "
                + "a value");

        var cli = Fixture.Create(root, "cli");
        cli.WriteRepository();

        // A copy of the bundle in a tracked folder, for the check that refuses it below.
        var docsBundle = Path.Combine(cli.Repository, "docs", "Sample.app");
        foreach (var file in Directory.EnumerateFiles(cli.App, "*", SearchOption.AllDirectories))
        {
            var copy = Path.Combine(docsBundle, Path.GetRelativePath(cli.App, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
        }

        var options =
            new Options(Mode.Release, "src/Sample/Sample.csproj", "src/Sample/bin/Sample.app", "build/notices");
        string? asked = null;
        var output = new StringWriter();
        var code = Cli.Run(
            options,
            cli.Repository,
            project =>
            {
                asked = project;
                return cli.Query();
            },
            output,
            annotate: false);
        tally.Expect(
            code == 0 && asked == Path.Combine(cli.Repository, "src", "Sample", "Sample.csproj")
                && File.Exists(Path.Combine(cli.App, Notices.Folder, Notices.NoticesFile))
                && File.Exists(Path.Combine(cli.Out, Notices.IndexFile))
                && Holds(
                    output.ToString(),
                    "src/Sample/packages.lock.json: 7 packages.",
                    "Runtime pack for Sample.Platform: Sample.Platform.Pack 7.0.0",
                    "Wrote THIRD_PARTY_NOTICES.md, index.json and 8 texts into Sample.app/licenses/ and "
                        + "build/notices/."),
            "runs from the repository root with the lock file beside the project and MSBuild's answer, reports, and "
                + "writes into the bundle and the output folder");

        // The line that names what it wrote holds the bundle's name, so under GitHub Actions it comes
        // while commands are stopped too.
        var annotatedRun = new StringWriter();
        var rerun = Cli.Run(options, cli.Repository, _ => cli.Query(), annotatedRun, annotate: true);
        var printedLines = annotatedRun.ToString().Split('\r', '\n').Where(line => line.Length > 0).ToList();
        var wrote = printedLines.FindIndex(line => line.StartsWith("Wrote ", StringComparison.Ordinal));
        tally.Expect(
            rerun == 0 && wrote > 0 && wrote + 1 < printedLines.Count
                && StopCommands().Match(printedLines[wrote - 1]) is { Success: true } wroteStop
                && printedLines[wrote + 1] == $"::{wroteStop.Groups[1].Value}::",
            "under GitHub Actions prints the line that names what it wrote while workflow commands are stopped");

        var failing = Fixture.Create(root, "cli-failing");
        failing.Locked.Add(new LockedPackage("Sample.Unmapped", "1.0.0"));
        failing.WriteRepository();
        var failed = new StringWriter();
        tally.Expect(
            Cli.Run(options, failing.Repository, _ => failing.Query(), failed, annotate: true) == 1
                && !Directory.Exists(Path.Combine(failing.App, Notices.Folder)) && !Directory.Exists(failing.Out)
                && Holds(
                    failed.ToString(),
                    "error: Sample.app: the package Sample.Unmapped",
                    "::error title=Notices::Sample.app: the package Sample.Unmapped"),
            "exits with 1 after a failure, annotates it under GitHub Actions, and writes nothing");

        var foreign = Fixture.Create(root, "cli-foreign");
        foreign.WriteRepository();
        Fixture.WriteFile(Path.Combine(foreign.Out, "keep.txt"), "kept");
        tally.Expect(
            Throws(() => Cli.Run(options, foreign.Repository, _ => foreign.Query(), TextWriter.Null, annotate: false))
                && !Directory.Exists(Path.Combine(foreign.App, Notices.Folder))
                && File.Exists(Path.Combine(foreign.Out, "keep.txt")),
            "checks both destinations before it writes either: an output folder that it may not replace leaves the "
                + "bundle as it is");
        Func<string, string> query = _ => cli.Query();
        var docsApp = options with { App = "docs/Sample.app" };
        var docsOut = options with { Out = "docs/notices" };
        bool outside(Options refused) =>
            Message(() => Cli.Run(refused, cli.Repository, query, TextWriter.Null, annotate: false)) is { } refusal
                && refusal.Contains("outside build/", StringComparison.Ordinal);
        tally.Expect(
            File.Exists(Path.Combine(docsBundle, "Info.plist")) && outside(docsApp)
                && !Directory.Exists(Path.Combine(docsBundle, Notices.Folder))
                && outside(docsOut) && !Directory.Exists(Path.Combine(cli.Repository, "docs", "notices")),
            "refuses an app or an output folder in the repository outside build/, bin/ and obj/, and writes nothing "
                + "there");

        // A lone CR ends a line for the runner of GitHub Actions as LF does.
        const string Injected =
            "Sample.app: a\r::error title=Injected::b\n::error title=Again::c 100% is attributed to no component.";
        var injected = new Result([], [Injected], [], [], [], [], [], [], null);
        var annotated = new StringWriter();
        Report.Print(Bundle.Read(cli.App), cli.Build(), cli.Load(), Mode.Ios, injected, annotated, annotate: true);
        var lines = annotated.ToString().Split('\r', '\n');
        tally.Expect(
            !lines.Any(line => line.TrimStart().StartsWith("::error", StringComparison.Ordinal))
                && lines.Contains(
                    "warning: Sample.app: a%0D::error title=Injected::b%0A::error title=Again::c 100% is attributed to "
                        + "no component.")
                && lines.Contains(
                    "::warning title=Notices::Sample.app: a%0D::error title=Injected::b%0A::error title=Again::c "
                        + "100%25 is attributed to no component."),
            "escapes CR and LF in every line of the report, and % in an annotation too, so that no name starts a line");

        // Names that run a workflow command without a line break: one that starts with "::", and one
        // that holds "##[", which the runner's older parser finds anywhere in a line.
        string[] commands = ["::error title=Spoof::x", "::stop-commands::tok", "Frameworks/a##[error]spoofed"];
        var spoofing = new Result(
            [],
            [.. commands.Select(name => $"Sample.app: {name} is attributed to no component.")],
            [.. commands.Select(name => new Attribution(name, []))],
            [],
            [],
            [],
            [$"Sample.Ui: {commands[2]} of the package Sample.Ui 1.0.0"],
            [],
            null);
        string printed()
        {
            var writer = new StringWriter();
            Report.Print(Bundle.Read(cli.App), cli.Build(), cli.Load(), Mode.Ios, spoofing, writer, annotate: true);
            return writer.ToString();
        }

        var first = printed().Split('\r', '\n');
        var token = StopCommands().Match(first[0]) is { Success: true } stop ? stop.Groups[1].Value : null;
        var resume = token is null ? -1 : Array.IndexOf(first, $"::{token}::");
        var named = first.Select((line, position) => (line, position))
            .Where(entry => commands.Any(name => entry.line.Contains(name, StringComparison.Ordinal)))
            .ToList();
        const string Annotation = "::warning title=Notices::";
        var after = first.Skip(resume + 1).Where(line => line.Length > 0).ToList();
        tally.Expect(
            resume > 0 && named.Count > 0
                && named.All(entry => (entry.position > 0 && entry.position < resume)
                    || (entry.position > resume && entry.line.StartsWith(Annotation, StringComparison.Ordinal)))
                && after.Count == commands.Length
                && after.All(line => line.StartsWith(Annotation, StringComparison.Ordinal)),
            "under GitHub Actions prints the report while workflow commands are stopped, so that a name with \"::\" or "
                + "\"##[\" runs none, and the annotations after they resume");
        tally.Expect(
            token is not null && StopCommands().Match(printed().Split('\r', '\n')[0]) is { Success: true } again
                && again.Groups[1].Value != token,
            "stops workflow commands with a new random token in each run");
        var ended = new StringWriter();
        var spoofed = new IOException("Frameworks/a##[error]x\n::error title=Spoof::y");
        var exitCode = Cli.Fail(ended, actions: true, spoofed);
        var failedLines = ended.ToString().Split('\r', '\n').Where(line => line.Length > 0).ToArray();
        tally.Expect(
            exitCode == 2 && failedLines.Length == 3
                && StopCommands().Match(failedLines[0]) is { Success: true } stopped
                && failedLines[1] == "error: Frameworks/a##[error]x%0A::error title=Spoof::y"
                && failedLines[2] == $"::{stopped.Groups[1].Value}::",
            "prints the error that ends a run while workflow commands are stopped, its line breaks escaped, and "
                + "exits with 2");

        var listed = new StringWriter();
        var expected = $"{Fixture.Hash(Fixture.ApacheTerms)} {Fixture.ApacheUrl}\n"
            + $"{Fixture.Hash(Fixture.Header)} {Fixture.HeaderUrl}\n";
        tally.Expect(
            Cli.ListUpstream(cli.Repository, listed) == 0 && listed.ToString().ReplaceLineEndings("\n") == expected,
            "lists each upstream text once for the ios job to fetch, as \"<sha256> <url>\"");
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
            ios.Failures.Count == 0 && ios.Outputs is not null
                && Names(ios.Warnings, "Unknown.dll") && Names(ios.Warnings, ".hidden"),
            "an unattributed file, hidden ones too, is a warning in ios mode, and the notices are still written");
        tally.Expect(
            release.Outputs is null && Names(release.Failures, "Unknown.dll") && Names(release.Failures, ".hidden"),
            "an unattributed file fails in release mode, and nothing is written");

        var gap = Fixture.Create(root, "missing-text");
        gap.MappingText = EditMapping(
            "\"Sample.Graphics\": {\n      \"texts\": [",
            "\"Sample.Graphics\": {\n      \"missing\": [{ \"text\": \"the notices of a sample library\", \"note\": "
                + "\"No source carries them.\" }],\n      \"texts\": [");
        var gapIos = gap.Generate(Mode.Ios);
        var gapRelease = gap.Generate(Mode.Release);
        const string GapWords = "Sample.Graphics needs the notices of a sample library, which no source provides yet";
        tally.Expect(
            gapIos.Failures.Count == 0 && gapIos.Outputs is not null
                && gapIos.Warnings.Any(warning => warning.Contains(GapWords, StringComparison.Ordinal))
                && gapRelease.Outputs is null
                && gapRelease.Failures.Any(failure => failure.Contains(GapWords, StringComparison.Ordinal)),
            "a text that has no source yet is a warning in ios mode and a failure in release mode, named");

        // A guarded missing text counts only while the bundle's assembly defines its type, by its exact
        // namespace and name as a top-level type, and when the assembly is missing or cannot be read.
        const string GuardedGap =
            "\"Sample.Graphics\": {\n      \"missing\": [{ \"text\": \"the notices of a sample gesture\", \"note\": "
                + "\"No source carries them.\", \"whileDefined\": { \"assembly\": \"Graphics.dll\", \"type\": "
                + "\"Sample.Graphics.Pinch\" } }],\n      \"texts\": [";
        const string GuardWords = "Sample.Graphics needs the notices of a sample gesture";
        Result guarded(string name, byte[]? assembly, Mode mode)
        {
            var fixture = Fixture.Create(root, name);
            fixture.MappingText = EditMapping("\"Sample.Graphics\": {\n      \"texts\": [", GuardedGap);
            var dll = Path.Combine(fixture.App, "Graphics.dll");
            if (assembly is null)
            {
                File.Delete(dll);
            }
            else
            {
                File.WriteAllBytes(dll, assembly);
            }

            return fixture.Generate(mode);
        }

        static bool names(IReadOnlyList<string> lines, params string[] words) =>
            lines.Any(line => words.All(word => line.Contains(word, StringComparison.Ordinal)));
        var keeps = TinyAssembly.Build("Graphics", ["Sample.Graphics.Other", "Sample.Graphics.Pinch"]);
        var trimmed = TinyAssembly.Build(
            "Graphics",
            [
                "Sample.Graphics.Other",
                "Other.Pinch",
                "Sample.Graphics.PinchGesture",
                "sample.graphics.Pinch",
                "Sample.Graphics.Outer",
            ],
            ("Sample.Graphics.Outer", "Pinch"));
        var keptRelease = guarded("guard-keeps-release", keeps, Mode.Release);
        var keptIos = guarded("guard-keeps-ios", keeps, Mode.Ios);
        tally.Expect(
            keptRelease.Outputs is null
                && names(keptRelease.Failures, GuardWords, "Graphics.dll defines Sample.Graphics.Pinch")
                && keptIos.Outputs is not null && names(keptIos.Warnings, GuardWords),
            "a guarded text counts while its assembly defines the type: a failure in release mode, a warning in ios "
                + "mode");
        var trimmedRelease = guarded("guard-trimmed", trimmed, Mode.Release);
        tally.Expect(
            trimmedRelease.Outputs is not null && !names(trimmedRelease.Failures, GuardWords)
                && !names(trimmedRelease.Warnings, GuardWords),
            "a guarded text is not needed once the type is gone, even beside a type of the same name in another "
                + "namespace, of another case, or nested");
        tally.Expect(
            names(
                guarded("guard-unreadable", "not an assembly"u8.ToArray(), Mode.Release).Failures,
                GuardWords,
                "cannot be read")
                && names(guarded("guard-absent", null, Mode.Release).Failures, GuardWords, "has no Graphics.dll"),
            "a guarded text counts when its assembly cannot be read or is not in the bundle");
        string withGuard(string guard) => EditMapping(
            "\"Sample.Graphics\": {\n      \"texts\": [",
            "\"Sample.Graphics\": {\n      \"missing\": [{ \"text\": \"t\", \"note\": \"n\", \"whileDefined\": "
                + guard + " }],\n      \"texts\": [");
        tally.Expect(
            Mapping.Parse(withGuard("{ \"assembly\": \"A.dll\", \"type\": \"N.T\" }")).Components["Sample.Graphics"]
                .Missing[0].Guard == new TypeGuard("A.dll", "N", "T")
                && Rejects(() => Mapping.Parse(withGuard("{ \"assembly\": \"A.dll\", \"type\": \"N.T\", \"x\": 1 }")))
                && Rejects(() => Mapping.Parse(withGuard("{ \"assembly\": \"A.dll\" }")))
                && Rejects(() => Mapping.Parse(withGuard("{ \"assembly\": \"lib/A.dll\", \"type\": \"N.T\" }")))
                && Rejects(() => Mapping.Parse(withGuard("{ \"assembly\": \"A.dll\", \"type\": \"T\" }")))
                && Rejects(() => Mapping.Parse(withGuard("{ \"assembly\": \"A.dll\", \"type\": \"N. T\" }")))
                && Rejects(() => Mapping.Parse(withGuard("[]"))),
            "reads a guard, and rejects an unknown key, a missing type, a path, a type without a namespace or with a "
                + "space, and a guard that is not an object");

        foreach (var (component, word, description) in new[]
        {
            ("Sample.Missing", "not in the register", "a component that the register lacks fails, named"),
            ("Sample.Review", "is Under review in the register", "a component that is Under review fails, named"),
            ("Sample.Forbidden", "is Forbidden in the register", "a component that is Forbidden fails, named"),
            ("Sample.Tool", "build tool", "a component whose row is a Tool row fails, named"),
        })
        {
            Expect(
                tally,
                root,
                component,
                fixture => fixture.MappingText = EditMapping(
                    "{ \"path\": \"Platform.dll\", \"components\": [\"Sample.Platform\"]",
                    $"{{ \"path\": \"Platform.dll\", \"components\": [\"{component}\"]"),
                [component, word],
                description);
        }

        Expect(
            tally,
            root,
            "no-texts",
            fixture => fixture.MappingText = EditMapping("\"Sample.Ui\": {", "\"Sample.Unused\": {"),
            ["missing license text of Sample.Ui"],
            "a component without texts fails");
        Expect(
            tally,
            root,
            "no-file",
            fixture => File.Delete(fixture.PackageFile("Sample.Graphics.Native", "2.0.0", "THIRD-PARTY-NOTICES.txt")),
            ["missing license text of Sample.Graphics", "THIRD-PARTY-NOTICES.txt"],
            "a missing license file fails, named");
        foreach (var (name, content, words) in new[]
        {
            ("empty-text", Array.Empty<byte>(), "LICENSE.txt is empty"),
            ("nul-text", "Sample\0graphics."u8.ToArray(), "LICENSE.txt is not text"),
            ("latin-1-text", new byte[] { 0x53, 0xE9, 0x0A }, "LICENSE.txt is not UTF-8 text"),
        })
        {
            Expect(
                tally,
                root,
                name,
                fixture => File.WriteAllBytes(fixture.PackageFile("Sample.Graphics", "2.0.0", "LICENSE.txt"), content),
                ["missing license text of Sample.Graphics", words],
                $"a text that is {name.Replace("-text", string.Empty, StringComparison.Ordinal)} counts as missing");
        }

        Expect(
            tally,
            root,
            "no-copyright",
            fixture => fixture.Package("Sample.Ui", "1.0.0", "expression", "MIT", null),
            ["missing license text of Sample.Ui", "copyright"],
            "an MIT package without a copyright line fails");
        Expect(
            tally,
            root,
            "no-copyright-word",
            fixture => fixture.Package("Sample.Ui", "1.0.0", "expression", "MIT", "2020 Sample UI Authors"),
            ["missing license text of Sample.Ui", "declares no copyright notice"],
            "an MIT package whose copyright line says no copyright fails");
        Expect(
            tally,
            root,
            "no-mit",
            fixture => fixture.Package("Sample.Ui", "1.0.0", "expression", "BSD-3-Clause", "Copyright 2020 Sample"),
            ["missing license text of Sample.Ui", "does not declare the expression MIT"],
            "an MIT text for a package that declares another license fails");
        Expect(
            tally,
            root,
            "mit-as-file",
            fixture => fixture.Package("Sample.Ui", "1.0.0", "file", "MIT", "Copyright 2020 Sample UI Authors"),
            ["missing license text of Sample.Ui", "does not declare the expression MIT"],
            "an MIT text for a package that names a file called MIT, not the expression, fails");
        var typed = Fixture.Create(root, "typed-expression");
        typed.Package("Sample.Ui", "1.0.0", " Expression ", "MIT", "Copyright 2020 Sample UI Authors");
        tally.Expect(
            typed.Generate(Mode.Release).Failures.Count == 0,
            "reads a license type as NuGet does, trimmed and without regard to case");
        foreach (var type in new[] { "Expression", " expression ", "EXPRESSION" })
        {
            Expect(
                tally,
                root,
                $"declared-{type.Trim()}",
                fixture => fixture.Package("Sample.Graphics.Native", "2.0.0", type, "GPL-3.0-only", null),
                ["Sample.Graphics.Native 2.0.0 declares GPL-3.0-only", "register row Sample.Graphics"],
                $"a declared license is checked when the type is \"{type}\"");
        }

        foreach (var (name, expression) in new[]
        {
            ("empty", string.Empty),
            ("parentheses", "()"),
            ("operator", "AND"),
            ("exception", "WITH GPL-3.0-only"),
            ("spaces", "   "),
            ("line-break", "\n"),
        })
        {
            Expect(
                tally,
                root,
                $"expression-{name}",
                fixture => fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", expression, null),
                ["Sample.Graphics.Native 2.0.0 declares a license expression that names no license."],
                $"a license expression that names no license fails: {name}");
        }
        var unexpressed = Fixture.Create(root, "unexpressed");
        unexpressed.Description("Sample.Ui.Extra", "1.0.0", "<licenseUrl>https://example.org/license</licenseUrl>");
        unexpressed.Description("Sample.Graphics.Native", "2.0.0", "<license>MIT</license>");
        string[] reported =
        [
            "Sample.Ui.Extra 1.0.0 declares its license only by licenseUrl",
            "Sample.Graphics.Native 2.0.0 declares a license without a type",
        ];
        var unexpressedResult = unexpressed.Generate(Mode.Release);
        tally.Expect(
            unexpressedResult.Failures.Count == 0 && unexpressedResult.Unexpressed.SequenceEqual(reported),
            "reports a package that declares its license only by licenseUrl, or without a type");
        Expect(
            tally,
            root,
            "other-component",
            fixture => fixture.MappingText = EditMapping(
                "{ \"package\": \"Sample.Graphics\", \"file\": \"LICENSE.txt\" }",
                "{ \"package\": \"Sample.Apache\", \"file\": \"LICENSE.txt\" }"),
            [
                "a text of Sample.Graphics names the package Sample.Apache",
                "gives to Sample.Apache, not to Sample.Graphics",
            ],
            "a text from a package of another component fails, naming both");
        Expect(
            tally,
            root,
            "no-pack",
            fixture => fixture.Packs.RemoveAt(1),
            ["missing license text of Sample.Platform", "no runtime pack for Sample.Platform"],
            "a runtime pack that the build lacks fails");
        Expect(
            tally,
            root,
            "not-locked",
            fixture => fixture.Locked.RemoveAll(package => package.Id == "SteamKit2"),
            ["missing license text of SteamKit2", "SteamKit2 is not in"],
            "a package that the lock file lacks fails");
        Expect(
            tally,
            root,
            "not-restored",
            fixture => Directory.Delete(fixture.PackageFolder("Sample.Ui.Extra", "1.0.0"), recursive: true),
            ["Sample.Ui.Extra 1.0.0 is not in NuGet's package folder"],
            "a package that is not restored fails");
        Expect(
            tally,
            root,
            "unmapped",
            fixture => fixture.Locked.Add(new LockedPackage("Sample.Unmapped", "1.0.0")),
            ["Sample.Unmapped", "no row in eng/license-map.json"],
            "a package of the lock file that the license map lacks fails");

        Expect(
            tally,
            root,
            "upstream-missing",
            fixture => File.Delete(fixture.Upstream(Fixture.ApacheTerms)),
            ["missing license text of Sample.Apache", "is not in build/upstream/", UpstreamUrl.FetchStep],
            "an upstream text that is not fetched fails, naming the step that fetches it");
        Expect(
            tally,
            root,
            "upstream-changed",
            fixture => File.WriteAllText(fixture.Upstream(Fixture.ApacheTerms), "Changed."),
            ["missing license text of Sample.Apache", "has the SHA-256", UpstreamUrl.FetchStep],
            "an upstream text whose SHA-256 differs fails");
        Expect(
            tally,
            root,
            "terms-not-held",
            fixture => fixture.MappingText = EditMapping(
                "\"lines\": [1, 4] }",
                "\"lines\": [1, 4], \"terms\": \"Apache-2.0\" }"),
            ["missing license text of Sample.Ui", "carries the full terms of Apache-2.0, but it does not hold them"],
            "an upstream text marked as carrying a license's terms fails when it does not hold them");
        const string GraphicsText = "{ \"package\": \"Sample.Graphics\", \"file\": \"LICENSE.txt\" }";
        static string unicode(Fixture fixture, string text)
        {
            Fixture.WriteFile(fixture.Upstream(text), text);
            return EditMapping(
                GraphicsText,
                $"{GraphicsText},\n        {{ \"upstream\": \"{Fixture.UnicodeUrl}\", \"sha256\": "
                    + $"\"{Fixture.Hash(text)}\", \"terms\": \"Unicode-3.0\" }}");
        }

        var v3 = Fixture.Create(root, "unicode-terms");
        v3.MappingText = unicode(v3, Fixture.UnicodeTerms);
        var v3Result = v3.Generate(Mode.Release);
        var v3Graphics = v3Result.Components.SingleOrDefault(component => component.Name == "Sample.Graphics");
        tally.Expect(
            v3Result.Failures.Count == 0 && v3Graphics is not null
                && v3Graphics.Texts.Any(text => text.Title == "Unicode-3.0 license, copied from LICENSE of sample/data "
                    + "at commit fedcba9876543210fedcba9876543210fedcba98"),
            "recognizes the full terms of the Unicode License V3 in a text marked as carrying them");
        Expect(
            tally,
            root,
            "unicode-older-terms",
            fixture => fixture.MappingText = unicode(fixture, Fixture.OlderUnicodeTerms),
            ["missing license text of Sample.Graphics", "carries the full terms of Unicode-3.0, but it does not hold"],
            "but not the older Unicode terms, whose permission names the Unicode data files");
        Expect(
            tally,
            root,
            "range-past-end",
            fixture => fixture.MappingText = EditMapping("\"lines\": [1, 4]", "\"lines\": [1, 99]"),
            ["missing license text of Sample.Ui", "run past the end of src/Header.cs", "which has 7 lines"],
            "a range of lines past the end of its file fails");
        Expect(
            tally,
            root,
            "range-code",
            fixture => fixture.MappingText = EditMapping("\"lines\": [1, 4]", "\"lines\": [1, 5]"),
            ["missing license text of Sample.Ui", "line 5 of src/Header.cs", "neither a comment nor blank"],
            "a range with a line of code fails");
        Expect(
            tally,
            root,
            "range-blank",
            fixture => fixture.MappingText = EditMapping("\"lines\": [1, 4]", "\"lines\": [4, 4]"),
            ["missing license text of Sample.Ui", "the lines 4 to 4 of src/Header.cs", "are blank"],
            "a range of blank lines only counts as missing");
        Expect(
            tally,
            root,
            "apache-terms",
            fixture => fixture.MappingText = EditMapping(
                $",\n        {{ \"upstream\": \"{Fixture.ApacheUrl}\", \"sha256\": \""
                    + $"{Fixture.Hash(Fixture.ApacheTerms)}\", \"terms\": \"Apache-2.0\" }}",
                string.Empty),
            ["Sample.Apache is under Apache-2.0, but none of its texts holds the full terms of Apache-2.0"],
            "an Apache-2.0 component with a short notice and no full terms fails");
        Expect(
            tally,
            root,
            "lgpl-terms",
            fixture => fixture.MappingText = EditMapping(
                ",\n        { \"repository\": \"external/Sample-lgpl/COPYING.LIB\" }",
                string.Empty),
            ["SteamKit2 is under LGPL-2.1, but none of its texts holds the full terms of LGPL-2.1"],
            "an LGPL-2.1 component with a short notice and no full terms fails");
        Expect(
            tally,
            root,
            "declared-terms",
            fixture =>
            {
                fixture.RegisterText = Fixture.Register.Replace(
                    Fixture.GraphicsLicense,
                    "MIT. Its native package is under Apache-2.0.",
                    StringComparison.Ordinal);
                fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", "Apache-2.0", null);
            },
            ["Sample.Graphics is under Apache-2.0, but none of its texts holds the full terms"],
            "a component whose package declares Apache-2.0 needs the full terms too");
        Expect(
            tally,
            root,
            "row-unicode-terms",
            fixture => fixture.RegisterText = Fixture.Register.Replace(
                Fixture.GraphicsLicense,
                "MIT AND Unicode-3.0. Its native package lists more components.",
                StringComparison.Ordinal),
            ["Sample.Graphics is under Unicode-3.0, but none of its texts holds the full terms"],
            "a component whose row names Unicode-3.0 needs the full terms of the Unicode License V3");

        // The register row alone asks for the full terms, for a component without a package such as a
        // fork's, in each identifier of LGPL-2.1 and in any case.
        string[] rowLicenses = ["LGPL-2.1-or-later", "LGPL-2.1", "LGPL-2.1+", "lgpl-2.1-or-later", "LGPL-2.1-only"];
        for (var index = 0; index < rowLicenses.Length; index++)
        {
            var license = rowLicenses[index];
            Expect(
                tally,
                root,
                $"row-terms-{index}",
                fixture =>
                {
                    fixture.RegisterText = Fixture.Register.Replace(
                        "| Sample.Lgpl | A library the user can replace | LGPL-2.1-or-later |",
                        $"| Sample.Lgpl | A library the user can replace | {license} |",
                        StringComparison.Ordinal);
                    fixture.MappingText = EditMapping(
                        "\"Sample.Lgpl\": { \"texts\": [{ \"repository\": \"external/Sample-lgpl/COPYING.LIB\" }] }",
                        "\"Sample.Lgpl\": { \"texts\": [{ \"repository\": \"external/Sample-lgpl/NOTICE\" }] }");
                    Fixture.WriteFile(
                        Path.Combine(fixture.Repository, "external", "Sample-lgpl", "NOTICE"),
                        "Sample LGPL notice.");
                },
                ["Sample.Lgpl is under LGPL-2.1, but none of its texts holds the full terms of LGPL-2.1"],
                $"a component without a package whose row says {license} needs the full terms of LGPL-2.1");
        }

        Expect(
            tally,
            root,
            "split-terms",
            fixture =>
            {
                fixture.MappingText = EditMapping(
                    ",\n        { \"repository\": \"external/Sample-lgpl/COPYING.LIB\" }",
                    ",\n        { \"repository\": \"external/Sample-lgpl/part-1.txt\" },"
                        + "\n        { \"repository\": \"external/Sample-lgpl/part-2.txt\" }");
                var folder = Path.Combine(fixture.Repository, "external", "Sample-lgpl");
                Fixture.WriteFile(Path.Combine(folder, "part-1.txt"), "GNU LESSER GENERAL PUBLIC LICENSE");
                Fixture.WriteFile(
                    Path.Combine(folder, "part-2.txt"),
                    "TERMS AND CONDITIONS FOR COPYING, DISTRIBUTION AND MODIFICATION");
            },
            ["SteamKit2 is under LGPL-2.1, but none of its texts holds the full terms of LGPL-2.1"],
            "the full terms stand in one text, not split between two");
        Expect(
            tally,
            root,
            "static-lgpl",
            fixture =>
            {
                fixture.Locked.Add(new LockedPackage("Sample.Lgpl.Static", "1.0.0"));
                fixture.Package("Sample.Lgpl.Static", "1.0.0", "expression", "LGPL-2.1-or-later", null);
            },
            ["Sample.Lgpl", "main executable Sample", "only for SteamKit2"],
            "an LGPL component other than SteamKit2 in the main executable fails");
        Expect(
            tally,
            root,
            "declared-lgpl",
            fixture =>
            {
                fixture.RegisterText = Fixture.Register.Replace(
                    Fixture.GraphicsLicense,
                    "MIT. Its native package is under LGPL-2.1-only.",
                    StringComparison.Ordinal);
                fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", "LGPL-2.1-only", null);
            },
            ["Sample.Graphics, under LGPL-2.1-only, is compiled into the main executable Sample"],
            "so does a component in it whose package declares the LGPL");
        var noted = Fixture.Create(root, "lgpl-in-a-note");
        noted.RegisterText = Fixture.Register.Replace(
            Fixture.GraphicsLicense,
            "MIT. Its native framework holds a library under LGPL-2.1-or-later.",
            StringComparison.Ordinal);
        tally.Expect(
            noted.Generate(Mode.Release).Failures.Count == 0,
            "a register row that names the LGPL only after its leading expression does not make the component LGPL");
        string[] runtimeLicenses = ["LGPL-2.1-only", "lgpl-2.1-only"];
        for (var index = 0; index < runtimeLicenses.Length; index++)
        {
            var license = runtimeLicenses[index];
            Expect(
                tally,
                root,
                $"row-lgpl-{index}",
                fixture => fixture.RegisterText = Fixture.Register.Replace(
                    "| Sample.Runtime | A runtime | MIT |",
                    $"| Sample.Runtime | A runtime | {license} |",
                    StringComparison.Ordinal),
                [$"Sample.Runtime, under {license}, is compiled into the main executable Sample"],
                $"so does a component in it that only its row puts under {license}, without a package");
        }

        // An OR that a package declares lets a component of the executable out of the LGPL only through an
        // alternative that names licenses, all of them in the expression that the register row starts
        // with, since the row records the license chosen (C-0003 section 5).
        const string Either = "MIT OR LGPL-2.1-only";
        var chosen = Fixture.Create(root, "or-chooses-mit");
        chosen.RegisterText = Fixture.Register.Replace(
            Fixture.GraphicsLicense,
            "MIT. Its native package is also offered under LGPL-2.1-only.",
            StringComparison.Ordinal);
        chosen.Package("Sample.Graphics.Native", "2.0.0", "expression", Either, null);
        chosen.MappingText = EditMapping(
            "{ \"package\": \"Sample.Graphics\", \"file\": \"LICENSE.txt\" },",
            "{ \"package\": \"Sample.Graphics\", \"file\": \"LICENSE.txt\" },\n"
                + "        { \"repository\": \"external/Sample-lgpl/COPYING.LIB\" },");
        var chosenResult = chosen.Generate(Mode.Release);
        tally.Expect(
            chosenResult.Failures.Count == 0 && chosenResult.Outputs is not null,
            $"a component in it whose package declares {Either} passes when its register row chooses MIT");
        Expect(
            tally,
            root,
            "or-chooses-lgpl",
            fixture =>
            {
                fixture.RegisterText = Fixture.Register.Replace(
                    Fixture.GraphicsLicense,
                    "LGPL-2.1-only. Its native package is also offered under MIT.",
                    StringComparison.Ordinal);
                fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", Either, null);
            },
            ["Sample.Graphics, under LGPL-2.1-only, is compiled into the main executable Sample"],
            "and fails when its register row chooses LGPL-2.1-only");
        Expect(
            tally,
            root,
            "or-chooses-mit-terms",
            fixture =>
            {
                fixture.RegisterText = chosen.RegisterText;
                fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", Either, null);
            },
            ["Sample.Graphics is under LGPL-2.1, but none of its texts holds the full terms of LGPL-2.1"],
            $"the full-terms rule counts the LGPL alternative of {Either}");

        // An alternative lets the component out of the LGPL only when it names licenses, all of them in the
        // expression that the row starts with: GPL, AGPL or MPL beside the LGPL leaves only the LGPL.
        foreach (var (name, expression, lgpl) in new[]
        {
            ("gpl", "LGPL-2.1-only OR GPL-2.0-only", "LGPL-2.1-only"),
            ("gpl-first", "GPL-2.0-only OR LGPL-2.1-only", "LGPL-2.1-only"),
            ("agpl", "LGPL-2.1-only OR AGPL-3.0-only", "LGPL-2.1-only"),
            ("mpl", "LGPL-2.1-or-later OR MPL-2.0", "LGPL-2.1-or-later"),
            ("lgpl-3", "LGPL-2.1-only OR LGPL-3.0-only", "LGPL-2.1-only"),
            ("and-mit", "LGPL-2.1-only AND MIT", "LGPL-2.1-only"),
        })
        {
            Expect(
                tally,
                root,
                $"way-out-{name}",
                fixture =>
                {
                    var offered = expression.Replace(" OR ", " or ", StringComparison.Ordinal);
                    fixture.RegisterText = Fixture.Register.Replace(
                        Fixture.GraphicsLicense,
                        $"MIT. Its native package is under {offered}.",
                        StringComparison.Ordinal);
                    fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", expression, null);
                },
                [$"Sample.Graphics, under {lgpl}, is compiled into the main executable Sample"],
                $"so does a component in it whose package declares {expression}, with a row that starts with MIT");
        }

        var both = Fixture.Create(root, "or-chooses-mit-and");
        both.RegisterText = Fixture.Register.Replace(
            Fixture.GraphicsLicense,
            "MIT. Its native package is also offered under LGPL-2.1-only and Zlib.",
            StringComparison.Ordinal);
        both.Package("Sample.Graphics.Native", "2.0.0", "expression", "MIT OR (LGPL-2.1-only AND Zlib)", null);
        both.MappingText = chosen.MappingText;
        tally.Expect(
            both.Generate(Mode.Release).Failures.Count == 0,
            "a component in it whose package declares MIT OR (LGPL-2.1-only AND Zlib) passes when its row chooses MIT");
        foreach (var (name, expression) in new[]
        {
            ("lgpl-or", "LGPL-2.1-only OR"),
            ("empty-or-lgpl", "() OR LGPL-2.1-only"),
            ("mit-or", "MIT OR"),
        })
        {
            Expect(
                tally,
                root,
                $"alternative-{name}",
                fixture => fixture.Package("Sample.Graphics.Native", "2.0.0", "expression", expression, null),
                ["Sample.Graphics.Native 2.0.0 declares a license expression that names no license in one of its"],
                $"an expression with an alternative that names no license fails: {expression}");
        }
        Expect(
            tally,
            root,
            "no-notice",
            fixture => fixture.MappingText = EditMapping(
                ",\n        { \"package\": \"Sample.Apache\", \"file\": \"NOTICE.txt\" }",
                string.Empty),
            ["Sample.Apache leaves out the NOTICE file NOTICE.txt"],
            "an Apache-2.0 NOTICE file left out fails");
        Expect(
            tally,
            root,
            "no-pack-notice",
            fixture =>
            {
                fixture.RegisterText = Fixture.Register.Replace(
                    "| Sample.Platform | Platform bindings | MIT |",
                    "| Sample.Platform | Platform bindings | Apache-2.0 |",
                    StringComparison.Ordinal);
                Fixture.WriteFile(Path.Combine(fixture.Packs[1].Folder, "NOTICE.txt"), "Sample platform NOTICE.");
            },
            ["Sample.Platform leaves out the NOTICE file NOTICE.txt of the runtime pack Sample.Platform.Pack 7.0.0"],
            "so does one of a runtime pack");
        Expect(
            tally,
            root,
            "no-credit",
            fixture => fixture.MappingText = EditMapping($"\"credit\": \"{Fixture.Credit}\",", string.Empty),
            ["Sample.Fonts is under the FTL"],
            "an FTL component without its credit fails");
        Expect(
            tally,
            root,
            "other-license",
            fixture => fixture.Package(
                "Sample.Graphics.Native",
                "2.0.0",
                "expression",
                "BSD-3-Clause",
                null,
                ("THIRD-PARTY-NOTICES.txt", "Notices.")),
            ["Sample.Graphics.Native 2.0.0 declares BSD-3-Clause", "register row Sample.Graphics"],
            "a package whose declared license its register row lacks fails");
        Expect(
            tally,
            root,
            "no-executable",
            fixture => fixture.WriteInfo("Missing", binary: true),
            ["executable Missing"],
            "an executable that the bundle lacks fails");
    }

    // A fixture with one change, which must fail with a message that holds every word.
    private static void Expect(
        Tally tally,
        string root,
        string name,
        Action<Fixture> change,
        string[] words,
        string description)
    {
        var fixture = Fixture.Create(root, name);
        change(fixture);
        var result = fixture.Generate(Mode.Ios);
        tally.Expect(
            result.Outputs is null
                && result.Failures.Any(failure => words.All(word => failure.Contains(word, StringComparison.Ordinal))),
            description);
    }

    private static string EditMapping(string from, string to) =>
        Fixture.Resolve(Fixture.Mapping).Replace(from, to, StringComparison.Ordinal);

    private static bool Holds(string text, params string[] parts) =>
        parts.All(part => text.Contains(part, StringComparison.Ordinal));

    private static bool Names(IEnumerable<string> messages, string file) =>
        messages.Any(message => message.Contains($": {file} is attributed to no component", StringComparison.Ordinal));

    private static bool Matches(string pattern, params string[] paths) => paths.All(Glob.ToRegex(pattern).IsMatch);

    private static bool MatchesNone(string pattern, params string[] paths) =>
        !paths.Any(Glob.ToRegex(pattern).IsMatch);

    private static bool Rejects(Action action) => Message(action) is not null;

    // The message of the InvalidDataException that the action throws, or null.
    private static string? Message(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (InvalidDataException exception)
        {
            return exception.Message;
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

    [GeneratedRegex(@"^## (\d+)\. (.+)$")]
    private static partial Regex SectionHeading();

    [GeneratedRegex(@"^- Text: \[(.+?)\]\((.+?)\)$")]
    private static partial Regex TextLink();

    [GeneratedRegex(@"^===== (.+) =====$")]
    private static partial Regex TextTitle();

    [GeneratedRegex(@"\A::stop-commands::([0-9a-f]{32})\z")]
    private static partial Regex StopCommands();
}
