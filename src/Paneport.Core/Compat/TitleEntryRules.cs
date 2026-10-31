// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Text.RegularExpressions;
using Paneport.Core.Library;

namespace Paneport.Core.Compat;

// The rules of a title entry that the app checks in code (C-0008 sections 2, 4 and 5), the same
// for an entry read from a file and one made in code. compat/schema/title.schema.json states the
// same rules for CI, and docs/design/compat-db.md maps each rule to its clause. Patterns end in \z,
// because $ in .NET also matches before a final line break.
internal static partial class TitleEntryRules
{
    // The characters that Windows forbids in file names besides the slash: the backslash, the colon,
    // the wildcards, the quote, the angle brackets, the bar and the controls U+0000 to U+001F.
    private static readonly SearchValues<char> s_forbiddenInFileNames = SearchValues.Create(
        "\\:*?\"<>|" + new string([.. Enumerable.Range(0, 0x20).Select(code => (char)code)]));

    private const string RelativePathRule =
        "The path is relative to the install folder, with forward slashes: no leading or trailing slash, no empty, "
        + ". or .. segment, and none of the characters that Windows forbids in file names.";

    public static void Check(TitleEntry entry, CompatLayer layer, List<CompatProblem> problems)
    {
        CheckId(entry.Id, problems);

        if (entry.Name is null)
        {
            if (layer == CompatLayer.Bundled)
            {
                problems.Add(Missing("$.name", "A reviewed entry names its title."));
            }
        }
        else if (string.IsNullOrWhiteSpace(entry.Name))
        {
            problems.Add(Invalid("$.name", "The name holds a character other than white space."));
        }

        if (entry.Executable is { } executable)
        {
            CheckExecutable(executable, problems);
        }

        CheckNamed(entry.Architecture, CompatNames.Architecture, "$.architecture", problems);
        CheckNamed(entry.Drm?.Ceg, CompatNames.Ceg, "$.drm.ceg", problems);
        CheckNamed(entry.Launch, CompatNames.Launch, "$.launch", problems);
        CheckList(entry.GraphicsApi, "$.graphicsApi", problems, (api, path) =>
            CheckNamed(api, CompatNames.GraphicsApi, path, problems));
        CheckList(entry.Runtimes, "$.runtimes", problems, (runtime, path) =>
        {
            if (runtime is null || !CompatLists.RuntimeIds.Contains(runtime))
            {
                problems.Add(Invalid(path, "The runtime is not on the schema's fixed list."));
            }
        });
        CheckRange(entry.MemoryEstimateMiB, IntRange.MemoryEstimateMiB, "$.memoryEstimateMiB", problems);
        CheckRange(entry.JitPoolMiB, IntRange.JitPoolMiB, "$.jitPoolMiB", problems);
        CheckMap(entry.Translator, "$.translator", CompatLists.TranslatorKeys, "translator option", problems);

        if (entry.Graphics is { } graphics)
        {
            if (graphics.RenderScale is { } scale && !(double.IsFinite(scale) && scale > 0 && scale <= 1))
            {
                problems.Add(Invalid("$.graphics.renderScale", "renderScale must be greater than 0 and at most 1."));
            }

            CheckNamed(graphics.MetalFx, CompatNames.MetalFx, "$.graphics.metalFx", problems);
            CheckRange(graphics.FrameRateCap, IntRange.FrameRateCap, "$.graphics.frameRateCap", problems);
        }

        CheckMap(entry.Environment, "$.environment", CompatLists.EnvironmentKeys, "environment variable", problems);
        var blocked = entry.HelperPolicy?.BlockedExecutables;
        CheckList(blocked, "$.helperPolicy.blockedExecutables", problems, (program, path) =>
        {
            if (!IsRelativePath(program))
            {
                problems.Add(Invalid(path, RelativePathRule));
            }
        });
        CheckNamed(entry.Prefix, CompatNames.Prefix, "$.prefix", problems);
        CheckNamed(entry.Rating, CompatNames.Rating, "$.rating", problems);

        if (entry.Reports is { } reports)
        {
            for (var i = 0; i < reports.Count; i++)
            {
                CheckReport(reports[i], CompatPath.Index("$.reports", i), entry.Id.SteamAppId is not null, problems);
            }
        }

        if (layer == CompatLayer.Bundled)
        {
            CheckStatedLaunch(entry, problems);
        }
        else
        {
            CheckDeviceLayer(entry, problems);
        }
    }

