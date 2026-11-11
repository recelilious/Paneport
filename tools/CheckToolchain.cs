// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Compares the installed toolchain with its pins (C-0009 section 2): the .NET SDK with
// global.json, the runner image of the workflows' macOS jobs with eng/toolchain.json, and,
// when asked, the tools that only some machines have with eng/toolchain.json.
//
//   dotnet run tools/CheckToolchain.cs                    check the SDK and the runner image
//   dotnet run tools/CheckToolchain.cs -- --ci-tools      also check the tools of the checks job
//   dotnet run tools/CheckToolchain.cs -- --xcode         also check Xcode, on a Mac
//   dotnet run tools/CheckToolchain.cs -- --ios-workload  also check the installed iOS workload set
//   dotnet run tools/CheckToolchain.cs -- --cmake         also check CMake
//   dotnet run tools/CheckToolchain.cs -- --llvm          also check clang-format and clang-tidy
//   dotnet run tools/CheckToolchain.cs -- --fork-tools    also check the build tools of the forks
//   dotnet run tools/CheckToolchain.cs -- --self-test     check the parsers on synthetic outputs
//
// --ci-tools, --xcode, --ios-workload, --cmake, --llvm and --fork-tools may be combined. Exit
// codes: 0 everything matches its pin, 1 something does not, 2 usage or configuration error.
// docs/design/repository-checks.md describes the pins and how to change one.

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
        ["--self-test"] => SelfTest.Run(),
        _ => Cli.Options(args) is { } options ? ToolchainCheck.Run(options) : Cli.Usage(),
    };
}
catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal static class Cli
{
    // The options that also check the pins whose tools only some machines have.
    public static readonly string[] HostOptions =
        [Probes.CiTools, Probes.Xcode, Probes.IosWorkload, Probes.Cmake, Probes.Llvm, Probes.ForkTools];

    // The options given, or null when one is unknown or given twice.
    public static HashSet<string>? Options(string[] args)
    {
        var options = new HashSet<string>(StringComparer.Ordinal);
        return args.All(arg => HostOptions.Contains(arg) && options.Add(arg)) ? options : null;
    }

    public static int Usage()
    {
        Console.Error.WriteLine(
            "usage: dotnet run tools/CheckToolchain.cs "
                + "[-- [--ci-tools] [--xcode] [--ios-workload] [--cmake] [--llvm] [--fork-tools] | --self-test]");
        return 2;
    }
}

// What a command printed. A runner returns null when the command could not be started.
internal sealed record CommandResult(int ExitCode, string Output, string Error);

internal delegate CommandResult? CommandRunner(string command, string[] arguments);

internal sealed record SdkPin(string Version, string RollForward);

internal sealed record Download(string Runtime, string Url, string Sha256);

internal sealed record ToolPin(string Name, string Version, string Source, List<Download> Downloads);

internal static class GlobalJson
{
    public const string FilePath = "global.json";

    public static SdkPin Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("sdk", out var sdk)
                || sdk.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} has no \"sdk\" object.");
            }

            var version = Pins.Text(sdk, "version", FilePath);
            var rollForward = sdk.TryGetProperty("rollForward", out var property)
                && property.ValueKind == JsonValueKind.String
                    ? property.GetString()!
                    : "(not set)";
            return new SdkPin(version, rollForward);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }
}

// eng/toolchain.json: each tool has its exact version and the source it is installed from.
// A tool that CI downloads has, per runtime identifier, the download's address and SHA-256.
// The file holds nothing but "tools", and no field beyond these, so that no pin escapes the
// version check here and the register check of tools/CheckLicenses.cs.
internal static partial class ToolchainJson
{
    public const string FilePath = "eng/toolchain.json";

    private static readonly string[] s_toolFields = ["version", "source", "downloads"];
    private static readonly string[] s_downloadFields = ["url", "sha256"];

    public static List<ToolPin> Parse(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("tools", out var tools)
                || tools.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{FilePath} has no \"tools\" object.");
            }

            var other = root.EnumerateObject().Select(key => key.Name).FirstOrDefault(name => name != "tools");
            if (other is not null)
            {
                throw new InvalidDataException(
                    $"{FilePath}: \"{other}\" is not \"tools\". tools/CheckToolchain.cs and tools/CheckLicenses.cs "
                        + "read only \"tools\", so a pin elsewhere would go unchecked.");
            }

            return tools.EnumerateObject().Select(tool => ParseTool(tool.Name, tool.Value)).ToList();
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }
    }

    private static ToolPin ParseTool(string name, JsonElement tool)
    {
        var where = $"{FilePath}, tool {name}";
        if (tool.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{where}: not an object.");
        }

        var unknown = tool.EnumerateObject()
            .Select(field => field.Name)
            .FirstOrDefault(field => !s_toolFields.Contains(field));
        if (unknown is not null)
        {
            throw new InvalidDataException($"{where}: unknown field \"{unknown}\".");
        }

        var downloads = new List<Download>();
        if (tool.TryGetProperty("downloads", out var runtimes))
        {
            if (runtimes.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{where}: \"downloads\" is not an object.");
            }

            foreach (var runtime in runtimes.EnumerateObject())
            {
                var field = runtime.Value.ValueKind == JsonValueKind.Object
                    ? runtime.Value.EnumerateObject()
                        .Select(property => property.Name)
                        .FirstOrDefault(name => !s_downloadFields.Contains(name))
                    : throw new InvalidDataException($"{where}: the download for {runtime.Name} is not an object.");
                if (field is not null)
                {
                    throw new InvalidDataException(
                        $"{where}: unknown field \"{field}\" in the download for {runtime.Name}.");
                }

                var download = new Download(
                    runtime.Name,
                    Https(Pins.Text(runtime.Value, "url", where), where),
                    Pins.Text(runtime.Value, "sha256", where));
                downloads.Add(Sha256().IsMatch(download.Sha256)
                    ? download
                    : throw new InvalidDataException(
                        $"{where}: the sha256 of {runtime.Name} is not 64 lowercase hex digits."));
            }
        }

        var version = Pins.Text(tool, "version", where);
        return new ToolPin(name, version, Https(Pins.Text(tool, "source", where), where), downloads);
    }

    private static string Https(string url, string where) => url.StartsWith("https://", StringComparison.Ordinal)
        ? url
        : throw new InvalidDataException($"{where}: \"{url}\" is not an https address.");

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256();
}

internal static class Pins
{
    public static string Text(JsonElement element, string name, string where)
    {
        var value = element.ValueKind == JsonValueKind.Object
            && element.TryGetProperty(name, out var property)
            && property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{where}: \"{name}\" is missing or empty.")
            : value;
    }
}

// How to ask each pinned tool for its version, and how to read the answer. Each tool exists
// only on some machines, so Option names the option that runs its check; Hint, when there is
// one, follows a mismatch. Label names the program in a mismatch when it differs from the pin,
// as clang-format does for the llvm pin. A tool pinned in eng/toolchain.json without an entry
// here or a check of its own, as the runner image has, fails every run, so no pin goes
// unchecked.
internal sealed record VersionProbe(
    string Option, string Command, string[] Arguments, Regex Pattern, string Hint = "", string Label = "");

internal static partial class Probes
{
    public const string CiTools = "--ci-tools";
    public const string Xcode = "--xcode";
    public const string IosWorkload = "--ios-workload";
    public const string Cmake = "--cmake";
    public const string Llvm = "--llvm";
    public const string ForkTools = "--fork-tools";

    private const string DownloadHint =
        "eng/mac/bootstrap.sh installs the pinned release outside the repository and puts it first on the PATH.";

    // The second program of the llvm pin, which --llvm checks after clang-format.
    public static readonly VersionProbe ClangTidy =
        new(Llvm, "clang-tidy", ["--version"], ClangTidyVersion(), DownloadHint, "clang-tidy");

