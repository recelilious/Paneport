// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Makes the source archives that a release attaches beside its .ipa (S9.1.2; C-0009 section 6,
// C-0003 sections 3 and 4): one of this repository at the tag, and one of each fork that the tagged
// tree records as a submodule, at the recorded revision. A fork's archive also holds the files of
// the fork's own submodules at the revisions that the fork's tree records, up to three levels deep.
// A submodule marked update = none is never fetched and is listed as skipped. Every revision is
// fetched over https without credentials, so a release fails when a repository is private or a
// revision is not pushed. The archive of each LGPL component to which
// eng/release/bundle-components.json gives a source is made by eng/mac/make-source-archives.sh;
// this tool checks that it is in the output folder. Last, it writes the list of all the archives,
// from which the release notes link them.
//
//   dotnet run tools/ArchiveSources.cs -- --mode release|dry-run --tag <tag> --out <folder>
//       --list <sources.json> [--components eng/release/bundle-components.json]
//   dotnet run tools/ArchiveSources.cs -- --self-test   check every rule on generated repositories
//
// In dry-run mode, a tag or revision that cannot be read without credentials is a warning, and the
// archive of such a fork comes from its checked-out submodule. An archive holds each tree exactly as
// committed: git archive runs in a temporary bare repository with --worktree-attributes and apart
// from the user's configuration, so that neither .gitattributes nor the machine changes a path or
// a byte. After a failure nothing is written. Findings name repositories, paths, URLs and commits.
// Exit codes: 0 every archive is made, 1 a check failed, 2 usage or an input that cannot be read.

#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args is ["--self-test"])
{
    return SelfTest.Run();
}

return Cli.Run(
    args,
    Directory.GetCurrentDirectory(),
    allowFile: false,
    Console.Out,
    Console.Error,
    Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true");

internal enum Mode
{
    // The release workflow, at a pushed tag: what cannot be read without credentials fails.
    Release,

    // A trial of the workflow: what cannot be read without credentials is a warning.
    DryRun,
}

internal sealed record Options(Mode Mode, string Tag, string Out, string List, string? Components);

internal static class Arguments
{
    private static readonly string[] s_required = ["--mode", "--tag", "--out", "--list"];

    // The options of a run, or null and what is wrong with the arguments.
    public static Options? Parse(IReadOnlyList<string> args, out string problem)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < args.Count; index += 2)
        {
            var name = args[index];
            if (Array.IndexOf(s_required, name) < 0 && name != "--components")
            {
                problem = $"unknown argument {name}";
                return null;
            }

            if (index + 1 == args.Count || args[index + 1].Length == 0)
            {
                problem = $"{name} needs a value";
                return null;
            }

            if (!values.TryAdd(name, args[index + 1]))
            {
                problem = $"{name} is given twice";
                return null;
            }
        }

        if (s_required.FirstOrDefault(name => !values.ContainsKey(name)) is { } missing)
        {
            problem = $"{missing} is missing";
            return null;
        }

        Mode? mode = values["--mode"] switch
        {
            "release" => Mode.Release,
            "dry-run" => Mode.DryRun,
            _ => null,
        };
        if (mode is not { } chosen)
        {
            problem = $"the mode {values["--mode"]} is neither release nor dry-run";
            return null;
        }

        var tag = values["--tag"];
        if (!Names.IsPlain(tag))
        {
            problem = $"the tag {tag} cannot name an archive, which takes letters, digits, '.', '_', '+' and '-'";
            return null;
        }

        problem = string.Empty;
        return new Options(chosen, tag, values["--out"], values["--list"], values.GetValueOrDefault("--components"));
    }
}

internal static class Names
{
    // A name that can stand in a file name and in a folder of a tar: ASCII letters, digits, '.', '_', '+'
    // and '-', starting with a letter or a digit.
    public static bool IsPlain(string name) =>
        name.Length > 0
            && char.IsAsciiLetterOrDigit(name[0])
            && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '+' or '-');

    // A full commit ID as git prints it.
    public static bool IsCommit(string text) => text.Length == 40 && text.All(char.IsAsciiHexDigitLower);
}

internal static class Urls
{
    // Why the tool does not fetch from the URL, or null when it does: an https URL with a host and without a
    // user, a query or a fragment, and with allowFile, which only the self-test sets, a file URL too.
    public static string? Problem(string url, bool allowFile)
    {
        var https = url.StartsWith("https://", StringComparison.Ordinal);
        if (!https && !(allowFile && url.StartsWith("file:///", StringComparison.Ordinal)))
        {
            return "is not an https URL";
        }

        if (url.Any(character => char.IsWhiteSpace(character) || char.IsControl(character))
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return "is not a valid URL";
        }

        var plain = uri.Host.Length > 0
            && uri.UserInfo.Length == 0
            && uri.Query.Length == 0
            && uri.Fragment.Length == 0;
        return https && !plain ? "is not a plain https URL: it names no host, or a user, a query or a fragment" : null;
    }

    // The URL as the list gives it: without a trailing slash or .git.
    public static string Normalize(string url)
    {
        var trimmed = url.TrimEnd('/');
        return trimmed.EndsWith(".git", StringComparison.Ordinal) ? trimmed[..^4].TrimEnd('/') : trimmed;
    }

    // A relative submodule URL, one that starts with ./ or ../, resolved as git resolves it against the URL of
    // the repository that records the submodule: each ../ removes the last part of that URL's path. Null when
    // it would go above the host. Any other URL stays as it is.
    public static string? Resolve(string baseUrl, string url)
    {
        if (!url.StartsWith("./", StringComparison.Ordinal) && !url.StartsWith("../", StringComparison.Ordinal))
        {
            return url;
        }

        var scheme = baseUrl.IndexOf("://", StringComparison.Ordinal);
        var current = baseUrl.EndsWith('/') ? baseUrl[..^1] : baseUrl;
        var path = scheme < 0 ? -1 : current.IndexOf('/', scheme + 3);
        var rest = url;
        while (true)
        {
            if (rest.StartsWith("../", StringComparison.Ordinal))
            {
                var slash = current.LastIndexOf('/');
                if (path < 0 || slash < path)
                {
                    return null;
                }

                current = current[..slash];
                rest = rest[3..];
            }
            else if (rest.StartsWith("./", StringComparison.Ordinal))
            {
                rest = rest[2..];
            }
            else
            {
                break;
            }
        }

        var resolved = $"{current}/{rest}";
        return resolved.EndsWith('/') ? resolved[..^1] : resolved;
    }
}

internal sealed class Findings(Mode mode)
{
    public List<string> Failures { get; } = [];

    public List<string> Warnings { get; } = [];

    public void Fail(string message) => Failures.Add(message);

    // What a release cannot have: a failure in release mode, and a warning in a dry run.
    public void Problem(string message) => (mode == Mode.Release ? Failures : Warnings).Add(message);
}

internal static class Report
{
    public const string Title = "Source archives";

    // Prints the lines, the warnings, the failures and the closing line, and under GitHub Actions an annotation
    // of each warning and failure after them.
    public static void Print(
        TextWriter output,
        bool actions,
        IEnumerable<string> lines,
        Findings findings,
        string closing)
    {
        Untrusted(
            output,
            actions,
            [
                .. lines,
                .. findings.Warnings.Select(warning => $"warning: {warning}"),
                .. findings.Failures.Select(failure => $"error: {failure}"),
                closing,
            ]);
        if (!actions)
        {
            return;
        }

        foreach (var warning in findings.Warnings)
        {
            output.WriteLine($"::warning title={Title}::{Data(warning)}");
        }

        foreach (var failure in findings.Failures)
        {
            output.WriteLine($"::error title={Title}::{Data(failure)}");
        }
    }

    // Lines that may hold what a remote or a .gitmodules gives, such as a URL, a path or git's own message.
    // Under GitHub Actions they are printed between ::stop-commands::<token> and ::<token>::, with a new
    // random token in each run, so that none of them runs a workflow command.
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

internal static class Cli
{
    private const string Usage =
        "usage: dotnet run tools/ArchiveSources.cs -- --mode release|dry-run --tag <tag> --out <folder> "
            + "--list <sources.json> [--components <bundle-components.json>]";

    // Runs the tool for the git repository that holds the directory. allowFile lets the self-test fetch from
    // file:// URLs, which a run of the tool never does.
    public static int Run(
        IReadOnlyList<string> args,
        string directory,
        bool allowFile,
        TextWriter output,
        TextWriter error,
        bool actions)
    {
        if (Arguments.Parse(args, out var problem) is not { } options)
        {
            error.WriteLine($"error: {problem}");
            error.WriteLine(Usage);
            return 2;
        }

        try
        {
            return Make(options, directory, allowFile, output, actions);
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Report.Untrusted(error, actions, [$"error: {exception.Message}"]);
            if (actions)
            {
                error.WriteLine($"::error title={Report.Title}::{Report.Data(exception.Message)}");
            }

            return 2;
        }
    }

