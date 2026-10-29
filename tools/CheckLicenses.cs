// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Fails on any dependency that the register in C-0003 section 6 does not cover (C-0011
// section 2). It reads every NuGet lock file (direct and transitive packages), the package
// directives of the file-based tools in tools/, .gitmodules, eng/toolchain.json, and the uses:
// lines and container images of the workflows and actions, because actions are CI tools under
// C-0003 section 5. eng/license-map.json maps each of them to the register row that covers
// it. A way to bring in a package that no lock file records fails too, as does a project
// without a lock file, because the checker would not see its packages.
//
//   dotnet run tools/CheckLicenses.cs                 check the repository
//   dotnet run tools/CheckLicenses.cs -- --self-test  check the parsers and rules on fixtures
//
// Exit codes: 0 every dependency is covered, 1 something is not, 2 usage or configuration
// error. docs/design/repository-checks.md describes the mapping policy.

#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return args switch
    {
        [] => LicenseCheck.Run(),
        ["--self-test"] => SelfTest.Run(),
        _ => Cli.Usage(),
    };
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal static class Cli
{
    public static int Usage()
    {
        Console.Error.WriteLine("usage: dotnet run tools/CheckLicenses.cs [-- --self-test]");
        return 2;
    }
}

internal enum Kind
{
    Package,
    Tool,
    Action,
    Submodule,
}

// One place where the repository uses a dependency.
internal sealed record Use(Kind Kind, string Name, string File, int Line);

internal sealed record RegisterRow(string Component, string License, string Category, int Line);

internal enum Reason
{
    Unmapped,
    MissingRow,
    CategoryDoesNotCover,
    Unused,
    Unseen,
}

internal sealed record Problem(Reason Reason, Kind Kind, string Name, string File, int Line, string Message);

// The register table of C-0003 section 6. If the table changes shape, parsing fails with a
// message that says what changed, rather than guessing.
internal static partial class Register
{
    public const string FilePath = "docs/contracts/C-0003-licensing.md";
    public const string Heading = "## 6. Component register";

    public static readonly string[] Categories = ["Allowed", "Conditional", "Forbidden", "Under review", "Tool"];

    private static readonly string[] s_columns = ["Component", "Purpose", "License", "Category"];

    public static List<RegisterRow> Parse(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var heading = Array.FindIndex(lines, line => line.Trim() == Heading);
        if (heading < 0)
        {
            throw new InvalidDataException($"{FilePath} has no heading \"{Heading}\".");
        }

        var header = heading + 1;
        while (header < lines.Length
            && !IsTableLine(lines[header])
            && !lines[header].StartsWith("## ", StringComparison.Ordinal))
        {
            header++;
        }

        if (header >= lines.Length || !IsTableLine(lines[header]))
        {
            throw new InvalidDataException($"{FilePath}: section 6 holds no table.");
        }

        var columns = Cells(lines[header]);
        if (!columns.SequenceEqual(s_columns, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{FilePath} line {header + 1}: the register's columns are \"{string.Join(" | ", columns)}\", "
                    + $"not \"{string.Join(" | ", s_columns)}\". Update tools/CheckLicenses.cs to the new shape.");
        }

        var rows = new List<RegisterRow>();
        for (var index = header + 2; index < lines.Length && IsTableLine(lines[index]); index++)
        {
            var cells = Cells(lines[index]);
            if (cells.Length != s_columns.Length)
            {
                throw new InvalidDataException(
                    $"{FilePath} line {index + 1}: a register row has {cells.Length} cells, not {s_columns.Length}.");
            }

            var category = CategoryOf(cells[3]) ?? throw new InvalidDataException(
                $"{FilePath} line {index + 1}: the category \"{cells[3]}\" starts with none of "
                    + $"{string.Join(", ", Categories)}.");
            if (rows.Any(row => row.Component == cells[0]))
            {
                throw new InvalidDataException($"{FilePath} line {index + 1}: \"{cells[0]}\" is registered twice.");
            }

            rows.Add(new RegisterRow(cells[0], cells[2], category, index + 1));
        }

        return rows.Count > 0
            ? rows
            : throw new InvalidDataException($"{FilePath}: the register table has no rows.");
    }

    // A category cell starts with its category, and may go on after a comma, semicolon or colon,
    // as in "Allowed, under FTL" or "Under review: collect the notices".
    public static string? CategoryOf(string cell) => Categories.FirstOrDefault(
        category => cell == category
            || (cell.StartsWith(category, StringComparison.Ordinal) && ",;:".Contains(cell[category.Length])));

    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    private static string[] Cells(string line)
    {
        var trimmed = line.Trim();
        trimmed = trimmed.StartsWith('|') ? trimmed[1..] : trimmed;
        trimmed = trimmed.EndsWith('|') ? trimmed[..^1] : trimmed;
        return CellSeparator()
            .Split(trimmed)
            .Select(cell => cell.Trim().Replace(@"\|", "|", StringComparison.Ordinal))
            .ToArray();
    }

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellSeparator();
}

// eng/license-map.json: for each package ID, tool, action and submodule, the register row that
// covers it. Package IDs compare without regard to case, as NuGet compares them.
internal sealed class LicenseMap(Dictionary<Kind, Dictionary<string, string>> sections)
{
    public const string FilePath = "eng/license-map.json";

    private static readonly Dictionary<string, Kind> s_sectionNames = new(StringComparer.Ordinal)
    {
        ["packages"] = Kind.Package,
        ["tools"] = Kind.Tool,
        ["actions"] = Kind.Action,
        ["submodules"] = Kind.Submodule,
    };

    public IEnumerable<(Kind Kind, string Name, string Component)> Entries =>
        sections.SelectMany(section => section.Value.Select(entry => (section.Key, entry.Key, entry.Value)));

    public static LicenseMap Parse(string json)
    {
        var sections = Enum.GetValues<Kind>()
            .ToDictionary(kind => kind, _ => new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} must be a JSON object.");
            }

