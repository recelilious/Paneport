#!/usr/bin/env dotnet
// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Checks the version of a release and composes the body of its draft release (S9.1.6 and S9.1.4; C-0009
// section 6). Versions follow Semantic Versioning from 0.1.0. A tag v<MAJOR>.<MINOR>.<PATCH> on main releases
// that version, and a tag with a SemVer pre-release part, such as v0.0.0-dryrun.1, makes a dry run of the
// release workflow, whose draft is never published; build metadata is refused. CFBundleShortVersionString
// carries MAJOR.MINOR.PATCH, and CFBundleVersion, the build number, is the number of commits in the history of
// the tagged commit, so that it grows with every published build.
//
//   dotnet run tools/ReleaseVersion.cs -- --tag <tag> [--main <ref>] [--require-dry-run] [--output <file>]
//       [--repository <path>]
//   dotnet run tools/ReleaseVersion.cs -- --check-app <bundle.app> --version <x.y.z> --build <n>
//   dotnet run tools/ReleaseVersion.cs -- --compose --tag <tag> --mode release|dry-run --version <x.y.z>
//       --build <n> --commit <sha> --notes <file> --sources <sources.json> --checksums <file> --out <file>
//       [--evidence <file>]
//   dotnet run tools/ReleaseVersion.cs -- --self-test
//
// --tag checks a tag in the repository, by default that of the current folder, which must hold the whole
// history. The tag must exist; a release tag must be on main (--main, by default refs/remotes/origin/main) with
// docs/releases/<tag>.md in its commit, and a dry-run tag needs docs/releases/release-notes-template.md there.
// It prints mode=, version=, build=, commit= and notes= on standard output, and with --output appends them to
// that file, such as $GITHUB_OUTPUT. --require-dry-run, which a manual run of the workflow passes, refuses a
// release tag. --check-app checks the two keys in the app's Info.plist, binary or XML, which
// tools/Shared/PropertyList.cs reads. --compose replaces the markers <!-- release: build -->,
// <!-- release: sources --> and <!-- release: checksums --> of the notes with the build, a table of the sources
// that tools/ArchiveSources.cs archived and the checksums, and points the relative links of the notes at the
// tagged files. In release mode it fails on a gate in scope that docs/evidence/README.md does not give as passed
// or waived, on an empty list of known issues, on a <!-- fill: ... --> placeholder and on a repository or fork
// that is not public; a dry run only warns on them and opens the body with a banner. Findings name the tag,
// file, key or gate, on standard error, and under GitHub Actions each is also an annotation.
// Exit codes: 0 the checks pass, 1 a check fails, 2 usage or an input that cannot be read.

#:include Shared/PropertyList.cs
#:property PublishAot=false

using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var actions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
return args is ["--self-test"]
    ? SelfTest.Run()
    : Cli.Run(args, new Io(Console.Out, Console.Error, actions, new Git(null), Directory.GetCurrentDirectory()));

internal enum Mode
{
    // A tag v<MAJOR>.<MINOR>.<PATCH>: the release of that version.
    Release,

    // A tag with a pre-release part: a dry run of the release workflow, whose draft is never published.
    DryRun,
}

internal static class Modes
{
    public static string Name(Mode mode) => mode == Mode.Release ? "release" : "dry-run";

    public static Mode? Parse(string name) => name switch
    {
        "release" => Mode.Release,
        "dry-run" => Mode.DryRun,
        _ => null,
    };
}

// What a run writes to and works with: its results on Out, its findings on Error, annotations under GitHub
// Actions, the git that it runs, and the folder against which relative paths resolve.
internal sealed record Io(TextWriter Out, TextWriter Error, bool Actions, Git Git, string Folder)
{
    public string Resolve(string path) => Path.GetFullPath(Path.Combine(Folder, path));
}

// The failures and warnings of a run, printed when it ends: plain lines first, which under GitHub Actions stand
// between ::stop-commands:: and its token, so that no value in them runs a workflow command, then annotations.
internal sealed class Report(Io io)
{
    private readonly List<(bool Failure, string Message)> _findings = [];

    public int Failures { get; private set; }

    public int Warnings { get; private set; }

    public void Fail(string message)
    {
        _findings.Add((true, message));
        Failures++;
    }

    public void Warn(string message)
    {
        _findings.Add((false, message));
        Warnings++;
    }

    // A rule of a release: it fails in release mode and warns in a dry run.
    public void ForRelease(Mode mode, string message)
    {
        if (mode == Mode.Release)
        {
            Fail(message);
        }
        else
        {
            Warn(message);
        }
    }

    // Prints the findings and returns the exit code: 1 after a failure, 0 otherwise.
    public int Finish()
    {
        Print();
        return Failures == 0 ? 0 : 1;
    }

    public void Print()
    {
        if (_findings.Count == 0)
        {
            return;
        }

        var token = io.Actions ? Guid.NewGuid().ToString("N") : null;
        if (token is not null)
        {
            io.Error.WriteLine($"::stop-commands::{token}");
        }

        foreach (var (failure, message) in _findings)
        {
            io.Error.WriteLine($"{(failure ? "error" : "warning")}: {Plain(message)}");
        }

        if (token is not null)
        {
            io.Error.WriteLine($"::{token}::");
            foreach (var (failure, message) in _findings)
            {
                io.Error.WriteLine($"::{(failure ? "error" : "warning")} title=Release version::{Data(message)}");
            }
        }

        _findings.Clear();
    }

    // A line of the report, with CR and LF written as %0D and %0A.
    public static string Plain(string text) =>
        text.Replace("\r", "%0D", StringComparison.Ordinal).Replace("\n", "%0A", StringComparison.Ordinal);

    // The data of an annotation, escaped as GitHub Actions asks: %, CR and LF as %25, %0D and %0A.
    public static string Data(string text) => Plain(text.Replace("%", "%25", StringComparison.Ordinal));
}

internal static class Cli
{
    private static readonly string[] s_usage =
    [
        "usage: dotnet run tools/ReleaseVersion.cs -- --tag <tag> [--main <ref>] [--require-dry-run]",
        "           [--output <file>] [--repository <path>]",
        "       dotnet run tools/ReleaseVersion.cs -- --check-app <bundle.app> --version <x.y.z> --build <n>",
        "       dotnet run tools/ReleaseVersion.cs -- --compose --tag <tag> --mode release|dry-run",
        "           --version <x.y.z> --build <n> --commit <sha> --notes <file> --sources <sources.json>",
        "           --checksums <file> --out <file> [--evidence <file>]",
        "       dotnet run tools/ReleaseVersion.cs -- --self-test",
    ];

    private static readonly HashSet<string> s_flags = new(StringComparer.Ordinal) { "--compose", "--require-dry-run" };

    private static readonly HashSet<string> s_values = new(StringComparer.Ordinal)
    {
        "--tag", "--main", "--output", "--repository", "--check-app", "--version", "--build", "--mode", "--commit",
        "--notes", "--sources", "--checksums", "--out", "--evidence",
    };

    public static int Run(IReadOnlyList<string> args, Io io)
    {
        var report = new Report(io);
        try
        {
            var options = Parse(args);
            if (options.ContainsKey("--compose"))
            {
                return Composer.Run(Compose(options), io, report);
            }

            if (options.TryGetValue("--check-app", out var bundle))
            {
                Allow(options, "--check-app", "--version", "--build");
                var version = Required(options, "--check-app", "--version");
                var build = Required(options, "--check-app", "--build");
                if (!Semver.IsCore(version))
                {
                    throw new ArgumentException($"--version \"{version}\": not MAJOR.MINOR.PATCH, such as 0.1.0");
                }

                return Semver.IsBuild(build)
                    ? AppCheck.Run(bundle, version, build, io, report)
                    : throw new ArgumentException($"--build \"{build}\": not a positive integer without leading zeros");
            }

            if (options.TryGetValue("--tag", out var tag))
            {
                Allow(options, "--tag", "--main", "--require-dry-run", "--output", "--repository");
                var main = options.GetValueOrDefault("--main");
                return main is not null && (main.Length == 0 || main.StartsWith('-'))
                    ? throw new ArgumentException($"--main \"{main}\": not a ref, such as refs/remotes/origin/main")
                    : TagCheck.Run(
                        new TagOptions(
                            tag,
                            main ?? TagCheck.DefaultMain,
                            options.ContainsKey("--require-dry-run"),
                            options.GetValueOrDefault("--output"),
                            options.GetValueOrDefault("--repository")),
                        io,
                        report);
            }

            throw new ArgumentException("give --tag, --check-app, --compose or --self-test");
        }
        catch (ArgumentException exception)
        {
            report.Fail(exception.Message);
            report.Print();
            foreach (var line in s_usage)
            {
                io.Error.WriteLine(line);
            }

            return 2;
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            report.Fail(exception.Message);
            report.Print();
            return 2;
        }
    }

    // Options by name; a flag has an empty value. An unknown option, a missing value and an option given twice are
    // usage errors.
    private static Dictionary<string, string> Parse(IReadOnlyList<string> args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index++)
        {
            var name = args[index];
            var value = string.Empty;
            if (s_values.Contains(name))
            {
                if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new ArgumentException($"{name} needs a value");
                }

                value = args[++index];
            }
            else if (!s_flags.Contains(name))
            {
                throw new ArgumentException($"unknown argument \"{name}\"");
            }

            if (!options.TryAdd(name, value))
            {
                throw new ArgumentException($"{name} is given twice");
            }
        }

        return options;
    }

    private static void Allow(Dictionary<string, string> options, string command, params string[] allowed)
    {
        foreach (var name in options.Keys)
        {
            if (name != command && Array.IndexOf(allowed, name) < 0)
            {
                throw new ArgumentException($"{command} does not take {name}");
            }
        }
    }

    private static string Required(Dictionary<string, string> options, string command, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"{command} needs {name}");

    // The arguments of --compose, checked against each other: the tag gives the mode and the version.
    private static ComposeOptions Compose(Dictionary<string, string> options)
    {
        const string Command = "--compose";
        Allow(
            options,
            Command,
            "--tag",
            "--mode",
            "--version",
            "--build",
            "--commit",
            "--notes",
            "--sources",
            "--checksums",
            "--out",
            "--evidence");
        var name = Required(options, Command, "--tag");
        var tag = ReleaseTag.Parse(name) ?? throw new ArgumentException($"--tag \"{name}\": {ReleaseTag.Grammar}");
        var mode = Required(options, Command, "--mode");
        if (Modes.Parse(mode) != tag.Mode)
        {
            throw new ArgumentException($"--mode \"{mode}\": tag \"{name}\" makes a {Modes.Name(tag.Mode)}");
        }

        var version = Required(options, Command, "--version");
        if (version != tag.Version)
        {
            throw new ArgumentException($"--version \"{version}\": tag \"{name}\" gives the version {tag.Version}");
        }

        var build = Required(options, Command, "--build");
        if (!Semver.IsBuild(build))
        {
            throw new ArgumentException($"--build \"{build}\": not a positive integer without leading zeros");
        }

        var commit = Required(options, Command, "--commit");
        if (!Patterns.Commit().IsMatch(commit))
        {
            throw new ArgumentException($"--commit \"{commit}\": not 40 lowercase hexadecimal digits");
        }

        // The notes are named from the repository root, which the relative links of the notes resolve against.
        var notes = Required(options, Command, "--notes");
        var inRepository = Path.IsPathRooted(notes) ? null : Paths.Resolve(string.Empty, notes.Replace('\\', '/'));
        if (inRepository is null or "")
        {
            throw new ArgumentException(
                $"--notes \"{notes}\": not a path from the repository root, such as docs/releases/v0.1.0.md");
        }

        return new ComposeOptions(
            tag,
            build,
            commit,
            notes,
            inRepository,
            Required(options, Command, "--sources"),
            Required(options, Command, "--checksums"),
            Required(options, Command, "--out"),
            options.GetValueOrDefault("--evidence") ?? "docs/evidence/README.md");
    }
}

internal sealed record TagOptions(string Tag, string Main, bool RequireDryRun, string? Output, string? Repository);

internal sealed record ComposeOptions(
    ReleaseTag Tag,
    string Build,
    string Commit,
    string Notes,
    string NotesInRepository,
    string Sources,
    string Checksums,
    string Out,
    string Evidence);

// The numbers and identifiers of Semantic Versioning 2.0.0.
internal static class Semver
{
    // MAJOR.MINOR.PATCH: three numbers without leading zeros.
    public static bool IsCore(string text)
    {
        var parts = text.Split('.');
        return parts.Length == 3 && parts.All(IsNumber);
    }

    // A pre-release part: identifiers of [0-9A-Za-z-] between dots, none empty, a numeric one without leading
    // zeros.
    public static bool IsPreRelease(string text) => text.Split('.').All(IsIdentifier);

    // A build number: a positive integer without leading zeros.
    public static bool IsBuild(string text) => IsNumber(text) && text != "0";

    private static bool IsNumber(string text) =>
        text.Length > 0 && text.All(char.IsAsciiDigit) && (text.Length == 1 || text[0] != '0');

    private static bool IsIdentifier(string text) =>
        text.Length > 0
        && text.All(character => char.IsAsciiLetterOrDigit(character) || character == '-')
        && (!text.All(char.IsAsciiDigit) || IsNumber(text));
}