    private static int Make(Options options, string directory, bool allowFile, TextWriter output, bool actions)
    {
        using var workspace = new Workspace(allowFile);
        var git = workspace.Git;
        var top = git.Run(new Repository(directory, Temporary: false), "rev-parse", "--show-toplevel");
        if (!top.Ok)
        {
            throw new InvalidDataException($"the current folder is not in a git repository: {top.Reason}");
        }

        var root = new Repository(Path.GetFullPath(top.Text.Trim()), Temporary: false);
        var mapping = options.Components is { } given
            ? Path.GetFullPath(given, directory)
            : Path.GetFullPath(Path.Combine(root.Folder, Mapping.DefaultPath));
        var components = Mapping.Read(mapping, options.Components ?? Mapping.DefaultPath);
        var outFolder = Path.GetFullPath(options.Out, directory);
        var findings = new Findings(options.Mode);
        var plan = new Planner(workspace, findings, options.Mode)
            .Make(root, options.Tag, components, outFolder, options.Out);
        var mode = options.Mode == Mode.Release ? "release" : "dry-run";
        var heading = $"Source archives of {options.Tag}, in {mode} mode:";
        if (plan is null || findings.Failures.Count > 0)
        {
            var counts = $"{Count(findings.Failures.Count, "failure")} and {Count(findings.Warnings.Count, "warning")}";
            Report.Print(output, actions, [heading], findings, $"{counts}; nothing was written.");
            return 1;
        }

        Directory.CreateDirectory(outFolder);
        var lines = new List<string> { heading };
        void write(string archive, IReadOnlyList<Tree> trees, string what)
        {
            var size = Archives.Write(git, Path.Combine(outFolder, archive), trees);
            lines.Add($"  {archive}: {Size(size)}, {what}");
        }

        write(plan.Archive, [plan.Tree], $"this repository at {plan.Commit}{(plan.Public ? "" : ", not public")}");
        var skipped = 0;
        foreach (var fork in plan.Submodules)
        {
            skipped += fork.Submodules.Count(nested => nested.Skipped is not null);
            if (fork.Archive is null)
            {
                skipped++;
                lines.Add($"  {fork.Path} at {fork.Commit}: skipped, {fork.Skipped}");
                continue;
            }

            var from = fork.Public ? $"fetched from {fork.Url}" : $"from the checkout at {fork.Path}, not public";
            var nested = Count(fork.Trees.Count - 1, "nested submodule");
            write(fork.Archive, fork.Trees, $"{fork.Path} at {fork.Commit}, {from}, with {nested}");
        }

        foreach (var component in plan.Components)
        {
            var size = new FileInfo(Path.Combine(outFolder, component.Archive)).Length;
            lines.Add(
                $"  {component.Archive}: {Size(size)}, the component {component.Name} at {component.Commit}, made by "
                    + Mapping.Script);
        }

        var list = Path.GetFullPath(options.List, directory);
        Directory.CreateDirectory(Path.GetDirectoryName(list) ?? outFolder);
        File.WriteAllBytes(list, SourceList.Json(plan, options.Mode));
        var archives = lines.Count - 1 - plan.Submodules.Count(fork => fork.Archive is null);
        Report.Print(
            output,
            actions,
            lines,
            findings,
            $"Wrote {options.List}: {Count(archives, "archive")} in {options.Out}, "
                + $"{Count(skipped, "skipped submodule")}, {Count(findings.Warnings.Count, "warning")}.");
        return 0;
    }

    private static string Count(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

    private static string Size(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes:N0} bytes");
}

// A revision that goes into an archive under the folder that Prefix names, and the temporary repository that
// holds it.
internal sealed record Tree(Repository Repository, string Commit, string Prefix);

// A submodule of a fork as the list gives it: its path in the fork, its URL and recorded commit, and why it is
// not in the fork's archive when it is not.
internal sealed record Nested(string Path, string Url, string Commit, string? Skipped);

// A submodule of this repository, a fork. Skipped says why it has no archive; otherwise its archive holds its
// trees in order, the fork's own first, and Public says whether its revision was fetched without credentials.
internal sealed record Fork(
    string Path,
    string Name,
    string Url,
    string Commit,
    string? Skipped,
    string? Archive,
    bool Public,
    IReadOnlyList<Nested> Submodules,
    IReadOnlyList<Tree> Trees);

internal sealed record Plan(
    string Url,
    string Tag,
    string Commit,
    string Archive,
    bool Public,
    Tree Tree,
    IReadOnlyList<Fork> Submodules,
    IReadOnlyList<ComponentSource> Components);

internal sealed class Planner(Workspace workspace, Findings findings, Mode mode)
{
    public const string Name = "Paneport";

    public const string UpdateNone = "update = none: never fetched or built";

    // How many levels of submodules a fork's archive holds; the fork's own submodules are the first.
    public const int Levels = 3;

    private Git Git => workspace.Git;

    // What the archives of the tag hold, or null after a failure that leaves nothing to check.
    public Plan? Make(
        Repository root,
        string tag,
        IReadOnlyList<ComponentSource> components,
        string outFolder,
        string shownOut)
    {
        var resolved = Git.Run(root, "rev-parse", "--verify", "--quiet", $"refs/tags/{tag}^{{commit}}");
        var commit = resolved.Text.Trim();
        if (!resolved.Ok || !Names.IsCommit(commit))
        {
            findings.Fail($"the tag {tag} does not resolve to a commit in this repository");
            return null;
        }

        var origin = Git.Run(root, "remote", "get-url", "origin");
        var url = origin.Text.Trim();
        if (!origin.Ok || url.Length == 0)
        {
            findings.Fail("this repository has no remote named origin, so its public URL is not known");
            return null;
        }

        if (Urls.Problem(url, Git.AllowFile) is { } problem)
        {
            findings.Fail($"the URL of the remote origin, {url}, {problem}");
            return null;
        }

        var plan = new Plan(
            url,
            tag,
            commit,
            $"{Name}-{tag}-source.tar.gz",
            Pushed(url, tag, commit),
            new Tree(workspace.Borrow(root), commit, $"{Name}-{tag}/"),
            Forks(root, url, commit),
            components);
        Check(components, outFolder, shownOut);
        Unique(plan);
        return plan;
    }

    private static string CannotFetch(string commit, string url) =>
        $"the revision {commit} cannot be fetched from {url} without credentials: the repository is private, or "
            + "the commit is not on it";

    // Whether the remote shows the tag at the commit to anyone who asks without credentials.
    private bool Pushed(string url, string tag, string commit)
    {
        var tagged = $"refs/tags/{tag}";
        var peeled = $"{tagged}^{{}}";
        var listed = Git.Network(workspace.Probe(), url, "ls-remote", url, tagged, peeled);
        string? direct = null;
        string? target = null;
        foreach (var line in listed.Ok ? listed.Text.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [])
        {
            var fields = line.TrimEnd('\r').Split('\t');
            if (fields.Length == 2 && fields[1] == tagged)
            {
                direct = fields[0];
            }
            else if (fields.Length == 2 && fields[1] == peeled)
            {
                target = fields[0];
            }
        }

        // An annotated tag is listed with the commit that it points to, as <tag>^{}.
        var remote = target ?? direct;
        if (remote == commit)
        {
            return true;
        }

        var found = !listed.Ok
            ? $"it cannot be read: {listed.Reason}"
            : remote is null ? "it has no such tag" : $"it has the tag at {remote}";
        findings.Problem(
            $"{url} does not show the tag {tag} at {commit} to anyone without credentials ({found}): the repository "
                + "is not public, or the tag is not pushed");
        return false;
    }

    private List<Fork> Forks(Repository root, string url, string commit)
    {
        var forks = new List<Fork>();
        foreach (var module in Submodules.Read(Git, root, commit, string.Empty, findings))
        {
            var name = module.Path[(module.Path.LastIndexOf('/') + 1)..];
            if (Resolve(module.Path, url, module.Url) is not { } resolved)
            {
                continue;
            }

            var shown = Urls.Normalize(resolved);
            if (module.UpdateNone)
            {
                forks.Add(new Fork(module.Path, name, shown, module.Commit, UpdateNone, null, false, [], []));
                continue;
            }

            if (!Names.IsPlain(name))
            {
                findings.Fail(
                    $"{module.Path}: the folder name {name} cannot name an archive, which takes letters, digits, "
                        + "'.', '_', '+' and '-'");
                continue;
            }

            var repository = Fetch(resolved, module.Commit, out var reason);
            var fetched = repository is not null;
            if (repository is null)
            {
                findings.Problem($"{module.Path}: {CannotFetch(module.Commit, resolved)} ({reason})");
                if (mode == Mode.Release)
                {
                    continue;
                }

                repository = Checkout(root, module.Path, module.Commit);
                if (repository is null)
                {
                    findings.Fail(
                        $"{module.Path}: the revision {module.Commit} is not in a checked-out submodule at "
                            + $"{module.Path} either, so this dry run cannot archive it");
                    continue;
                }
            }

            var contents = new Contents($"{module.Path}/", $"{name}-{module.Commit[..12]}");
            contents.Trees.Add(new Tree(repository, module.Commit, $"{contents.Top}/"));
            Nest(contents, repository, module.Commit, resolved, string.Empty, 1);
            forks.Add(new Fork(
                module.Path,
                name,
                shown,
                module.Commit,
                null,
                $"{contents.Top}-source.tar.gz",
                fetched,
                contents.Submodules,
                contents.Trees));
        }

        return forks;
    }

    // Adds the submodules of a tree in the fork, depth first in the order of each .gitmodules: each one to the
    // fork's list, and the tree of each one that it archives to the fork's archive.
    private void Nest(Contents contents, Repository repository, string commit, string url, string within, int level)
    {
        foreach (var module in Submodules.Read(Git, repository, commit, $"{contents.Fork}{within}", findings))
        {
            var path = $"{within}{module.Path}";
            var where = $"{contents.Fork}{path}";
            if (Resolve(where, url, module.Url) is not { } resolved)
            {
                continue;
            }

            Repository? fetched = null;
            string? skipped = null;
            if (module.UpdateNone)
            {
                skipped = UpdateNone;
            }
            else if (level > Levels)
            {
                skipped = $"nested more than {Levels} levels deep in the fork";
                findings.Problem($"{where}: {skipped}, so its files are not archived");
            }
            else
            {
                fetched = Fetch(resolved, module.Commit, out var reason);
                if (fetched is null)
                {
                    skipped = CannotFetch(module.Commit, resolved);
                    findings.Problem($"{where}: {skipped} ({reason})");
                }
            }

            contents.Submodules.Add(new Nested(path, Urls.Normalize(resolved), module.Commit, skipped));
            if (fetched is not null)
            {
                contents.Trees.Add(new Tree(fetched, module.Commit, $"{contents.Top}/{path}/"));
                Nest(contents, fetched, module.Commit, resolved, $"{path}/", level + 1);
            }
        }
    }