            foreach (var section in document.RootElement.EnumerateObject())
            {
                if (!s_sectionNames.TryGetValue(section.Name, out var kind)
                    || section.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException(
                        $"{FilePath}: \"{section.Name}\" is not one of the sections "
                            + $"{string.Join(", ", s_sectionNames.Keys)}.");
                }

                foreach (var entry in section.Value.EnumerateObject())
                {
                    var component = entry.Value.ValueKind == JsonValueKind.String ? entry.Value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(component))
                    {
                        throw new InvalidDataException($"{FilePath}: \"{entry.Name}\" must name a register row.");
                    }

                    if (!sections[kind].TryAdd(entry.Name, component))
                    {
                        throw new InvalidDataException(
                            $"{FilePath}: \"{entry.Name}\" is listed twice in \"{section.Name}\".");
                    }
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }

        return new LicenseMap(sections);
    }

    public string? ComponentFor(Kind kind, string name) => sections[kind].GetValueOrDefault(name);
}

internal static partial class Inventory
{
    // NuGet's lock files: packages.lock.json, and packages.<project>.lock.json, which NuGet uses
    // in its place when it exists. The project of a file-based app tools/X.cs is named X.cs.
    public static bool IsLockFile(string file) => LockFileName().IsMatch(file.Split('/')[^1]);

    // Where actions are used: the workflows and every other YAML file under .github/, and the
    // action.yml of each local action, wherever it lives.
    public static bool IsActionFile(string file)
    {
        var name = file.Split('/')[^1];
        var yaml = name.EndsWith(".yml", StringComparison.Ordinal) || name.EndsWith(".yaml", StringComparison.Ordinal);
        return name is "action.yml" or "action.yaml" || (yaml && file.StartsWith(".github/", StringComparison.Ordinal));
    }