// A tag of the release workflow: v<MAJOR>.<MINOR>.<PATCH> releases that version, and the same with a pre-release
// part makes a dry run.
internal sealed record ReleaseTag(string Name, string Version, Mode Mode)
{
    public const string Grammar =
        "a release tag is v<MAJOR>.<MINOR>.<PATCH>, three numbers without leading zeros such as v0.1.0, and a "
        + "dry-run tag adds a SemVer pre-release part, such as v0.0.0-dryrun.1; build metadata (+...) is refused";

    public static ReleaseTag? Parse(string name)
    {
        if (!name.StartsWith('v'))
        {
            return null;
        }

        var dash = name.IndexOf('-');
        var core = dash < 0 ? name[1..] : name[1..dash];
        var preRelease = dash < 0 ? null : name[(dash + 1)..];
        return Semver.IsCore(core) && (preRelease is null || Semver.IsPreRelease(preRelease))
            ? new ReleaseTag(name, core, preRelease is null ? Mode.Release : Mode.DryRun)
            : null;
    }
}

internal static partial class Patterns
{
    // A full commit hash of a SHA-1 repository.
    [GeneratedRegex(@"\A[0-9a-f]{40}\z", RegexOptions.CultureInvariant)]
    public static partial Regex Commit();

    // The start of a release marker, however it is spaced or cased: <!-- release: ... -->.
    [GeneratedRegex(@"<!--\s*release\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex Marker();

    // The start of a placeholder that the notes must fill in: <!-- fill: ... -->.
    [GeneratedRegex(@"<!--\s*fill\s*:", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    public static partial Regex Fill();

    // An item of a Markdown list: -, * or a number and a dot, then its text.
    [GeneratedRegex(@"\A {0,3}(?:[-*]|[0-9]{1,9}\.)[ \t]+\S", RegexOptions.CultureInvariant)]
    public static partial Regex ListItem();

    // A cell of the delimiter row of a table.
    [GeneratedRegex(@"\A:?-+:?\z", RegexOptions.CultureInvariant)]
    public static partial Regex Delimiter();

    // A Markdown inline link, [text](target) or [text](target "title"), that is not an image.
    [GeneratedRegex(@"(?<!!)\[[^\[\]]*\]\((?<target>[^\s()<>]*)(?:\s+""[^""]*"")?\)", RegexOptions.CultureInvariant)]
    public static partial Regex Link();

    // A table cell that is one link: [text](target).
    [GeneratedRegex(@"\A\[[^\[\]]+\]\((?<target>[^\s()<>]+)\)\z", RegexOptions.CultureInvariant)]
    public static partial Regex CellLink();

    // A URL scheme, such as https: or mailto:.
    [GeneratedRegex(@"\A[A-Za-z][A-Za-z0-9+.-]*:", RegexOptions.CultureInvariant)]
    public static partial Regex Scheme();

    // The line that opens or closes a fenced code block.
    [GeneratedRegex(@"\A {0,3}(?<fence>`{3,}|~{3,})(?<info>.*)\z", RegexOptions.CultureInvariant)]
    public static partial Regex Fence();

    // A line of a checksum file as shasum -a 256 writes it: the hash, two spaces and a file name without a slash.
    [GeneratedRegex(@"\A(?<hash>[0-9a-f]{64})  (?<name>[^/\\`\x00-\x1F\x7F]+)\z", RegexOptions.CultureInvariant)]
    public static partial Regex Checksum();

    // The forms of sources.json: an https URL, a path in a repository, a file name, a name, a tag and a text.
    [GeneratedRegex(
        @"\Ahttps://[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+(?:/[A-Za-z0-9._~-]+)+\z",
        RegexOptions.CultureInvariant)]
    public static partial Regex Url();

    [GeneratedRegex(
        @"\A(?!\.\.?(?:/|\z))[A-Za-z0-9._+-]+(?:/(?!\.\.?(?:/|\z))[A-Za-z0-9._+-]+)*\z",
        RegexOptions.CultureInvariant)]
    public static partial Regex PathInRepository();

    [GeneratedRegex(@"\A[A-Za-z0-9][A-Za-z0-9._+-]*\z", RegexOptions.CultureInvariant)]
    public static partial Regex FileName();

    [GeneratedRegex(@"\A[A-Za-z0-9._+-]+(?: [A-Za-z0-9._+-]+)*\z", RegexOptions.CultureInvariant)]
    public static partial Regex Name();

    [GeneratedRegex(@"\A[A-Za-z0-9._+-]+(?:/[A-Za-z0-9._+-]+)*\z", RegexOptions.CultureInvariant)]
    public static partial Regex Tag();

    [GeneratedRegex(@"\A[^\x00-\x1F\x7F]+\z", RegexOptions.CultureInvariant)]
    public static partial Regex Text();
}

// Paths in a repository: relative, with / between their segments.
internal static class Paths
{
    // Resolves a relative path against a folder of the repository and returns the path from the repository root,
    // without . and .. segments, or null when it leaves the repository.
    public static string? Resolve(string folder, string relative)
    {
        var parts = new List<string>();
        foreach (var segment in $"{folder}/{relative}".Split('/'))
        {
            switch (segment)
            {
                case "" or ".":
                    break;
                case "..":
                    if (parts.Count == 0)
                    {
                        return null;
                    }

                    parts.RemoveAt(parts.Count - 1);
                    break;
                default:
                    parts.Add(segment);
                    break;
            }
        }

        return string.Join('/', parts);
    }

    public static string Folder(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}

// Text files, read as strict UTF-8.
internal static class TextFile
{
    public static readonly UTF8Encoding Utf8 = new(false);

    private static readonly UTF8Encoding s_strict = new(false, true);

    // The text of a file, named in findings as the user gave it.
    public static string Read(string path, string shown)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{shown}: not found");
        }

        try
        {
            return File.ReadAllText(path, s_strict);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{shown}: not UTF-8", exception);
        }
    }

    // The lines of a text file without their line breaks; CR LF and CR count as LF.
    public static List<string> Lines(string path, string shown)
    {
        var text = Read(path, shown).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var lines = text.Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return lines;
    }
}

// Runs git without a shell, each argument passed as it is. The self-test gives it an environment of its own:
// without the GIT_ variables of the caller, and with those it names.
internal sealed class Git(IReadOnlyDictionary<string, string>? environment)
{
    public (int Code, string Output, string Error) Run(string folder, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        if (environment is not null)
        {
            var inherited = start.Environment.Keys
                .Where(name => name.StartsWith("GIT_", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var name in inherited)
            {
                start.Environment.Remove(name);
            }

            foreach (var (name, value) in environment)
            {
                start.Environment[name] = value;
            }
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidDataException("git could not be started");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"git could not be started: {exception.Message}", exception);
        }

        using (process)
        {
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output, error.Result);
        }
    }

    // The output of a git command that must succeed, without its final line break.
    public string Text(string folder, params string[] arguments)
    {
        var (code, output, error) = Run(folder, arguments);
        return code == 0
            ? output.TrimEnd('\r', '\n')
            : throw new InvalidDataException($"git {string.Join(' ', arguments)} failed: {FirstLine(error)}");
    }

    public static string FirstLine(string text) =>
        text.Trim().Split('\n')[0].Trim() is { Length: > 0 } line ? line : "no message";
}

// The rules of S9.1.6 for a tag, in the repository that holds it.
internal static class TagCheck
{
    public const string DefaultMain = "refs/remotes/origin/main";

    public const string Template = "docs/releases/release-notes-template.md";

    public static int Run(TagOptions options, Io io, Report report)
    {
        var name = options.Tag;
        if (ReleaseTag.Parse(name) is not { } tag)
        {
            report.Fail($"tag \"{name}\": {ReleaseTag.Grammar}");
            return report.Finish();
        }

        if (options.RequireDryRun && tag.Mode == Mode.Release)
        {
            report.Fail(
                $"tag \"{name}\": a release tag, and --require-dry-run, which a manual run of the workflow passes, "
                    + "accepts only a dry-run tag, one with a pre-release part such as v0.0.0-dryrun.1");
        }

        var root = Root(options.Repository, io);
        var shallow = io.Git.Text(root, "rev-parse", "--is-shallow-repository") switch
        {
            "true" => true,
            "false" => false,
            var other => throw new InvalidDataException(
                $"git rev-parse --is-shallow-repository gave \"{other}\"; the check needs git 2.15 or later"),
        };
        if (shallow)
        {
            report.Fail(
                $"tag \"{name}\": the repository is a shallow clone, and the build number counts the whole history "
                    + "of the tagged commit; fetch the whole history, as actions/checkout does with fetch-depth: 0");
        }

        var found = io.Git.Run(root, "rev-parse", "--verify", "--quiet", $"refs/tags/{name}^{{commit}}");
        if (found.Code != 0)
        {
            report.Fail($"tag \"{name}\": the repository holds no such tag of a commit; fetch the tags first");
            return report.Finish();
        }

        var commit = found.Output.Trim();
        if (!Patterns.Commit().IsMatch(commit))
        {
            throw new InvalidDataException($"tag \"{name}\": git gave \"{commit}\", not a 40-digit commit hash");
        }

        var notes = tag.Mode == Mode.Release ? $"docs/releases/{name}.md" : Template;
        if (tag.Mode == Mode.Release)
        {
            OnMain(name, commit, options.Main, root, io, report);
        }

        var kind = io.Git.Run(root, "cat-file", "-t", $"{commit}:{notes}");
        if (kind.Code != 0 || kind.Output.Trim() != "blob")
        {
            report.Fail(
                $"tag \"{name}\": {notes} is not in the tagged commit, and a {Modes.Name(tag.Mode)} takes its notes "
                    + "from it");
        }

        var build = shallow ? string.Empty : io.Git.Text(root, "rev-list", "--count", commit);
        if (report.Failures > 0)
        {
            return report.Finish();
        }

        if (!Semver.IsBuild(build))
        {
            throw new InvalidDataException($"tag \"{name}\": git counted \"{build}\" commits");
        }

        string[] lines =
        [
            $"mode={Modes.Name(tag.Mode)}",
            $"version={tag.Version}",
            $"build={build}",
            $"commit={commit}",
            $"notes={notes}",
        ];
        foreach (var line in lines)
        {
            io.Out.WriteLine(line);
        }

        if (options.Output is { } output)
        {
            File.AppendAllText(io.Resolve(output), string.Concat(lines.Select(line => line + "\n")), TextFile.Utf8);
        }

        return report.Finish();
    }

    private static string Root(string? repository, Io io)
    {
        if (repository is null)
        {
            return io.Git.Text(io.Folder, "rev-parse", "--show-toplevel");
        }

        var path = io.Resolve(repository);
        return Directory.Exists(path)
            ? path
            : throw new ArgumentException($"--repository \"{repository}\": no such folder");
    }

    // A release is tagged on main: the tagged commit must be in the history of the main ref.
    private static void OnMain(string name, string commit, string main, string root, Io io, Report report)
    {
        if (io.Git.Run(root, "rev-parse", "--verify", "--quiet", $"{main}^{{commit}}").Code != 0)
        {
            report.Fail(
                $"tag \"{name}\": {main}, the main branch that a release tag must be on, is not in the repository; "
                    + "fetch it first");
            return;
        }

        var (code, _, error) = io.Git.Run(root, "merge-base", "--is-ancestor", commit, main);
        if (code == 1)
        {
            report.Fail($"tag \"{name}\": its commit {commit[..12]} is not on {main}, and a release is tagged on main");
        }
        else if (code != 0)
        {
            throw new InvalidDataException($"git merge-base --is-ancestor failed: {Git.FirstLine(error)}");
        }
    }
}

// The version and the build number in the Info.plist of a built app (C-0009 section 6).
internal static class AppCheck
{
    public static int Run(string bundle, string version, string build, Io io, Report report)
    {
        var shown = $"{bundle.TrimEnd('/', '\\')}/Info.plist";
        var path = Path.Combine(io.Resolve(bundle), "Info.plist");
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"{shown}: not found");
        }

        object list;
        try
        {
            list = Plist.Parse(File.ReadAllBytes(path), new Budget());
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException($"{shown}: {exception.Message}", exception);
        }

        if (list is not Dictionary<string, object> keys)
        {
            throw new InvalidDataException($"{shown}: the property list does not hold a dictionary at its top");
        }

        Expect(keys, "CFBundleShortVersionString", version, shown, report);
        Expect(keys, "CFBundleVersion", build, shown, report);
        if (report.Failures == 0)
        {
            io.Out.WriteLine($"{shown}: CFBundleShortVersionString is {version} and CFBundleVersion is {build}.");
        }

        return report.Finish();
    }

    private static void Expect(
        Dictionary<string, object> keys,
        string key,
        string expected,
        string shown,
        Report report)
    {
        if (!keys.TryGetValue(key, out var value))
        {
            report.Fail($"{shown}: {key} is missing, and the release expects \"{expected}\"");
        }
        else if (value is not string text)
        {
            report.Fail($"{shown}: {key} is not a string, and the release expects \"{expected}\"");
        }
        else if (text != expected)
        {
            report.Fail($"{shown}: {key} is \"{text}\", and the release expects \"{expected}\"");
        }
    }
}

// The sources that tools/ArchiveSources.cs archived, as its sources.json lists them.
internal sealed record Repository(string Name, string Url, string Tag, string Commit, string Archive, bool Public);

internal sealed record NestedSubmodule(string Path, string Url, string Commit, string? Skipped);

internal sealed record Submodule(
    string Path,
    string Name,
    string Url,
    string Commit,
    string? Archive,
    bool Public,
    IReadOnlyList<NestedSubmodule> Submodules,
    string? Skipped);

internal sealed record Component(string Name, string Url, string Tag, string Commit, string Archive);