    // The URL of a submodule, resolved against the URL of the repository that records it, or null after a
    // failure when it is not one that the tool fetches from.
    private string? Resolve(string where, string baseUrl, string url)
    {
        if (Urls.Resolve(baseUrl, url) is not { } resolved)
        {
            findings.Fail($"{where}: the relative URL {url} cannot be resolved against {baseUrl}");
            return null;
        }

        if (Urls.Problem(resolved, Git.AllowFile) is { } problem)
        {
            findings.Fail($"{where}: the URL {resolved} {problem}");
            return null;
        }

        return resolved;
    }

    // A new temporary repository with the commit fetched from the URL without credentials, or null and why not.
    private Repository? Fetch(string url, string commit, out string reason)
    {
        var repository = workspace.Create();
        var fetched = Git.Network(repository, url, "fetch", "--depth", "1", "--no-tags", url, commit);
        if (!fetched.Ok)
        {
            reason = fetched.Reason;
            return null;
        }

        if (!Git.Run(repository, "cat-file", "-e", $"{commit}^{{commit}}").Ok)
        {
            reason = "the fetch did not bring the commit";
            return null;
        }

        reason = string.Empty;
        return repository;
    }

    // In a dry run: the job's own checkout of the submodule, when it holds the revision.
    private Repository? Checkout(Repository root, string path, string commit)
    {
        var folder = Path.Combine(root.Folder, path);
        var marker = Path.Combine(folder, ".git");
        var checkout = new Repository(folder, Temporary: false);
        return (File.Exists(marker) || Directory.Exists(marker))
            && Git.Run(checkout, "cat-file", "-e", $"{commit}^{{commit}}").Ok
                ? workspace.Borrow(checkout)
                : null;
    }

    // The archive of each component, which eng/mac/make-source-archives.sh makes before this tool runs.
    private void Check(IReadOnlyList<ComponentSource> components, string outFolder, string shownOut)
    {
        foreach (var component in components)
        {
            if (Urls.Problem(component.Repository, allowFile: false) is { } problem)
            {
                findings.Fail($"the component {component.Name}: its repository URL {component.Repository} {problem}");
            }

            if (!File.Exists(Path.Combine(outFolder, component.Archive)))
            {
                findings.Fail(
                    $"the component {component.Name}: its source archive {component.Archive} is not in {shownOut}; "
                        + $"{Mapping.Script} makes it before this tool runs");
            }
        }
    }

    // Two archives of one name would overwrite each other, also on a file system that ignores letter case.
    private void Unique(Plan plan)
    {
        var owners = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void claim(string archive, string owner)
        {
            if (!owners.TryAdd(archive, owner))
            {
                findings.Fail($"{owners[archive]} and {owner} would have the same archive, {archive}");
            }
        }

        claim(plan.Archive, "this repository");
        foreach (var fork in plan.Submodules)
        {
            if (fork.Archive is not null)
            {
                claim(fork.Archive, fork.Path);
            }
        }

        foreach (var component in plan.Components)
        {
            claim(component.Archive, $"the component {component.Name}");
        }
    }

    // What goes into the archive of a fork: its trees in order, the fork's own first, and the list of its
    // submodules. Fork is the fork's path with a slash, and Top the archive's top folder.
    private sealed class Contents(string fork, string top)
    {
        public string Fork { get; } = fork;

        public string Top { get; } = top;

        public List<Nested> Submodules { get; } = [];

        public List<Tree> Trees { get; } = [];
    }
}

// A submodule that a tree records: the gitlink at its path, with what .gitmodules gives it.
internal sealed record Submodule(string Name, string Path, string Url, bool UpdateNone, string Commit);

internal static class Submodules
{
    private const string Section = "submodule.";

    // The submodules that the tree of the commit records, in the order of its .gitmodules. Shown is the path of
    // the repository in this one, with a slash, or empty for this repository. A gitlink that .gitmodules does not
    // name, or names without a URL, fails, because its source cannot be fetched.
    public static List<Submodule> Read(Git git, Repository repository, string commit, string shown, Findings findings)
    {
        var where = shown.Length == 0 ? "this repository" : shown.TrimEnd('/');
        var links = new Dictionary<string, string>(StringComparer.Ordinal);
        string? gitmodules = null;
        var tree = git.Run(repository, "ls-tree", "-r", "-z", commit).Require($"{where}: the tree of {commit}");
        foreach (var record in tree.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            string[] fields = tab < 0 ? [] : record[..tab].Split(' ');
            if (fields.Length != 3)
            {
                throw new InvalidDataException($"{where}: git ls-tree printed a line that the tool does not read");
            }

            var path = record[(tab + 1)..];
            if (fields[0] == "160000")
            {
                links[path] = fields[2];
            }
            else if (path == ".gitmodules" && fields[0] is "100644" or "100755")
            {
                gitmodules = fields[2];
            }
        }

        var modules = new List<Submodule>();
        if (links.Count == 0)
        {
            return modules;
        }

        foreach (var entry in gitmodules is null ? [] : Entries(git, repository, gitmodules, where))
        {
            if (entry.Path is null || !links.Remove(entry.Path, out var linked))
            {
                continue;
            }

            if (entry.Url is null)
            {
                findings.Fail($"{shown}{entry.Path}: .gitmodules gives the submodule {entry.Name} no url");
                continue;
            }

            if (!Names.IsCommit(linked))
            {
                throw new InvalidDataException($"{shown}{entry.Path}: git ls-tree printed an unexpected commit ID");
            }

            modules.Add(new Submodule(entry.Name, entry.Path, entry.Url, entry.Update == "none", linked));
        }

        foreach (var path in links.Keys.Order(StringComparer.Ordinal))
        {
            findings.Fail(
                $"{shown}{path}: the tree records a submodule there that .gitmodules does not name, so its source "
                    + "cannot be fetched");
        }

        return modules;
    }

    // The entries of a .gitmodules in the order in which it names them.
    private static List<Entry> Entries(Git git, Repository repository, string blob, string where)
    {
        var listing = git.Run(repository, "config", "--blob", blob, "-z", "--list").Require($"{where}: .gitmodules");
        var entries = new List<Entry>();
        var named = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (var record in listing.Text.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var newline = record.IndexOf('\n');
            var key = newline < 0 ? record : record[..newline];
            var value = newline < 0 ? null : record[(newline + 1)..];
            var dot = key.LastIndexOf('.');
            if (!key.StartsWith(Section, StringComparison.Ordinal) || dot <= Section.Length)
            {
                continue;
            }

            var name = key[Section.Length..dot];
            if (!named.TryGetValue(name, out var entry))
            {
                entry = new Entry(name);
                named.Add(name, entry);
                entries.Add(entry);
            }

            switch (key[(dot + 1)..])
            {
                case "path":
                    entry.Path = value?.TrimEnd('/');
                    break;
                case "url":
                    entry.Url = value;
                    break;
                case "update":
                    entry.Update = value;
                    break;
            }
        }

        return entries;
    }

    private sealed class Entry(string name)
    {
        public string Name { get; } = name;

        public string? Path { get; set; }

        public string? Url { get; set; }

        public string? Update { get; set; }
    }
}

internal static class Archives
{
    // Writes the trees, in order, as one gzip-compressed tar at the path, which it replaces only once the
    // archive is complete. One tree is git archive's own tar. Several are merged: the pax global header of
    // the first, whose comment names its commit, as git wrote it, then every entry of each tree with its name,
    // mode and time, a folder only once. git archive writes an extended header only for a name or a link too
    // long for a ustar header; such an entry is written as a GNU entry, because .NET names a pax extended
    // header after its process ID, and its own global header after the temporary folder, which would make the
    // archive differ from run to run and name a local folder.
    public static long Write(Git git, string path, IReadOnlyList<Tree> trees)
    {
        var partial = $"{path}.partial";
        try
        {
            using (var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                using var gzip = new GZipStream(file, CompressionLevel.Optimal);
                if (trees.Count == 1)
                {
                    Archive(git, trees[0], stream => stream.CopyTo(gzip));
                }
                else
                {
                    Merge(git, trees, gzip);
                }
            }

            File.Move(partial, path, overwrite: true);
            return new FileInfo(path).Length;
        }
        finally
        {
            File.Delete(partial);
        }
    }

    private static void Archive(Git git, Tree tree, Action<Stream> consume) =>
        git.Archive(tree, consume).Require($"git archive of {tree.Commit}");

    private static void Merge(Git git, IReadOnlyList<Tree> trees, Stream output)
    {
        using var writer = new TarWriter(output, TarEntryFormat.Gnu, leaveOpen: true);
        var folders = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < trees.Count; index++)
        {
            var outermost = index == 0;
            Archive(
                git,
                trees[index],
                stream =>
                {
                    GlobalHeader(stream, outermost ? output : null);
                    using var reader = new TarReader(stream, leaveOpen: true);

                    // TarWriter takes the size of an entry from its data when it writes into a stream that it
                    // cannot seek, such as the gzip stream, so the reader copies the data of each entry.
                    while (reader.GetNextEntry(copyData: true) is { } entry)
                    {
                        if (entry.EntryType == TarEntryType.GlobalExtendedAttributes
                            || (entry.EntryType == TarEntryType.Directory && !folders.Add(entry.Name)))
                        {
                            continue;
                        }

                        writer.WriteEntry(entry is PaxTarEntry ? new GnuTarEntry(entry) : entry);
                    }
                });
        }
    }