    public static readonly Dictionary<string, VersionProbe> ByTool = new(StringComparer.Ordinal)
    {
        ["reuse"] = new(CiTools, "reuse", ["--version"], ReuseVersion()),
        ["gitleaks"] = new(CiTools, "gitleaks", ["version"], GitleaksVersion()),
        ["lychee"] = new(CiTools, "lychee", ["--version"], LycheeVersion()),
        ["xcode"] = new(
            Xcode,
            "xcodebuild",
            ["-version"],
            XcodeVersion(),
            "xcodebuild reports the Xcode that DEVELOPER_DIR or xcode-select selects."),
        ["ios-workload-set"] = new(
            IosWorkload,
            "dotnet",
            ["workload", "--version"],
            WorkloadSetVersion(),
            "eng/mac/bootstrap.sh installs the pinned workload set."),
        ["cmake"] = new(Cmake, "cmake", ["--version"], CmakeVersion(), DownloadHint),
        ["llvm"] = new(Llvm, "clang-format", ["--version"], ClangFormatVersion(), DownloadHint, "clang-format"),
        ["ninja"] = new(ForkTools, "ninja", ["--version"], BareVersion(), DownloadHint),
        ["llvm-mingw"] = new(
            ForkTools,
            "x86_64-w64-mingw32-clang",
            ["--version"],
            LlvmMingwRelease(),
            "eng/mac/bootstrap.sh installs the pinned release outside the repository and names its folder in "
                + "LLVM_MINGW, off the PATH; to check it alone, put $LLVM_MINGW/bin at the end of the PATH. The "
                + "release is read from that folder's name on clang's InstalledDir line."),
        ["meson"] = new(ForkTools, "meson", ["--version"], BareVersion(), DownloadHint),
        ["python"] = new(ForkTools, "python3", ["--version"], PythonVersion(), DownloadHint),
        ["bison"] = new(
            ForkTools,
            "bison",
            ["--version"],
            BisonVersion(),
            "eng/mac/bootstrap.sh builds the pinned release outside the repository and puts it first on the PATH."),
        ["flex"] = new(
            ForkTools,
            "flex",
            ["--version"],
            FlexVersion(),
            "flex comes with the Xcode that DEVELOPER_DIR or xcode-select selects."),
    };

    // "reuse, version 6.2.0", followed by the license notice.
    [GeneratedRegex(@"\Areuse, version ([0-9][^\s]*)")]
    private static partial Regex ReuseVersion();

    // "8.30.1", or "v8.30.1" in older releases.
    [GeneratedRegex(@"\Av?([0-9][^\s]*)\z")]
    private static partial Regex GitleaksVersion();

    // "lychee 0.24.2".
    [GeneratedRegex(@"\Alychee ([0-9][^\s]*)")]
    private static partial Regex LycheeVersion();

    // "Xcode 27.1", followed by the build version on the next line.
    [GeneratedRegex(@"\AXcode ([0-9][^\s]*)")]
    private static partial Regex XcodeVersion();

    // "10.0.403", alone on its line. Without a workload set the SDK prints
    // "<feature band>-manifests.<hash>", and for a workload set that global.json names but that
    // is missing it adds "(not installed)"; neither passes.
    [GeneratedRegex(@"\A([0-9][^\s]*)(\r?\n|\z)")]
    private static partial Regex WorkloadSetVersion();

    // A row of the table that dotnet workload list prints: the workload ID, the manifest
    // version and the installation source, as in "ios   27.1.10018/10.0.100   SDK 10.0.400".
    [GeneratedRegex(@"^[ \t]*ios[ \t]+[0-9]", RegexOptions.Multiline)]
    public static partial Regex IosWorkloadRow();

    // "cmake version 4.4.5", followed by Kitware's line.
    [GeneratedRegex(@"\Acmake version ([0-9][^\s]*)")]
    private static partial Regex CmakeVersion();

    // "clang-format version 23.1.5 (https://github.com/llvm/llvm-project <commit>)", or with a
    // packager's word in front, such as "Homebrew clang-format version 23.1.5".
    [GeneratedRegex(@"\A(?:[^\s]+ )?clang-format version ([0-9][^\s]*)")]
    private static partial Regex ClangFormatVersion();

    // "LLVM (http://llvm.org/):", then "  LLVM version 23.1.5" on a line of its own, or with a
    // packager's word in front of "LLVM".
    [GeneratedRegex(@"^[ \t]*(?:[^\s]+ )?LLVM version ([0-9][^\s]*)", RegexOptions.Multiline)]
    private static partial Regex ClangTidyVersion();

    // A version alone, as ninja and meson print it: "1.13.2".
    [GeneratedRegex(@"\A([0-9][^\s]*)\z")]
    private static partial Regex BareVersion();