internal sealed record SourceList(
    Mode Mode,
    Repository Repository,
    IReadOnlyList<Submodule> Submodules,
    IReadOnlyList<Component> Components)
{
    // Every archive that the release attaches.
    public IEnumerable<string> Archives =>
    [
        Repository.Archive,
        .. Submodules.Select(submodule => submodule.Archive).OfType<string>(),
        .. Components.Select(component => component.Archive),
    ];
}

// Reads sources.json strictly: an unknown or a missing key, a key given twice and a value of another form make the
// file unreadable. A skipped submodule has "skipped" in place of "archive", "public" and "submodules".
internal static class SourcesFile
{
    private static readonly string[] s_top = ["format", "mode", "repository", "submodules", "components"];
    private static readonly string[] s_repository = ["name", "url", "tag", "commit", "archive", "public"];
    private static readonly string[] s_archived = ["path", "name", "url", "commit", "archive", "public", "submodules"];
    private static readonly string[] s_skipped = ["path", "name", "url", "commit", "skipped"];
    private static readonly string[] s_nested = ["path", "url", "commit"];
    private static readonly string[] s_nestedSkipped = ["path", "url", "commit", "skipped"];
    private static readonly string[] s_component = ["name", "url", "tag", "commit", "archive"];

    public static SourceList Read(string path, string shown)
    {
        var text = TextFile.Read(path, shown);
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 16 });
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{shown}: not valid JSON: {exception.Message}", exception);
        }

        using (document)
        {
            var shape = new JsonShape(shown);
            var top = shape.Object(document.RootElement, "the file", s_top);
            if (top["format"] is not { ValueKind: JsonValueKind.Number } format || !format.TryGetInt32(out var number)
                || number != 1)
            {
                throw shape.Bad("format", "is not 1, the format that the tool reads");
            }

            var mode = Modes.Parse(shape.Text(top, "mode", string.Empty, Patterns.Text(), "a mode"))
                ?? throw shape.Bad("mode", "is neither release nor dry-run");
            var values = shape.Object(top["repository"], "repository", s_repository);
            var repository = new Repository(
                shape.Text(values, "name", "repository", Patterns.Name(), "a name"),
                shape.Url(values, "url", "repository"),
                shape.Text(values, "tag", "repository", Patterns.Tag(), "a tag"),
                shape.Commit(values, "commit", "repository"),
                shape.Text(values, "archive", "repository", Patterns.FileName(), "a file name"),
                shape.Flag(values, "public", "repository"));
            var submodules = new List<Submodule>();
            foreach (var element in shape.Items(top, "submodules", string.Empty))
            {
                submodules.Add(Submodule(shape, element, $"submodules[{submodules.Count}]"));
            }

            var components = new List<Component>();
            foreach (var element in shape.Items(top, "components", string.Empty))
            {
                var where = $"components[{components.Count}]";
                var component = shape.Object(element, where, s_component);
                components.Add(new Component(
                    shape.Text(component, "name", where, Patterns.Name(), "a name"),
                    shape.Url(component, "url", where),
                    shape.Text(component, "tag", where, Patterns.Tag(), "a tag"),
                    shape.Commit(component, "commit", where),
                    shape.Text(component, "archive", where, Patterns.FileName(), "a file name")));
            }

            return new SourceList(mode, repository, submodules, components);
        }
    }

    private static Submodule Submodule(JsonShape shape, JsonElement element, string where)
    {
        var skipped = element.ValueKind == JsonValueKind.Object && element.TryGetProperty("skipped", out _);
        var values = shape.Object(element, where, skipped ? s_skipped : s_archived);
        var path = shape.Text(values, "path", where, Patterns.PathInRepository(), "a relative path");
        var name = shape.Text(values, "name", where, Patterns.Name(), "a name");
        var url = shape.Url(values, "url", where);
        var commit = shape.Commit(values, "commit", where);
        if (skipped)
        {
            var reason = shape.Text(values, "skipped", where, Patterns.Text(), "a reason");
            return new Submodule(path, name, url, commit, null, false, [], reason);
        }

        var nested = new List<NestedSubmodule>();
        foreach (var item in shape.Items(values, "submodules", where))
        {
            var at = $"{where}.submodules[{nested.Count}]";
            var held = item.ValueKind == JsonValueKind.Object && !item.TryGetProperty("skipped", out _);
            var inner = shape.Object(item, at, held ? s_nested : s_nestedSkipped);
            nested.Add(new NestedSubmodule(
                shape.Text(inner, "path", at, Patterns.PathInRepository(), "a relative path"),
                shape.Url(inner, "url", at),
                shape.Commit(inner, "commit", at),
                held ? null : shape.Text(inner, "skipped", at, Patterns.Text(), "a reason")));
        }

        var archive = shape.Text(values, "archive", where, Patterns.FileName(), "a file name");
        return new Submodule(path, name, url, commit, archive, shape.Flag(values, "public", where), nested, null);
    }
}

// The objects of a JSON file, read strictly; a problem names the file and the place in it.
internal sealed class JsonShape(string file)
{
    public Dictionary<string, JsonElement> Object(JsonElement element, string where, string[] keys)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw Bad(where, "is not an object");
        }

        var found = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (Array.IndexOf(keys, property.Name) < 0)
            {
                throw Bad(where, $"has the unknown key \"{property.Name}\"");
            }

            if (!found.TryAdd(property.Name, property.Value))
            {
                throw Bad(where, $"gives the key \"{property.Name}\" twice");
            }
        }

        var missing = keys.FirstOrDefault(key => !found.ContainsKey(key));
        return missing is null ? found : throw Bad(where, $"lacks the key \"{missing}\"");
    }

    public string Text(Dictionary<string, JsonElement> values, string key, string where, Regex form, string what)
    {
        var value = values[key];
        return value.ValueKind == JsonValueKind.String && value.GetString() is { } text && form.IsMatch(text)
            ? text
            : throw Bad(Place(where, key), $"is not {what} in the form that the tool reads");
    }

    // An https URL without a .git suffix, as a link to <url>/tree/<commit> needs it.
    public string Url(Dictionary<string, JsonElement> values, string key, string where)
    {
        var url = Text(values, key, where, Patterns.Url(), "an https URL");
        return url.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? throw Bad(Place(where, key), "ends with .git")
            : url;
    }

    public string Commit(Dictionary<string, JsonElement> values, string key, string where) =>
        Text(values, key, where, Patterns.Commit(), "a commit hash of 40 lowercase hexadecimal digits");

    public bool Flag(Dictionary<string, JsonElement> values, string key, string where) => values[key].ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => throw Bad(Place(where, key), "is neither true nor false"),
    };

    public JsonElement.ArrayEnumerator Items(Dictionary<string, JsonElement> values, string key, string where) =>
        values[key].ValueKind == JsonValueKind.Array
            ? values[key].EnumerateArray()
            : throw Bad(Place(where, key), "is not an array");

    public InvalidDataException Bad(string where, string problem) => new($"{file}: {where} {problem}");

    private static string Place(string where, string key) => where.Length == 0 ? key : $"{where}.{key}";
}

// The checksum file of the release, as shasum -a 256 writes it: a line "<hash>  <file name>" for each file.
internal static class Checksums
{
    public static List<(string Hash, string Name)> Read(string path, string shown)
    {
        var lines = TextFile.Read(path, shown).Split('\n');
        var count = lines[^1].Length == 0 ? lines.Length - 1 : lines.Length;
        var entries = new List<(string Hash, string Name)>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var match = Patterns.Checksum().Match(lines[index]);
            if (!match.Success)
            {
                throw new InvalidDataException(
                    $"{shown} line {index + 1}: not \"<64 lowercase hexadecimal digits>  <file name>\" with a file "
                        + "name without a slash, as shasum -a 256 writes it");
            }

            var name = match.Groups["name"].Value;
            if (!names.Add(name))
            {
                throw new InvalidDataException($"{shown} line {index + 1}: a second checksum of {name}");
            }

            entries.Add((match.Groups["hash"].Value, name));
        }

        return [.. entries.OrderBy(entry => entry.Name, StringComparer.Ordinal)];
    }
}

// The parts of Markdown that the notes and the evidence README use: sections, tables and links.
internal static class Markdown
{
    // The lines under a heading, up to the next heading of level 1 or 2, or null when the heading is missing.
    public static (int Start, int End)? Section(IReadOnlyList<string> lines, string heading)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            if (lines[index].TrimEnd() == heading)
            {
                var end = index + 1;
                while (end < lines.Count && !IsMajorHeading(lines[end]))
                {
                    end++;
                }

                return (index + 1, end);
            }
        }

        return null;
    }

    // The rows of the first table in the range whose header has these cells, each with its line number, or null.
    public static List<(int Line, List<string> Cells)>? Table(
        IReadOnlyList<string> lines,
        (int Start, int End) range,
        string[] header)
    {
        for (var index = range.Start; index + 1 < range.End; index++)
        {
            if (IsRow(lines[index]) && Cells(lines[index]).SequenceEqual(header) && IsDelimiter(lines[index + 1]))
            {
                var rows = new List<(int Line, List<string> Cells)>();
                for (var row = index + 2; row < range.End && IsRow(lines[row]); row++)
                {
                    rows.Add((row + 1, Cells(lines[row])));
                }

                return rows;
            }
        }

        return null;
    }

    // The target of a cell that is one link, [text](target), or null.
    public static string? CellLink(string cell) =>
        Patterns.CellLink().Match(cell) is { Success: true } match ? match.Groups["target"].Value : null;

    // The file name of a link target: its last segment, without a fragment or a query.
    public static string FileName(string target)
    {
        var end = target.IndexOf('#');
        var path = end < 0 ? target : target[..end];
        end = path.IndexOf('?');
        path = end < 0 ? path : path[..end];
        return path[(path.LastIndexOf('/') + 1)..];
    }

    private static bool IsMajorHeading(string line) =>
        line is "#" or "##"
        || line.StartsWith("# ", StringComparison.Ordinal)
        || line.StartsWith("## ", StringComparison.Ordinal);

    private static bool IsRow(string line) => line.TrimStart().StartsWith('|');

    private static bool IsDelimiter(string line) =>
        IsRow(line) && Cells(line).All(cell => Patterns.Delimiter().IsMatch(cell));

    // The cells of a row, trimmed; a | after a backslash stays in its cell.
    private static List<string> Cells(string line)
    {
        var text = line.Trim();
        text = text.StartsWith('|') ? text[1..] : text;
        text = text.EndsWith('|') && !text.EndsWith("\\|", StringComparison.Ordinal) ? text[..^1] : text;
        var cells = new List<string>();
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '|' && (index == 0 || text[index - 1] != '\\'))
            {
                cells.Add(text[start..index].Trim());
                start = index + 1;
            }
        }

        cells.Add(text[start..].Trim());
        return cells;
    }
}

// The Gates table of docs/evidence/README.md: the verdict and the latest record of each gate.
internal static class Evidence
{
    private static readonly string[] s_header = ["Phase", "Gate", "Verdict", "Latest record"];

    public static Dictionary<string, (string Verdict, string Record)> Read(string path, string shown)
    {
        var lines = TextFile.Lines(path, shown);
        var section = Markdown.Section(lines, "## Gates")
            ?? throw new InvalidDataException($"{shown}: no \"## Gates\" section");
        var rows = Markdown.Table(lines, section, s_header)
            ?? throw new InvalidDataException(
                $"{shown}: the Gates section holds no table | Phase | Gate | Verdict | Latest record |");
        var gates = new Dictionary<string, (string Verdict, string Record)>(StringComparer.Ordinal);
        foreach (var (line, cells) in rows)
        {
            if (cells.Count != s_header.Length)
            {
                throw new InvalidDataException($"{shown} line {line}: a row of {cells.Count} cells in the Gates table");
            }

            if (!gates.TryAdd(cells[1], (cells[2], cells[3])))
            {
                throw new InvalidDataException($"{shown} line {line}: gate \"{cells[1]}\" is in the Gates table twice");
            }
        }

        return gates;
    }
}

// The relative links of the notes, which break on the release page: each one outside code becomes a link to the
// file at the tag, resolved against the folder of the notes in the repository, with its fragment kept.
internal static class Links
{
    public static List<string> Rewrite(IReadOnlyList<string> lines, ComposeOptions options, string url, Report report)
    {
        var folder = Paths.Folder(options.NotesInRepository);
        var rewritten = new List<string>(lines.Count);
        string? fence = null;
        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            var match = Patterns.Fence().Match(line);
            var marks = match.Groups["fence"].Value;
            var info = match.Groups["info"].Value;
            if (fence is not null)
            {
                // A fence closes with a line of its character, at least as long, and nothing after it.
                if (match.Success && marks[0] == fence[0] && marks.Length >= fence.Length && info.Trim().Length == 0)
                {
                    fence = null;
                }

                rewritten.Add(line);
            }
            else if (match.Success && (marks[0] == '~' || !info.Contains('`', StringComparison.Ordinal)))
            {
                fence = marks;
                rewritten.Add(line);
            }
            else
            {
                rewritten.Add(Line(line, index + 1, folder, url, options, report));
            }
        }