    // Copies the pax global header that git archive starts with to output, as git wrote it, or reads past it.
    private static void GlobalHeader(Stream input, Stream? output)
    {
        var header = new byte[512];
        input.ReadExactly(header);
        if (header[156] != (byte)'g')
        {
            throw new InvalidDataException("git archive did not start with a pax global header");
        }

        var size = 0;
        foreach (var digit in header.AsSpan(124, 12))
        {
            if (digit is >= (byte)'0' and <= (byte)'7' && size < 1 << 16)
            {
                size = (size * 8) + (digit - '0');
            }
            else if (digit is not (byte)' ' and not 0)
            {
                throw new InvalidDataException("git archive wrote a pax global header that the tool does not read");
            }
        }

        var data = new byte[(size + 511) / 512 * 512];
        input.ReadExactly(data);
        output?.Write(header);
        output?.Write(data);
    }
}

// Writes the list of the archives, which tools/ReleaseVersion.cs reads: format 1, UTF-8 without a byte-order
// mark, with LF line ends.
internal static class SourceList
{
    public static byte[] Json(Plan plan, Mode mode)
    {
        var options = new JsonWriterOptions
        {
            Indented = true,
            NewLine = "\n",
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, options))
        {
            json.WriteStartObject();
            json.WriteNumber("format", 1);
            json.WriteString("mode", mode == Mode.Release ? "release" : "dry-run");
            json.WriteStartObject("repository");
            json.WriteString("name", Planner.Name);
            json.WriteString("url", Urls.Normalize(plan.Url));
            json.WriteString("tag", plan.Tag);
            json.WriteString("commit", plan.Commit);
            json.WriteString("archive", plan.Archive);
            json.WriteBoolean("public", plan.Public);
            json.WriteEndObject();
            json.WriteStartArray("submodules");
            foreach (var fork in plan.Submodules)
            {
                Write(json, fork);
            }

            json.WriteEndArray();
            json.WriteStartArray("components");
            foreach (var component in plan.Components)
            {
                json.WriteStartObject();
                json.WriteString("name", component.Name);
                json.WriteString("url", Urls.Normalize(component.Repository));
                json.WriteString("tag", component.Tag);
                json.WriteString("commit", component.Commit);
                json.WriteString("archive", component.Archive);
                json.WriteEndObject();
            }

            json.WriteEndArray();
            json.WriteEndObject();
        }

        stream.WriteByte((byte)'\n');
        return stream.ToArray();
    }

    private static void Write(Utf8JsonWriter json, Fork fork)
    {
        json.WriteStartObject();
        json.WriteString("path", fork.Path);
        json.WriteString("name", fork.Name);
        json.WriteString("url", fork.Url);
        json.WriteString("commit", fork.Commit);
        if (fork.Archive is null)
        {
            json.WriteString("skipped", fork.Skipped);
            json.WriteEndObject();
            return;
        }

        json.WriteString("archive", fork.Archive);
        json.WriteBoolean("public", fork.Public);
        json.WriteStartArray("submodules");
        foreach (var nested in fork.Submodules)
        {
            json.WriteStartObject();
            json.WriteString("path", nested.Path);
            json.WriteString("url", nested.Url);
            json.WriteString("commit", nested.Commit);
            if (nested.Skipped is not null)
            {
                json.WriteString("skipped", nested.Skipped);
            }

            json.WriteEndObject();
        }

        json.WriteEndArray();
        json.WriteEndObject();
    }
}

// The source of an LGPL component that is neither this repository nor a fork (C-0003 section 3).
internal sealed record ComponentSource(string Name, string Repository, string Tag, string Commit, string Archive);

internal static class Mapping
{
    public const string DefaultPath = "eng/release/bundle-components.json";

    public const string Script = "eng/mac/make-source-archives.sh";

    // Reads the repository, tag, commit and archive of each components.<name>.source of the mapping, and
    // nothing else of it, so that the rest, which tools/GenerateNotices.cs reads, may change without this tool.
    public static List<ComponentSource> Read(string path, string shown)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var document = JsonDocument.Parse(stream);
            return Sources(document.RootElement, shown);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{shown} is not valid JSON: {exception.Message}", exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException($"{shown} cannot be read: {exception.Message}", exception);
        }
    }

    private static List<ComponentSource> Sources(JsonElement root, string shown)
    {
        var sources = new List<ComponentSource>();
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{shown} does not hold a JSON object");
        }

        if (!root.TryGetProperty("components", out var components))
        {
            return sources;
        }

        if (components.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException($"{shown}: components is not an object");
        }

        foreach (var component in components.EnumerateObject())
        {
            if (component.Value.ValueKind != JsonValueKind.Object
                || !component.Value.TryGetProperty("source", out var source))
            {
                continue;
            }

            string text(string name) =>
                source.ValueKind == JsonValueKind.Object
                    && source.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && value.GetString() is { Length: > 0 } found
                        ? found
                        : throw new InvalidDataException(
                            $"{shown}: components.{component.Name}.source.{name} is missing or is not a string");
            var commit = text("commit");
            var archive = text("archive");
            var field = $"{shown}: components.{component.Name}.source";
            if (!Names.IsCommit(commit))
            {
                throw new InvalidDataException($"{field}.commit is not a full commit ID");
            }

            if (!Names.IsPlain(archive))
            {
                throw new InvalidDataException($"{field}.archive is not a plain file name");
            }

            sources.Add(new ComponentSource(component.Name, text("repository"), text("tag"), commit, archive));
        }

        return sources;
    }
}

// A repository that git commands run in. A temporary one is one of the workspace's, in which they run apart from
// the user's configuration.
internal sealed record Repository(string Folder, bool Temporary);

internal sealed record GitResult(int ExitCode, byte[] Output, string Error)
{
    public bool Ok => ExitCode == 0;

    public string Text => Encoding.UTF8.GetString(Output);

    // What git printed to its error stream, on one line, for a finding.
    public string Reason
    {
        get
        {
            var lines = Error.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            return lines.Length == 0 ? $"git exited with {ExitCode}" : string.Join(' ', lines);
        }
    }

    public GitResult Require(string what) => Ok ? this : throw new InvalidDataException($"{what}: {Reason}");
}

// Runs git. Toward a remote, it uses no credential helper, no program that asks for a password, no extra
// HTTP header and no protocol but https, and it runs in a temporary repository apart from the user's
// configuration, so that every fetch is one that anyone could make. git archive turns off what .gitattributes
// and the configuration would change in the files.
internal sealed class Git(string home, bool allowFile)
{
    private static readonly string[] s_archive = ["core.autocrlf=false", "core.eol=lf", "tar.umask=002"];

    public bool AllowFile => allowFile;

    public GitResult Run(Repository repository, params string[] arguments) =>
        GitProcess.Execute(Start(repository, [], arguments), consume: null, input: null);

    public GitResult Network(Repository repository, string url, params string[] arguments)
    {
        List<string> config =
        [
            "credential.helper=",
            "core.askPass=",
            "http.extraHeader=",
            $"http.{url}.extraHeader=",
            "protocol.allow=never",
            "protocol.https.allow=always",
        ];
        if (allowFile)
        {
            config.Add("protocol.file.allow=always");
        }

        return GitProcess.Execute(Start(repository, config, arguments), consume: null, input: null);
    }

    // Streams git archive of the tree, its files exactly as committed. --worktree-attributes reads the attributes
    // of a work tree instead of the archived tree's .gitattributes, and a bare repository has none, so that no
    // export-ignore, export-subst, eol or filter attribute changes the archive.
    public GitResult Archive(Tree tree, Action<Stream> consume)
    {
        string[] arguments =
            ["archive", "--worktree-attributes", "--format=tar", $"--prefix={tree.Prefix}", tree.Commit];
        return GitProcess.Execute(Start(tree.Repository, s_archive, arguments), consume, input: null);
    }

    private ProcessStartInfo Start(Repository repository, IEnumerable<string> config, IEnumerable<string> arguments) =>
        GitProcess.Start(repository.Folder, repository.Temporary ? home : null, config, arguments);
}

internal static class GitProcess
{
    // Variables that point git at another repository than the folder that it runs in, or at a program that
    // asks for a credential. Every command runs without them.
    private static readonly string[] s_dropped =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_INDEX_FILE",
        "GIT_OBJECT_DIRECTORY",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_COMMON_DIR",
        "GIT_NAMESPACE",
        "GIT_DEFAULT_HASH",
        "GIT_ASKPASS",
        "SSH_ASKPASS",
    ];

    // Configuration that the calling process passes on, which a command apart from the user's drops too.
    private static readonly string[] s_passedOn = ["GIT_CONFIG_PARAMETERS", "GIT_CONFIG_COUNT"];

    // A git command in the folder, with the configuration settings first, through ArgumentList and never a
    // shell, and with GIT_TERMINAL_PROMPT=0. With home, it runs apart from the user's configuration and the
    // system's attributes: home stands in for the user's home folder and holds no configuration, no
    // credentials and no .netrc.
    public static ProcessStartInfo Start(
        string folder,
        string? home,
        IEnumerable<string> config,
        IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = folder,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            UseShellExecute = false,
        };
        foreach (var name in s_dropped)
        {
            start.Environment.Remove(name);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        if (home is not null)
        {
            foreach (var name in s_passedOn)
            {
                start.Environment.Remove(name);
            }

            start.Environment["HOME"] = home;
            start.Environment["XDG_CONFIG_HOME"] = home;
            start.Environment["GIT_CONFIG_GLOBAL"] = Path.Combine(home, "gitconfig");
            start.Environment["GIT_ATTR_NOSYSTEM"] = "1";
        }

        foreach (var setting in config)
        {
            start.ArgumentList.Add("-c");
            start.ArgumentList.Add(setting);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return start;
    }

    // Runs the command. consume reads its output as it comes, such as an archive; without it the output is
    // collected. input, if any, is written to the command's input, which is closed in any case.
    public static GitResult Execute(ProcessStartInfo start, Action<Stream>? consume, byte[]? input)
    {
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
            var feeding = Feed(process.StandardInput, input);
            var completed = false;
            try
            {
                var stream = process.StandardOutput.BaseStream;
                byte[] output = [];
                if (consume is null)
                {
                    using var buffer = new MemoryStream();
                    stream.CopyTo(buffer);
                    output = buffer.ToArray();
                }
                else
                {
                    consume(stream);
                    stream.CopyTo(Stream.Null);
                }

                feeding.Wait();
                process.WaitForExit();
                completed = true;
                return new GitResult(process.ExitCode, output, error.Result);
            }
            finally
            {
                if (!completed)
                {
                    Stop(process);
                }
            }
        }
    }

    private static Task Feed(StreamWriter writer, byte[]? input) =>
        Task.Run(() =>
        {
            try
            {
                if (input is not null)
                {
                    writer.BaseStream.Write(input);
                }

                writer.Close();
            }
            catch (IOException)
            {
                // The command ended without reading its input.
            }
        });

    private static void Stop(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception exception) when (exception is InvalidOperationException or Win32Exception)
        {
            // The command has ended already.
        }
    }
}