    // A path relative to the install folder, the same rule as the schema's $defs/relativePath.
    public static bool IsRelativePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.AsSpan().ContainsAny(s_forbiddenInFileNames))
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckId(TitleEntryId id, List<CompatProblem> problems)
    {
        if ((id.SteamAppId is null) == (id.ExeSha256 is null))
        {
            problems.Add(new CompatProblem(
                CompatRule.Identity,
                "$.id",
                "An id has either steamAppId, for a Steam title, or exeSha256, for a local title."));
        }

        if (id.SteamAppId is { } appId && !IntRange.SteamAppId.Contains(appId))
        {
            problems.Add(Invalid("$.id.steamAppId", IntRange.SteamAppId.Message));
        }

        if (id.ExeSha256 is { Count: 0 })
        {
            problems.Add(Invalid("$.id.exeSha256", "exeSha256 lists at least one value."));
        }

        CheckList(id.ExeSha256, "$.id.exeSha256", problems, (hash, path) =>
        {
            if (hash is null || !Sha256Pattern().IsMatch(hash))
            {
                problems.Add(Invalid(path, "A SHA-256 value is 64 lowercase hexadecimal digits."));
            }
        });

        if (!TitleId.IsValidSlug(id.Slug))
        {
            problems.Add(Invalid(
                "$.id.slug", "A slug is lowercase ASCII letters and digits joined by single hyphens."));
        }
    }

    private static void CheckExecutable(ExecutableSpec executable, List<CompatProblem> problems)
    {
        if (!IsRelativePath(executable.Path))
        {
            problems.Add(Invalid("$.executable.path", RelativePathRule));
        }

        if (executable.WorkingDirectory is { } directory && directory != "." && !IsRelativePath(directory))
        {
            problems.Add(Invalid(
                "$.executable.workingDirectory",
                "The working directory is . for the install folder, or a relative path. " + RelativePathRule));
        }

        // Duplicate arguments are allowed: a command line may repeat a switch.
        if (executable.Arguments is { } arguments)
        {
            for (var i = 0; i < arguments.Count; i++)
            {
                if (arguments[i] is null || arguments[i].Contains('\0', StringComparison.Ordinal))
                {
                    problems.Add(Invalid(
                        CompatPath.Index("$.executable.arguments", i), "An argument holds no NUL character."));
                }
            }
        }
    }

    private static void CheckReport(TestReport? report, string path, bool steamTitle, List<CompatProblem> problems)
    {
        if (report is null)
        {
            problems.Add(new CompatProblem(CompatRule.Type, path, "A report is an object."));
            return;
        }

        if (!DevicePattern().IsMatch(report.Device ?? string.Empty))
        {
            problems.Add(Invalid(path + ".device", "The device is a model identifier such as iPhone18,1."));
        }

        if (!IosVersionPattern().IsMatch(report.IosVersion ?? string.Empty))
        {
            problems.Add(Invalid(
                path + ".iosVersion", "The iOS version is written with its build, such as 27.2 (24B119)."));
        }

        var version = report.PaneportVersion ?? string.Empty;
        if (!ReleaseTagPattern().IsMatch(version) && !CommitHashPattern().IsMatch(version))
        {
            problems.Add(Invalid(
                path + ".paneportVersion", "The version is a release tag such as v0.1.0, or a full commit hash."));
        }

        var build = report.GameBuild ?? string.Empty;
        if (steamTitle ? !SteamBuildPattern().IsMatch(build) : !Sha256Pattern().IsMatch(build))
        {
            problems.Add(Invalid(
                path + ".gameBuild",
                steamTitle
                    ? "A Steam title's game build is its Steam build ID."
                    : "A local title's game build is the SHA-256 of its program file."));
        }

        CheckNamed<CompatRating>(report.Rating, CompatNames.Rating, path + ".rating", problems);
        if (report.Rating == CompatRating.Untested)
        {
            problems.Add(Invalid(path + ".rating", "A report observes a rating; untested is not one."));
        }

        if (!EvidencePattern().IsMatch(report.Evidence ?? string.Empty))
        {
            problems.Add(Invalid(
                path + ".evidence", "The evidence is the path of a record docs/evidence/YYYY-MM-DD-<topic>.md."));
        }

        CheckFrameRate(report.FpsMedian, path + ".fpsMedian", problems);
        CheckFrameRate(report.FpsLow1, path + ".fpsLow1", problems);
        CheckRange(report.PeakMemoryMiB, IntRange.PeakMemoryMiB, path + ".peakMemoryMiB", problems);
        CheckNamed(report.ThermalState, CompatNames.ThermalState, path + ".thermalState", problems);

        if (report.Rating is CompatRating.Perfect or CompatRating.Playable or CompatRating.Runs)
        {
            const string Measured = "A rating of runs or better comes with the frame rates and the peak memory.";
            if (report.FpsMedian is null)
            {
                problems.Add(Missing(path + ".fpsMedian", Measured));
            }

            if (report.FpsLow1 is null)
            {
                problems.Add(Missing(path + ".fpsLow1", Measured));
            }

            if (report.PeakMemoryMiB is null)
            {
                problems.Add(Missing(path + ".peakMemoryMiB", Measured));
            }
        }
    }

    // C-0008 section 5, for a reviewed entry: a stated launch path follows from the entry's own drm,
    // whatever the device's classification adds, and a local title shows no Steam marker.
    private static void CheckStatedLaunch(TitleEntry entry, List<CompatProblem> problems)
    {
        if (entry.Launch == LaunchPath.Direct && !LaunchRule.AllowsDirect(entry.Drm))
        {
            problems.Add(new CompatProblem(
                CompatRule.Launch,
                "$.launch",
                "An entry that states direct has steamApi and steamStub false and ceg no in its own drm."));
        }

        if (entry.Launch == LaunchPath.Dock && !LaunchRule.BlocksDirect(entry.Drm))
        {
            problems.Add(new CompatProblem(
                CompatRule.Launch,
                "$.launch",
                "An entry that states dock shows steamApi or steamStub true, or ceg yes or unknown, in its own drm."));
        }

        // Only a well-formed local id counts; an id with both members is reported as such alone.
        if (entry.Id.ExeSha256 is null || entry.Id.SteamAppId is not null)
        {
            return;
        }

        const string Marker = "A local title shows no Steam marker (C-0008 section 5).";
        if (entry.Drm?.SteamApi == true)
        {
            problems.Add(new CompatProblem(CompatRule.Launch, "$.drm.steamApi", Marker));
        }

        if (entry.Drm?.SteamStub == true)
        {
            problems.Add(new CompatProblem(CompatRule.Launch, "$.drm.steamStub", Marker));
        }

        if (entry.Drm?.Ceg == CegStatus.Yes)
        {
            problems.Add(new CompatProblem(CompatRule.Launch, "$.drm.ceg", Marker));
        }

        if (entry.Launch == LaunchPath.Dock)
        {
            problems.Add(new CompatProblem(
                CompatRule.Launch, "$.launch", "A local title is launched only on the Direct path."));
        }

        if (entry.Executable is null)
        {
            problems.Add(Missing("$.executable", "A local title names the program that its exeSha256 values hash."));
        }
    }

    // The device layer holds the computed classification and the user's overrides (C-0008 section 1).
    private static void CheckDeviceLayer(TitleEntry entry, List<CompatProblem> problems)
    {
        if (entry.Launch is not null)
        {
            problems.Add(new CompatProblem(
                CompatRule.DeviceLayer, "$.launch", "The device layer never holds launch, which follows from drm."));
        }

        if (entry.Rating is not null)
        {
            problems.Add(new CompatProblem(
                CompatRule.DeviceLayer, "$.rating", "Only a reviewed entry holds a rating."));
        }

        if (entry.Reports is not null)
        {
            problems.Add(new CompatProblem(
                CompatRule.DeviceLayer, "$.reports", "Only a reviewed entry holds reports."));
        }

        if (entry.Drm?.Ceg is { } ceg && ceg != CegStatus.Unknown)
        {
            problems.Add(new CompatProblem(
                CompatRule.DeviceLayer,
                "$.drm.ceg",
                "The device's classification cannot detect CEG, so its ceg is unknown; only a reviewed entry sets it "
                + "(C-0007 section 5)."));
        }
    }

    private static void CheckNamed<TEnum>(
        TEnum? value, JsonNames<TEnum> names, string path, List<CompatProblem> problems)
        where TEnum : struct, Enum
    {
        if (value is { } named && !names.HasName(named))
        {
            problems.Add(Invalid(path, names.Expected));
        }
    }

    private static void CheckRange(int? value, IntRange range, string path, List<CompatProblem> problems)
    {
        if (value is { } number && !range.Contains(number))
        {
            problems.Add(Invalid(path, range.Message));
        }
    }

    private static void CheckFrameRate(double? value, string path, List<CompatProblem> problems)
    {
        if (value is { } rate && !(double.IsFinite(rate) && rate > 0))
        {
            problems.Add(Invalid(path, "A frame rate is greater than 0."));
        }
    }

    // Checks each item, and that no item appears twice: the schema's lists are sets.
    private static void CheckList<T>(
        IReadOnlyList<T>? items, string path, List<CompatProblem> problems, Action<T, string> checkItem)
    {
        if (items is null)
        {
            return;
        }

        var seen = new HashSet<T>();
        for (var i = 0; i < items.Count; i++)
        {
            var itemPath = CompatPath.Index(path, i);
            checkItem(items[i], itemPath);
            if (items[i] is not null && !seen.Add(items[i]))
            {
                problems.Add(Invalid(itemPath, "The item appears twice in the list."));
            }
        }
    }

    private static void CheckMap(
        IReadOnlyDictionary<string, string>? map,
        string path,
        IReadOnlySet<string> allowed,
        string kind,
        List<CompatProblem> problems)
    {
        if (map is null)
        {
            return;
        }

        foreach (var key in map.Keys.Order(StringComparer.Ordinal))
        {
            var keyPath = CompatPath.Property(path, key);
            if (!allowed.Contains(key))
            {
                problems.Add(new CompatProblem(
                    CompatRule.NotAllowed, keyPath, $"The {kind} is not on the schema's allow-list."));
            }
            else if (map[key] is not { } value || value.Contains('\0', StringComparison.Ordinal))
            {
                problems.Add(Invalid(keyPath, "The value is a string without a NUL character."));
            }
        }
    }

    private static CompatProblem Invalid(string path, string message) => new(CompatRule.InvalidValue, path, message);

    private static CompatProblem Missing(string path, string message) => new(CompatRule.MissingField, path, message);

    [GeneratedRegex("^[0-9a-f]{64}\\z")]
    internal static partial Regex Sha256Pattern();

    [GeneratedRegex("^[A-Za-z]+[0-9]+,[0-9]+\\z")]
    private static partial Regex DevicePattern();

    [GeneratedRegex("^[0-9]+\\.[0-9]+(\\.[0-9]+)? \\([0-9]+[A-Z][0-9]+[a-z]?\\)\\z")]
    private static partial Regex IosVersionPattern();

    [GeneratedRegex("^v(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)\\.(0|[1-9][0-9]*)(-[0-9A-Za-z-]+(\\.[0-9A-Za-z-]+)*)?\\z")]
    private static partial Regex ReleaseTagPattern();

    [GeneratedRegex("^[0-9a-f]{40}\\z")]
    private static partial Regex CommitHashPattern();

    [GeneratedRegex("^[1-9][0-9]{0,19}\\z")]
    private static partial Regex SteamBuildPattern();

    [GeneratedRegex("^docs/evidence/[0-9]{4}-[0-9]{2}-[0-9]{2}-[a-z0-9]+(-[a-z0-9]+)*\\.md\\z")]
    private static partial Regex EvidencePattern();
}