        return rewritten;
    }

    private static string Line(
        string line,
        int number,
        string folder,
        string url,
        ComposeOptions options,
        Report report)
    {
        var code = CodeSpans(line);
        return Patterns.Link().Replace(line, match =>
        {
            var target = match.Groups["target"];
            if (code.Any(span => match.Index >= span.Start && match.Index < span.End) || !IsRelative(target.Value))
            {
                return match.Value;
            }

            var hash = target.Value.IndexOf('#');
            var path = hash < 0 ? target.Value : target.Value[..hash];
            var fragment = hash < 0 ? string.Empty : target.Value[hash..];
            var resolved = Paths.Resolve(folder, path);
            if (resolved is null)
            {
                report.Fail($"{options.Notes} line {number}: the link target {target.Value} leaves the repository");
                return match.Value;
            }

            var absolute = resolved.Length == 0
                ? $"{url}/tree/{options.Tag.Name}{fragment}"
                : $"{url}/blob/{options.Tag.Name}/{resolved}{fragment}";
            var start = target.Index - match.Index;
            return string.Concat(match.Value.AsSpan(0, start), absolute, match.Value.AsSpan(start + target.Length));
        });
    }

    // A target relative to the notes: not empty, without a URL scheme, and with neither # nor / first.
    private static bool IsRelative(string target) =>
        target.Length > 0 && target[0] is not ('#' or '/') && !Patterns.Scheme().IsMatch(target);

    // The code spans of a line: from a run of backticks to the next run of as many.
    private static List<(int Start, int End)> CodeSpans(string line)
    {
        var spans = new List<(int Start, int End)>();
        var index = 0;
        while (index < line.Length)
        {
            if (line[index] != '`')
            {
                index++;
                continue;
            }

            var run = Run(line, index);
            var close = index + run;
            while (close < line.Length && (line[close] != '`' || Run(line, close) != run))
            {
                close += line[close] == '`' ? Run(line, close) : 1;
            }

            if (close < line.Length)
            {
                spans.Add((index, close + run));
                index = close + run;
            }
            else
            {
                // A run without its closing run is text.
                index += run;
            }
        }

        return spans;
    }

    private static int Run(string line, int start)
    {
        var end = start;
        while (end < line.Length && line[end] == '`')
        {
            end++;
        }

        return end - start;
    }
}

// Composes the body of the draft release from the notes (S9.1.4).
internal static class Composer
{
    public const string BuildMarker = "<!-- release: build -->";

    public const string SourcesMarker = "<!-- release: sources -->";

    public const string ChecksumsMarker = "<!-- release: checksums -->";

    public const string Banner =
        "**Dry run.** This draft tests the release workflow on the tag `{0}`. It is never published, and it is "
        + "deleted after checking.";

    private static readonly string[] s_markers = [BuildMarker, SourcesMarker, ChecksumsMarker];

    private static readonly string[] s_gates = ["Phase", "Gate", "Verdict", "Record"];

    public static int Run(ComposeOptions options, Io io, Report report)
    {
        var notes = TextFile.Lines(io.Resolve(options.Notes), options.Notes);
        var sources = SourcesFile.Read(io.Resolve(options.Sources), options.Sources);
        var checksums = Checksums.Read(io.Resolve(options.Checksums), options.Checksums);
        var gates = Evidence.Read(io.Resolve(options.Evidence), options.Evidence);
        Describe(options, sources, checksums, report);
        Markers(options, notes, report);
        Gates(options, notes, gates, report);
        KnownIssues(options, notes, report);
        for (var index = 0; index < notes.Count; index++)
        {
            if (Patterns.Fill().IsMatch(notes[index]))
            {
                report.ForRelease(
                    options.Tag.Mode,
                    $"{options.Notes} line {index + 1}: a <!-- fill: ... --> placeholder remains");
            }
        }

        var linked = Links.Rewrite(notes, options, sources.Repository.Url, report);
        if (report.Failures > 0)
        {
            return report.Finish();
        }

        var path = io.Resolve(options.Out);
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? io.Folder);
        File.WriteAllText(path, Body(options, linked, sources, checksums), TextFile.Utf8);
        io.Out.WriteLine(
            $"{options.Out}: the body of the {(options.Tag.Mode == Mode.Release ? "draft release" : "dry-run draft")} "
                + $"of {options.Tag.Name}, with {checksums.Count} checksums.");
        return report.Finish();
    }

    // sources.json describes this tag at this commit in this mode, every archive that it names has a checksum, and
    // a release comes from a public repository with public forks (C-0009 section 6).
    private static void Describe(
        ComposeOptions options,
        SourceList sources,
        List<(string Hash, string Name)> checksums,
        Report report)
    {
        var tag = options.Tag;
        if (sources.Mode != tag.Mode)
        {
            report.Fail(
                $"{options.Sources}: the mode is {Modes.Name(sources.Mode)}, and tag \"{tag.Name}\" makes a "
                    + Modes.Name(tag.Mode));
        }

        if (sources.Repository.Tag != tag.Name)
        {
            report.Fail(
                $"{options.Sources}: the repository's tag is \"{sources.Repository.Tag}\", and --tag is "
                    + $"\"{tag.Name}\"");
        }

        if (sources.Repository.Commit != options.Commit)
        {
            report.Fail(
                $"{options.Sources}: the repository's commit is {sources.Repository.Commit}, and --commit is "
                    + options.Commit);
        }

        var listed = checksums.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var archive in sources.Archives.Where(archive => !listed.Contains(archive)))
        {
            report.Fail($"{options.Checksums}: no checksum of {archive}, an archive that {options.Sources} names");
        }

        if (!sources.Repository.Public)
        {
            report.ForRelease(
                tag.Mode,
                $"{options.Sources}: the repository {sources.Repository.Name} is not public, and a release needs it "
                    + "public (C-0009 section 6)");
        }

        foreach (var fork in sources.Submodules.Where(submodule => submodule.Skipped is null && !submodule.Public))
        {
            report.ForRelease(
                tag.Mode,
                $"{options.Sources}: the fork {fork.Path} is not public, and a release needs every fork public "
                    + "(C-0009 section 6)");
        }
    }

    // Each marker stands once, on a line of its own, and no other release marker may stand anywhere.
    private static void Markers(ComposeOptions options, List<string> notes, Report report)
    {
        var lines = s_markers.ToDictionary(marker => marker, _ => new List<int>(), StringComparer.Ordinal);
        for (var index = 0; index < notes.Count; index++)
        {
            if (!Patterns.Marker().IsMatch(notes[index]))
            {
                continue;
            }

            if (lines.TryGetValue(notes[index].Trim(' ', '\t'), out var found))
            {
                found.Add(index + 1);
            }
            else
            {
                report.Fail(
                    $"{options.Notes} line {index + 1}: a release marker other than {string.Join(", ", s_markers)}, "
                        + "each on a line of its own");
            }
        }

        foreach (var (marker, found) in lines)
        {
            if (found.Count == 0)
            {
                report.Fail($"{options.Notes}: the marker {marker} is missing; it stands once, on a line of its own");
            }
            else if (found.Count > 1)
            {
                report.Fail(
                    $"{options.Notes}: the marker {marker} is on {found.Count} lines ({string.Join(", ", found)}), "
                        + "and it stands once");
            }
        }
    }

    // Every gate in scope has passed or been waived in the Gates table of the evidence README, and the notes give
    // its verdict and link to its latest record as that table does (C-0012 section 5).
    private static void Gates(
        ComposeOptions options,
        List<string> notes,
        Dictionary<string, (string Verdict, string Record)> gates,
        Report report)
    {
        var mode = options.Tag.Mode;
        var section = Markdown.Section(notes, "## Gates in scope");
        var rows = section is { } range ? Markdown.Table(notes, range, s_gates) : null;
        if (rows is null || rows.Count == 0)
        {
            var problem = section is null
                ? "no \"## Gates in scope\" section"
                : rows is null
                    ? "the Gates in scope section holds no table | Phase | Gate | Verdict | Record |"
                    : "the Gates in scope table lists no gate";
            report.ForRelease(mode, $"{options.Notes}: {problem}, and a release lists each gate that it covers");
            return;
        }

        foreach (var (line, cells) in rows)
        {
            var at = $"{options.Notes} line {line}";
            if (cells.Count != s_gates.Length)
            {
                report.ForRelease(mode, $"{at}: a row of {cells.Count} cells in the Gates in scope table");
                continue;
            }

            var gate = cells[1];
            if (!gates.TryGetValue(gate, out var evidence))
            {
                report.ForRelease(mode, $"{at}: gate \"{gate}\" is not in the Gates table of {options.Evidence}");
                continue;
            }

            if (evidence.Verdict is not ("Pass" or "Waived"))
            {
                report.ForRelease(
                    mode,
                    $"{at}: gate \"{gate}\" has the verdict \"{evidence.Verdict}\" in {options.Evidence}, and a "
                        + "release needs Pass or Waived");
                continue;
            }

            if (cells[2] != evidence.Verdict)
            {
                report.ForRelease(
                    mode,
                    $"{at}: gate \"{gate}\" has the verdict \"{cells[2]}\" in the notes and \"{evidence.Verdict}\" in "
                        + options.Evidence);
            }

            var expected = Markdown.CellLink(evidence.Record);
            var record = Markdown.CellLink(cells[3]);
            if (expected is null)
            {
                report.ForRelease(mode, $"{at}: gate \"{gate}\" has no link to its record in {options.Evidence}");
            }
            else if (record is null || Markdown.FileName(record) != Markdown.FileName(expected))
            {
                var given = record is null ? "not a link" : Markdown.FileName(record);
                report.ForRelease(
                    mode,
                    $"{at}: the record of gate \"{gate}\" is {given}, and {options.Evidence} links to "
                        + Markdown.FileName(expected));
            }
        }
    }

    private static void KnownIssues(ComposeOptions options, List<string> notes, Report report)
    {
        var section = Markdown.Section(notes, "## Known issues");
        var listed = section is { } range
            && notes.Skip(range.Start).Take(range.End - range.Start).Any(line => Patterns.ListItem().IsMatch(line));
        if (!listed)
        {
            report.ForRelease(
                options.Tag.Mode,
                $"{options.Notes}: no \"## Known issues\" section that lists an item, a line that starts with -, * "
                    + "or 1.");
        }
    }

    private static string Body(
        ComposeOptions options,
        List<string> notes,
        SourceList sources,
        List<(string Hash, string Name)> checksums)
    {
        var tag = options.Tag.Name;
        var url = sources.Repository.Url;
        var body = new List<string>();
        if (options.Tag.Mode == Mode.DryRun)
        {
            body.Add(Banner.Replace("{0}", tag, StringComparison.Ordinal));
            body.Add(string.Empty);
        }

        foreach (var line in notes)
        {
            switch (line.Trim(' ', '\t'))
            {
                case BuildMarker:
                    body.Add(
                        $"Paneport {options.Tag.Version}, build {options.Build}, built from the tag "
                            + $"{Link(tag, $"{url}/tree/{tag}")} at commit {Revision(url, options.Commit)}.");
                    break;
                case SourcesMarker:
                    body.AddRange(SourceTable(sources));
                    break;
                case ChecksumsMarker:
                    body.Add("```text");
                    body.AddRange(checksums.Select(entry => $"{entry.Hash}  {entry.Name}"));
                    body.Add("```");
                    break;
                default:
                    body.Add(line);
                    break;
            }
        }

        while (body.Count > 0 && string.IsNullOrWhiteSpace(body[^1]))
        {
            body.RemoveAt(body.Count - 1);
        }

        return string.Join('\n', body) + "\n";
    }

    // A row for this repository, each submodule, each submodule that the archive of a fork holds, and each LGPL
    // component that is neither this repository nor a fork (C-0009 section 6).
    private static List<string> SourceTable(SourceList sources)
    {
        var repository = sources.Repository;
        List<string> table =
        [
            "| Source | Revision | Archive |",
            "|---|---|---|",
            $"| {Cell(repository.Name)} | {Link(repository.Tag, $"{repository.Url}/tree/{repository.Tag}")} at "
                + $"{Revision(repository.Url, repository.Commit)} | {Code(repository.Archive)} |",
        ];
        foreach (var submodule in sources.Submodules)
        {
            var archive = submodule.Skipped is { } reason ? $"not archived: {Cell(reason)}" : Code(submodule.Archive!);
            table.Add($"| {Code(submodule.Path)} | {Revision(submodule.Url, submodule.Commit)} | {archive} |");
            foreach (var nested in submodule.Submodules)
            {
                var held = nested.Skipped is { } why ? $"not archived: {Cell(why)}" : $"in {Code(submodule.Archive!)}";
                table.Add(
                    $"| {Code($"{submodule.Path}/{nested.Path}")} | {Revision(nested.Url, nested.Commit)} | {held} |");
            }
        }

        foreach (var component in sources.Components)
        {
            table.Add(
                $"| {Cell(component.Name)} | {Link(component.Tag, $"{component.Url}/tree/{component.Commit}")} | "
                    + $"{Code(component.Archive)} |");
        }

        table.Add(string.Empty);
        table.Add("Every archive is attached to this release.");
        return table;
    }

    private static string Revision(string url, string commit) => Link(commit[..12], $"{url}/tree/{commit}");

    private static string Link(string text, string target) => $"[`{text}`]({target})";

    private static string Code(string text) => $"`{text}`";

    private static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal);
}

internal sealed class Tally
{
    private readonly List<string> _skipped = [];

    public int Total { get; private set; }

    public int Failed { get; private set; }

    public void Expect(bool passed, string description, string why)
    {
        Total++;
        Failed += passed ? 0 : 1;
        Console.WriteLine(passed ? $"ok   {description}" : $"FAIL {description}: {why}");
    }