// The temporary folder of a run: the bare repositories that it fetches into or borrows objects through, and an
// empty folder that stands in for the home folder of the git commands that run in them. Deleted at the end.
internal sealed class Workspace : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"ArchiveSources-{Guid.NewGuid():N}"[..27]);
    private int _repositories;
    private Repository? _probe;

    public Workspace(bool allowFile)
    {
        var home = Path.Combine(_root, "home");
        Directory.CreateDirectory(home);
        Git = new Git(home, allowFile);
    }

    public Git Git { get; }

    // A new, empty bare repository.
    public Repository Create()
    {
        _repositories++;
        var folder = Path.Combine(_root, _repositories.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var repository = new Repository(folder, Temporary: true);
        Git.Run(repository, "-c", "init.defaultBranch=main", "init", "--quiet", "--bare").Require("git init");
        return repository;
    }

    // A bare repository that reads the objects of another one through objects/info/alternates, so that git
    // archive runs there apart from the other repository's attributes and configuration.
    public Repository Borrow(Repository source)
    {
        var objects = Git.Run(source, "rev-parse", "--path-format=absolute", "--git-path", "objects")
            .Require("the objects folder of a repository")
            .Text.Trim();
        var repository = Create();
        var info = Path.Combine(repository.Folder, "objects", "info");
        Directory.CreateDirectory(info);
        File.WriteAllText(Path.Combine(info, "alternates"), $"{objects}\n", new UTF8Encoding(false));
        return repository;
    }

    // The repository in which the tool asks a remote for its tags.
    public Repository Probe() => _probe ??= Create();

    // A folder that cannot be deleted stays in the temporary folder; the run's result stands.
    public void Dispose()
    {
        try
        {
            Folders.Delete(_root);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"warning: the temporary folder of the run cannot be deleted: {exception.Message}");
        }
    }
}

internal static class Folders
{
    // Deletes a folder and everything in it. git makes its object files read-only, which Windows does not
    // delete, and a program that scans new files may hold one for a moment, so a failed attempt is retried.
    public static void Delete(string path)
    {
        for (var attempt = 1; Directory.Exists(path); attempt++)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = 0 };
                    foreach (var file in new DirectoryInfo(path).EnumerateFiles("*", options))
                    {
                        file.Attributes &= ~FileAttributes.ReadOnly;
                    }
                }

                Directory.Delete(path, recursive: true);
            }
            catch (Exception exception) when (attempt < 5 && exception is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(200 * attempt);
            }
        }
    }
}

internal sealed class Tally
{
    public int Total { get; private set; }

    public int Failed { get; private set; }

    public void Expect(bool passed, string description, string? why = null)
    {
        Total++;
        Failed += passed ? 0 : 1;
        var reason = (why ?? "not as expected").ReplaceLineEndings(" | ");
        Console.WriteLine(passed ? $"  ok   {description}" : $"  FAIL {description}: {reason}");
    }

    // A check whose problem is null when it passes.
    public void Expect(string? problem, string description) => Expect(problem is null, description, problem);

    public string Summary() =>
        Failed == 0
            ? $"Self-test passed: {Total} of {Total} checks as expected."
            : $"Self-test FAILED: {Failed} of {Total} checks not as expected.";
}

internal sealed record ArchiveEntry(
    string Name,
    TarEntryType Type,
    UnixFileMode Mode,
    DateTimeOffset Time,
    string Link,
    byte[] Data);

// What a .tar.gz holds: its entries, and its pax global headers and whether one comes first.
internal sealed record ArchiveContents(
    List<ArchiveEntry> Entries,
    List<Dictionary<string, string>> Globals,
    bool GlobalFirst)
{
    public static ArchiveContents? Read(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var entries = new List<ArchiveEntry>();
        var globals = new List<Dictionary<string, string>>();
        var first = false;
        using var file = File.OpenRead(path);
        using var gzip = new GZipStream(file, CompressionMode.Decompress);
        using var reader = new TarReader(gzip);
        while (reader.GetNextEntry() is { } entry)
        {
            if (entry is PaxGlobalExtendedAttributesTarEntry global)
            {
                first |= entries.Count == 0 && globals.Count == 0;
                globals.Add(new Dictionary<string, string>(global.GlobalExtendedAttributes, StringComparer.Ordinal));
                continue;
            }

            using var data = new MemoryStream();
            entry.DataStream?.CopyTo(data);
            entries.Add(new ArchiveEntry(
                entry.Name,
                entry.EntryType,
                entry.Mode,
                entry.ModificationTime,
                entry.LinkName,
                data.ToArray()));
        }

        return new ArchiveContents(entries, globals, first);
    }
}

// The repositories of the self-test, made with git in a temporary folder that it deletes at the end. Each name
// and address in them is made up (C-0011 section 1), and each commit is a day after the one before, so that the
// entries of each tree carry a time of their own.
internal sealed class Fixture : IDisposable
{
    // 2026-01-01T00:00:00Z.
    private const long FirstDay = 1767225600;

    private static readonly string[] s_config =
    [
        "user.name=Fixture",
        "user.email=fixture@example.com",
        "commit.gpgSign=false",
        "tag.gpgSign=false",
        "init.defaultBranch=main",
        "core.autocrlf=false",
        "core.longpaths=true",
        "protocol.file.allow=always",
    ];

    private int _days;

    public Fixture()
    {
        Root = Path.Combine(Path.GetTempPath(), $"ArchiveSources-{Guid.NewGuid():N}"[..27]);
        Home = Path.Combine(Root, "home");
        Directory.CreateDirectory(Home);
    }

    public string Root { get; }

    public string Home { get; }

    public string Folder(string repository) => Path.Combine(Root, repository);

    public string Url(string repository) => new Uri(Folder(repository)).AbsoluteUri;

    public void Write(string repository, string path, string text)
    {
        var file = Path.Combine(Folder(repository), path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, text, new UTF8Encoding(false));
    }

    public GitResult Git(string repository, params string[] arguments) => Feed(repository, null, arguments);

    public GitResult Feed(string repository, byte[]? input, params string[] arguments)
    {
        var start = GitProcess.Start(Folder(repository), Home, s_config, arguments);
        var date = string.Create(CultureInfo.InvariantCulture, $"{FirstDay + (_days * 86400L)} +0000");
        start.Environment["GIT_AUTHOR_DATE"] = date;
        start.Environment["GIT_COMMITTER_DATE"] = date;
        return GitProcess.Execute(start, consume: null, input)
            .Require($"fixture: git {string.Join(' ', arguments)}");
    }

    // Commits what is staged, a day after the commit before, and returns the commit's ID.
    public string Commit(string repository, string message)
    {
        _days++;
        Git(repository, "commit", "--quiet", "--message", message);
        return Git(repository, "rev-parse", "HEAD").Text.Trim();
    }

    public DateTimeOffset Time(string repository, string commit)
    {
        var seconds = Git(repository, "log", "-1", "--format=%ct", commit).Text.Trim();
        return DateTimeOffset.FromUnixTimeSeconds(long.Parse(seconds, CultureInfo.InvariantCulture));
    }

    // What git archive gives of the commit's tree under the prefix: every folder, with each submodule as an
    // empty one, every file with its bytes and every link with its target.
    public Dictionary<string, (TarEntryType Type, byte[] Data)> Tree(string repository, string commit, string prefix)
    {
        var entries = new Dictionary<string, (TarEntryType Type, byte[] Data)>(StringComparer.Ordinal)
        {
            [prefix] = (TarEntryType.Directory, []),
        };
        var blobs = new List<(string Name, string Blob, TarEntryType Type)>();
        var listing = Git(repository, "ls-tree", "-r", "-t", "-z", commit).Text;
        foreach (var record in listing.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            var fields = record[..tab].Split(' ');
            var name = prefix + record[(tab + 1)..];
            if (fields[0] is "040000" or "160000")
            {
                entries[$"{name}/"] = (TarEntryType.Directory, []);
            }
            else
            {
                var type = fields[0] == "120000" ? TarEntryType.SymbolicLink : TarEntryType.RegularFile;
                blobs.Add((name, fields[2], type));
            }
        }

        var input = Encoding.ASCII.GetBytes(string.Concat(blobs.Select(blob => $"{blob.Blob}\n")));
        var output = Feed(repository, input, "cat-file", "--batch").Output;
        var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var position = 0;
        while (position < output.Length)
        {
            var newline = Array.IndexOf(output, (byte)'\n', position);
            var header = Encoding.ASCII.GetString(output, position, newline - position).Split(' ');
            var size = int.Parse(header[2], CultureInfo.InvariantCulture);
            contents[header[0]] = output[(newline + 1)..(newline + 1 + size)];
            position = newline + 1 + size + 1;
        }

        foreach (var blob in blobs)
        {
            entries[blob.Name] = (blob.Type, contents[blob.Blob]);
        }

        return entries;
    }