    // Direct, transitive and centrally pinned packages of every target and runtime. Project
    // entries are the solution's own projects.
    public static List<Use> LockFile(string path, string json)
    {
        var uses = new List<Use>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("dependencies", out var targets)
                || targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path} has no \"dependencies\" object.");
            }

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"{path}: the target {target.Name} is not an object.");
                }

                foreach (var package in target.Value.EnumerateObject())
                {
                    if (package.Value.ValueKind != JsonValueKind.Object)
                    {
                        throw new InvalidDataException($"{path}: package {package.Name} is not an object.");
                    }

                    var type = package.Value.TryGetProperty("type", out var property)
                        && property.ValueKind == JsonValueKind.String
                            ? property.GetString()
                            : null;
                    if (type == "Project")
                    {
                        continue;
                    }

                    if (type is not ("Direct" or "Transitive" or "CentralTransitive"))
                    {
                        throw new InvalidDataException(
                            $"{path}: package {package.Name} has the unknown type \"{type}\".");
                    }

                    uses.Add(new Use(Kind.Package, package.Name, path, LineOf(json, $"\"{package.Name}\": {{")));
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}");
        }

        return uses;
    }

    // The #: directives of a file-based app, with their line numbers. The compiler accepts them
    // before the first token of the file: after blank lines, comments and preprocessor lines
    // such as #nullable, but not after #if. Reading stops where the compiler stops accepting.
    // The SDK also accepts whitespace after "#:", as in "#: package", so each directive comes
    // back without it.
    public static List<(int Line, string Text)> HeaderDirectives(string source)
    {
        var directives = new List<(int Line, string Text)>();
        var lines = source.Split('\n');
        var inComment = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (inComment)
            {
                var end = line.IndexOf("*/", StringComparison.Ordinal);
                if (end < 0)
                {
                    continue;
                }

                (line, inComment) = (line[(end + 2)..].TrimStart(), false);
            }

            while (line.StartsWith("/*", StringComparison.Ordinal))
            {
                var end = line.IndexOf("*/", 2, StringComparison.Ordinal);
                (line, inComment) = end < 0 ? (string.Empty, true) : (line[(end + 2)..].TrimStart(), false);
            }

            if (line.Length == 0
                || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("#!", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("#:", StringComparison.Ordinal))
            {
                directives.Add((index + 1, $"#:{line[2..].TrimStart()}"));
                continue;
            }

            if (!line.StartsWith('#') || IfDirective().IsMatch(line))
            {
                break;
            }
        }

        return directives;
    }

    // #:package Name@Version, #:package Name (with central package management), and
    // #:sdk Name@Version, which NuGet also resolves.
    public static List<Use> Directives(string path, string source)
    {
        var uses = new List<Use>();
        foreach (var (line, text) in HeaderDirectives(source))
        {
            if (!text.StartsWith("#:package", StringComparison.Ordinal)
                && !text.StartsWith("#:sdk", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Directive().Match(text);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"{path} line {line}: a package directive that the checker cannot read.");
            }

            uses.Add(new Use(Kind.Package, match.Groups[1].Value, path, line));
        }

        return uses;
    }

    public static List<Use> Toolchain(string path, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("tools", out var tools)
                || tools.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path} has no \"tools\" object.");
            }

            // A pin outside "tools" would need no register row, so the file holds nothing else.
            var other = document.RootElement.EnumerateObject().Select(key => key.Name).FirstOrDefault(
                name => name != "tools");
            if (other is not null)
            {
                throw new InvalidDataException(
                    $"{path}: \"{other}\" is not \"tools\", which is all that this checker reads.");
            }

            return tools.EnumerateObject()
                .Select(tool => new Use(Kind.Tool, tool.Name, path, LineOf(json, $"\"{tool.Name}\": {{")))
                .ToList();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{path} is not valid JSON: {exception.Message}");
        }
    }

    // The actions and images of a workflow or action file. owner/repo/path@ref names the action
    // owner/repo; a local action (./path) is our own code, and its action.yml is read too;
    // docker://image:tag, and the image of a job container or service, name the image without
    // its tag. A uses:, container: or image: key in a form that the checker cannot read, such
    // as a flow mapping or a value on the next line, stops it, so that no step escapes it, and
    // so does an image built from a Dockerfile, whose base image the checker does not read.
    public static List<Use> Workflow(string path, string yaml)
    {
        var uses = new List<Use>();
        var lines = yaml.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].TrimEnd('\r');
            var number = index + 1;
            if (line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (UsesKey().IsMatch(line))
            {
                var value = UsesLine().Match(line) is { Success: true } usesLine
                    ? usesLine.Groups[3].Value
                    : throw Unreadable(path, number, "uses: owner/repo@ref");
                if (!value.StartsWith("./", StringComparison.Ordinal))
                {
                    uses.Add(new Use(Kind.Action, ActionName(value, path, number), path, number));
                }
            }
            else if (ImageKey().IsMatch(line))
            {
                var image = ImageLine().Match(line) is { Success: true } imageLine
                    ? imageLine
                    : throw Unreadable(path, number, "image: name:tag");
                var (key, value) = (image.Groups[2].Value, image.Groups[4].Value);
                if (value.Length == 0 && key == "image")
                {
                    throw Unreadable(path, number, "image: name:tag");
                }

                if (value == "Dockerfile" || value.StartsWith("./", StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        $"{path} line {number}: an image built from a Dockerfile, whose base image the checker does "
                            + "not read. Use a published image, or teach tools/CheckLicenses.cs to read Dockerfiles.");
                }

                // container: followed by a mapping names its image on an image: line of its own.
                if (value.Length > 0)
                {
                    uses.Add(new Use(Kind.Action, ImageName(value, path, number), path, number));
                }
            }
        }

        return uses;
    }

    // Each submodule is named by its path, for example external/Paneport-wine. Git reads section
    // and key names without regard to case.
    public static List<Use> GitModules(string path, string text)
    {
        var uses = new List<Use>();
        var lines = text.Split('\n');
        var inSubmodule = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith('['))
            {
                inSubmodule = line.StartsWith("[submodule ", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            var match = SubmodulePath().Match(line);
            if (inSubmodule && match.Success)
            {
                uses.Add(new Use(Kind.Submodule, match.Groups[1].Value.Trim(), path, index + 1));
            }
        }

        return uses;
    }

    private static string ActionName(string value, string path, int line)
    {
        if (value.StartsWith("docker://", StringComparison.Ordinal))
        {
            return ImageName(value, path, line);
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        var segments = at < 0 ? [] : value[..at].Split('/');
        return segments.Length >= 2
            ? $"{segments[0]}/{segments[1]}"
            : throw new InvalidDataException($"{path} line {line}: \"{value}\" is not of the form owner/repo@ref.");
    }

    private static string ImageName(string value, string path, int line)
    {
        if (value.Contains("${{", StringComparison.Ordinal))
        {
            throw Unreadable(path, line, "a fixed image name");
        }

        var image = value.StartsWith("docker://", StringComparison.Ordinal) ? value : $"docker://{value}";
        return DockerTag().Replace(image, string.Empty);
    }

    private static InvalidDataException Unreadable(string path, int line, string form) => new(
        $"{path} line {line}: a line that the checker cannot read. Write it in block style on one line, as {form}.");

    private static int LineOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? 0 : text.AsSpan(0, index).Count('\n') + 1;
    }

    [GeneratedRegex(@"^packages(?:\.[^/]+)?\.lock\.json$")]
    private static partial Regex LockFileName();

    [GeneratedRegex(@"^#:(?:package|sdk)\s+([A-Za-z0-9_.-]+)(?:@\S+)?$")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^#\s*if\b")]
    private static partial Regex IfDirective();

    // A uses: key where YAML has keys: first on its line, or in a flow mapping. A step name, a
    // comment or an expression that mentions uses: is no key.
    [GeneratedRegex(@"^\s*(?:-\s+)?[""']?uses[""']?\s*:|(?<![${])\{(?!\{)(?:[^{}]*[\s,])?[""']?uses[""']?\s*:")]
    private static partial Regex UsesKey();

    // The form the checker reads: the key and its value on one line, in block style.
    [GeneratedRegex(@"^\s*(?:-\s+)?([""']?)uses\1\s*:\s*([""']?)([^""'\s#]+)\2\s*(?:#.*)?$")]
    private static partial Regex UsesLine();

    [GeneratedRegex(
        @"^\s*(?:-\s+)?[""']?(?:container|image)[""']?\s*:"
            + @"|(?<![${])\{(?!\{)(?:[^{}]*[\s,])?[""']?(?:container|image)[""']?\s*:")]
    private static partial Regex ImageKey();

    [GeneratedRegex(@"^\s*(?:-\s+)?([""']?)(container|image)\1\s*:\s*([""']?)([^""'\s#]*)\3\s*(?:#.*)?$")]
    private static partial Regex ImageLine();

    [GeneratedRegex(@"(?:@[^/]*|:[^/:]*)$")]
    private static partial Regex DockerTag();

    [GeneratedRegex(@"^path\s*=\s*(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SubmodulePath();
}

// Packages that no lock file records, and projects and tools whose lock file the checker
// cannot find. The checker would not see their packages, so each one fails until the
// checker learns to read it.
internal static partial class Unseen
{
    private const string LockFilePathMessage =
        "NuGetLockFilePath moves the lock file where tools/CheckLicenses.cs does not look. Keep the lock file "
            + "next to the project, as packages.lock.json or packages.<project>.lock.json.";

    public static List<Problem> Find(IEnumerable<string> files, Func<string, string> read)
    {
        var tracked = files.Order(StringComparer.Ordinal).ToList();
        var problems = new List<Problem>();
        foreach (var project in tracked.Where(file => file.EndsWith(".csproj", StringComparison.Ordinal)))
        {
            var (folder, name) = Split(project);
            if (!tracked.Contains($"{folder}packages.lock.json")
                && !tracked.Contains($"{folder}packages.{name[..^".csproj".Length]}.lock.json"))
            {
                problems.Add(Problem(
                    project,
                    0,
                    "the project has no lock file, so its packages cannot be checked. Restore it and commit "
                        + "its packages.lock.json (C-0009 section 2)."));
            }
        }

        foreach (var tool in tracked.Where(IsTool))
        {
            var source = read(tool);
            var (folder, name) = Split(tool);
            var own = $"{folder}packages.{name}.lock.json";
            if (Inventory.Directives(tool, source).Count > 0 && !tracked.Contains(own))
            {
                problems.Add(Problem(
                    tool,
                    0,
                    $"the tool has package directives but no lock file of its own. Create {own} with the content "
                        + "of tools/packages.lock.json, run the tool once, and commit the lock file."));
            }

            foreach (var (line, text) in Inventory.HeaderDirectives(source))
            {
                if (LockFilePathProperty().IsMatch(text))
                {
                    problems.Add(Problem(tool, line, LockFilePathMessage));
                }
            }
        }

        foreach (var file in tracked.Where(file => BuildFileName().IsMatch(file)))
        {
            problems.AddRange(UnseenForms(read(file)).Select(form => Problem(file, form.Line, form.Message)));
        }

        if (tracked.Contains("global.json") && HasMsbuildSdks(read("global.json")))
        {
            problems.Add(Problem(
                "global.json",
                0,
                "msbuild-sdks names MSBuild SDKs, which NuGet resolves without a lock file. Remove them, or "
                    + "teach tools/CheckLicenses.cs to read them first."));
        }

        foreach (var manifest in tracked.Where(file => file.Split('/')[^1] == "dotnet-tools.json"))
        {
            problems.Add(Problem(
                manifest,
                0,
                "a tool manifest names .NET tools, packages that no lock file records. Remove it, or teach "
                    + "tools/CheckLicenses.cs to read it first."));
        }

        return problems;
    }

    private static bool IsTool(string file) =>
        file.StartsWith("tools/", StringComparison.Ordinal) && file.EndsWith(".cs", StringComparison.Ordinal);

    private static (string Folder, string Name) Split(string file)
    {
        var slash = file.LastIndexOf('/');
        return (file[..(slash + 1)], file[(slash + 1)..]);
    }

    // Each form in the whole text of a build file, so that an element split over lines counts
    // too, with the line where it starts.
    private static IEnumerable<(int Line, string Message)> UnseenForms(string text)
    {
        (Regex Pattern, string Message)[] forms =
        [
            (LockFilePathSetting(), LockFilePathMessage),
            (
                VersionedSdk(),
                "an MSBuild SDK with a version is a package that NuGet resolves without a lock file. Remove it, or "
                    + "teach tools/CheckLicenses.cs to read it first."),
            (
                PackageDownload(),
                "PackageDownload fetches a package that no lock file records. Remove it, or teach "
                    + "tools/CheckLicenses.cs to read it first."),
        ];
        return forms.SelectMany(form => form.Pattern.Matches(text)
            .Select(match => (text.AsSpan(0, match.Index).Count('\n') + 1, form.Message)));
    }

    private static bool HasMsbuildSdks(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("msbuild-sdks", out _);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"global.json is not valid JSON: {exception.Message}");
        }
    }

    private static Problem Problem(string file, int line, string message) =>
        new(Reason.Unseen, Kind.Package, file, file, line, message);

    [GeneratedRegex(@"^#:property\s+NuGetLockFilePath\b", RegexOptions.IgnoreCase)]
    private static partial Regex LockFilePathProperty();

    // MSBuild project files of any language, .props and .targets files, and response files such
    // as Directory.Build.rsp, which can set properties for every build below them.
    [GeneratedRegex(@"\.(?:[A-Za-z]*proj|props|targets|rsp)$")]
    private static partial Regex BuildFileName();

    // The property as an element, or as a command-line setting such as -p:NuGetLockFilePath=.
    [GeneratedRegex(@"<NuGetLockFilePath\b|\bNuGetLockFilePath\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex LockFilePathSetting();

    // Sdk="Name/Version" on a project or an import, in either quotes, and <Sdk> or <Import> with
    // a Version, also when the element spans lines.
    [GeneratedRegex(@"\bSdk\s*=\s*[""'][^""']*/|<(?:Sdk|Import)\b[^>]*\bVersion\s*=", RegexOptions.IgnoreCase)]
    private static partial Regex VersionedSdk();

    [GeneratedRegex(@"<PackageDownload\b", RegexOptions.IgnoreCase)]
    private static partial Regex PackageDownload();
}

internal static class Coverage
{
    // Which register categories cover which kind of dependency (C-0003 section 5). A row in
    // the Forbidden or Under review category covers nothing. A Tool row covers tools and
    // actions, never a package in a lock file or a submodule.
    public static readonly Dictionary<Kind, string[]> CoveringCategories = new()
    {
        [Kind.Package] = ["Allowed", "Conditional"],
        [Kind.Submodule] = ["Allowed", "Conditional"],
        [Kind.Tool] = ["Allowed", "Conditional", "Tool"],
        [Kind.Action] = ["Allowed", "Conditional", "Tool"],
    };

    public static List<Problem> Check(List<Use> uses, List<RegisterRow> register, LicenseMap map)
    {
        var problems = new List<Problem>();
        var distinct = uses
            .GroupBy(use => (use.Kind, Name: use.Name.ToLowerInvariant()))
            .Select(group => group.First());
        foreach (var use in distinct)
        {
            var component = map.ComponentFor(use.Kind, use.Name);
            var row = register.FirstOrDefault(candidate => candidate.Component == component);
            var (reason, message) = (component, row) switch
            {
                (null, _) => (
                    Reason.Unmapped,
                    $"{Describe(use.Kind)} {use.Name} has no entry in {LicenseMap.FilePath}. Register it under C-0003 "
                        + "first, then map it to its row."),
                (_, null) => (
                    Reason.MissingRow,
                    $"{LicenseMap.FilePath} maps {use.Name} to \"{component}\", which is not a row of the register."),
                _ when !CoveringCategories[use.Kind].Contains(row.Category) => (
                    Reason.CategoryDoesNotCover,
                    $"{Describe(use.Kind)} {use.Name} maps to \"{component}\", whose category {row.Category} does not "
                        + $"cover a {Describe(use.Kind)}."),
                _ => ((Reason?)null, string.Empty),
            };
            if (reason is { } found)
            {
                problems.Add(new Problem(found, use.Kind, use.Name, use.File, use.Line, message));
            }
        }

        foreach (var (kind, name, _) in map.Entries)
        {
            if (!uses.Any(use => use.Kind == kind && string.Equals(use.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                problems.Add(new Problem(
                    Reason.Unused,
                    kind,
                    name,
                    LicenseMap.FilePath,
                    0,
                    $"the entry for {Describe(kind)} {name} matches nothing in the repository; remove it."));
            }
        }

        return problems;
    }

    public static string Describe(Kind kind) => kind switch
    {
        Kind.Package => "package",
        Kind.Tool => "tool",
        Kind.Action => "action",
        _ => "submodule",
    };
}

internal static class LicenseCheck
{
    public static int Run()
    {
        var root = Git.Run(Directory.GetCurrentDirectory(), "rev-parse", "--show-toplevel").Trim();
        var files = Git.Run(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(file => File.Exists(Path.Combine(root, file)))
            .Order(StringComparer.Ordinal)
            .ToList();

        var register = Register.Parse(Read(root, Register.FilePath));
        var map = LicenseMap.Parse(Read(root, LicenseMap.FilePath));

        var lockFiles = files.Where(Inventory.IsLockFile).ToList();
        var tools = files.Where(file => IsIn(file, "tools/") && HasExtension(file, ".cs")).ToList();
        var actionFiles = files.Where(Inventory.IsActionFile).ToList();
        var toolchain = files.Contains("eng/toolchain.json");
        var gitModules = files.Contains(".gitmodules");

        var uses = new List<Use>();
        uses.AddRange(lockFiles.SelectMany(file => Inventory.LockFile(file, Read(root, file))));
        uses.AddRange(tools.SelectMany(file => Inventory.Directives(file, Read(root, file))));
        uses.AddRange(actionFiles.SelectMany(file => Inventory.Workflow(file, Read(root, file))));
        uses.AddRange(toolchain ? Inventory.Toolchain("eng/toolchain.json", Read(root, "eng/toolchain.json")) : []);
        uses.AddRange(gitModules ? Inventory.GitModules(".gitmodules", Read(root, ".gitmodules")) : []);

        Console.WriteLine("Dependency licenses (C-0011 section 2)");
        Console.WriteLine($"Register: {register.Count} rows in {Register.FilePath} section 6.");
        Console.WriteLine(
            $"Read {lockFiles.Count} lock files, the directives of {tools.Count} file-based tools, "
                + $"{actionFiles.Count} workflow and action files, "
                + $"{(toolchain ? "eng/toolchain.json" : "no eng/toolchain.json")} "
                + $"and {(gitModules ? ".gitmodules" : "no .gitmodules")}.");

        var problems = Coverage.Check(uses, register, map);
        problems.AddRange(Unseen.Find(files, file => Read(root, file)));
        foreach (var kind in Enum.GetValues<Kind>())
        {
            PrintCovered(kind, uses, problems, register, map);
        }

        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        foreach (var problem in problems)
        {
            var line = problem.Line > 0 ? $":{problem.Line}" : string.Empty;
            if (annotate)
            {
                var property = problem.Line > 0 ? $",line={problem.Line}" : string.Empty;
                Console.WriteLine(
                    $"::error file={problem.File}{property},title=Dependency licenses::{problem.Message}");
            }

            Console.WriteLine($"{problem.File}{line}: {problem.Message}");
        }

        Console.WriteLine(problems.Count == 0
            ? "Every dependency is covered by a row of the register."
            : $"{problems.Count} problems. A dependency merges only with an accepted register row (C-0003 section 5).");
        return problems.Count == 0 ? 0 : 1;
    }

    private static void PrintCovered(
        Kind kind, List<Use> uses, List<Problem> problems, List<RegisterRow> register, LicenseMap map)
    {
        var names = uses.Where(use => use.Kind == kind)
            .GroupBy(use => use.Name.ToLowerInvariant())
            .Select(group => group.First().Name)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        Console.WriteLine($"{Coverage.Describe(kind)}s: {names.Count}");
        var covered = names.Where(name => !problems.Any(problem => problem.Kind == kind && problem.Name == name));
        foreach (var name in covered)
        {
            var component = map.ComponentFor(kind, name)!;
            var category = register.First(row => row.Component == component).Category;
            Console.WriteLine($"  {name,-52} {component} ({category})");
        }
    }

    private static bool IsIn(string file, string folder) => file.StartsWith(folder, StringComparison.Ordinal);

    private static bool HasExtension(string file, string extension) =>
        file.EndsWith(extension, StringComparison.Ordinal);

    private static string Read(string root, string file)
    {
        var path = Path.Combine(root, file);
        return File.Exists(path) ? File.ReadAllText(path) : throw new InvalidDataException($"{file} does not exist.");
    }
}

internal static class Git
{
    public static string Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Start(start);
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output
            : throw new InvalidDataException($"git {string.Join(' ', arguments)} failed: {error.Result.Trim()}");
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidDataException("git could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"git could not be started: {exception.Message}");
        }
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

// Every fixture is synthetic: a made-up register, and made-up package IDs, tools, actions and
// submodules (C-0011 section 1).
internal static class SelfTest
{
    private const string SampleRegister =
        """
        # Sample licensing contract

        ## 6. Component register

        | Component | Purpose | License | Category |
        |---|---|---|---|
        | Sample.Allowed | A library | MIT | Allowed |
        | Sample.Conditional | A library | LGPL-2.1-only | Conditional, as a separate component |
        | Sample.Tool | A build tool | GPL-3.0-or-later | Tool |
        | Sample.Review | A component | Proprietary | Under review: not used yet |
        | Sample.Forbidden | A library | GPL-3.0 | Forbidden |

        Text after the table.
        """;

    private const string SampleLockFile =
        """
        {
          "version": 2,
          "dependencies": {
            "net10.0": {
              "Sample.Direct": {
                "type": "Direct",
                "requested": "[1.0.0, )",
                "resolved": "1.0.0",
                "contentHash": "c2FtcGxl",
                "dependencies": { "Sample.Transitive": "2.0.0" }
              },
              "Sample.Transitive": { "type": "Transitive", "resolved": "2.0.0", "contentHash": "c2FtcGxl" },
              "Sample.Central": { "type": "CentralTransitive", "resolved": "3.0.0", "contentHash": "c2FtcGxl" },
              "sample.project": { "type": "Project" }
            },
            "net10.0/linux-x64": {
              "Sample.Native": { "type": "Transitive", "resolved": "4.0.0", "contentHash": "c2FtcGxl" }
            }
          }
        }
        """;

    private const string SampleTool =
        """
        /* A sample file-based tool,
           whose header starts with a block comment. */
        // A line comment.
        #nullable enable
        #:package Sample.Directive@1.2.3
        #:package Sample.Pinned.Centrally
        #pragma warning disable CA1000
        #:sdk Sample.Sdk@4.5.6
        #:property PublishAot=false
        /* #:package Sample.In.Comment@1.0.0 */
        #if DEBUG
        #:package Sample.After.If@1.0.0
        #endif

        return 0;

        #:package Sample.After.Code@1.0.0
        """;

    private const string SampleWorkflow =
        """
        jobs:
          sample:
            container: sample/container:1.0
            services:
              cache:
                image: "sample/service@sha256:0123456789abcdef"
            steps:
              # uses: commented-out/action@v1
              - uses: actions/checkout@0123456789abcdef0123456789abcdef01234567 # v1.0.0
              - name: A local action
                uses: ./.github/actions/local
              - uses: "sample-owner/sample-action/sub/path@v2"
              - uses: docker://sample/image:1.0
          build:
            container:
              image: sample/mapped-container:2.0
        """;

    private const string SampleWorkflowProse =
        """
        jobs:
          sample:
            steps:
              - name: Print the runner image: the label above
                if: ${{ contains(github.ref, 'uses: x') }}
                run: |
                  echo done # nothing here uses: an action
                  echo "container: in a script"
        """;

    private const string SampleAction =
        """
        runs:
          using: composite
          steps:
            - uses: sample-owner/composite-step@v1
        """;

    private const string SampleGitModules =
        """
        [submodule "external/Paneport-sample"]
            path = external/Paneport-sample
            url = https://example.com/sample.git
        [Submodule "external/Paneport-other"]
            Path = external/Paneport-other
            url = https://example.com/other.git
        """;

    private const string SampleToolchain =
        """
        { "tools": { "sample-tool": { "version": "1.0.0", "source": "https://example.com/" } } }
        """;

    private const string SampleMap =
        """
        {
          "packages": {
            "sample.direct": "Sample.Allowed",
            "Sample.Transitive": "Sample.Conditional",
            "Sample.Central": "Sample.Tool",
            "Sample.Native": "Sample.Review",
            "Sample.Directive": "Sample.Forbidden",
            "Sample.Sdk": "No.Such.Row",
            "Sample.Unused": "Sample.Allowed"
          },
          "tools": { "sample-tool": "Sample.Tool" },
          "actions": {
            "actions/checkout": "Sample.Tool",
            "sample-owner/sample-action": "Sample.Allowed",
            "docker://sample/image": "Sample.Tool",
            "docker://sample/container": "Sample.Tool",
            "docker://sample/service": "Sample.Tool",
            "docker://sample/mapped-container": "Sample.Tool"
          },
          "submodules": {
            "external/Paneport-sample": "Sample.Tool",
            "external/Paneport-other": "Sample.Allowed"
          }
        }
        """;

    public static int Run()
    {
        Console.WriteLine("Self-test of tools/CheckLicenses.cs, with synthetic fixtures only.");
        var tally = new Tally();

        Console.WriteLine("Register table:");
        var register = Register.Parse(SampleRegister);
        string[] categories = ["Allowed", "Conditional", "Tool", "Under review", "Forbidden"];
        tally.Expect(
            register.Select(row => row.Category).SequenceEqual(categories),
            "reads every row and the leading word of its category");
        tally.Expect(Rejects(() => Register.Parse(Edit(SampleRegister, "| Purpose ", ""))), "rejects changed columns");
        tally.Expect(
            Rejects(() => Register.Parse(Edit(SampleRegister, "## 6.", "## 7."))),
            "rejects a contract without the register heading");
        tally.Expect(
            Rejects(() => Register.Parse(Edit(SampleRegister, "| Forbidden |", "| Maybe |"))),
            "rejects a category it does not know");
        tally.Expect(
            Rejects(() => Register.Parse(Edit(SampleRegister, "| MIT | Allowed |", "| Allowed |"))),
            "rejects a row with a missing cell");

        Console.WriteLine("Inputs:");
        string[] lockFiles =
            ["src/A/packages.lock.json", "src/A/packages.A.lock.json", "tools/packages.CheckCompat.cs.lock.json"];
        string[] otherFiles = ["src/A/packages.json", "src/A/my.packages.lock.json", "src/A/packages.lock.json.bak"];
        tally.Expect(
            lockFiles.All(Inventory.IsLockFile) && !otherFiles.Any(Inventory.IsLockFile),
            "packages.lock.json and packages.<project>.lock.json are lock files, and nothing else is");
        var lockUses = Inventory.LockFile("sample/packages.lock.json", SampleLockFile);
        tally.Expect(
            Names(lockUses).SetEquals(["Sample.Direct", "Sample.Transitive", "Sample.Central", "Sample.Native"]),
            "a lock file yields its direct, transitive and central packages of every runtime, not its projects");
        var directiveUses = Inventory.Directives("tools/Sample.cs", SampleTool);
        tally.Expect(
            Names(directiveUses).SetEquals(["Sample.Directive", "Sample.Pinned.Centrally", "Sample.Sdk"]),
            "a file-based tool yields the #:package and #:sdk directives before its first token, also after "
                + "comments and #nullable or #pragma, but not in a comment, after #if or after code");
        string[] actionFiles =
            [".github/workflows/ci.yml", ".github/actions/local/action.yml", "eng/actions/helper/action.yaml"];
        string[] notActionFiles = [".github/workflows/README.md", "docs/samples/workflow.yml"];
        tally.Expect(
            actionFiles.All(Inventory.IsActionFile) && !notActionFiles.Any(Inventory.IsActionFile),
            "the YAML files under .github/ and every action.yml are read for actions, and nothing else is");
        var actionUses = Inventory.Workflow(".github/workflows/sample.yml", SampleWorkflow);
        string[] actions =
        [
            "actions/checkout",
            "sample-owner/sample-action",
            "docker://sample/image",
            "docker://sample/container",
            "docker://sample/service",
            "docker://sample/mapped-container",
        ];
        tally.Expect(
            Names(actionUses).SetEquals(actions),
            "a workflow yields its actions and the images of its containers and services, not local actions");
        tally.Expect(
            Names(Inventory.Workflow(".github/actions/local/action.yml", SampleAction))
                .SetEquals(["sample-owner/composite-step"]),
            "a local action yields the actions its steps use");
        foreach (var (name, yaml) in UnreadableWorkflows())
        {
            tally.Expect(Rejects(() => Inventory.Workflow(".github/workflows/sample.yml", yaml)), $"rejects {name}");
        }

        tally.Expect(
            Inventory.Workflow(".github/workflows/sample.yml", SampleWorkflowProse).Count == 0,
            "reads past uses:, image: and container: in step names, comments, expressions and scripts");

        var submoduleUses = Inventory.GitModules(".gitmodules", SampleGitModules);
        tally.Expect(
            Names(submoduleUses).SetEquals(["external/Paneport-sample", "external/Paneport-other"]),
            ".gitmodules yields each path, whatever the case of its keys");
        var toolUses = Inventory.Toolchain("eng/toolchain.json", SampleToolchain);
        tally.Expect(Names(toolUses).SetEquals(["sample-tool"]), "eng/toolchain.json yields each pinned tool");
        tally.Expect(
            Rejects(() => Inventory.Directives("tools/Sample.cs", "#:package Sample Directive@1.0")),
            "rejects a directive it cannot read");
        tally.Expect(
            Names(Inventory.Directives("tools/Spaced.cs", "#: package Sample.Spaced\n#:\t sdk Sample.Tabbed@1.0\n"))
                .SetEquals(["Sample.Spaced", "Sample.Tabbed"]),
            "reads #: package and #: sdk with whitespace after #:, which the SDK accepts too");
        tally.Expect(
            Rejects(() => Inventory.Toolchain("eng/toolchain.json", """{ "tools": {}, "xcode": {} }""")),
            "rejects a pin beside \"tools\" in eng/toolchain.json");
        tally.Expect(
            Rejects(() => Inventory.LockFile("sample/packages.lock.json", """{ "dependencies": { "x": [] } }""")),
            "rejects a lock file of the wrong shape");
        tally.Expect(
            Rejects(() => Inventory.Toolchain("eng/toolchain.json", "[]")), "rejects a pin file that is no object");

        Console.WriteLine("Coverage:");
        var uses = lockUses.Concat(directiveUses).Concat(actionUses).Concat(submoduleUses).Concat(toolUses).ToList();
        var problems = Coverage.Check(uses, register, LicenseMap.Parse(SampleMap));
        Expect(tally, problems, "Sample.Direct", null, "a package on an Allowed row is covered, whatever its case");
        Expect(tally, problems, "Sample.Transitive", null, "a package on a Conditional row is covered");
        Expect(tally, problems, "Sample.Central", Reason.CategoryDoesNotCover, "a Tool row does not cover a package");
        Expect(tally, problems, "Sample.Native", Reason.CategoryDoesNotCover, "an Under review row covers nothing");
        Expect(tally, problems, "Sample.Directive", Reason.CategoryDoesNotCover, "a Forbidden row covers nothing");
        Expect(tally, problems, "Sample.Sdk", Reason.MissingRow, "a mapping to a row the register lacks fails");
        Expect(tally, problems, "Sample.Pinned.Centrally", Reason.Unmapped, "a package without a mapping fails");
        Expect(tally, problems, "Sample.Unused", Reason.Unused, "a mapping that matches nothing fails");
        Expect(tally, problems, "sample-tool", null, "a Tool row covers a tool");
        Expect(tally, problems, "actions/checkout", null, "a Tool row covers an action");
        Expect(tally, problems, "sample-owner/sample-action", null, "an Allowed row covers an action");
        Expect(tally, problems, "docker://sample/image", null, "a Tool row covers an image");
        Expect(tally, problems, "docker://sample/service", null, "a Tool row covers a service's image");
        Expect(
            tally,
            problems,
            "external/Paneport-sample",
            Reason.CategoryDoesNotCover,
            "a Tool row does not cover a submodule");
        Expect(tally, problems, "external/Paneport-other", null, "an Allowed row covers a submodule");
        tally.Expect(problems.Count == 7, $"exactly the seven problems above are reported ({problems.Count})");
        tally.Expect(
            Rejects(() => LicenseMap.Parse("""{ "libraries": { "Sample.Direct": "Sample.Allowed" } }""")),
            "rejects a map section it does not know");
        tally.Expect(
            Rejects(() => LicenseMap.Parse("""{ "packages": { "Sample.Direct": "A", "SAMPLE.DIRECT": "B" } }""")),
            "rejects a package listed twice");
        tally.Expect(Rejects(() => LicenseMap.Parse("[]")), "rejects a map that is no object");

        Console.WriteLine("Packages that no lock file shows:");
        CheckUnseen(tally);

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
    }

    private static List<(string Name, string Yaml)> UnreadableWorkflows() =>
    [
        ("a step written as a flow mapping", "steps:\n  - { name: Flow, uses: sample-owner/flow@v1 }"),
        ("a uses: value on the next line", "steps:\n  - uses:\n      sample-owner/next-line@v1"),
        ("a container written as a flow mapping", "jobs:\n  a:\n    container: { image: sample/flow:1.0 }"),
        ("an image on the next line", "jobs:\n  a:\n    container:\n      image:\n        sample/next-line:1.0"),
        ("an image named by an expression", "jobs:\n  a:\n    container:\n      image: ${{ matrix.image }}"),
        ("an image built from a Dockerfile", "runs:\n  using: docker\n  image: Dockerfile"),
    ];

    private static void CheckUnseen(Tally tally)
    {
        const string Project = "<Project Sdk=\"Microsoft.NET.Sdk\" />";
        var repository = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/Locked/Locked.csproj"] = Project,
            ["src/Locked/packages.lock.json"] = SampleLockFile,
            ["src/Named/Named.csproj"] = Project,
            ["src/Named/packages.Named.lock.json"] = SampleLockFile,
            ["src/Unlocked/Unlocked.csproj"] = Project,
            ["src/Sdk/Sdk.csproj"] =
                "<Project Sdk=\"Sample.Project.Sdk/1.0.0\">\n  <Sdk Name=\"Sample.Element.Sdk\" Version=\"1.0.0\" />\n"
                    + "  <Import Project=\"Sdk.props\" Sdk=\"Sample.Import.Sdk\" Version=\"1.0.0\" />\n</Project>",
            ["src/Sdk/packages.lock.json"] = SampleLockFile,
            ["src/Quoted/Quoted.csproj"] =
                "<Project Sdk='Sample.Quoted.Sdk/1.0.0'>\n  <Sdk Name=\"Sample.Split.Sdk\"\n"
                    + "       Version=\"1.0.0\" />",
            ["src/Quoted/packages.lock.json"] = SampleLockFile,
            ["build.proj"] = "<Project Sdk=\"Sample.Traversal.Sdk/1.0.0\" />",
            ["eng/Sample.targets"] = "<Project>\n  <PackageDownload Include=\"Sample.Download\" Version=\"[1.0.0]\" />",
            ["eng/Sample.props"] = "<Project>\n  <NuGetLockFilePath>elsewhere.json</NuGetLockFilePath>",
            ["Directory.Build.rsp"] = "-p:NuGetLockFilePath=elsewhere.json",
            ["tools/Plain.cs"] = "return 0;",
            ["tools/Owned.cs"] = "#:package Sample.Owned@1.0.0\nreturn 0;",
            ["tools/packages.Owned.cs.lock.json"] = SampleLockFile,
            ["tools/Shared.cs"] = "#:package Sample.Shared@1.0.0\nreturn 0;",
            ["tools/Moved.cs"] = "// A tool.\n#:property NuGetLockFilePath=elsewhere.json\nreturn 0;",
            ["tools/Spaced.cs"] = "#: property NuGetLockFilePath=elsewhere.json\n#:\tpackage Sample.Spaced\nreturn 0;",
            ["tools/packages.lock.json"] = SampleLockFile,
            ["global.json"] = """{ "sdk": { "version": "1.0.0" }, "msbuild-sdks": { "Sample.Sdk": "1.0.0" } }""",
            [".config/dotnet-tools.json"] = """{ "version": 1, "isRoot": true, "tools": {} }""",
        };
        var found = Unseen.Find(repository.Keys, file => repository[file])
            .Select(problem => (problem.File, problem.Line))
            .ToList();
        (string File, int Line)[] unlocked = [("src/Unlocked/Unlocked.csproj", 0)];
        (string File, int Line)[] sdks =
            [("src/Sdk/Sdk.csproj", 1), ("src/Sdk/Sdk.csproj", 2), ("src/Sdk/Sdk.csproj", 3)];
        (string File, int Line)[] otherSdks =
            [("src/Quoted/Quoted.csproj", 1), ("src/Quoted/Quoted.csproj", 2), ("build.proj", 1)];
        (string File, int Line)[] downloads = [("eng/Sample.targets", 2)];
        (string File, int Line)[] moved =
            [("eng/Sample.props", 2), ("Directory.Build.rsp", 1), ("tools/Moved.cs", 2), ("tools/Spaced.cs", 1)];
        (string File, int Line)[] ownLockFiles = [("tools/Shared.cs", 0), ("tools/Spaced.cs", 0)];
        (string File, int Line)[] others = [("global.json", 0), (".config/dotnet-tools.json", 0)];
        tally.Expect(unlocked.All(found.Contains), "a project without a lock file fails");
        tally.Expect(
            !found.Any(problem => problem.File.StartsWith("src/Locked/", StringComparison.Ordinal)
                || problem.File.StartsWith("src/Named/", StringComparison.Ordinal)),
            "a project with packages.lock.json or packages.<project>.lock.json passes");
        tally.Expect(
            sdks.All(found.Contains),
            "an MSBuild SDK with a version fails, on the project, as an element and on an import");
        tally.Expect(
            otherSdks.All(found.Contains),
            "so does one in single quotes, one split over lines, and one in any MSBuild project file");
        tally.Expect(downloads.All(found.Contains), "PackageDownload fails");
        tally.Expect(
            moved.All(found.Contains),
            "NuGetLockFilePath fails, in a project file, a response file and a directive, also as #: property");
        tally.Expect(
            ownLockFiles.All(found.Contains)
                && !found.Any(problem => problem.File is "tools/Owned.cs" or "tools/Plain.cs"),
            "a tool with package directives, also as #: package, needs a lock file of its own; others share one");
        tally.Expect(others.All(found.Contains), "msbuild-sdks in global.json fails, and so does a tool manifest");
        var count = unlocked.Length + sdks.Length + otherSdks.Length + downloads.Length + moved.Length
            + ownLockFiles.Length + others.Length;
        tally.Expect(found.Count == count, $"exactly the {count} problems above are reported ({found.Count})");
    }

    private static string Edit(string text, string from, string to) => text.Replace(from, to, StringComparison.Ordinal);

    private static HashSet<string> Names(List<Use> uses) =>
        uses.Select(use => use.Name).ToHashSet(StringComparer.Ordinal);

    private static void Expect(Tally tally, List<Problem> problems, string name, Reason? reason, string description)
    {
        var found = problems.Where(problem => problem.Name == name).Select(problem => (Reason?)problem.Reason).ToList();
        tally.Expect(reason is null ? found.Count == 0 : found.SequenceEqual([reason]), description);
    }

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
}