    // A check that cannot run here counts neither as passed nor as failed, and the summary names its reason.
    public void Skip(string description, string reason)
    {
        _skipped.Add(reason);
        Console.WriteLine($"skip {description}: {reason}");
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

// What a run of the tool gave: its exit code and what it printed.
internal sealed record Outcome(int Code, string Out, string Error)
{
    public string Why
    {
        get
        {
            var printed = (Error.Length > 0 ? Error : Out).Trim().Replace("\n", " | ", StringComparison.Ordinal);
            return $"exit code {Code}, {(printed.Length == 0 ? "nothing printed" : printed)}";
        }
    }

    public bool Fails(int code, params string[] named) =>
        Code == code && Out.Length == 0 && named.All(name => Error.Contains(name, StringComparison.Ordinal));

    public bool Warns(params string[] named) =>
        Code == 0
        && Error.Split('\n').Any(line =>
            line.StartsWith("warning: ", StringComparison.Ordinal)
            && named.All(name => line.Contains(name, StringComparison.Ordinal)));
}

// The temporary folder of the self-test and the git repositories that it builds there. Git runs without the
// caller's GIT_ variables and without any global or system configuration, and each commit gets the fixture's
// name and e-mail address on its command line.
internal sealed class Fixture
{
    public Fixture(string root)
    {
        Root = root;
        var configuration = Path.Combine(root, "gitconfig");
        File.WriteAllText(configuration, string.Empty);
        Git = new Git(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GIT_CONFIG_NOSYSTEM"] = "1",
            ["GIT_CONFIG_GLOBAL"] = configuration,
            ["GIT_TERMINAL_PROMPT"] = "0",
        });
    }

    public string Root { get; }

    public Git Git { get; }

    public string Run(string folder, params string[] arguments) => Git.Text(folder, arguments);

    public string Init(string name)
    {
        var folder = Path.Combine(Root, name);
        Directory.CreateDirectory(folder);
        Run(folder, "init", "--quiet", "--initial-branch=main");
        return folder;
    }

    // Writes the files, or deletes those without a text, commits and returns the new commit.
    public string Commit(string folder, string message, params (string Path, string? Text)[] files)
    {
        foreach (var (path, text) in files)
        {
            Write(folder, path, text);
        }

        Run(folder, "add", "--all");
        Run(folder, [.. Identity, "commit", "--quiet", "--allow-empty", "--message", message]);
        return Run(folder, "rev-parse", "HEAD");
    }

    public static readonly string[] Identity = ["-c", "user.name=Fixture", "-c", "user.email=fixture@example.com"];

    public static void Write(string folder, string path, string? text)
    {
        var full = Path.Combine(folder, path);
        if (text is null)
        {
            File.Delete(full);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(full) ?? folder);
        File.WriteAllText(full, text);
    }

    // Runs the tool as its command line does, with relative paths resolved against the folder.
    public Outcome Invoke(string folder, bool actions, params string[] args)
    {
        using var output = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        using var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var code = Cli.Run(args, new Io(output, error, actions, Git, folder));
        return new Outcome(code, output.ToString(), error.ToString());
    }

    public Outcome Invoke(params string[] args) => Invoke(Root, false, args);

    // Deletes the folder, whose git objects are read-only on Windows.
    public static bool Delete(string root)
    {
        try
        {
            if (Directory.Exists(root))
            {
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                Directory.Delete(root, recursive: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

// Every fixture is built at run time in a temporary folder and deleted at the end: real git repositories,
// property lists, notes, sources.json and checksum files with made-up names, example.com addresses and hashes
// computed from made-up texts (C-0011 section 1).
internal static partial class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("Self-test of tools/ReleaseVersion.cs, with fixtures built at run time.");
        var tally = new Tally();
        var root = Path.Combine(Path.GetTempPath(), $"ReleaseVersion-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var fixture = new Fixture(root);
            Console.WriteLine("Tag grammar:");
            Grammar(tally, fixture);
            Console.WriteLine("Tags in a repository:");
            Tags(tally, fixture);
            Console.WriteLine("Usage:");
            Usage(tally, fixture);
            Console.WriteLine("Info.plist:");
            Apps(tally, fixture);
            Console.WriteLine("Composition:");
            Composition(tally, fixture);
            Console.WriteLine("Rules of a release, which a dry run only warns on:");
            Releases(tally, fixture);
            Console.WriteLine("The template of the notes:");
            Template(tally, fixture);
            Console.WriteLine("Annotations:");
            Annotations(tally, fixture);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            tally.Expect(false, "the fixtures are built", exception.Message);
        }
        finally
        {
            tally.Expect(Fixture.Delete(root), "the temporary folder of the fixtures is deleted", "it is still there");
        }

        Console.WriteLine(tally.Summary());
        return tally.Failed == 0 ? 0 : 1;
    }

    private static void Grammar(Tally tally, Fixture fixture)
    {
        (string Tag, Mode Mode, string Version)[] good =
        [
            ("v0.1.0", Mode.Release, "0.1.0"),
            ("v0.0.0", Mode.Release, "0.0.0"),
            ("v10.20.30", Mode.Release, "10.20.30"),
            ("v0.0.0-dryrun.1", Mode.DryRun, "0.0.0"),
            ("v1.2.3-rc.1", Mode.DryRun, "1.2.3"),
            ("v1.2.3-0", Mode.DryRun, "1.2.3"),
            ("v1.2.3-0a", Mode.DryRun, "1.2.3"),
            ("v1.2.3--", Mode.DryRun, "1.2.3"),
            ("v1.2.3-x-y.10.z", Mode.DryRun, "1.2.3"),
        ];
        foreach (var (tag, mode, version) in good)
        {
            var parsed = ReleaseTag.Parse(tag);
            tally.Expect(
                parsed is not null && parsed.Mode == mode && parsed.Version == version,
                $"{tag} is a {Modes.Name(mode)} tag of version {version}",
                parsed is null ? "refused" : $"{Modes.Name(parsed.Mode)} of {parsed.Version}");
        }

        string[] bad =
        [
            "v1.2", "0.1.0", "v01.2.3", "v1.2.3-", "v1.2.3-01", "v1.2.3+x", "V1.2.3", "v1.2.3.4", "v1.02.3",
            "v1.2.03", "v1..3", "vv1.2.3", "v-1.2.3", "v1.2.3-a..b", "v1.2.3-.", "v1.2.3-rc.", "v1.2.3-a_b",
            "v1.2.3-rc.1+b", "v 1.2.3", "v1.2.3 ", "v1.2.3\n", "v", string.Empty,
            $"v1.2.3-{(char)0x00E9}", $"v{(char)0xFF11}.2.3",
        ];
        foreach (var tag in bad)
        {
            var outcome = fixture.Invoke("--tag", tag);
            tally.Expect(
                outcome.Fails(1, $"tag \"{Report.Plain(tag)}\"", "a release tag is v<MAJOR>.<MINOR>.<PATCH>"),
                $"the tag \"{Report.Plain(tag)}\" is refused and named",
                outcome.Why);
        }
    }

    private static void Tags(Tally tally, Fixture fixture)
    {
        var repository = fixture.Init("repository");
        fixture.Commit(repository, "Add the template", ("README.md", "Fixture\n"), (TagCheck.Template, "# Template\n"));
        var second = fixture.Commit(repository, "Add the notes of v0.1.0", ("docs/releases/v0.1.0.md", "# 0.1.0\n"));
        fixture.Run(repository, [.. Fixture.Identity, "tag", "--annotate", "--message", "Fixture", "v0.1.0", second]);
        var third = fixture.Commit(repository, "Add the notes of v0.1.1", ("docs/releases/v0.1.1.md", "# 0.1.1\n"));
        fixture.Run(repository, "tag", "v0.1.1", third);
        var fourth = fixture.Commit(repository, "Change the readme", ("README.md", "Fixture, changed\n"));
        fixture.Run(repository, "tag", "v0.3.0", fourth);
        fixture.Run(repository, "update-ref", "refs/remotes/origin/main", fourth);
        fixture.Run(repository, "checkout", "--quiet", "-b", "side", second);
        var side = fixture.Commit(repository, "Add the notes of v0.2.0", ("docs/releases/v0.2.0.md", "# 0.2.0\n"));
        fixture.Run(repository, "tag", "v0.2.0", side);
        fixture.Run(repository, "tag", "v0.0.0-dryrun.1", side);
        var bare = fixture.Commit(repository, "Remove the template", (TagCheck.Template, null));
        fixture.Run(repository, "tag", "v0.0.0-dryrun.2", bare);
        fixture.Run(repository, "checkout", "--quiet", "main");

        var release = fixture.Invoke("--tag", "v0.1.0", "--repository", "repository");
        tally.Expect(
            release.Code == 0 && release.Error.Length == 0
                && release.Out
                    == $"mode=release\nversion=0.1.0\nbuild=2\ncommit={second}\nnotes=docs/releases/v0.1.0.md\n",
            "v0.1.0 on main with its notes: release mode, version 0.1.0, build 2, the commit under the annotated tag",
            release.Why);
        var later = fixture.Invoke("--tag", "v0.1.1", "--repository", "repository");
        tally.Expect(
            later.Code == 0
                && Build(later) > Build(release)
                && later.Out.Contains($"commit={third}\n", StringComparison.Ordinal),
            "v0.1.1, a later commit on the same history, gets a larger build number",
            $"{later.Why}; build {Build(later)} after {Build(release)}");
        var dryRun = fixture.Invoke("--tag", "v0.0.0-dryrun.1", "--repository", "repository");
        tally.Expect(
            dryRun.Code == 0
                && dryRun.Out == $"mode=dry-run\nversion=0.0.0\nbuild=3\ncommit={side}\nnotes={TagCheck.Template}\n",
            "v0.0.0-dryrun.1 off main: dry-run mode, version 0.0.0, the template as notes",
            dryRun.Why);

        Expect(tally, fixture.Invoke("--tag", "v0.2.0", "--repository", "repository"), 1,
            "a release tag off main fails", "tag \"v0.2.0\"", $"{side[..12]} is not on refs/remotes/origin/main");
        Expect(tally, fixture.Invoke("--tag", "v0.3.0", "--repository", "repository"), 1,
            "a release tag without docs/releases/<tag>.md in its commit fails",
            "tag \"v0.3.0\"", "docs/releases/v0.3.0.md is not in the tagged commit");
        Expect(tally, fixture.Invoke("--tag", "v0.0.0-dryrun.2", "--repository", "repository"), 1,
            "a dry-run tag without the template in its commit fails",
            "tag \"v0.0.0-dryrun.2\"", $"{TagCheck.Template} is not in the tagged commit");
        Expect(tally, fixture.Invoke("--tag", "v9.9.9", "--repository", "repository"), 1, "a missing tag fails",
            "tag \"v9.9.9\"", "no such tag");
        Expect(tally, fixture.Invoke("--tag", "v0.1.0", "--repository", "repository", "--main", "refs/heads/gone"),
            1, "a missing main ref fails", "tag \"v0.1.0\"", "refs/heads/gone");
        Expect(tally, fixture.Invoke("--tag", "v0.1.1", "--repository", "repository", "--main", "side"), 1,
            "--main names the branch that a release tag must be on", "tag \"v0.1.1\"", "is not on side");
        Expect(tally, fixture.Invoke("--tag", "v0.1.0", "--repository", "repository", "--require-dry-run"), 1,
            "--require-dry-run refuses a release tag", "tag \"v0.1.0\"", "--require-dry-run");
        var required = fixture.Invoke("--tag", "v0.0.0-dryrun.1", "--repository", "repository", "--require-dry-run");
        tally.Expect(required.Code == 0, "--require-dry-run accepts a dry-run tag", required.Why);

        var output = Path.Combine(fixture.Root, "output.txt");
        File.WriteAllText(output, "earlier=1\n");
        var appended = fixture.Invoke("--tag", "v0.1.0", "--repository", "repository", "--output", "output.txt");
        tally.Expect(
            appended.Code == 0 && File.ReadAllText(output) == "earlier=1\n" + appended.Out && appended.Out.Length > 0,
            "--output appends the five lines to the file",
            appended.Why);
        var refused = fixture.Invoke("--tag", "v0.2.0", "--repository", "repository", "--output", "output.txt");
        tally.Expect(
            refused.Code == 1 && File.ReadAllText(output) == "earlier=1\n" + appended.Out,
            "--output appends nothing when a check fails",
            refused.Why);

        // A clone of depth 1 by a file:// URL, which a local path would not make shallow.
        var shallow = Path.Combine(fixture.Root, "shallow");
        var source = new Uri(Path.GetFullPath(repository)).AbsoluteUri;
        fixture.Run(
            fixture.Root, "-c", "protocol.file.allow=always", "clone", "--quiet", "--depth", "1", source, shallow);
        var tip = fixture.Commit(shallow, "Add the notes of v0.9.0", ("docs/releases/v0.9.0.md", "# 0.9.0\n"));
        fixture.Run(shallow, "tag", "v0.9.0", tip);
        fixture.Run(shallow, "update-ref", "refs/remotes/origin/main", tip);
        Expect(tally, fixture.Invoke("--tag", "v0.9.0", "--repository", "shallow"), 1, "a shallow clone fails",
            "tag \"v0.9.0\"", "shallow clone");
        fixture.Run(shallow, "-c", "protocol.file.allow=always", "fetch", "--quiet", "--unshallow", "origin");
        fixture.Run(shallow, "update-ref", "refs/remotes/origin/main", tip);
        var whole = fixture.Invoke("--tag", "v0.9.0", "--repository", "shallow");
        tally.Expect(
            whole.Code == 0 && whole.Out.Contains("build=5\n", StringComparison.Ordinal),
            "the same clone with its whole history passes, with build 5",
            whole.Why);
    }

    private static void Usage(Tally tally, Fixture fixture)
    {
        (string[] Args, string Named)[] cases =
        [
            ([], "give --tag"),
            (["--tag"], "--tag needs a value"),
            (["--tag", "v0.1.0", "--tag", "v0.1.1"], "--tag is given twice"),
            (["--tag", "v0.1.0", "--version", "0.1.0"], "--tag does not take --version"),
            (["--tag", "v0.1.0", "--main", "-x"], "--main \"-x\""),
            (["--tag", "v0.1.0", "--repository", "nowhere"], "--repository \"nowhere\""),
            (["--tag", "v0.1.0", "--self-test"], "unknown argument \"--self-test\""),
            (["--check-app", "a.app", "--version", "0.1", "--build", "1"], "--version \"0.1\""),
            (["--check-app", "a.app", "--version", "0.1.0", "--build", "01"], "--build \"01\""),
            (["--check-app", "a.app", "--version", "0.1.0"], "--check-app needs --build"),
        ];
        foreach (var (args, named) in cases)
        {
            Expect(tally, fixture.Invoke(args), 2, $"usage error: {named}", named, "usage: dotnet run");
        }
    }

    private static void Apps(Tally tally, Fixture fixture)
    {
        (string Key, string Value)[] keys =
        [
            ("CFBundleIdentifier", "com.example.fixture"),
            ("CFBundleDisplayName", $"Fixture {(char)0x00E9}"),
            ("CFBundleShortVersionString", "0.1.0"),
            ("CFBundleVersion", "42"),
        ];
        var read = Plist.Parse(BinaryPropertyList.Write(keys), new Budget()) as Dictionary<string, object>;
        tally.Expect(
            read is not null
                && read.Count == keys.Length
                && keys.All(key => read.TryGetValue(key.Key, out var value) && value as string == key.Value),
            "Plist.Parse reads back the binary property list that the self-test writes, with UTF-16 and long strings",
            read is null ? "no dictionary" : string.Join(", ", read.Select(pair => $"{pair.Key}={pair.Value}")));

        string[] formats = ["XML", "binary"];
        foreach (var format in formats)
        {
            Outcome check(string name, IEnumerable<(string Key, string Value)> entries, string version, string build)
            {
                var list = entries.ToList();
                var bundle = Path.Combine(fixture.Root, "apps", $"{format}-{name}", "Paneport.app");
                Directory.CreateDirectory(bundle);
                File.WriteAllBytes(
                    Path.Combine(bundle, "Info.plist"),
                    format == "XML" ? XmlPropertyList(list) : BinaryPropertyList.Write(list));
                return fixture.Invoke(
                    "--check-app", $"apps/{format}-{name}/Paneport.app", "--version", version, "--build", build);
            }

            var shown = $"apps/{format}-";
            var matching = check("matching", keys, "0.1.0", "42");
            tally.Expect(
                matching.Code == 0 && matching.Error.Length == 0,
                $"{format}: an Info.plist with the version and the build number passes",
                matching.Why);
            Expect(tally, check("version", keys, "0.2.0", "42"), 1,
                $"{format}: another CFBundleShortVersionString fails, naming the key and both values",
                $"{shown}version/Paneport.app/Info.plist: CFBundleShortVersionString is \"0.1.0\"",
                "expects \"0.2.0\"");
            Expect(tally, check("build", keys, "0.1.0", "43"), 1,
                $"{format}: another CFBundleVersion fails, naming the key and both values",
                $"{shown}build/Paneport.app/Info.plist: CFBundleVersion is \"42\"", "expects \"43\"");
            Expect(tally, check("short", keys.Where(key => key.Key != "CFBundleShortVersionString"), "0.1.0", "42"), 1,
                $"{format}: a missing CFBundleShortVersionString fails", "CFBundleShortVersionString is missing");
            Expect(tally, check("long", keys.Where(key => key.Key != "CFBundleVersion"), "0.1.0", "42"), 1,
                $"{format}: a missing CFBundleVersion fails", "CFBundleVersion is missing");
        }

        Expect(tally, fixture.Invoke("--check-app", "apps/none.app", "--version", "0.1.0", "--build", "42"), 2,
            "a bundle without Info.plist cannot be read", "apps/none.app/Info.plist: not found");
        Fixture.Write(fixture.Root, "apps/broken.app/Info.plist", "bplist00 is not enough");
        Expect(tally, fixture.Invoke("--check-app", "apps/broken.app", "--version", "0.1.0", "--build", "42"), 2,
            "an Info.plist that is not a property list cannot be read", "apps/broken.app/Info.plist: ");
    }

    // An Info.plist in XML, as Xcode writes it.
    private static byte[] XmlPropertyList(IEnumerable<(string Key, string Value)> entries) =>
        Encoding.UTF8.GetBytes(
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
                + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" "
                + "\"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
                + "<plist version=\"1.0\">\n<dict>\n"
                + string.Concat(
                    entries.Select(entry => $"\t<key>{entry.Key}</key>\n\t<string>{entry.Value}</string>\n"))
                + "</dict>\n</plist>\n");

    private static void Expect(Tally tally, Outcome outcome, int code, string description, params string[] named) =>
        tally.Expect(outcome.Fails(code, named), description, outcome.Why);

    private static int Build(Outcome outcome) =>
        outcome.Out.Split('\n').FirstOrDefault(line => line.StartsWith("build=", StringComparison.Ordinal)) is { } line
            && int.TryParse(line["build=".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var build)
            ? build
            : -1;
}

// Writes a binary property list in Apple's bplist00 format for the self-test: a dictionary of strings at the top,
// an ASCII string as an object of type 5 and any other in UTF-16 as type 6, a count of 15 or more as an integer
// object after the marker, references of one byte, the offset table and the 32-byte trailer.
internal static class BinaryPropertyList
{
    public static byte[] Write(IReadOnlyList<(string Key, string Value)> entries)
    {
        // The dictionary is object 0, its keys are objects 1 to n and its values n + 1 to 2n.
        var dictionary = new List<byte>();
        Marker(dictionary, 0xD, entries.Count);
        for (var index = 1; index <= 2 * entries.Count; index++)
        {
            dictionary.Add((byte)index);
        }

        List<byte[]> objects =
        [
            [.. dictionary],
            .. entries.Select(entry => Text(entry.Key)),
            .. entries.Select(entry => Text(entry.Value)),
        ];
        if (objects.Count > byte.MaxValue)
        {
            throw new InvalidDataException("the writer of the self-test makes references of one byte only");
        }

        var file = new List<byte>("bplist00"u8.ToArray());
        var offsets = new List<int>();
        foreach (var item in objects)
        {
            offsets.Add(file.Count);
            file.AddRange(item);
        }

        var table = file.Count;
        var offsetSize = table <= byte.MaxValue ? 1 : 2;
        foreach (var offset in offsets)
        {
            if (offsetSize == 2)
            {
                file.Add((byte)(offset >> 8));
            }

            file.Add((byte)offset);
        }

        // The trailer: five unused bytes, the sort version, the sizes of an offset and of a reference, the number
        // of objects, the top object and the offset of the table, big-endian.
        var trailer = new byte[32];
        trailer[6] = (byte)offsetSize;
        trailer[7] = 1;
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(8), (ulong)objects.Count);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(16), 0);
        BinaryPrimitives.WriteUInt64BigEndian(trailer.AsSpan(24), (ulong)table);
        file.AddRange(trailer);
        return [.. file];
    }

    private static byte[] Text(string text)
    {
        var ascii = text.All(char.IsAscii);
        var bytes = new List<byte>();
        Marker(bytes, ascii ? 0x5 : 0x6, text.Length);
        bytes.AddRange(ascii ? Encoding.ASCII.GetBytes(text) : Encoding.BigEndianUnicode.GetBytes(text));
        return [.. bytes];
    }

    // The type in the high nibble and the count in the low one, or 0xF there and the count as an integer object.
    private static void Marker(List<byte> bytes, int type, int count)
    {
        if (count < 0xF)
        {
            bytes.Add((byte)((type << 4) | count));
            return;
        }

        bytes.Add((byte)((type << 4) | 0xF));
        if (count <= byte.MaxValue)
        {
            bytes.AddRange([0x10, (byte)count]);
        }
        else
        {
            bytes.AddRange([0x11, (byte)(count >> 8), (byte)count]);
        }
    }
}

// The fixtures of --compose: notes, sources.json, a checksum file and an evidence README, in a folder that
// stands for the repository root, as the release workflow has them there and in build/.
internal static partial class SelfTest
{
    private const string Url = "https://example.com/fixture/Paneport";

    private const string ReleaseNotes = """
        # Paneport 0.1.0

        ## Summary

        <!-- release: build -->

        A fixture release, with its [record](../evidence/2026-12-01-x.md), its [issues](#known-issues), a
        [site](https://example.com/a), [`a file`](../../tools/README.md "Tools") and `[code](../x.md)`.

        ## Gates in scope

        | Phase | Gate | Verdict | Record |
        |---|---|---|---|
        | 0 | Fixture gate A | Pass | [2026-12-01-x.md](../evidence/2026-12-01-x.md) |
        | 0 | Fixture gate B | Waived | [2026-12-02-y.md](../evidence/2026-12-02-y.md#verdict) |

        ## Known issues

        - A fixture issue.

        ## Sources

        <!-- release: sources -->

        ## Checksums

        <!-- release: checksums -->

        ```text
        [fenced](../y.md)
        ```
        """;

    private const string EvidenceReadme = """
        # Evidence

        ## Gates

        | Phase | Gate | Verdict | Latest record |
        |---|---|---|---|
        | 0 | Fixture gate A | Pass | [2026-12-01-x.md](2026-12-01-x.md) |
        | 0 | Fixture gate B | Waived | [2026-12-02-y.md](2026-12-02-y.md) |
        | 1 | Fixture gate C | No record yet | |
        | 1 | Fixture gate D | Fail | [2026-12-03-z.md](2026-12-03-z.md) |

        ## Records

        | Record | Stage | Gate | Verdict |
        |---|---|---|---|
        | [2026-12-01-x.md](2026-12-01-x.md) | L1 Fixture | Fixture gate A | Pass |
        """;

    private const string Shown = "docs/releases/v0.1.0.md";

    private static readonly string s_commit = Hex("the tagged commit", 40);
    private static readonly string s_fork = Hex("the commit of the fork", 40);
    private static readonly string s_nested = Hex("the commit of a submodule of the fork", 40);
    private static readonly string s_skipped = Hex("the commit of a skipped submodule", 40);
    private static readonly string s_component = Hex("the commit of the component", 40);
    private static readonly string s_forkArchive = $"Paneport-wine-{s_fork[..12]}-source.tar.gz";

    private static void Composition(Tally tally, Fixture fixture)
    {
        var notes = Lf(ReleaseNotes);
        var (release, body) = Compose(fixture, Mode.Release, notes);
        var text = body ?? string.Empty;
        var blob = $"{Url}/blob/v0.1.0";
        var build =
            $"Paneport 0.1.0, build 42, built from the tag [`v0.1.0`]({Url}/tree/v0.1.0) at commit "
                + $"[`{s_commit[..12]}`]({Url}/tree/{s_commit}).";
        string[] table =
        [
            "| Source | Revision | Archive |",
            "|---|---|---|",
            $"| Paneport | [`v0.1.0`]({Url}/tree/v0.1.0) at [`{s_commit[..12]}`]({Url}/tree/{s_commit}) | "
                + "`Paneport-v0.1.0-source.tar.gz` |",
            $"| `external/Paneport-wine` | [`{s_fork[..12]}`](https://example.com/fixture/Paneport-wine/tree/"
                + $"{s_fork}) | `{s_forkArchive}` |",
            $"| `external/Paneport-wine/External/fmt` | [`{s_nested[..12]}`](https://example.com/upstream/fmt/tree/"
                + $"{s_nested}) | in `{s_forkArchive}` |",
            $"| `external/Paneport-wine/External/big` | [`{s_nested[..12]}`](https://example.com/upstream/big/tree/"
                + $"{s_nested}) | not archived: not checked out in the fork |",
            $"| `external/x` | [`{s_skipped[..12]}`](https://example.com/fixture/x/tree/{s_skipped}) | not archived: "
                + "update = none: never fetched or built |",
            $"| SampleKit | [`1.0.0-Beta.1`](https://example.com/upstream/SampleKit/tree/{s_component}) | "
                + "`SampleKit-1.0.0-Beta.1-source.tar.gz` |",
            string.Empty,
            "Every archive is attached to this release.",
        ];
        string[] checksums =
        [
            "```text",
            .. Assets(Mode.Release).Order(StringComparer.Ordinal).Select(name => $"{Hex(name, 64)}  {name}"),
            "```",
        ];
        string[] expected =
        [
            "# Paneport 0.1.0",
            string.Empty,
            "## Summary",
            string.Empty,
            build,
            string.Empty,
            $"A fixture release, with its [record]({blob}/docs/evidence/2026-12-01-x.md), its "
                + "[issues](#known-issues), a",
            $"[site](https://example.com/a), [`a file`]({blob}/tools/README.md \"Tools\") and `[code](../x.md)`.",
            string.Empty,
            "## Gates in scope",
            string.Empty,
            "| Phase | Gate | Verdict | Record |",
            "|---|---|---|---|",
            $"| 0 | Fixture gate A | Pass | [2026-12-01-x.md]({blob}/docs/evidence/2026-12-01-x.md) |",
            $"| 0 | Fixture gate B | Waived | [2026-12-02-y.md]({blob}/docs/evidence/2026-12-02-y.md#verdict) |",
            string.Empty,
            "## Known issues",
            string.Empty,
            "- A fixture issue.",
            string.Empty,
            "## Sources",
            string.Empty,
            .. table,
            string.Empty,
            "## Checksums",
            string.Empty,
            .. checksums,
            string.Empty,
            "```text",
            "[fenced](../y.md)",
            "```",
        ];
        var whole = string.Join('\n', expected) + "\n";
        tally.Expect(
            release.Code == 0 && release.Error.Length == 0 && text == whole,
            "release notes compose into the expected body",
            release.Code == 0 ? FirstDifference(whole, text) : release.Why);
        tally.Expect(
            text.Contains($"\n{build}\n", StringComparison.Ordinal),
            "the build marker becomes the version, the build number and links to the tag and the commit",
            FirstDifference(whole, text));
        tally.Expect(
            text.Contains(string.Join('\n', table), StringComparison.Ordinal),
            "the sources marker becomes a row for this repository, each submodule, each submodule in the archive of "
                + "a fork, each skipped one and each LGPL component, then the sentence on the archives",
            FirstDifference(whole, text));
        tally.Expect(
            text.Contains(string.Join('\n', checksums), StringComparison.Ordinal)
                && !text.Contains("<!-- release:", StringComparison.Ordinal),
            "the checksums marker becomes a text block of the checksum lines, sorted by file name",
            FirstDifference(whole, text));
        tally.Expect(
            text.Contains($"[record]({blob}/docs/evidence/2026-12-01-x.md)", StringComparison.Ordinal)
                && text.Contains($"({blob}/docs/evidence/2026-12-02-y.md#verdict)", StringComparison.Ordinal)
                && text.Contains($"[`a file`]({blob}/tools/README.md \"Tools\")", StringComparison.Ordinal),
            "a relative link becomes a link to the file at the tag, resolved from docs/releases/, its fragment kept",
            FirstDifference(whole, text));
        tally.Expect(
            text.Contains("[issues](#known-issues)", StringComparison.Ordinal)
                && text.Contains("[site](https://example.com/a)", StringComparison.Ordinal)
                && text.Contains("`[code](../x.md)`", StringComparison.Ordinal)
                && text.Contains("\n[fenced](../y.md)\n", StringComparison.Ordinal),
            "a link to a fragment, a link with a URL scheme, and links in a code span or a fenced block stay",
            FirstDifference(whole, text));
        var path = Path.Combine(fixture.Root, "compose", "build", "body.md");
        var bytes = File.Exists(path) ? File.ReadAllBytes(path) : [];
        tally.Expect(
            bytes.Length > 3 && bytes[0] != 0xEF && Array.IndexOf(bytes, (byte)'\r') < 0 && bytes[^1] == '\n'
                && bytes[^2] != '\n',
            "the body is UTF-8 without a byte-order mark, with LF line breaks and one final line break",
            $"{bytes.Length} bytes");
        tally.Expect(
            release.Code == 0 && !text.Contains("**Dry run.**", StringComparison.Ordinal),
            "a release body has no banner",
            release.Why);

        var (dryRun, draft) = Compose(fixture, Mode.DryRun, notes);
        var banner =
            "**Dry run.** This draft tests the release workflow on the tag `v0.0.0-dryrun.1`. It is never "
                + "published, and it is deleted after checking.";
        tally.Expect(
            dryRun.Code == 0
                && dryRun.Error.Length == 0
                && draft is not null
                && draft.StartsWith($"{banner}\n\n# Paneport 0.1.0\n", StringComparison.Ordinal)
                && draft.Contains(
                    $"({Url}/blob/v0.0.0-dryrun.1/docs/evidence/2026-12-01-x.md)",
                    StringComparison.Ordinal),
            "a dry run opens the body with its banner and a blank line, and links to the files at its tag",
            dryRun.Code == 0 ? draft ?? "no body" : dryRun.Why);

        Markers(tally, fixture, notes);
        ChecksumFiles(tally, fixture, notes);
        SourceFiles(tally, fixture, notes);
    }

    private static void Markers(Tally tally, Fixture fixture, string notes)
    {
        string[] markers = [Composer.BuildMarker, Composer.SourcesMarker, Composer.ChecksumsMarker];
        foreach (var marker in markers)
        {
            var without = notes.Replace(marker + "\n", string.Empty, StringComparison.Ordinal);
            Expect(tally, Compose(fixture, Mode.Release, without).Outcome, 1, $"a release without {marker} fails",
                $"{Shown}: the marker {marker} is missing");
        }

        // The Known issues heading is on line 17 of the notes.
        const string Issues = "## Known issues\n";
        string before(string inserted) => notes.Replace(Issues, $"{inserted}\n\n{Issues}", StringComparison.Ordinal);
        (string Name, string Text, string Named)[] cases =
        [
            (
                "a marker given twice",
                before(Composer.BuildMarker),
                $"{Shown}: the marker {Composer.BuildMarker} is on 2 lines (5, 17)"),
            (
                "another release marker",
                before("<!-- release: notices -->"),
                $"{Shown} line 17: a release marker other than"),
            (
                "a release marker in other letters",
                before("<!-- RELEASE: build -->"),
                $"{Shown} line 17: a release marker other than"),
            (
                "a marker inside a line",
                notes.Replace(Composer.BuildMarker, $"Built: {Composer.BuildMarker}", StringComparison.Ordinal),
                $"{Shown} line 5: a release marker other than"),
        ];
        foreach (var (name, text, named) in cases)
        {
            Expect(tally, Compose(fixture, Mode.Release, text).Outcome, 1, $"{name} fails", named);
        }

        var dryRun = notes.Replace(Composer.ChecksumsMarker + "\n", string.Empty, StringComparison.Ordinal);
        Expect(tally, Compose(fixture, Mode.DryRun, dryRun).Outcome, 1, "a missing marker fails a dry run too",
            $"{TagCheck.Template}: the marker {Composer.ChecksumsMarker} is missing");
    }

    private static void ChecksumFiles(Tally tally, Fixture fixture, string notes)
    {
        var good = ChecksumFile(Mode.Release);
        var first = good[..good.IndexOf('\n')];
        string change(string to) => good.Replace(first, to, StringComparison.Ordinal);
        (string Name, string Text, string Named)[] cases =
        [
            ("upper-case hexadecimal digits", change(first.ToUpperInvariant()), "line 1:"),
            ("one space", change(first.Replace("  ", " ", StringComparison.Ordinal)), "line 1:"),
            ("the asterisk of binary mode", change(first.Replace("  ", " *", StringComparison.Ordinal)), "line 1:"),
            ("a slash in the file name", good + $"{Hex("x", 64)}  assets/x.tar.gz\n", "line 6:"),
            ("a short hash", good + $"{Hex("x", 63)}  x.tar.gz\n", "line 6:"),
            ("CR LF line breaks", good.Replace("\n", "\r\n", StringComparison.Ordinal), "line 1:"),
            ("a blank line", good.Replace("\n", "\n\n", StringComparison.Ordinal), "line 2:"),
            ("a file given twice", good + first + "\n", "line 6: a second checksum of"),
        ];
        foreach (var (name, text, named) in cases)
        {
            Expect(tally, Compose(fixture, Mode.Release, notes, checksums: text).Outcome, 2,
                $"a checksum file with {name} cannot be read", $"build/SHA256SUMS {named}");
        }

        var missing = string.Concat(good.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.EndsWith(s_forkArchive, StringComparison.Ordinal))
            .Select(line => line + "\n"));
        Expect(tally, Compose(fixture, Mode.Release, notes, checksums: missing).Outcome, 1,
            "an archive of sources.json without a checksum fails",
            $"build/SHA256SUMS: no checksum of {s_forkArchive}");
    }

    private static void SourceFiles(Tally tally, Fixture fixture, string notes)
    {
        var json = SourcesJson(Mode.Release);
        string with(string from, string to) => json.Replace(from, to, StringComparison.Ordinal);
        (string Name, string Text, string Named)[] shapes =
        [
            (
                "an unknown key",
                with("\"format\": 1,", "\"format\": 1, \"extra\": 0,"),
                "the file has the unknown key \"extra\""),
            (
                "an unknown key in the repository",
                with("\"name\": \"Paneport\",", "\"name\": \"Paneport\", \"x\": 1,"),
                "repository has the unknown key \"x\""),
            (
                "a missing key",
                with("\"Paneport-v0.1.0-source.tar.gz\", \"public\": true", "\"Paneport-v0.1.0-source.tar.gz\""),
                "repository lacks the key \"public\""),
            (
                "a missing key in a submodule of a fork",
                with($", \"commit\": \"{s_nested}\" }}", " }"),
                "submodules[0].submodules[0] lacks the key \"commit\""),
            (
                "a skipped submodule with an archive",
                with("never fetched or built\"", "never fetched or built\", \"archive\": \"x.tar.gz\""),
                "submodules[1] has the unknown key \"archive\""),
            (
                "a URL with .git",
                with("https://example.com/fixture/x\"", "https://example.com/fixture/x.git\""),
                "submodules[1].url ends with .git"),
            (
                "an http URL",
                with("https://example.com/upstream/fmt", "http://example.com/upstream/fmt"),
                "submodules[0].submodules[0].url is not an https URL"),
            (
                "an upper-case commit",
                with(s_component, s_component.ToUpperInvariant()),
                "components[0].commit is not a commit hash"),
            (
                "a path with ..",
                with("\"path\": \"external/x\"", "\"path\": \"external/../x\""),
                "submodules[1].path is not a relative path"),
            (
                "public as a string",
                with("\"public\": true\n", "\"public\": \"yes\"\n"),
                "repository.public is neither true nor false"),
            ("another format", with("\"format\": 1,", "\"format\": 2,"), "format is not 1"),
            (
                "a key given twice",
                with("\"format\": 1,", "\"format\": 1, \"format\": 1,"),
                "the file gives the key \"format\" twice"),
            ("a trailing comma", with("\n  ]\n}", "\n  ],\n}"), "not valid JSON"),
        ];
        foreach (var (name, text, named) in shapes)
        {
            Expect(tally, Compose(fixture, Mode.Release, notes, sources: text).Outcome, 2,
                $"sources.json with {name} cannot be read", $"build/sources.json: {named}");
        }

        (string Name, string Text, string Named)[] mismatches =
        [
            ("the mode of a dry run", with("\"mode\": \"release\"", "\"mode\": \"dry-run\""), "the mode is dry-run"),
            ("another tag", with("\"tag\": \"v0.1.0\"", "\"tag\": \"v0.1.1\""), "the repository's tag is \"v0.1.1\""),
            ("another commit", with(s_commit, Hex("another commit", 40)), "the repository's commit is"),
        ];
        foreach (var (name, text, named) in mismatches)
        {
            Expect(tally, Compose(fixture, Mode.Release, notes, sources: text).Outcome, 1,
                $"sources.json with {name} fails", $"build/sources.json: {named}");
        }

        var folder = Path.Combine(fixture.Root, "compose");
        (string Option, string Value, string Label, string Named)[] arguments =
        [
            ("--mode", "dry-run", "the mode of a dry run", "--mode \"dry-run\": tag \"v0.1.0\" makes a release"),
            ("--version", "0.2.0", "another version", "--version \"0.2.0\": tag \"v0.1.0\" gives the version 0.1.0"),
            ("--tag", "v0.1.0+x", "build metadata", "--tag \"v0.1.0+x\""),
            ("--commit", s_commit.ToUpperInvariant(), "upper-case digits", "--commit \""),
            ("--build", "0", "zero", "--build \"0\""),
            ("--notes", "../outside.md", "a path outside the repository", "--notes \"../outside.md\": not a path"),
            (
                "--notes",
                Path.Combine(folder, "docs", "releases", "v0.1.0.md"),
                "an absolute path",
                "not a path from the repository root"),
        ];
        foreach (var (option, value, label, named) in arguments)
        {
            var args = Arguments(Mode.Release);
            args[Array.IndexOf(args, option) + 1] = value;
            Expect(tally, fixture.Invoke(folder, false, args), 2,
                $"--compose with {label} in {option} is a usage error", named);
        }
    }

    // Writes the fixtures into compose/, composes the body there and returns what the run printed and the body.
    private static (Outcome Outcome, string? Body) Compose(
        Fixture fixture,
        Mode mode,
        string notes,
        string? sources = null,
        string? checksums = null,
        bool actions = false)
    {
        var folder = Path.Combine(fixture.Root, "compose");
        Fixture.Write(folder, NotesOf(mode), notes);
        Fixture.Write(folder, "build/sources.json", sources ?? SourcesJson(mode));
        Fixture.Write(folder, "build/SHA256SUMS", checksums ?? ChecksumFile(mode));
        Fixture.Write(folder, "docs/evidence/README.md", Lf(EvidenceReadme));
        Fixture.Write(folder, "build/body.md", null);
        var outcome = fixture.Invoke(folder, actions, Arguments(mode));
        var body = Path.Combine(folder, "build", "body.md");
        return (outcome, File.Exists(body) ? Encoding.UTF8.GetString(File.ReadAllBytes(body)) : null);
    }

    private static string[] Arguments(Mode mode) =>
    [
        "--compose",
        "--tag",
        TagOf(mode),
        "--mode",
        Modes.Name(mode),
        "--version",
        mode == Mode.Release ? "0.1.0" : "0.0.0",
        "--build",
        "42",
        "--commit",
        s_commit,
        "--notes",
        NotesOf(mode),
        "--sources",
        "build/sources.json",
        "--checksums",
        "build/SHA256SUMS",
        "--out",
        "build/body.md",
    ];

    private static string TagOf(Mode mode) => mode == Mode.Release ? "v0.1.0" : "v0.0.0-dryrun.1";

    private static string NotesOf(Mode mode) => mode == Mode.Release ? Shown : TagCheck.Template;

    private static string SourcesJson(Mode mode) => Lf($$"""
        {
          "format": 1,
          "mode": "{{Modes.Name(mode)}}",
          "repository": {
            "name": "Paneport", "url": "{{Url}}", "tag": "{{TagOf(mode)}}", "commit": "{{s_commit}}",
            "archive": "Paneport-{{TagOf(mode)}}-source.tar.gz", "public": true
          },
          "submodules": [
            {
              "path": "external/Paneport-wine", "name": "Paneport-wine",
              "url": "https://example.com/fixture/Paneport-wine", "commit": "{{s_fork}}",
              "archive": "{{s_forkArchive}}", "public": true,
              "submodules": [
                { "path": "External/fmt", "url": "https://example.com/upstream/fmt", "commit": "{{s_nested}}" },
                {
                  "path": "External/big", "url": "https://example.com/upstream/big", "commit": "{{s_nested}}",
                  "skipped": "not checked out in the fork"
                }
              ]
            },
            {
              "path": "external/x", "name": "x", "url": "https://example.com/fixture/x", "commit": "{{s_skipped}}",
              "skipped": "update = none: never fetched or built"
            }
          ],
          "components": [
            {
              "name": "SampleKit", "url": "https://example.com/upstream/SampleKit", "tag": "1.0.0-Beta.1",
              "commit": "{{s_component}}", "archive": "SampleKit-1.0.0-Beta.1-source.tar.gz"
            }
          ]
        }
        """);

    // The files that the release attaches, the archives of sources.json among them.
    private static string[] Assets(Mode mode) =>
    [
        $"Paneport-{TagOf(mode)}-source.tar.gz",
        s_forkArchive,
        "SampleKit-1.0.0-Beta.1-source.tar.gz",
        "Paneport.ipa",
        "THIRD_PARTY_NOTICES.md",
    ];

    // The checksum file, in reverse order of the file names, so that the body shows them sorted.
    private static string ChecksumFile(Mode mode) =>
        string.Concat(
            Assets(mode).OrderDescending(StringComparer.Ordinal).Select(name => $"{Hex(name, 64)}  {name}\n"));

    // A hash computed from a made-up text, so that the source holds no value that looks like a secret.
    private static string Hex(string seed, int length) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(seed)))[..length];