    public void Dispose() => Folders.Delete(Root);
}

// The repositories of the self-test. nested and excluded are submodules of the fork, fork.git, in which nested
// has a relative URL and excluded is marked update = none. super stands for this repository, with the fork and a
// submodule marked update = none, and with its remote super-remote.git. Its tag v0.1.0 is pushed; v0.2.0, pushed
// too, has the fork's URL point at lacking.git, which lacks the fork's commits; v0.3.0 is not pushed; v0.4.0 is
// pushed at another commit than here. orphan records a submodule that its .gitmodules does not name.
internal sealed record World(
    string Super,
    string RemoteUrl,
    string ForkUrl,
    string NestedUrl,
    string ExcludedUrl,
    string LackingUrl,
    string S1,
    string S2,
    string F1,
    string N1,
    string X1,
    string Orphan,
    string NoSources,
    string Sources)
{
    // A file name longer than a ustar header holds, which git archive writes with a pax extended header.
    public static readonly string LongName = new string('n', 110) + ".txt";

    public static World Build(Fixture fixture)
    {
        fixture.Git(".", "init", "--quiet", "nested");
        fixture.Write("nested", "n1.txt", "nested one\n");
        fixture.Write("nested", "dir/n2.txt", "nested two\n");
        fixture.Write("nested", "tool.sh", "#!/bin/sh\necho nested\n");
        fixture.Write("nested", LongName, "a name too long for a ustar header\n");
        fixture.Git("nested", "add", "--all");
        fixture.Git("nested", "update-index", "--chmod=+x", "tool.sh");
        var target = fixture.Feed("nested", Encoding.UTF8.GetBytes("dir/n2.txt"), "hash-object", "-w", "--stdin");
        fixture.Git("nested", "update-index", "--add", "--cacheinfo", $"120000,{target.Text.Trim()},link");
        var n1 = fixture.Commit("nested", "nested one");

        fixture.Git(".", "init", "--quiet", "excluded");
        fixture.Write("excluded", "secret.txt", "never fetched\n");
        fixture.Git("excluded", "add", "--all");
        var x1 = fixture.Commit("excluded", "excluded one");

        fixture.Git(".", "init", "--quiet", "fork.git");
        fixture.Write("fork.git", "f1.txt", "fork one\n");
        fixture.Write("fork.git", "src/f2.c", "int fork;\n");
        fixture.Write("fork.git", "ignored.txt", "left out by export-ignore\n");
        fixture.Write("fork.git", "subst.txt", "$Format:%H$\n");
        // Attributes that git archive would apply, and that the archives must not.
        fixture.Write(
            "fork.git",
            ".gitattributes",
            "*.txt text eol=crlf\nignored.txt export-ignore\nsubst.txt export-subst\n");
        fixture.Git("fork.git", "submodule", "--quiet", "add", "../nested", "External/nested");
        fixture.Git("fork.git", "submodule", "--quiet", "add", fixture.Url("excluded"), "External/excluded");
        fixture.Git("fork.git", "config", "--file", ".gitmodules", "submodule.External/excluded.update", "none");
        fixture.Git("fork.git", "add", "--all");
        var f1 = fixture.Commit("fork.git", "fork one");

        fixture.Git(".", "init", "--quiet", "super");
        fixture.Write("super", "README.md", "super\n");
        fixture.Write("super", "ci-only.txt", "left out by export-ignore\n");
        fixture.Write("super", ".gitattributes", "*.md text eol=crlf\nci-only.txt export-ignore\n");
        fixture.Git("super", "submodule", "--quiet", "add", fixture.Url("fork.git"), SelfTest.ForkPath);
        fixture.Git("super", "submodule", "--quiet", "add", fixture.Url("excluded"), SelfTest.SkippedPath);
        fixture.Git("super", "config", "--file", ".gitmodules", $"submodule.{SelfTest.SkippedPath}.update", "none");
        fixture.Git("super", "add", "--all");
        var s1 = fixture.Commit("super", "super one");
        fixture.Git(".", "init", "--quiet", "--bare", "super-remote.git");
        fixture.Git("super", "remote", "add", "origin", fixture.Url("super-remote.git"));
        fixture.Git("super", "tag", "--annotate", "--message", "v0.1.0", "v0.1.0");
        fixture.Git("super", "tag", "--annotate", "--message", "v0.3.0", "v0.3.0");
        fixture.Git("super", "tag", "v0.4.0");
        fixture.Git("super", "push", "--quiet", "origin", "HEAD:refs/heads/main", "refs/tags/v0.1.0");

        fixture.Git(".", "init", "--quiet", "lacking.git");
        fixture.Write("lacking.git", "other.txt", "none of the fork's commits\n");
        fixture.Git("lacking.git", "add", "--all");
        fixture.Commit("lacking.git", "lacking one");
        var forkUrl = $"submodule.{SelfTest.ForkPath}.url";
        fixture.Git("super", "config", "--file", ".gitmodules", forkUrl, fixture.Url("lacking.git"));
        fixture.Git("super", "add", ".gitmodules");
        var s2 = fixture.Commit("super", "super two");
        fixture.Git("super", "tag", "v0.2.0");
        fixture.Git("super", "push", "--quiet", "origin", "refs/tags/v0.2.0", $"{s2}:refs/tags/v0.4.0");

        // Later commits of the fork and of its nested submodule, which no archive may hold.
        fixture.Write("fork.git", "f1.txt", "fork one, changed later\n");
        fixture.Write("fork.git", "later.txt", "a later commit\n");
        fixture.Git("fork.git", "add", "f1.txt", "later.txt");
        fixture.Commit("fork.git", "fork two");
        fixture.Write("nested", "n1.txt", "nested one, changed later\n");
        fixture.Git("nested", "add", "n1.txt");
        fixture.Commit("nested", "nested two");

        fixture.Git(".", "init", "--quiet", "orphan");
        fixture.Write("orphan", "a.txt", "a\n");
        fixture.Git("orphan", "add", "--all");
        fixture.Git("orphan", "update-index", "--add", "--cacheinfo", $"160000,{x1},vendor/unnamed");
        var o1 = fixture.Commit("orphan", "orphan one");

        var noSources = Path.Combine(fixture.Root, "no-sources.json");
        File.WriteAllText(noSources, "{ \"files\": [], \"components\": { \"Sample\": { \"texts\": [] } } }\n");
        var sources = Path.Combine(fixture.Root, "sources.json");
        File.WriteAllText(sources, $$"""
            {
              "files": [],
              "components": {
                "Sample": { "texts": [], "note": "A component without a source." },
                "SteamKit2": {
                  "texts": [{ "package": "SteamKit2", "file": "LICENSE" }],
                  "source": {
                    "repository": "https://github.com/SteamRE/SteamKit.git",
                    "tag": "4.0.0-Beta.2",
                    "commit": "{{SelfTest.ComponentCommit}}",
                    "package": "SteamKit2",
                    "archive": "{{SelfTest.SteamKitArchive}}",
                    "paths": ["SteamKit2"],
                    "submodules": [],
                    "note": "Made up for the self-test."
                  }
                }
              }
            }

            """);
        return new World(
            fixture.Folder("super"),
            fixture.Url("super-remote.git"),
            fixture.Url("fork.git"),
            fixture.Url("nested"),
            fixture.Url("excluded"),
            fixture.Url("lacking.git"),
            s1,
            s2,
            f1,
            n1,
            x1,
            o1,
            noSources,
            sources);
    }
}

internal static class SelfTest
{
    public const string ForkPath = "external/Paneport-fork";

    public const string SkippedPath = "external/skipped-one";

    public const string SteamKitArchive = "SteamKit2-4.0.0-Beta.2-source.tar.gz";

    private const string SkippedReason = "update = none: never fetched or built";

    // A made-up commit ID of the component, built at run time.
    public static readonly string ComponentCommit = string.Concat(Enumerable.Repeat("0123456789abcdef", 3))[..40];

    private static readonly UnixFileMode s_executable = (UnixFileMode)Convert.ToInt32("775", 8);

    private static readonly UnixFileMode s_regular = (UnixFileMode)Convert.ToInt32("664", 8);

    public static int Run()
    {
        Console.WriteLine("Self-test of tools/ArchiveSources.cs, with git repositories made in a temporary folder.");
        var tally = new Tally();
        Console.WriteLine("URLs:");
        UrlRules(tally);
        Console.WriteLine("Arguments, the mapping and annotations:");
        Inputs(tally);
        try
        {
            using var fixture = new Fixture();
            var world = World.Build(fixture);
            Console.WriteLine("A release whose repositories are public:");
            Release(tally, fixture, world);
            Console.WriteLine("A revision that its remote lacks:");
            MissingRevision(tally, fixture, world);
            Console.WriteLine("Tags:");
            Tags(tally, fixture, world);
            Console.WriteLine("Components:");
            Components(tally, fixture, world);
            Console.WriteLine("A submodule that .gitmodules does not name:");
            var findings = new Findings(Mode.Release);
            var orphan = new Repository(fixture.Folder("orphan"), Temporary: false);
            var git = new Git(fixture.Home, allowFile: true);
            var read = Submodules.Read(git, orphan, world.Orphan, string.Empty, findings);
            tally.Expect(
                read.Count == 0
                    && findings.Failures is [var failure]
                    && failure.StartsWith("vendor/unnamed: ", StringComparison.Ordinal),
                "fails on it, because its source cannot be fetched",
                string.Join("; ", findings.Failures));
        }
        catch (Exception exception) when (
            exception is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            tally.Expect(false, "the fixture repositories are made and removed", exception.Message);
        }

        Console.WriteLine(tally.Summary());
        return tally.Failed == 0 ? 0 : 1;
    }