    // clang names neither the llvm-mingw release nor anything but the LLVM version, so the
    // release comes from the folder that holds clang: "llvm-mingw-<release>" as the bootstrap
    // installs it, or "llvm-mingw-<release>-<variant>" as the release archive names it, as in
    // "InstalledDir: /x/llvm-mingw-20261104/bin". A cross compiler of another kind, such as GCC
    // from Homebrew's mingw-w64, prints no such line.
    [GeneratedRegex(@"^InstalledDir: .*/llvm-mingw-([0-9]{8})(?:-[^/\s]*)?/bin[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex LlvmMingwRelease();

    // "Python 3.14.10".
    [GeneratedRegex(@"\APython ([0-9][^\s]*)")]
    private static partial Regex PythonVersion();

    // "bison (GNU Bison) 3.8.2", followed by the authors. macOS ships an older bison, 2.3.
    [GeneratedRegex(@"\Abison \(GNU Bison\) ([0-9][^\s]*)")]
    private static partial Regex BisonVersion();

    // "flex 2.6.4", which Apple's build follows with its own tag, as in "flex 2.6.4 Apple(flex-35)".
    [GeneratedRegex(@"\Aflex ([0-9][^\s]*)")]
    private static partial Regex FlexVersion();
}

internal static class Checks
{
    // Returns the problems; an empty list means that the SDK matches its pin.
    public static List<string> Sdk(SdkPin pin, CommandResult? dotnetVersion)
    {
        var problems = new List<string>();
        if (pin.RollForward != "disable")
        {
            problems.Add(
                $"{GlobalJson.FilePath} sets rollForward to {pin.RollForward}. The pin is exact (C-0009 section 2), "
                    + "so it must be \"disable\".");
        }

        if (dotnetVersion is null)
        {
            problems.Add("dotnet could not be started.");
        }
        else if (dotnetVersion.ExitCode != 0)
        {
            problems.Add(
                $"dotnet --version exited with {dotnetVersion.ExitCode}, which usually means that the SDK "
                    + $"{pin.Version} is not installed: {FirstLine(dotnetVersion.Error + dotnetVersion.Output)}");
        }
        else if (dotnetVersion.Output.Trim() != pin.Version)
        {
            problems.Add(
                $"the SDK in use is {dotnetVersion.Output.Trim()}, but {GlobalJson.FilePath} pins {pin.Version}.");
        }

        return problems;
    }

    public static List<string> Tool(ToolPin pin, CommandRunner run) => Probes.ByTool.TryGetValue(pin.Name, out var probe)
        ? Version(pin, probe, run)
        : [$"{ToolchainJson.FilePath} pins {pin.Name}, but tools/CheckToolchain.cs cannot check it yet."];

    // Compares the version that one program reports with a pin. A mismatch names the program,
    // both versions and, when the program is not named like the pin, the pin too.
    public static List<string> Version(ToolPin pin, VersionProbe probe, CommandRunner run)
    {
        var command = $"{probe.Command} {string.Join(' ', probe.Arguments)}";
        var result = run(probe.Command, probe.Arguments);
        if (result is null)
        {
            var missing = probe.Label.Length > 0 ? $"{probe.Label}, from the {pin.Name} pin," : pin.Name;
            return [$"{missing} is not installed, or not on the PATH."];
        }

        if (result.ExitCode != 0)
        {
            return [$"{command} exited with {result.ExitCode}: {FirstLine(result.Error + result.Output)}"];
        }

        var match = probe.Pattern.Match(result.Output.Trim());
        if (!match.Success)
        {
            return [$"{command} printed no version that this tool can read: {FirstLine(result.Output)}"];
        }

        var installed = match.Groups[1].Value;
        var hint = probe.Hint.Length > 0 ? $" {probe.Hint}" : string.Empty;
        var program = probe.Label.Length > 0 ? probe.Label : pin.Name;
        var pinned = program == pin.Name ? pin.Version : $"{pin.Name} {pin.Version}";
        return installed == pin.Version
            ? []
            : [$"{program} {installed} is installed, but {ToolchainJson.FilePath} pins {pinned}.{hint}"];
    }

    // The pins that no option of this tool checks. They fail every run.
    public static List<string> Unchecked(IEnumerable<ToolPin> pins) => pins
        .Where(pin => !Probes.ByTool.ContainsKey(pin.Name) && pin.Name != RunnerImage.Tool)
        .Select(pin => $"{ToolchainJson.FilePath} pins {pin.Name}, but tools/CheckToolchain.cs cannot check it yet.")
        .ToList();

    // The tools that an option checks but that eng/toolchain.json does not pin. Without them the
    // option would compare nothing and still pass.
    public static List<string> Missing(IEnumerable<ToolPin> pins, string option) => Probes.ByTool
        .Where(probe => probe.Value.Option == option && !pins.Any(pin => pin.Name == probe.Key))
        .Select(probe => $"{ToolchainJson.FilePath} pins no {probe.Key}, so {option} has nothing to compare.")
        .ToList();

    // The workload set holds the version of every workload, so the ios workload must also be
    // installed from it.
    public static List<string> IosWorkload(CommandRunner run)
    {
        var result = run("dotnet", ["workload", "list"]);
        if (result is null)
        {
            return ["dotnet could not be started."];
        }

        if (result.ExitCode != 0)
        {
            return [$"dotnet workload list exited with {result.ExitCode}: {FirstLine(result.Error + result.Output)}"];
        }

        return Probes.IosWorkloadRow().IsMatch(result.Output)
            ? []
            : ["the ios workload is not installed. eng/mac/bootstrap.sh installs it from the pinned workload set."];
    }

    private static string FirstLine(string text) =>
        text.Trim().Split('\n').FirstOrDefault(line => line.Trim().Length > 0)?.Trim() ?? "(no output)";
}

// The macOS jobs of the workflows run on the runner image that eng/toolchain.json pins
// (C-0009 section 2). GitHub names its macOS images "macos-<version>", and the images that
// each carry the releases of one Xcode version "xcode-<version>", and it compares runner
// labels without regard to case. The check reads a workflow by its indentation, and only in
// these spellings, each key on a line of its own:
//   - one top-level "jobs:" key, with each job below it as a plain "<id>:" key;
//   - in each job, a "runs-on:" key whose value is a plain label, with or without quotes, or
//     ${{ matrix.<key> }};
//   - for the matrix form, the job's own "strategy:" key and the "matrix:" key below it, whose
//     every line is a one-line list of plain entries, "<key>: [a, b]", none of them "include".
// A job that calls a workflow of this repository ("uses: ./...") has no runner of its own, and
// the called workflow is read like any other. Everything else fails with its file and line:
// another spelling of these keys, a job without a runs-on key that the check can read, and a
// job that calls a workflow outside this repository, whose runner the check cannot see. Lines
// inside a job's steps are never read as its runner.
internal static partial class RunnerImage
{
    public const string Tool = "macos-runner-image";
    public const string Folder = ".github/workflows";

    public static List<string> Check(ToolPin? pin, IReadOnlyList<(string File, string Text)> workflows)
    {
        var problems = new List<string>();
        var labels = new List<(string File, int Line, string Label)>();
        foreach (var (file, text) in workflows)
        {
            var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
            var jobs = Enumerable.Range(0, lines.Length).Where(index => JobsKey().IsMatch(lines[index])).ToList();
            if (jobs.Count != 1)
            {
                problems.Add(
                    $"{file}: tools/CheckToolchain.cs cannot read its jobs. Write one plain \"jobs:\" key, on a line "
                        + "of its own, with the jobs below it.");
                continue;
            }

            foreach (var job in Children(lines, jobs[0]))
            {
                ReadJob(file, lines, job, labels, problems);
            }
        }

        if (pin is not null && !IsMacOS(pin.Version))
        {
            problems.Add($"{ToolchainJson.FilePath} pins {Tool} {pin.Version}, which is not a macOS runner image.");
        }

        foreach (var (file, line, label) in labels.Where(entry => IsMacOS(entry.Label) && !Same(entry.Label, pin)))
        {
            problems.Add(pin is null
                ? $"{file} line {line} runs on {label}, but {ToolchainJson.FilePath} pins no {Tool}."
                : $"{file} line {line} runs on {label}, but {ToolchainJson.FilePath} pins {pin.Version}.");
        }

        if (pin is not null && !labels.Any(entry => Same(entry.Label, pin)))
        {
            problems.Add($"{ToolchainJson.FilePath} pins {Tool} {pin.Version}, but no job in {Folder} runs on it.");
        }

        return problems;
    }

    // Reads the runner of one job, or reports why it cannot.
    private static void ReadJob(
        string file, string[] lines, int job, List<(string File, int Line, string Label)> labels, List<string> problems)
    {
        if (!BlockKey().IsMatch(lines[job]))
        {
            problems.Add(
                $"{file} line {job + 1}: tools/CheckToolchain.cs cannot read this job. Write it as a plain "
                    + "\"<id>:\" key, on a line of its own, with its keys below it.");
            return;
        }

        var children = Children(lines, job);
        var other = children.Where(child => AnyRunsOn().IsMatch(lines[child]) && !RunsOnKey().IsMatch(lines[child]));
        foreach (var child in other)
        {
            problems.Add(
                $"{file} line {child + 1}: tools/CheckToolchain.cs cannot read this runs-on key. Spell it "
                    + "\"runs-on:\", on a line of its own.");
        }

        foreach (var child in children.Where(child => RunsOnKey().IsMatch(lines[child])))
        {
            ReadRunsOn(file, lines, job, child, labels, problems);
        }

        if (children.Any(child => AnyRunsOn().IsMatch(lines[child])))
        {
            return;
        }

        var uses = children.Select(child => (Line: child, Match: UsesKey().Match(lines[child])))
            .FirstOrDefault(entry => entry.Match.Success);
        var local = uses.Match?.Groups["path"].Value.StartsWith("./", StringComparison.Ordinal) == true;
        if (uses.Match is { Success: true } && local)
        {
            return;
        }

        problems.Add(uses.Match is { Success: true }
            ? $"{file} line {uses.Line + 1}: this job calls a workflow outside this repository, whose runner "
                + "tools/CheckToolchain.cs cannot read."
            : $"{file} line {job + 1}: tools/CheckToolchain.cs cannot read the runner of this job. Give it a "
                + "\"runs-on:\" key, on a line of its own.");
    }

    private static void ReadRunsOn(
        string file,
        string[] lines,
        int job,
        int runsOn,
        List<(string File, int Line, string Label)> labels,
        List<string> problems)
    {
        var value = RunsOnKey().Match(lines[runsOn]).Groups["value"].Value;
        var matrix = MatrixReference().Match(value);
        if (PlainLabel().Match(value) is { Success: true } plain)
        {
            labels.Add((file, runsOn + 1, plain.Groups["label"].Value));
        }
        else if (matrix.Success && MatrixList(lines, job, matrix.Groups["key"].Value) is { } list)
        {
            labels.AddRange(list.Labels.Select(label => (file, list.Line, label)));
        }
        else if (matrix.Success)
        {
            var key = matrix.Groups["key"].Value;
            problems.Add(
                $"{file} line {runsOn + 1}: runs-on names matrix.{key}, but tools/CheckToolchain.cs cannot read the "
                    + $"job's list \"{key}: [...]\". Write it on one line, below the job's own \"strategy:\" and "
                    + "\"matrix:\" keys, which hold only such lists and no \"include\".");
        }
        else
        {
            problems.Add(
                $"{file} line {runsOn + 1}: tools/CheckToolchain.cs cannot read this runs-on value. Name "
                    + "the runner with a plain label, or with ${{ matrix.<key> }} and a one-line list of "
                    + "the key.");
        }
    }

    // The entries of the job's list for a matrix key, with the list's line, or null. The list is a
    // direct child of the job's own "strategy:" and "matrix:" keys, and every line below
    // "matrix:" is such a list of plain entries, so that no include can add a runner.
    private static (int Line, List<string> Labels)? MatrixList(string[] lines, int job, string key)
    {
        var strategy = Child(lines, job, "strategy");
        var matrix = strategy < 0 ? -1 : Child(lines, strategy, "matrix");
        var block = matrix < 0 ? [] : Block(lines, matrix);
        var rows = block.Select(index => (Line: index, Match: OneLineList().Match(lines[index]))).ToList();
        var readable = rows.Count > 0 && rows.All(row =>
            row.Match.Success
                && Indent(lines[row.Line]) == Indent(lines[block[0]])
                && row.Match.Groups["key"].Value != "include"
                && Entries(row.Match).All(entry => PlainEntry().IsMatch(entry)));
        var found = readable ? rows.Where(row => row.Match.Groups["key"].Value == key).ToList() : [];
        return found.Count == 1 ? (found[0].Line + 1, Entries(found[0].Match)) : null;
    }

    private static List<string> Entries(Match list) =>
        list.Groups["items"].Value.Split(',').Select(entry => entry.Trim().Trim('"', '\'')).ToList();

    // The line of a key's only direct child that is a plain "<name>:" key with nothing after it,
    // or -1.
    private static int Child(string[] lines, int parent, string name)
    {
        var found = Children(lines, parent)
            .Where(child => BlockKey().Match(lines[child]) is { Success: true } key && key.Groups["key"].Value == name)
            .ToList();
        return found.Count == 1 ? found[0] : -1;
    }

    // The content lines below a key that are indented more than it.
    private static List<int> Block(string[] lines, int parent)
    {
        var block = new List<int>();
        for (var index = parent + 1; index < lines.Length; index++)
        {
            if (!IsContent(lines[index]))
            {
                continue;
            }

            if (Indent(lines[index]) <= Indent(lines[parent]))
            {
                break;
            }

            block.Add(index);
        }

        return block;
    }

    // The direct children of a key: the lines of its block that are indented like the first one.
    private static List<int> Children(string[] lines, int parent)
    {
        var block = Block(lines, parent);
        return block.Where(index => Indent(lines[index]) == Indent(lines[block[0]])).ToList();
    }

    private static bool IsContent(string line) => line.Trim().Length > 0 && !line.TrimStart().StartsWith('#');

    private static int Indent(string line) => line.Length - line.TrimStart(' ').Length;

    private static bool IsMacOS(string label) =>
        label.StartsWith("macos-", StringComparison.OrdinalIgnoreCase)
            || label.StartsWith("xcode-", StringComparison.OrdinalIgnoreCase);

    private static bool Same(string label, ToolPin? pin) =>
        string.Equals(label, pin?.Version, StringComparison.OrdinalIgnoreCase);

    // The patterns below accept spaces and tabs but never a carriage return, so a file with
    // Windows line endings is read only because Check removes them.

    // "jobs:", at the top level.
    [GeneratedRegex(@"^jobs:[ \t]*(#.*)?$")]
    private static partial Regex JobsKey();

    // "strategy:", with nothing after it but a comment.
    [GeneratedRegex(@"^[ \t]*(?<key>[A-Za-z0-9_-]+):[ \t]*(#.*)?$")]
    private static partial Regex BlockKey();

    // "runs-on:" spelled so, with the rest of its line.
    [GeneratedRegex(@"^[ \t]*runs-on:(?:[ \t]+(?<value>.*))?$")]
    private static partial Regex RunsOnKey();

    // runs-on as a key in any spelling: quoted, with a space before the colon, as a complex
    // key, or in a flow mapping.
    [GeneratedRegex(@"(^|[{,])[ \t]*(\?[ \t]+)?[""']?runs-on[""']?[ \t]*(:|#|$)")]
    private static partial Regex AnyRunsOn();

    // "uses: ./.github/workflows/build.yml", with or without quotes.
    [GeneratedRegex(@"^[ \t]*uses:[ \t]+(?<q>[""']?)(?<path>[^""'\s#]+)\k<q>[ \t]*(#.*)?$")]
    private static partial Regex UsesKey();

    // "xcode-27", with or without quotes, and with an optional comment.
    [GeneratedRegex(@"^(?<q>[""']?)(?<label>[A-Za-z0-9._-]+)\k<q>[ \t]*(#.*)?$")]
    private static partial Regex PlainLabel();

    // "${{ matrix.os }}", with or without quotes, and with an optional comment.
    [GeneratedRegex(@"^(?<q>[""']?)\$\{\{[ \t]*matrix\.(?<key>[A-Za-z0-9_-]+)[ \t]*\}\}\k<q>[ \t]*(#.*)?$")]
    private static partial Regex MatrixReference();

    // "os: [windows-2025, ubuntu-24.04]", with an optional comment.
    [GeneratedRegex(@"^[ \t]*(?<key>[A-Za-z0-9_-]+):[ \t]+\[(?<items>[^\[\]]*)\][ \t]*(#.*)?$")]
    private static partial Regex OneLineList();

    [GeneratedRegex(@"^[A-Za-z0-9._-]+$")]
    private static partial Regex PlainEntry();
}

internal static class ToolchainCheck
{
    public static int Run(HashSet<string> options)
    {
        var root = Processes.Run("git", ["rev-parse", "--show-toplevel"], Directory.GetCurrentDirectory());
        if (root is null || root.ExitCode != 0)
        {
            throw new InvalidDataException("run this tool inside the repository.");
        }

        // Commands run at the repository root, where global.json selects the SDK.
        var directory = root.Output.Trim();
        CommandRunner runner = (command, arguments) => Processes.Run(command, arguments, directory);

        Console.WriteLine("Toolchain pins (C-0009 section 2)");
        var failed = 0;
        var sdk = GlobalJson.Parse(Read(directory, GlobalJson.FilePath));
        failed += Report(
            $".NET SDK {sdk.Version} from {GlobalJson.FilePath}", Checks.Sdk(sdk, runner("dotnet", ["--version"])));

        // The pin file and the workflows are read on every run, so that a local run finds a
        // broken pin, a pin without a check and a macOS job on another image too.
        var pins = ToolchainJson.Parse(Read(directory, ToolchainJson.FilePath));
        foreach (var problem in Checks.Unchecked(pins))
        {
            failed += Report("a pin without a check", [problem]);
        }

        var image = pins.FirstOrDefault(pin => pin.Name == RunnerImage.Tool);
        failed += Report(
            $"{RunnerImage.Tool} {image?.Version ?? "(not pinned)"} from {ToolchainJson.FilePath}, "
                + $"against the macOS jobs in {RunnerImage.Folder}",
            RunnerImage.Check(image, ReadWorkflows(directory)));

        foreach (var option in Cli.HostOptions)
        {
            var checkedHere = pins.Where(pin => Probes.ByTool.GetValueOrDefault(pin.Name)?.Option == option).ToList();
            if (!options.Contains(option) && checkedHere.Count > 0)
            {
                Console.WriteLine(
                    $"  Not run: {string.Join(", ", checkedHere.Select(pin => pin.Name))}; {option} checks them.");
            }

            if (!options.Contains(option))
            {
                continue;
            }

            foreach (var problem in Checks.Missing(pins, option))
            {
                failed += Report($"a tool that {option} checks", [problem]);
            }

            foreach (var tool in checkedHere)
            {
                var label = $"{tool.Name} {tool.Version} from {ToolchainJson.FilePath}";
                failed += Report(label, Checks.Tool(tool, runner));
            }

            if (option == Probes.IosWorkload)
            {
                failed += Report("the ios workload, from that workload set", Checks.IosWorkload(runner));
            }

            if (option == Probes.Llvm && checkedHere.FirstOrDefault(pin => pin.Name == "llvm") is { } llvm)
            {
                failed += Report(
                    $"clang-tidy from llvm {llvm.Version}", Checks.Version(llvm, Probes.ClangTidy, runner));
            }
        }

        Console.WriteLine(failed == 0 ? "Everything checked matches its pin." : $"{failed} pins do not match.");
        return failed == 0 ? 0 : 1;
    }

    private static string Read(string directory, string file)
    {
        var path = Path.Combine(directory, file);
        return File.Exists(path) ? File.ReadAllText(path) : throw new InvalidDataException($"{file} does not exist.");
    }

    // GitHub reads workflows only from the top of this folder.
    private static List<(string File, string Text)> ReadWorkflows(string directory)
    {
        var folder = Path.Combine(directory, RunnerImage.Folder);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder)
                .Where(path => path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)
                .Select(path => ($"{RunnerImage.Folder}/{Path.GetFileName(path)}", File.ReadAllText(path)))
                .ToList()
            : [];
    }

    private static int Report(string pin, List<string> problems)
    {
        Console.WriteLine($"  {(problems.Count == 0 ? "ok  " : "FAIL")} {pin}");
        foreach (var problem in problems)
        {
            if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true")
            {
                Console.WriteLine($"::error title=Toolchain pins::{problem}");
            }

            Console.WriteLine($"       {problem}");
        }

        return problems.Count == 0 ? 0 : 1;
    }
}

internal static class Processes
{
    public static CommandResult? Run(string command, string[] arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(command)
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

        try
        {
            using var process = Process.Start(start);
            if (process is null)
            {
                return null;
            }

            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return new CommandResult(process.ExitCode, output, error.Result);
        }
        catch (Win32Exception)
        {
            return null;
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

// Every fixture is a synthetic version output or pin file (C-0011 section 1).
internal static class SelfTest
{
    private const string Pinned =
        """
        {
          "tools": {
            "reuse": { "version": "6.2.0", "source": "https://example.com/reuse" },
            "gitleaks": {
              "version": "8.30.1",
              "source": "https://example.com/gitleaks",
              "downloads": {
                "linux-x64": {
                  "url": "https://example.com/gitleaks.tar.gz",
                  "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
                }
              }
            },
            "lychee": { "version": "0.24.2", "source": "https://example.com/lychee" },
            "xcode": { "version": "27.1", "source": "https://example.com/xcode" },
            "ios-workload-set": { "version": "10.0.403", "source": "https://example.com/workloads" },
            "macos-runner-image": { "version": "xcode-27", "source": "https://example.com/images" },
            "cmake": {
              "version": "4.4.5",
              "source": "https://example.com/cmake",
              "downloads": {
                "osx-arm64": {
                  "url": "https://example.com/cmake.tar.gz",
                  "sha256": "1111111111111111111111111111111111111111111111111111111111111111"
                }
              }
            },
            "llvm": {
              "version": "23.1.5",
              "source": "https://example.com/llvm",
              "downloads": {
                "osx-arm64": {
                  "url": "https://example.com/llvm.tar.xz",
                  "sha256": "2222222222222222222222222222222222222222222222222222222222222222"
                }
              }
            },
            "ninja": { "version": "1.13.2", "source": "https://example.com/ninja" },
            "llvm-mingw": { "version": "20261104", "source": "https://example.com/llvm-mingw" },
            "meson": { "version": "1.12.1", "source": "https://example.com/meson" },
            "python": { "version": "3.14.10", "source": "https://example.com/python" },
            "bison": { "version": "3.8.2", "source": "https://example.com/bison" },
            "flex": { "version": "2.6.4", "source": "https://example.com/flex" }
          }
        }
        """;

    // As llvm-mingw's clang prints it, with a made-up commit and install folder.
    private const string LlvmMingwOutput =
        """
        clang version 23.1.5 (https://github.com/llvm/llvm-project.git 0123456789abcdef0123456789abcdef01234567)
        Target: x86_64-w64-windows-gnu
        Thread model: posix
        InstalledDir: /opt/tools/llvm-mingw-20261104/bin
        Configuration file: /opt/tools/llvm-mingw-20261104/bin/x86_64.cfg
        """;

    private const string BisonOutput =
        """
        bison (GNU Bison) 3.8.2
        Written by Robert Corbett and Richard Stallman.
        """;

    private const string CmakeOutput =
        """
        cmake version 4.4.5

        CMake suite maintained and supported by Kitware (kitware.com/cmake).
        """;

    private const string ClangTidyOutput =
        """
        LLVM (http://llvm.org/):
          LLVM version 23.1.5
          Optimized build.
        """;

    private const string ReuseOutput =
        """
        reuse, version 6.2.0

        This program is free software: you can redistribute it and/or modify it under the terms of the
        GNU General Public License as published by the Free Software Foundation, either version 3 of the
        License, or (at your option) any later version.
        """;

    private const string XcodeOutput =
        """
        Xcode 27.1
        Build version 27A9275
        """;

    private const string WorkloadList =
        """

        Installed Workload Id      Manifest Version       Installation Source
        --------------------------------------------------------------------
        ios                        27.1.10018/10.0.100    SDK 10.0.400

        Use `dotnet workload search` to find additional workloads to install.
        """;

    private const string Workflow =
        """
        jobs:
          dotnet:
            strategy:
              matrix:
                os: [windows-2025, ubuntu-24.04]
            runs-on: ${{ matrix.os }}
          checks:
            runs-on: ubuntu-24.04
          ios:
            runs-on: xcode-27 # the pinned image
        """;

    public static int Run()
    {
        Console.WriteLine("Self-test of tools/CheckToolchain.cs, with synthetic outputs only.");
        var tally = new Tally();

        Console.WriteLine("SDK:");
        var sdk = GlobalJson.Parse("""{ "sdk": { "version": "10.0.403", "rollForward": "disable" } }""");
        tally.Expect(sdk == new SdkPin("10.0.403", "disable"), "global.json yields the version and roll-forward");
        tally.Expect(Checks.Sdk(sdk, new CommandResult(0, "10.0.403\n", "")).Count == 0, "the pinned SDK passes");
        tally.Expect(Checks.Sdk(sdk, new CommandResult(0, "10.0.404\n", "")).Count == 1, "another SDK fails");
        tally.Expect(
            Checks.Sdk(sdk, new CommandResult(145, "", "A compatible .NET SDK was not found.")).Count == 1,
            "a missing SDK fails");
        tally.Expect(Checks.Sdk(sdk, null).Count == 1, "a missing dotnet fails");
        tally.Expect(
            Checks.Sdk(sdk with { RollForward = "latestFeature" }, new CommandResult(0, "10.0.403", "")).Count == 1,
            "a pin that may roll forward fails");
        tally.Expect(Rejects(() => GlobalJson.Parse("""{ "sdk": { } }""")), "global.json without a version fails");
        tally.Expect(Rejects(() => GlobalJson.Parse("[]")), "global.json that is no object fails");

        Console.WriteLine("Pin file:");
        var pins = ToolchainJson.Parse(Pinned);
        string[] expected =
        [
            "reuse 6.2.0", "gitleaks 8.30.1", "lychee 0.24.2", "xcode 27.1", "ios-workload-set 10.0.403",
            "macos-runner-image xcode-27", "cmake 4.4.5", "llvm 23.1.5", "ninja 1.13.2", "llvm-mingw 20261104",
            "meson 1.12.1", "python 3.14.10", "bison 3.8.2", "flex 2.6.4",
        ];
        tally.Expect(
            pins.Select(pin => $"{pin.Name} {pin.Version}").SequenceEqual(expected),
            "every tool and its version are read");
        tally.Expect(pins[1].Downloads.Single().Runtime == "linux-x64", "a download is read with its runtime");
        tally.Expect(Rejects(() => ToolchainJson.Parse(Edit(Pinned, "000\"", "00\""))), "a short SHA-256 is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "https://example.com/gitleaks.tar.gz", "http://x"))),
            "a download that is not https is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"version\": \"0.24.2\", ", ""))),
            "a tool without a version is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"6.2.0\",", "\"6.2.0\", \"x\": \"y\","))),
            "an unknown field is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"sha256\":", "\"sha512\": \"x\", \"sha256\":"))),
            "an unknown field in a download is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(Edit(Pinned, "\"tools\": {", "\"xcode\": {}, \"tools\": {"))),
            "a pin beside \"tools\" is rejected");
        tally.Expect(
            Rejects(() => ToolchainJson.Parse(
                """{ "tools": { "x": { "version": "1.0", "source": "https://example.com/", "downloads": [] } } }""")),
            "downloads that are no object are rejected");
        tally.Expect(Rejects(() => ToolchainJson.Parse("[]")), "a pin file that is no object is rejected");

        Console.WriteLine("Tool versions:");
        var outputs = new Dictionary<string, CommandResult>
        {
            ["reuse"] = new(0, ReuseOutput, ""),
            ["gitleaks"] = new(0, "8.30.1\n", ""),
            ["lychee"] = new(0, "lychee 0.24.2\n", ""),
        };
        CommandRunner installed = (command, _) => outputs.GetValueOrDefault(command);
        var ciTools = pins.Where(pin => Probes.ByTool.GetValueOrDefault(pin.Name)?.Option == Probes.CiTools).ToList();
        tally.Expect(
            ciTools.Count == 3 && ciTools.All(pin => Checks.Tool(pin, installed).Count == 0),
            "reuse, gitleaks and lychee match");
        tally.Expect(
            Checks.Tool(pins[1], (_, _) => new CommandResult(0, "v8.30.1", "")).Count == 0,
            "a gitleaks version with a leading v is read");
        tally.Expect(
            Checks.Tool(pins[2], (_, _) => new CommandResult(0, "lychee 0.24.1", "")).Count == 1,
            "another lychee version fails");
        tally.Expect(
            Checks.Tool(pins[0], (_, _) => new CommandResult(0, "usage: reuse [OPTIONS]", "")).Count == 1,
            "output without a version fails");
        tally.Expect(
            Checks.Tool(pins[0], (_, _) => new CommandResult(2, "", "unknown option")).Count == 1,
            "a version command that fails fails");
        tally.Expect(Checks.Tool(pins[1], (_, _) => null).Count == 1, "a tool that is not installed fails");
        tally.Expect(
            Checks.Tool(new ToolPin("unchecked-tool", "1.0.0", "https://example.com/", []), installed).Count == 1,
            "a pinned tool without a version check fails");
        tally.Expect(Checks.Unchecked(pins).Count == 0, "every pin of the fixture has a check");
        tally.Expect(
            Checks.Unchecked([new ToolPin("unchecked-tool", "1.0.0", "https://example.com/", [])]).Count == 1,
            "a pin without a check fails every run, whatever the options");

        Console.WriteLine("Xcode and the iOS workload set:");
        var xcode = pins.Single(pin => pin.Name == "xcode");
        var workloadSet = pins.Single(pin => pin.Name == "ios-workload-set");
        tally.Expect(
            Checks.Tool(xcode, Output(XcodeOutput)).Count == 0
                && Checks.Tool(workloadSet, Output("10.0.403\n")).Count == 0
                && Checks.IosWorkload(Output(WorkloadList)).Count == 0,
            "the pinned pair passes: Xcode 27.1 and workload set 10.0.403 with the ios workload");
        tally.Expect(
            Names(Checks.Tool(xcode, Output("Xcode 27.0\nBuild version 27A266a\n")), "xcode 27.0", "pins 27.1"),
            "another Xcode fails and names the found and the pinned version");
        tally.Expect(
            Names(Checks.Tool(xcode, Output("Xcode 27.1.1\nBuild version 27A9300\n")), "27.1.1", "pins 27.1"),
            "a later release of the pinned Xcode fails, because the pin is exact");
        var commandLineToolsOnly = new CommandResult(1, "", "xcode-select: error: tool 'xcodebuild' requires Xcode");
        tally.Expect(
            Checks.Tool(xcode, (_, _) => commandLineToolsOnly).Count == 1, "Command Line Tools without Xcode fail");
        tally.Expect(Checks.Tool(xcode, (_, _) => null).Count == 1, "a Mac without xcodebuild fails");
        tally.Expect(
            Names(Checks.Tool(workloadSet, Output("10.0.402.1\n")), "ios-workload-set 10.0.402.1", "pins 10.0.403"),
            "another workload set fails and names the found and the pinned version");
        tally.Expect(
            Checks.Tool(workloadSet, Output("10.0.400-manifests.4c6e1d8a\n")).Count == 1,
            "the SDK's own manifests without a workload set fail");
        tally.Expect(
            Checks.Tool(workloadSet, Output("10.0.403 (not installed)\n")).Count == 1,
            "a workload set that is named but not installed fails");
        tally.Expect(
            Checks.IosWorkload(Output(Edit(WorkloadList, "ios       ", "maui-ios  "))).Count == 1,
            "a list with maui-ios but not ios fails");
        tally.Expect(Checks.IosWorkload(Output("\nNo workloads are installed.\n")).Count == 1, "no workload fails");
        tally.Expect(
            Checks.IosWorkload((_, _) => new CommandResult(1, "", "error")).Count == 1,
            "a workload list that fails fails");

        Console.WriteLine("CMake and the LLVM release:");
        var cmake = pins.Single(pin => pin.Name == "cmake");
        var llvm = pins.Single(pin => pin.Name == "llvm");
        tally.Expect(
            cmake.Downloads.Single().Runtime == "osx-arm64" && llvm.Downloads.Single().Runtime == "osx-arm64",
            "the macOS downloads of CMake and LLVM are read");
        var clangFormatOutput =
            "clang-format version 23.1.5 (https://github.com/llvm/llvm-project 0123456789abcdef)\n";
        tally.Expect(
            Checks.Tool(cmake, Output(CmakeOutput)).Count == 0
                && Checks.Tool(llvm, Output(clangFormatOutput)).Count == 0
                && Checks.Version(llvm, Probes.ClangTidy, Output(ClangTidyOutput)).Count == 0,
            "the pinned CMake, clang-format and clang-tidy pass");
        tally.Expect(
            Names(Checks.Tool(cmake, Output("cmake version 4.4.4\n")), "cmake 4.4.4 is installed", "pins 4.4.5."),
            "another CMake fails and names the found and the pinned version");
        tally.Expect(
            Names(
                Checks.Tool(llvm, Output("clang-format version 22.1.8 (https://github.com/llvm/llvm-project x)\n")),
                "clang-format 22.1.8 is installed",
                "pins llvm 23.1.5."),
            "another clang-format fails and names the program, the pin and both versions");
        tally.Expect(
            Checks.Tool(llvm, Output("Homebrew clang-format version 23.1.5\n")).Count == 0
                && Checks.Version(
                    llvm, Probes.ClangTidy, Output("Homebrew LLVM version 23.1.5\n  Optimized build.\n")).Count == 0,
            "a packager's word in front of the version is read");
        tally.Expect(
            Names(
                Checks.Version(llvm, Probes.ClangTidy, Output(Edit(ClangTidyOutput, "23.1.5", "23.1.4"))),
                "clang-tidy 23.1.4 is installed",
                "pins llvm 23.1.5."),
            "a clang-tidy from another release fails, even when clang-format matches");
        tally.Expect(
            Checks.Version(
                llvm, Probes.ClangTidy, (_, _) => new CommandResult(134, "", "dyld: Library not loaded")).Count == 1
                && Names(Checks.Version(llvm, Probes.ClangTidy, (_, _) => null), "clang-tidy, from the llvm pin"),
            "a clang-tidy that cannot start, or is missing, fails");
        tally.Expect(
            Checks.Tool(cmake, Output("cmake3 version 3.28.3\n")).Count == 1,
            "output without the version line fails");

        Console.WriteLine("Build tools of the forks:");
        var forkOutputs = new Dictionary<string, CommandResult>
        {
            ["ninja"] = new(0, "1.13.2\n", ""),
            ["x86_64-w64-mingw32-clang"] = new(0, LlvmMingwOutput, ""),
            ["meson"] = new(0, "1.12.1\n", ""),
            ["python3"] = new(0, "Python 3.14.10\n", ""),
            ["bison"] = new(0, BisonOutput, ""),
            ["flex"] = new(0, "flex 2.6.4 Apple(flex-35)\n", ""),
        };
        CommandRunner forkInstalled = (command, _) => forkOutputs.GetValueOrDefault(command);
        var forkTools = pins
            .Where(pin => Probes.ByTool.GetValueOrDefault(pin.Name)?.Option == Probes.ForkTools)
            .ToList();
        tally.Expect(
            forkTools.Count == 6 && forkTools.All(pin => Checks.Tool(pin, forkInstalled).Count == 0),
            "ninja, llvm-mingw, meson, python, bison and flex match");
        ToolPin pinned(string name) => pins.Single(pin => pin.Name == name);
        tally.Expect(
            Names(
                Checks.Tool(pinned("llvm-mingw"), Output(Edit(LlvmMingwOutput, "20261104", "20261020"))),
                "llvm-mingw 20261020",
                "pins 20261104"),
            "another llvm-mingw release fails and names both releases");
        tally.Expect(
            Checks.Tool(
                    pinned("llvm-mingw"),
                    Output(Edit(LlvmMingwOutput, "llvm-mingw-20261104/", "llvm-mingw-20261104-ucrt-macos-universal/")))
                .Count == 0,
            "the folder name of the release archive, with its variant, is read too");
        tally.Expect(
            Names(
                Checks.Tool(
                    pinned("llvm-mingw"),
                    Output("clang version 23.1.5\nTarget: x86_64-w64-windows-gnu\nInstalledDir: /opt/homebrew/bin\n")),
                "printed no version"),
            "a cross compiler outside an llvm-mingw release folder fails");
        tally.Expect(
            Names(
                Checks.Tool(pinned("bison"), Output("bison (GNU Bison) 2.3\nWritten by Robert Corbett.\n")),
                "bison 2.3",
                "pins 3.8.2"),
            "the bison that macOS ships fails and names both versions");
        tally.Expect(
            Names(Checks.Tool(pinned("python"), Output("Python 3.15.2\n")), "python 3.15.2", "pins 3.14.10"),
            "another Python, such as the one earlier on the PATH, fails");
        tally.Expect(
            Checks.Tool(pinned("flex"), Output("flex 2.6.4\n")).Count == 0
                && Checks.Tool(pinned("ninja"), Output("1.13.2")).Count == 0,
            "flex without Apple's tag and a version without a line break are read");
        tally.Expect(
            Names(Checks.Tool(pinned("meson"), (_, _) => null), "meson is not installed"),
            "a tool that is not on the PATH fails and is named");

        Console.WriteLine("Runner image:");
        var image = pins.Single(pin => pin.Name == RunnerImage.Tool);
        List<string> withJob(string job) => RunnerImage.Check(image, [("ci.yml", $"{Workflow}\n  extra:\n{job}")]);
        List<string> edited(string from, string to) => RunnerImage.Check(image, [("ci.yml", Edit(Workflow, from, to))]);
        tally.Expect(RunnerImage.Check(image, [("ci.yml", Workflow)]).Count == 0, "the pinned macOS image passes");
        tally.Expect(
            Names(
                RunnerImage.Check(image, [("ci.yml", Edit(Workflow, "xcode-27 #", "macos-26 #"))]),
                "ci.yml line 10 runs on macos-26",
                "pins xcode-27"),
            "a job on another macOS image fails, with its file, line and both images");
        tally.Expect(
            RunnerImage.Check(image, [("ci.yml", Edit(Workflow, "xcode-27 #", "\"xcode-27\" #"))]).Count == 0,
            "a quoted runs-on value is read");
        tally.Expect(
            RunnerImage.Check(image, [("ci.yml", Edit(Workflow, "xcode-27 #", "ubuntu-24.04 #"))]).Count == 1,
            "a pinned image that no job runs on fails");
        tally.Expect(
            RunnerImage.Check(null, [("ci.yml", Workflow)]).Count == 1,
            "a macOS job without a pinned image fails");
        tally.Expect(
            RunnerImage.Check(image with { Version = "ubuntu-24.04" }, [("ci.yml", Workflow)]).Count == 2,
            "a pin that is no macOS image fails, and so does the macOS job");
        tally.Expect(
            RunnerImage.Check(image, [("ci.yml", Workflow), ("other.yml", "jobs:\n  x:\n    runs-on: macos-latest\n")])
                .Count == 1,
            "every workflow is read");
        tally.Expect(
            Names(withJob("    runs-on: [macos-26]"), "line 12", "cannot read"),
            "a runs-on list in brackets fails");
        tally.Expect(
            Names(withJob("    runs-on:\n      - macos-26"), "line 12", "cannot read"),
            "a runs-on list in block form fails");
        tally.Expect(
            Names(withJob("    runs-on:\n      group: big\n      labels: macos-26"), "line 12", "cannot read"),
            "runs-on with a group and labels fails");
        tally.Expect(
            Names(withJob("    runs-on: { labels: [macos-26] }"), "cannot read"),
            "runs-on as a mapping fails");
        tally.Expect(
            Names(withJob("    runs-on: ${{ 'macos-26' }}"), "cannot read"),
            "runs-on as another expression fails");
        tally.Expect(
            Names(withJob("    runs-on: macOS-26"), "runs on macOS-26")
                && Names(withJob("    runs-on: Xcode-26"), "runs on Xcode-26"),
            "macOS images are found in any letter case");
        tally.Expect(
            edited("runs-on: xcode-27", "runs-on: Xcode-27").Count == 0,
            "the pinned image in another letter case passes, as GitHub compares labels so");
        tally.Expect(
            Names(edited("ubuntu-24.04]", "ubuntu-24.04, macos-26]"), "ci.yml line 5 runs on macos-26"),
            "a macOS image in the matrix list of runs-on fails, with the list's line");
        tally.Expect(
            Names(edited("[windows-2025, ubuntu-24.04]", "[\"windows-2025\", 'macos-26']"), "runs on macos-26"),
            "quoted entries of a matrix list are read");
        tally.Expect(
            Names(withJob("    runs-on: ${{ matrix.platform }}"), "matrix.platform"),
            "a matrix key without a list in its job fails");
        tally.Expect(
            Names(
                withJob(
                    "    strategy:\n      matrix:\n        os: [ubuntu-24.04]\n"
                        + "        include:\n          - os: macos-26\n    runs-on: ${{ matrix.os }}"),
                "matrix.os"),
            "a matrix key that the job sets twice, as include does, fails");

        Console.WriteLine("Matrix lists:");
        List<string> withMatrix(string rows) =>
            withJob($"    strategy:\n      matrix:\n{rows}\n    runs-on: ${{{{ matrix.os }}}}");
        tally.Expect(
            withMatrix("        os: [ubuntu-24.04]").Count == 0
                && withJob(
                    "    strategy:\n      fail-fast: false\n      matrix:\n        # one runner\n"
                        + "        os: [ubuntu-24.04] # Linux\n    runs-on: ${{ matrix.os }}").Count == 0,
            "a one-line list below the job's own strategy and matrix keys is read, with comments");
        tally.Expect(
            Names(withMatrix("        os: [ubuntu-24.04]\n        include: [{ os: macos-26 }]"), "matrix.os"),
            "an include on one line fails");
        tally.Expect(
            Names(withMatrix("        os: [ubuntu-24.04]\n        include:\n          - { os: macos-26 }"), "matrix.os")
                && Names(
                    withMatrix("        os: [ubuntu-24.04]\n        include:\n          - { name: mac, os: macos-26 }"),
                    "matrix.os"),
            "an include of flow mappings fails");
        tally.Expect(
            Names(withMatrix("        os: [ubuntu-24.04]\n        include: ${{ fromJSON(inputs.more) }}"), "matrix.os"),
            "an include from an expression fails");
        tally.Expect(
            Names(withMatrix("        os: [ubuntu-24.04]\n        include:\n          - \"os\": macos-26"), "matrix.os")
                && Names(
                    withMatrix("        os: [ubuntu-24.04]\n        include:\n          - os : macos-26"), "matrix.os"),
            "an include with the key quoted or before a space fails");
        tally.Expect(
            Names(withMatrix("        sets:\n          os: [macos-26]"), "matrix.os"),
            "a list deeper than the matrix key fails");
        tally.Expect(
            Names(
                withJob(
                    "    strategy:\n      matrix: { os: [macos-26] }\n    runs-on: ${{ matrix.os }}\n"
                        + "    steps:\n      - run: |\n          os: [ubuntu-24.04]"),
                "matrix.os")
                && Names(
                    withJob(
                        "    strategy:\n      matrix: ${{ fromJSON(inputs.matrix) }}\n    runs-on: ${{ matrix.os }}\n"
                            + "    steps:\n      - run: |\n          os: [ubuntu-24.04]"),
                    "matrix.os"),
            "a matrix in flow form or from an expression fails, whatever a script line says");

        Console.WriteLine("Keys and jobs:");
        List<string> withOther(string text) => RunnerImage.Check(image, [("ci.yml", Workflow), ("other.yml", text)]);
        tally.Expect(
            Names(withJob("    \"runs-on\": macos-26"), "line 12", "cannot read")
                && Names(withJob("    'runs-on': macos-26"), "line 12", "cannot read")
                && Names(withJob("    runs-on : macos-26"), "line 12", "cannot read"),
            "runs-on quoted or before a space fails, with its line");
        tally.Expect(
            Names(withJob("    ? runs-on\n    : macos-26"), "line 12", "cannot read"),
            "runs-on as a complex key fails");
        tally.Expect(
            Names(
                RunnerImage.Check(
                    image, [("ci.yml", $"{Workflow}\n  extra: {{ runs-on: macos-26, steps: [ {{ run: echo }} ] }}")]),
                "line 11",
                "cannot read"),
            "a job written as a flow mapping fails");
        tally.Expect(
            Names(withJob("    <<: *mac\n    steps:\n      - run: echo"), "line 11", "cannot read"),
            "a job without a runs-on key that the check can read fails");
        tally.Expect(
            Names(
                withJob("    uses: an-org/a-repo/.github/workflows/build.yml@v1"), "line 12", "outside this repository")
                && withJob("    uses: ./.github/workflows/build.yml").Count == 0,
            "a job that calls a workflow of another repository fails, and one of this repository passes");
        tally.Expect(
            RunnerImage.Check(
                image,
                [("ci.yml", Edit(Workflow, "xcode-27 #", "ubuntu-24.04 #")
                    + "\n  extra:\n    runs-on: ubuntu-24.04\n    steps:\n"
                    + "      - run: |\n          runs-on: xcode-27")])
                .Count == 1,
            "a script line is never read as a runner");
        tally.Expect(
            Names(withOther("\"jobs\":\n  x:\n    runs-on: macos-26"), "other.yml", "cannot read its jobs")
                && Names(withOther("jobs: { x: { runs-on: macos-26 } }"), "other.yml", "cannot read its jobs"),
            "jobs in another spelling fail");
        static string crlf(string text) => text.Replace("\n", "\r\n", StringComparison.Ordinal);
        tally.Expect(
            RunnerImage.Check(image, [("ci.yml", crlf(Workflow))]).Count == 0
                && Names(
                    RunnerImage.Check(image, [("ci.yml", crlf($"{Workflow}\n  extra:\n    runs-on: [macos-26]"))]),
                    "line 12",
                    "cannot read"),
            "Windows line endings are read like Unix ones");

        Console.WriteLine("Missing pins:");
        tally.Expect(
            Checks.Missing(pins, Probes.Xcode).Count == 0 && Checks.Missing(pins, Probes.IosWorkload).Count == 0,
            "every tool of --xcode and --ios-workload is pinned in the fixture");
        var withoutXcode = ToolchainJson.Parse(
            Edit(Pinned, "\"xcode\": { \"version\": \"27.1\", \"source\": \"https://example.com/xcode\" },", ""));
        tally.Expect(
            Names(Checks.Missing(withoutXcode, Probes.Xcode), "pins no xcode", "--xcode"),
            "--xcode without an Xcode pin fails instead of comparing nothing");
        var withoutWorkloadSet = ToolchainJson.Parse(Edit(
            Pinned,
            "\"ios-workload-set\": { \"version\": \"10.0.403\", \"source\": \"https://example.com/workloads\" },",
            ""));
        tally.Expect(
            Names(Checks.Missing(withoutWorkloadSet, Probes.IosWorkload), "pins no ios-workload-set", "--ios-workload"),
            "--ios-workload without a workload set pin fails");
        var withoutNative = pins.Where(pin => pin.Name is not ("cmake" or "llvm")).ToList();
        tally.Expect(
            Names(Checks.Missing(withoutNative, Probes.Cmake), "pins no cmake", "--cmake")
                && Names(Checks.Missing(withoutNative, Probes.Llvm), "pins no llvm", "--llvm")
                && Checks.Missing(pins, Probes.Cmake).Count == 0
                && Checks.Missing(pins, Probes.Llvm).Count == 0,
            "--cmake and --llvm without their pins fail");
        var withoutMeson = ToolchainJson.Parse(
            Edit(Pinned, "\"meson\": { \"version\": \"1.12.1\", \"source\": \"https://example.com/meson\" },", ""));
        tally.Expect(
            Checks.Missing(pins, Probes.ForkTools).Count == 0
                && Names(Checks.Missing(withoutMeson, Probes.ForkTools), "pins no meson", "--fork-tools"),
            "--fork-tools without a meson pin fails");

        Console.WriteLine("Options:");
        tally.Expect(Cli.Options([]) is { Count: 0 }, "no option checks the SDK and the runner image only");
        tally.Expect(
            Cli.Options(["--xcode", "--ios-workload"]) is { Count: 2 },
            "--xcode and --ios-workload combine");
        tally.Expect(
            Cli.Options(["--xcode", "--cmake", "--llvm"]) is { Count: 3 }
                && Cli.Options(["--xcode", "--ios-workload", "--cmake"]) is { Count: 3 },
            "--cmake and --llvm combine with the others, as the bootstrap gives them");
        tally.Expect(
            Cli.Options(["--xcode", "--ios-workload", "--cmake", "--fork-tools"]) is { Count: 4 },
            "--fork-tools combines with the others, as the bootstrap gives them");
        tally.Expect(Cli.Options(["--xcode", "--xcode"]) is null, "an option given twice is rejected");
        tally.Expect(Cli.Options(["--self-test", "--xcode"]) is null, "--self-test combines with nothing");
        tally.Expect(Cli.Options(["--macos"]) is null, "an unknown option is rejected");

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
    }

    private static string Edit(string text, string from, string to) => text.Replace(from, to, StringComparison.Ordinal);

    private static CommandRunner Output(string output) => (_, _) => new CommandResult(0, output, "");

    // Whether a problem names every part, such as the found and the pinned version.
    private static bool Names(List<string> problems, params string[] parts) =>
        problems.Any(problem => parts.All(part => problem.Contains(part, StringComparison.Ordinal)));

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