    private static string Lf(string text) => text.ReplaceLineEndings("\n");

    private static string FirstDifference(string expected, string actual)
    {
        var wanted = expected.Split('\n');
        var got = actual.Split('\n');
        for (var index = 0; index < Math.Max(wanted.Length, got.Length); index++)
        {
            var left = index < wanted.Length ? wanted[index] : "(nothing)";
            var right = index < got.Length ? got[index] : "(nothing)";
            if (left != right)
            {
                return $"line {index + 1} is \"{right}\", and the self-test expects \"{left}\"";
            }
        }

        return "no difference";
    }
}

// The rules that a release fails on and a dry run only warns on, the relative links, the committed template of the
// notes, and the annotations of GitHub Actions.
internal static partial class SelfTest
{
    private const string RowA = "| 0 | Fixture gate A | Pass | [2026-12-01-x.md](../evidence/2026-12-01-x.md) |";

    private const string RowB =
        "| 0 | Fixture gate B | Waived | [2026-12-02-y.md](../evidence/2026-12-02-y.md#verdict) |";

    private const string RowC = "| 1 | Fixture gate C | No record yet | |";

    private static void Releases(Tally tally, Fixture fixture)
    {
        var notes = Lf(ReleaseNotes);
        string add(string row) => notes.Replace(RowB + "\n", $"{RowB}\n{row}\n", StringComparison.Ordinal);
        string change(string row) => notes.Replace(RowA, row, StringComparison.Ordinal);
        string with(string from, string to) => notes.Replace(from, to, StringComparison.Ordinal);
        (string Name, string Notes, (string From, string To)? Sources, string[] Named)[] cases =
        [
            (
                "a gate in scope without a record",
                add(RowC),
                null,
                ["line 16: gate \"Fixture gate C\" has the verdict \"No record yet\"", "needs Pass or Waived"]),
            (
                "a gate in scope that failed",
                add("| 1 | Fixture gate D | Fail | [2026-12-03-z.md](../evidence/2026-12-03-z.md) |"),
                null,
                ["line 16: gate \"Fixture gate D\" has the verdict \"Fail\""]),
            (
                "a gate that the Gates table lacks",
                add("| 1 | Fixture gate Z | Pass | [2026-12-04-w.md](../evidence/2026-12-04-w.md) |"),
                null,
                ["line 16: gate \"Fixture gate Z\" is not in the Gates table of docs/evidence/README.md"]),
            (
                "a verdict that differs from the Gates table",
                change(RowA.Replace("| Pass |", "| Waived |", StringComparison.Ordinal)),
                null,
                ["line 14: gate \"Fixture gate A\" has the verdict \"Waived\" in the notes and \"Pass\" in"]),
            (
                "a record link to another file",
                change(RowA.Replace("x.md)", "other.md)", StringComparison.Ordinal)),
                null,
                ["line 14: the record of gate \"Fixture gate A\" is 2026-12-01-other.md", "links to 2026-12-01-x.md"]),
            (
                "a record that is not a link",
                change("| 0 | Fixture gate A | Pass | 2026-12-01-x.md |"),
                null,
                ["line 14: the record of gate \"Fixture gate A\" is not a link"]),
            (
                "a table of gates in scope without a row",
                with($"{RowA}\n{RowB}\n", string.Empty),
                null,
                ["the Gates in scope table lists no gate"]),
            (
                "notes without a section of gates in scope",
                with("## Gates in scope\n", "## Gates\n"),
                null,
                ["no \"## Gates in scope\" section"]),
            (
                "an empty list of known issues",
                with("- A fixture issue.\n", "None so far.\n"),
                null,
                ["no \"## Known issues\" section that lists an item"]),
            (
                "notes without a section of known issues",
                with("## Known issues\n", "## Issues\n"),
                null,
                ["no \"## Known issues\" section that lists an item"]),
            (
                "a placeholder left in the notes",
                with("- A fixture issue.\n", "- A fixture issue.\n- <!-- fill: the other known issues -->\n"),
                null,
                ["line 20: a <!-- fill: ... --> placeholder remains"]),
            (
                "a repository that is not public",
                notes,
                ("\"public\": true\n", "\"public\": false\n"),
                ["build/sources.json: the repository Paneport is not public"]),
            (
                "a fork that is not public",
                notes,
                ("\"public\": true,", "\"public\": false,"),
                ["build/sources.json: the fork external/Paneport-wine is not public"]),
        ];
        foreach (var (name, text, sources, named) in cases)
        {
            var release = Compose(fixture, Mode.Release, text, Changed(Mode.Release, sources));
            tally.Expect(
                release.Outcome.Fails(1, [.. named.Select(part => part.StartsWith("line ", StringComparison.Ordinal)
                    ? $"{Shown} {part}"
                    : part)])
                    && release.Body is null,
                $"a release with {name} fails, names it and writes no body",
                release.Outcome.Why);
            var dryRun = Compose(fixture, Mode.DryRun, text, Changed(Mode.DryRun, sources));
            tally.Expect(
                dryRun.Outcome.Warns(named) && dryRun.Body is not null
                    && dryRun.Body.StartsWith("**Dry run.**", StringComparison.Ordinal),
                $"a dry run with {name} warns, names it and writes its body",
                dryRun.Outcome.Why);
        }

        var outside = with("[issues](#known-issues)", "[issues](../../../x.md)");
        Mode[] modes = [Mode.Release, Mode.DryRun];
        foreach (var mode in modes)
        {
            var (outcome, body) = Compose(fixture, mode, outside);
            tally.Expect(
                outcome.Fails(1, $"{NotesOf(mode)} line 7: the link target ../../../x.md leaves the repository")
                    && body is null,
                $"a {(mode == Mode.Release ? "release" : "dry run")} with a link that leaves the repository fails",
                outcome.Why);
        }

        var (root, linked) = Compose(fixture, Mode.Release, with("[issues](#known-issues)", "[root](../..)"));
        tally.Expect(
            root.Code == 0
                && linked is not null
                && linked.Contains($"[root]({Url}/tree/v0.1.0)", StringComparison.Ordinal),
            "a link to the repository root becomes a link to the tree of the tag",
            root.Why);
    }