    private static void UrlRules(Tally tally)
    {
        List<(string Url, bool AllowFile, bool Accepted)> cases =
        [
            ("https://github.com/recelilious/Paneport-wine.git", false, true),
            ("https://github.com/recelilious/Paneport-wine", false, true),
            ("http://github.com/recelilious/Paneport-wine", false, false),
            ("ssh://git@github.com/recelilious/Paneport-wine.git", false, false),
            ("git@github.com:recelilious/Paneport-wine.git", false, false),
            ("git://github.com/recelilious/Paneport-wine", false, false),
            ("https://fixture@github.com/recelilious/Paneport-wine", false, false),
            ("https://github.com/recelilious/Paneport-wine?ref=main", false, false),
            ("../Paneport-wine.git", false, false),
            ("file:///C:/fixture/Paneport-wine", false, false),
            ("file:///C:/fixture/Paneport-wine", true, true),
        ];
        foreach (var (url, allowFile, accepted) in cases)
        {
            var problem = Urls.Problem(url, allowFile);
            var switched = allowFile ? " with the self-test's switch" : string.Empty;
            tally.Expect(
                (problem is null) == accepted,
                $"{(accepted ? "accepts" : "rejects")} {url}{switched}",
                problem ?? "accepted");
        }

        tally.Expect(
            Urls.Normalize("https://github.com/recelilious/Paneport-wine.git/")
                == "https://github.com/recelilious/Paneport-wine",
            "lists a URL without a trailing slash or .git");
        List<(string Base, string Url, string? Resolved)> relative =
        [
            (
                "https://github.com/recelilious/Paneport.git",
                "../Paneport-wine.git",
                "https://github.com/recelilious/Paneport-wine.git"),
            ("https://github.com/recelilious/Paneport", "./sub", "https://github.com/recelilious/Paneport/sub"),
            ("https://github.com/recelilious/Paneport/", "../../fmtlib/fmt", "https://github.com/fmtlib/fmt"),
            ("https://github.com/recelilious/Paneport", "../../../fmt", null),
            ("https://github.com/recelilious/Paneport", "https://example.com/fmt", "https://example.com/fmt"),
        ];
        foreach (var (baseUrl, url, resolved) in relative)
        {
            var actual = Urls.Resolve(baseUrl, url);
            tally.Expect(
                actual == resolved,
                $"resolves {url} against {baseUrl} to {resolved ?? "nothing"}",
                actual ?? "nothing");
        }
    }

    private static void Inputs(Tally tally)
    {
        string[] places = ["--out", "o", "--list", "l"];
        tally.Expect(
            Arguments.Parse(["--mode", "release", "--tag", "v0.1.0", .. places], out _)
                is { Mode: Mode.Release, Tag: "v0.1.0", Out: "o", List: "l", Components: null },
            "reads the arguments of a release");
        tally.Expect(
            Arguments.Parse(["--mode", "dry-run", "--tag", "v0.1.0", .. places, "--components", "m"], out _)
                is { Mode: Mode.DryRun, Components: "m" },
            "reads the arguments of a dry run with a mapping");
        List<(string[] Args, string What)> wrong =
        [
            ([], "no argument"),
            (["--mode", "publish", "--tag", "v0.1.0", .. places], "a mode other than release and dry-run"),
            (["--mode", "release", "--tag", "v0.1.0", "--out", "o"], "no --list"),
            (["--mode", "release", "--tag", "release/v0.1.0", .. places], "a tag that cannot name an archive"),
            (["--mode", "release", "--mode", "release", "--tag", "v0.1.0", .. places], "an argument twice"),
            (["--mode", "release", "--tag", "v0.1.0", .. places, "--force"], "an unknown argument"),
        ];
        foreach (var (args, what) in wrong)
        {
            var writer = new StringWriter();
            var code = Cli.Run(args, Directory.GetCurrentDirectory(), allowFile: true, writer, writer, false);
            tally.Expect(code == 2, $"exits with 2 on {what}", writer.ToString());
        }

        if (File.Exists(Mapping.DefaultPath))
        {
            string? problem = null;
            try
            {
                Mapping.Read(Path.GetFullPath(Mapping.DefaultPath), Mapping.DefaultPath);
            }
            catch (InvalidDataException exception)
            {
                problem = exception.Message;
            }

            tally.Expect(problem, $"reads the component sources of {Mapping.DefaultPath}");
        }

        tally.Expect(Report.Data("100% a\r\nb") == "100%25 a%0D%0Ab", "escapes %, CR and LF in an annotation");
    }

    private static void Release(Tally tally, Fixture fixture, World world)
    {
        var output = fixture.Folder("out-release");
        var (code, text) = Invoke(world, "release", "v0.1.0", output);
        tally.Expect(code == 0, "makes the archives of a tag whose repositories are public, and exits with 0", text);
        tally.Expect(Directory.Exists(output), "creates the output folder");

        var self = ArchiveContents.Read(Path.Combine(output, "Paneport-v0.1.0-source.tar.gz"));
        tally.Expect(
            Compare(fixture.Tree("super", world.S1, "Paneport-v0.1.0/"), self),
            "the archive of this repository holds exactly the tagged tree, every path and byte as committed, under its "
                + "top folder, and no file of a submodule");
        tally.Expect(
            StartsWithHeaderOf(self, world.S1),
            "the archive of this repository starts with git's pax global header, which names the tagged commit");

        var top = $"Paneport-fork-{world.F1[..12]}";
        var nested = $"{top}/External/nested/";
        var fork = ArchiveContents.Read(Path.Combine(output, $"{top}-source.tar.gz"));
        tally.Expect(
            Compare(ForkTree(fixture, world), fork),
            "the fork's archive holds exactly its recorded revision, and the nested submodule's files at their "
                + "recorded revision under its path");
        tally.Expect(
            fork is not null && fork.Entries.All(entry => entry.Name != $"{top}/later.txt"),
            "the fork's archive holds the recorded revision, not the later commit of the fork's branch");
        tally.Expect(
            fork is not null && fork.Entries.Any(entry => entry.Name == $"{nested}n1.txt"),
            "the fork's archive includes the files of its nested submodule");
        var excluded = $"{top}/External/excluded/";
        tally.Expect(
            fork is not null && !fork.Entries.Any(entry => Under(entry.Name, excluded)),
            "the fork's archive holds nothing of its submodule marked update = none");
        tally.Expect(
            !File.Exists(Path.Combine(output, $"skipped-one-{world.X1[..12]}-source.tar.gz")),
            "makes no archive of a submodule of this repository marked update = none");
        tally.Expect(
            StartsWithHeaderOf(fork, world.F1),
            "the fork's archive keeps only the outermost pax global header, the fork's");
        var forkTime = fixture.Time("fork.git", world.F1);
        var nestedTime = fixture.Time("nested", world.N1);
        tally.Expect(
            fork is not null
                && fork.Entries.All(entry => entry.Time == (Under(entry.Name, nested) ? nestedTime : forkTime)),
            "the fork's archive keeps the time of each entry, the commit time of the tree that it comes from");
        tally.Expect(
            fork is not null
                && fork.Entries.Any(entry => entry.Name == $"{nested}tool.sh" && entry.Mode == s_executable)
                && fork.Entries.Any(entry => entry.Name == $"{nested}n1.txt" && entry.Mode == s_regular),
            "the fork's archive keeps the mode of each entry, an executable's among them");

        var expected = new JsonObject
        {
            ["format"] = 1,
            ["mode"] = "release",
            ["repository"] = new JsonObject
            {
                ["name"] = "Paneport",
                ["url"] = Urls.Normalize(world.RemoteUrl),
                ["tag"] = "v0.1.0",
                ["commit"] = world.S1,
                ["archive"] = "Paneport-v0.1.0-source.tar.gz",
                ["public"] = true,
            },
            ["submodules"] = new JsonArray(
                new JsonObject
                {
                    ["path"] = ForkPath,
                    ["name"] = "Paneport-fork",
                    ["url"] = Urls.Normalize(world.ForkUrl),
                    ["commit"] = world.F1,
                    ["archive"] = $"{top}-source.tar.gz",
                    ["public"] = true,
                    ["submodules"] = new JsonArray(
                        new JsonObject
                        {
                            ["path"] = "External/nested",
                            ["url"] = world.NestedUrl,
                            ["commit"] = world.N1,
                        },
                        new JsonObject
                        {
                            ["path"] = "External/excluded",
                            ["url"] = world.ExcludedUrl,
                            ["commit"] = world.X1,
                            ["skipped"] = SkippedReason,
                        }),
                },
                new JsonObject
                {
                    ["path"] = SkippedPath,
                    ["name"] = "skipped-one",
                    ["url"] = world.ExcludedUrl,
                    ["commit"] = world.X1,
                    ["skipped"] = SkippedReason,
                }),
            ["components"] = new JsonArray(),
        };
        tally.Expect(
            Same(Path.Combine(output, "sources.json"), expected),
            "the list names each archive, commit and URL and the skipped submodules, says that each repository is "
                + "public, and is UTF-8 without a byte-order mark with LF line ends");
    }

