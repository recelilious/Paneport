// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Fails on any dependency that the register in C-0003 section 6 does not cover (C-0011
// section 2). It reads every packages.lock.json (direct and transitive packages), the package
// directives of the file-based tools in tools/, .gitmodules, eng/toolchain.json, and the uses:
// lines of the workflows, because actions are CI tools under C-0003 section 5.
// eng/license-map.json maps each of them to the register row that covers it.
//
//   dotnet run tools/CheckLicenses.cs                 check the repository
//   dotnet run tools/CheckLicenses.cs -- --self-test  check the parsers and rules on fixtures
//
// Exit codes: 0 every dependency is covered, 1 something is not, 2 usage or configuration
// error. docs/design/repository-checks.md describes the mapping policy.

#:property PublishAot=false

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
catch (InvalidDataException exception)
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
    // Direct, transitive and centrally pinned packages of every target and runtime. Project
    // entries are the solution's own projects.
    public static List<Use> LockFile(string path, string json)
    {
        var uses = new List<Use>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("dependencies", out var targets)
                || targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path} has no \"dependencies\" object.");
            }

            foreach (var target in targets.EnumerateObject())
            {
                foreach (var package in target.Value.EnumerateObject())
                {
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

    // #:package Name@Version, #:package Name (with central package management), and
    // #:sdk Name@Version, which NuGet also resolves. The compiler accepts directives only at the
    // top of a file, before any code, so reading stops at the first line of code.
    public static List<Use> Directives(string path, string source)
    {
        var uses = new List<Use>();
        var lines = source.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0
                || line.StartsWith("//", StringComparison.Ordinal)
                || line.StartsWith("#!", StringComparison.Ordinal))
            {
                continue;
            }

            if (!line.StartsWith("#:", StringComparison.Ordinal))
            {
                break;
            }

            if (!line.StartsWith("#:package", StringComparison.Ordinal)
                && !line.StartsWith("#:sdk", StringComparison.Ordinal))
            {
                continue;
            }

            var match = Directive().Match(line);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"{path} line {index + 1}: a package directive that the checker cannot read.");
            }

            uses.Add(new Use(Kind.Package, match.Groups[1].Value, path, index + 1));
        }

        return uses;
    }

    public static List<Use> Toolchain(string path, string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!document.RootElement.TryGetProperty("tools", out var tools) || tools.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{path} has no \"tools\" object.");
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

    // The uses: lines of a workflow. owner/repo/path@ref names the action owner/repo; a local
    // action (./path) is our own code; docker://image:tag names the image.
    public static List<Use> Workflow(string path, string yaml)
    {
        var uses = new List<Use>();
        var lines = yaml.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var match = UsesLine().Match(lines[index]);
            if (!match.Success || match.Groups[1].Value.StartsWith("./", StringComparison.Ordinal))
            {
                continue;
            }

            uses.Add(new Use(Kind.Action, ActionName(match.Groups[1].Value, path, index + 1), path, index + 1));
        }

        return uses;
    }

    // Each submodule is named by its path, for example external/Paneport-wine.
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
                inSubmodule = line.StartsWith("[submodule ", StringComparison.Ordinal);
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
            return DockerTag().Replace(value, string.Empty);
        }

        var at = value.IndexOf('@', StringComparison.Ordinal);
        var segments = at < 0 ? [] : value[..at].Split('/');
        return segments.Length >= 2
            ? $"{segments[0]}/{segments[1]}"
            : throw new InvalidDataException($"{path} line {line}: \"{value}\" is not of the form owner/repo@ref.");
    }

    private static int LineOf(string text, string needle)
    {
        var index = text.IndexOf(needle, StringComparison.Ordinal);
        return index < 0 ? 0 : text.AsSpan(0, index).Count('\n') + 1;
    }

    [GeneratedRegex(@"^#:(?:package|sdk)\s+([A-Za-z0-9_.-]+)(?:@\S+)?$")]
    private static partial Regex Directive();

    [GeneratedRegex(@"^\s*(?:-\s+)?uses:\s*[""']?([^""'\s#]+)[""']?")]
    private static partial Regex UsesLine();

    [GeneratedRegex(@"(?:@[^/]*|:[^/:]*)$")]
    private static partial Regex DockerTag();

    [GeneratedRegex(@"^path\s*=\s*(.+)$")]
    private static partial Regex SubmodulePath();
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

        var lockFiles = files.Where(file => file.Split('/')[^1] == "packages.lock.json").ToList();
        var tools = files.Where(file => IsIn(file, "tools/") && HasExtension(file, ".cs")).ToList();
        var workflows = files
            .Where(file => IsIn(file, ".github/workflows/"))
            .Where(file => HasExtension(file, ".yml") || HasExtension(file, ".yaml"))
            .ToList();
        var toolchain = files.Contains("eng/toolchain.json");
        var gitModules = files.Contains(".gitmodules");

        var uses = new List<Use>();
        uses.AddRange(lockFiles.SelectMany(file => Inventory.LockFile(file, Read(root, file))));
        uses.AddRange(tools.SelectMany(file => Inventory.Directives(file, Read(root, file))));
        uses.AddRange(workflows.SelectMany(file => Inventory.Workflow(file, Read(root, file))));
        uses.AddRange(toolchain ? Inventory.Toolchain("eng/toolchain.json", Read(root, "eng/toolchain.json")) : []);
        uses.AddRange(gitModules ? Inventory.GitModules(".gitmodules", Read(root, ".gitmodules")) : []);

        Console.WriteLine("Dependency licenses (C-0011 section 2)");
        Console.WriteLine($"Register: {register.Count} rows in {Register.FilePath} section 6.");
        Console.WriteLine(
            $"Read {lockFiles.Count} lock files, the directives of {tools.Count} file-based tools, "
                + $"{workflows.Count} workflows, {(toolchain ? "eng/toolchain.json" : "no eng/toolchain.json")} "
                + $"and {(gitModules ? ".gitmodules" : "no .gitmodules")}.");

        var problems = Coverage.Check(uses, register, map);
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

        using var process = Process.Start(start) ?? throw new InvalidDataException("git could not be started.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output
            : throw new InvalidDataException($"git {string.Join(' ', arguments)} failed: {error.Result.Trim()}");
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
        // A sample file-based tool.

        #:package Sample.Directive@1.2.3
        #:package Sample.Pinned.Centrally
        #:sdk Sample.Sdk@4.5.6
        #:property PublishAot=false

        return 0;

        #:package Sample.After.Code@1.0.0
        """;

    private const string SampleWorkflow =
        """
        jobs:
          sample:
            steps:
              - uses: actions/checkout@0123456789abcdef0123456789abcdef01234567 # v1.0.0
              - name: A local action
                uses: ./.github/actions/local
              - uses: "sample-owner/sample-action/sub/path@v2"
              - uses: docker://sample/image:1.0
        """;

    private const string SampleGitModules =
        """
        [submodule "external/Paneport-sample"]
            path = external/Paneport-sample
            url = https://example.com/sample.git
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
            "docker://sample/image": "Sample.Tool"
          },
          "submodules": { "external/Paneport-sample": "Sample.Tool" }
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
        var lockUses = Inventory.LockFile("sample/packages.lock.json", SampleLockFile);
        tally.Expect(
            Names(lockUses).SetEquals(["Sample.Direct", "Sample.Transitive", "Sample.Central", "Sample.Native"]),
            "a lock file yields its direct, transitive and central packages of every runtime, not its projects");
        var directiveUses = Inventory.Directives("tools/Sample.cs", SampleTool);
        tally.Expect(
            Names(directiveUses).SetEquals(["Sample.Directive", "Sample.Pinned.Centrally", "Sample.Sdk"]),
            "a file-based tool yields the #:package and #:sdk directives at its top, not #:property");
        var actionUses = Inventory.Workflow(".github/workflows/sample.yml", SampleWorkflow);
        tally.Expect(
            Names(actionUses).SetEquals(["actions/checkout", "sample-owner/sample-action", "docker://sample/image"]),
            "a workflow yields its actions and images, not its local actions");
        var submoduleUses = Inventory.GitModules(".gitmodules", SampleGitModules);
        tally.Expect(Names(submoduleUses).SetEquals(["external/Paneport-sample"]), ".gitmodules yields each path");
        var toolUses = Inventory.Toolchain("eng/toolchain.json", SampleToolchain);
        tally.Expect(Names(toolUses).SetEquals(["sample-tool"]), "eng/toolchain.json yields each pinned tool");
        tally.Expect(
            Rejects(() => Inventory.Directives("tools/Sample.cs", "#:package Sample Directive@1.0")),
            "rejects a directive it cannot read");

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
        Expect(
            tally,
            problems,
            "external/Paneport-sample",
            Reason.CategoryDoesNotCover,
            "a Tool row does not cover a submodule");
        tally.Expect(problems.Count == 7, $"exactly the seven problems above are reported ({problems.Count})");
        tally.Expect(
            Rejects(() => LicenseMap.Parse("""{ "libraries": { "Sample.Direct": "Sample.Allowed" } }""")),
            "rejects a map section it does not know");
        tally.Expect(
            Rejects(() => LicenseMap.Parse("""{ "packages": { "Sample.Direct": "A", "SAMPLE.DIRECT": "B" } }""")),
            "rejects a package listed twice");

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
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