    private static string? Changed(Mode mode, (string From, string To)? change) =>
        change is { } pair ? SourcesJson(mode).Replace(pair.From, pair.To, StringComparison.Ordinal) : null;

    // The template that a dry run takes its notes from, as the repository holds it; the self-test runs from the
    // repository root, as CI runs it.
    private static void Template(Tally tally, Fixture fixture)
    {
        var path = Path.GetFullPath(TagCheck.Template);
        if (!File.Exists(path))
        {
            tally.Skip("the template of the notes", $"{TagCheck.Template} is not under the current folder");
            return;
        }

        var template = File.ReadAllText(path);
        var (dryRun, body) = Compose(fixture, Mode.DryRun, template);
        var text = body ?? string.Empty;
        var checksum = $"```text\n{Hex($"Paneport-{TagOf(Mode.DryRun)}-source.tar.gz", 64)}  ";
        tally.Expect(
            dryRun.Code == 0
                && text.StartsWith("**Dry run.** This draft tests the release workflow", StringComparison.Ordinal)
                && text.Contains(
                    "\nPaneport 0.0.0, build 42, built from the tag [`v0.0.0-dryrun.1`]",
                    StringComparison.Ordinal)
                && text.Contains("\n| Source | Revision | Archive |\n", StringComparison.Ordinal)
                && text.Contains(checksum, StringComparison.Ordinal)
                && !text.Contains("<!-- release:", StringComparison.Ordinal),
            $"a dry run composes a body from {TagCheck.Template}, with each of its three markers replaced",
            dryRun.Code == 0 ? text : dryRun.Why);
        tally.Expect(
            dryRun.Warns("placeholder remains"),
            "the dry run warns on the placeholders of the template",
            dryRun.Why);
        var release = Compose(fixture, Mode.Release, template).Outcome;
        tally.Expect(
            release.Fails(1, $"{Shown} line ", "placeholder remains"),
            "the template as the notes of a release fails on its placeholders",
            release.Why);
    }