    private static void MissingRevision(Tally tally, Fixture fixture, World world)
    {
        var failed = fixture.Folder("out-missing-release");
        var (code, text) = Invoke(world, "release", "v0.2.0", failed);
        var named = $"{ForkPath}: the revision {world.F1} cannot be fetched from {world.LackingUrl} without "
            + "credentials";
        tally.Expect(
            code == 1 && text.Contains($"error: {named}", StringComparison.Ordinal),
            "fails in release mode on a revision that its remote lacks, naming the path, the commit and the URL",
            text);
        tally.Expect(!Directory.Exists(failed), "writes nothing after a failure");

        var dry = fixture.Folder("out-missing-dry-run");
        (code, text) = Invoke(world, "dry-run", "v0.2.0", dry);
        tally.Expect(
            code == 0 && text.Contains($"warning: {named}", StringComparison.Ordinal),
            "only warns about it in dry-run mode",
            text);
        var top = $"Paneport-fork-{world.F1[..12]}";
        tally.Expect(
            Compare(ForkTree(fixture, world), ArchiveContents.Read(Path.Combine(dry, $"{top}-source.tar.gz"))),
            "in dry-run mode takes the fork's archive from the checked-out submodule, at the recorded revision and "
                + "with its nested submodule");
        var list = Listed(Path.Combine(dry, "sources.json"));
        tally.Expect(
            list?["submodules"]?[0]?["public"]?.GetValue<bool>() == false
                && list["repository"]?["public"]?.GetValue<bool>() == true,
            "lists that fork as not public",
            list?.ToJsonString());

        (code, text) = Invoke(world, "release", "v0.2.0", fixture.Folder("out-annotated"), actions: true);
        var lines = text.Split('\n');
        var token = lines[0].StartsWith("::stop-commands::", StringComparison.Ordinal) ? lines[0][17..] : null;
        var resumed = token is null ? -1 : Array.IndexOf(lines, $"::{token}::");
        var annotation = $"::error title=Source archives::{named}";
        tally.Expect(
            code == 1
                && resumed > 0
                && lines[(resumed + 1)..].Any(line => line.StartsWith(annotation, StringComparison.Ordinal)),
            "under GitHub Actions prints the report while workflow commands are stopped, then annotates each failure",
            text);
    }

    private static void Tags(Tally tally, Fixture fixture, World world)
    {
        var (code, text) = Invoke(world, "release", "v0.3.0", fixture.Folder("out-unpushed"));
        tally.Expect(
            code == 1
                && text.Contains("the tag v0.3.0 at", StringComparison.Ordinal)
                && text.Contains("the tag is not pushed", StringComparison.Ordinal),
            "fails in release mode when the remote does not have the tag",
            text);

        var dry = fixture.Folder("out-unpushed-dry-run");
        (code, text) = Invoke(world, "dry-run", "v0.3.0", dry);
        tally.Expect(
            code == 0
                && text.Contains("warning: ", StringComparison.Ordinal)
                && Listed(Path.Combine(dry, "sources.json"))?["repository"]?["public"]?.GetValue<bool>() == false,
            "only warns about it in dry-run mode, and lists this repository as not public",
            text);

        (code, text) = Invoke(world, "release", "v0.4.0", fixture.Folder("out-moved"));
        tally.Expect(
            code == 1 && text.Contains($"it has the tag at {world.S2}", StringComparison.Ordinal),
            "fails when the remote has the tag at another commit",
            text);

        (code, text) = Invoke(world, "release", "v9.9.9", fixture.Folder("out-no-tag"));
        tally.Expect(
            code == 1 && text.Contains("the tag v9.9.9 does not resolve to a commit", StringComparison.Ordinal),
            "fails on a tag that this repository does not have",
            text);
    }

    private static void Components(Tally tally, Fixture fixture, World world)
    {
        var output = fixture.Folder("out-components");
        var (code, text) = Invoke(world, "release", "v0.1.0", output, world.Sources);
        var missing = $"error: the component SteamKit2: its source archive {SteamKitArchive} is not in";
        tally.Expect(
            code == 1
                && text.Contains(missing, StringComparison.Ordinal)
                && text.Contains(Mapping.Script, StringComparison.Ordinal),
            "fails when the source archive of a component is not in the output folder, naming the component and the "
                + "script that makes it",
            text);

        Directory.CreateDirectory(output);
        var component = Path.Combine(output, SteamKitArchive);
        var other = Path.Combine(output, "keep.txt");
        var old = Path.Combine(output, "Paneport-v0.1.0-source.tar.gz");
        File.WriteAllText(component, "made by the script");
        File.WriteAllText(other, "not an archive of the tool");
        File.WriteAllText(old, "an archive of an earlier run");
        (code, text) = Invoke(world, "release", "v0.1.0", output, world.Sources);
        tally.Expect(code == 0, "passes once a file of that name is there", text);
        var expected = new JsonArray(new JsonObject
        {
            ["name"] = "SteamKit2",
            ["url"] = "https://github.com/SteamRE/SteamKit",
            ["tag"] = "4.0.0-Beta.2",
            ["commit"] = ComponentCommit,
            ["archive"] = SteamKitArchive,
        });
        var listed = Listed(Path.Combine(output, "sources.json"))?["components"];
        tally.Expect(
            JsonNode.DeepEquals(listed, expected),
            "lists the component with its upstream's URL, tag and commit, and its archive",
            listed?.ToJsonString());
        tally.Expect(
            File.ReadAllText(other) == "not an archive of the tool"
                && File.ReadAllText(component) == "made by the script"
                && Compare(fixture.Tree("super", world.S1, "Paneport-v0.1.0/"), ArchiveContents.Read(old)) is null,
            "replaces only its own archives in the output folder, and keeps every other file");
        var sizes = string.Create(
            CultureInfo.InvariantCulture,
            $"  Paneport-v0.1.0-source.tar.gz: {new FileInfo(old).Length:N0} bytes, this repository at {world.S1}");
        tally.Expect(
            text.Contains(sizes, StringComparison.Ordinal)
                && text.Contains($"  {SteamKitArchive}: 18 bytes, the component SteamKit2", StringComparison.Ordinal),
            "prints a line for each archive with its size",
            text);

        var broken = Path.Combine(fixture.Root, "broken.json");
        File.WriteAllText(broken, "{ \"components\": ");
        (code, text) = Invoke(world, "release", "v0.1.0", fixture.Folder("out-broken"), broken);
        tally.Expect(code == 2, "exits with 2 on a mapping that is not valid JSON", text);
        var shapeless = Path.Combine(fixture.Root, "shapeless.json");
        File.WriteAllText(
            shapeless,
            """{ "components": { "SteamKit2": { "source": { "repository": "https://example.com/SteamKit" } } } }""");
        (code, text) = Invoke(world, "release", "v0.1.0", fixture.Folder("out-shapeless"), shapeless);
        tally.Expect(code == 2, "exits with 2 on the source of a component without its tag, commit and archive", text);
    }

    // What git archive gives of the fork's recorded revision, with the files of its nested submodule at their
    // recorded revision under its path.
    private static Dictionary<string, (TarEntryType Type, byte[] Data)> ForkTree(Fixture fixture, World world)
    {
        var top = $"Paneport-fork-{world.F1[..12]}/";
        var tree = fixture.Tree("fork.git", world.F1, top);
        foreach (var (name, entry) in fixture.Tree("nested", world.N1, $"{top}External/nested/"))
        {
            tree[name] = entry;
        }

        return tree;
    }

    private static (int Code, string Text) Invoke(
        World world,
        string mode,
        string tag,
        string output,
        string? mapping = null,
        bool actions = false)
    {
        var writer = new StringWriter();
        string[] args =
        [
            "--mode",
            mode,
            "--tag",
            tag,
            "--out",
            output,
            "--list",
            Path.Combine(output, "sources.json"),
            "--components",
            mapping ?? world.NoSources,
        ];
        var code = Cli.Run(args, world.Super, allowFile: true, writer, writer, actions);
        return (code, writer.ToString().ReplaceLineEndings("\n"));
    }

    // Whether the entry's name is in the folder, which is not the folder itself.
    private static bool Under(string name, string folder) =>
        name.Length > folder.Length && name.StartsWith(folder, StringComparison.Ordinal);

    // Whether the archive starts with its only pax global header, and that header names the commit.
    private static bool StartsWithHeaderOf(ArchiveContents? archive, string commit) =>
        archive is { GlobalFirst: true, Globals: [var global] } && global.GetValueOrDefault("comment") == commit;

    // Null when the archive holds exactly the expected entries, each once, or the first difference.
    private static string? Compare(
        Dictionary<string, (TarEntryType Type, byte[] Data)> expected,
        ArchiveContents? archive)
    {
        if (archive is null)
        {
            return "the archive is missing";
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in archive.Entries)
        {
            if (!seen.Add(entry.Name))
            {
                return $"{entry.Name} is in the archive twice";
            }

            if (!expected.TryGetValue(entry.Name, out var wanted))
            {
                return $"{entry.Name} is not in the tree";
            }

            var data = entry.Type == TarEntryType.SymbolicLink ? Encoding.UTF8.GetBytes(entry.Link) : entry.Data;
            if (entry.Type != wanted.Type || !data.AsSpan().SequenceEqual(wanted.Data))
            {
                return $"{entry.Name} differs from the tree";
            }
        }

        var missing = expected.Keys.FirstOrDefault(name => !seen.Contains(name));
        return missing is null ? null : $"{missing} is missing";
    }

    private static JsonNode? Listed(string path) => File.Exists(path) ? JsonNode.Parse(File.ReadAllBytes(path)) : null;

    // Null when the list at the path holds the expected JSON, as UTF-8 without a byte-order mark with LF line
    // ends, or what is wrong with it.
    private static string? Same(string path, JsonNode expected)
    {
        if (!File.Exists(path))
        {
            return "the list is missing";
        }

        var bytes = File.ReadAllBytes(path);
        if (bytes.Length == 0
            || bytes[0] != (byte)'{'
            || bytes.AsSpan().Contains((byte)'\r')
            || bytes[^1] != (byte)'\n')
        {
            return "the list is not UTF-8 without a byte-order mark with LF line ends";
        }

        var listed = JsonNode.Parse(bytes);
        return JsonNode.DeepEquals(listed, JsonNode.Parse(expected.ToJsonString()))
            ? null
            : $"the list differs: {listed?.ToJsonString()}";
    }
}