    private static void Annotations(Tally tally, Fixture fixture)
    {
        using var error = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var report = new Report(new Io(TextWriter.Null, error, true, fixture.Git, fixture.Root));
        report.Fail("50% of a\r\nb");
        report.Warn("c\nd");
        report.Print();
        var lines = error.ToString().Split('\n');
        const string Stop = "::stop-commands::";
        var token = lines[0].StartsWith(Stop, StringComparison.Ordinal) ? lines[0][Stop.Length..] : string.Empty;
        tally.Expect(
            lines.Length == 7
                && token.Length == 32
                && lines[1] == "error: 50% of a%0D%0Ab"
                && lines[2] == "warning: c%0Ad"
                && lines[3] == $"::{token}::"
                && lines[4] == "::error title=Release version::50%25 of a%0D%0Ab"
                && lines[5] == "::warning title=Release version::c%0Ad",
            "under GitHub Actions the plain lines stand between ::stop-commands:: and its token, then each failure "
                + "is an ::error and each warning a ::warning annotation, with %, CR and LF escaped",
            string.Join(" | ", lines));

        using var plain = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
        var quiet = new Report(new Io(TextWriter.Null, plain, false, fixture.Git, fixture.Root));
        quiet.Fail("x");
        quiet.Print();
        tally.Expect(
            plain.ToString() == "error: x\n",
            "outside GitHub Actions only the plain line is printed",
            plain.ToString());

        var gate = Lf(ReleaseNotes).Replace(RowB + "\n", $"{RowB}\n{RowC}\n", StringComparison.Ordinal);
        var failed = Compose(fixture, Mode.Release, gate, actions: true).Outcome;
        tally.Expect(
            failed.Code == 1
                && failed.Error.Contains(
                    $"::error title=Release version::{Shown} line 16: gate \"Fixture gate C\"",
                    StringComparison.Ordinal),
            "a release that breaks a rule under GitHub Actions annotates it as an error",
            failed.Why);
        var warned = Compose(fixture, Mode.DryRun, gate, actions: true).Outcome;
        tally.Expect(
            warned.Code == 0
                && warned.Error.Contains(
                    $"::warning title=Release version::{TagCheck.Template} line 16: gate \"Fixture gate C\"",
                    StringComparison.Ordinal),
            "a dry run under GitHub Actions annotates the same rule as a warning",
            warned.Why);
        var tag = fixture.Invoke(fixture.Root, true, "--tag", "v1.2");
        tally.Expect(
            tag.Code == 1
                && tag.Error.Contains("::error title=Release version::tag \"v1.2\"", StringComparison.Ordinal),
            "a refused tag under GitHub Actions is annotated, naming the tag",
            tag.Why);
    }
}
