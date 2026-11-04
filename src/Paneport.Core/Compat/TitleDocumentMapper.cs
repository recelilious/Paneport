// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text.RegularExpressions;
using Paneport.Core.Hosting;

namespace Paneport.Core.Compat;

// Turns the JSON shape of a title file into the typed model and back. Reading reports what the
// typed model cannot hold: a missing required field, a name that is not on a list, an integer
// outside its range, a date that is not a calendar date. TitleEntryRules checks the rest.
internal static partial class TitleDocumentMapper
{
    private const string DateFormat = "yyyy-MM-dd";

    public static TitleEntry? FromDocument(TitleDocument document, List<CompatProblem> problems)
    {
        if (document.SchemaVersion is null)
        {
            problems.Add(Missing("$.schemaVersion"));
        }
        else if (document.SchemaVersion != TitleEntryJson.SchemaVersion)
        {
            problems.Add(Invalid("$.schemaVersion", "schemaVersion is 1."));
        }

        TitleEntryId? id = null;
        if (document.Id is null)
        {
            problems.Add(Missing("$.id"));
        }
        else
        {
            id = FromId(document.Id, problems);
        }

        ExecutableSpec? executable = null;
        if (document.Executable is { } executableDocument)
        {
            if (executableDocument.Path is null)
            {
                problems.Add(Missing("$.executable.path"));
            }
            else
            {
                executable = new ExecutableSpec
                {
                    Path = executableDocument.Path,
                    WorkingDirectory = executableDocument.WorkingDirectory,
                    Arguments = executableDocument.Arguments,
                };
            }
        }

        var drm = document.Drm is { } drmDocument
            ? new DrmClassification
            {
                SteamApi = drmDocument.SteamApi,
                SteamStub = drmDocument.SteamStub,
                SteamAppIdFile = drmDocument.SteamAppIdFile,
                Ceg = FromName(drmDocument.Ceg, CompatNames.Ceg, "$.drm.ceg", problems),
            }
            : null;

        var graphics = document.Graphics is { } graphicsDocument
            ? new GraphicsSettings
            {
                RenderScale = graphicsDocument.RenderScale,
                MetalFx = FromName(graphicsDocument.MetalFx, CompatNames.MetalFx, "$.graphics.metalFx", problems),
                FrameRateCap = FromInteger(
                    graphicsDocument.FrameRateCap, IntRange.FrameRateCap, "$.graphics.frameRateCap", problems),
            }
            : null;

        var entry = new TitleEntry
        {
            Id = id!,
            Name = document.Name,
            Executable = executable,
            Architecture = FromName(document.Architecture, CompatNames.Architecture, "$.architecture", problems),
            Drm = drm,
            Launch = FromName(document.Launch, CompatNames.Launch, "$.launch", problems),
            GraphicsApi = FromNames(document.GraphicsApi, CompatNames.GraphicsApi, "$.graphicsApi", problems),
            Runtimes = document.Runtimes,
            MemoryEstimateMiB = FromInteger(
                document.MemoryEstimateMiB, IntRange.MemoryEstimateMiB, "$.memoryEstimateMiB", problems),
            JitPoolMiB = FromInteger(document.JitPoolMiB, IntRange.JitPoolMiB, "$.jitPoolMiB", problems),
            Translator = document.Translator,
            Graphics = graphics,
            Environment = document.Environment,
            HelperPolicy = document.HelperPolicy is { } policy
                ? new HelperPolicy { BlockedExecutables = policy.BlockedExecutables }
                : null,
            Prefix = FromName(document.Prefix, CompatNames.Prefix, "$.prefix", problems),
            Rating = FromName(document.Rating, CompatNames.Rating, "$.rating", problems),
            Reports = FromReports(document.Reports, problems),
        };

        return problems.Count == 0 ? entry : null;
    }

    public static TitleDocument ToDocument(TitleEntry entry) => new()
    {
        SchemaVersion = TitleEntryJson.SchemaVersion,
        Id = new TitleDocument.IdDocument
        {
            SteamAppId = entry.Id.SteamAppId,
            ExeSha256 = entry.Id.ExeSha256?.ToList(),
            Slug = entry.Id.Slug,
        },
        Name = entry.Name,
        Executable = entry.Executable is { } executable
            ? new TitleDocument.ExecutableDocument
            {
                Path = executable.Path,
                WorkingDirectory = executable.WorkingDirectory,
                Arguments = executable.Arguments?.ToList(),
            }
            : null,
        Architecture = NameOf(entry.Architecture, CompatNames.Architecture),
        Drm = entry.Drm is { } drm
            ? new TitleDocument.DrmDocument
            {
                SteamApi = drm.SteamApi,
                SteamStub = drm.SteamStub,
                SteamAppIdFile = drm.SteamAppIdFile,
                Ceg = NameOf(drm.Ceg, CompatNames.Ceg),
            }
            : null,
        Launch = NameOf(entry.Launch, CompatNames.Launch),
        GraphicsApi = entry.GraphicsApi?.Select(CompatNames.GraphicsApi.NameOf).ToList(),
        Runtimes = entry.Runtimes?.ToList(),
        MemoryEstimateMiB = entry.MemoryEstimateMiB,
        JitPoolMiB = entry.JitPoolMiB,
        Translator = ToSortedMap(entry.Translator),
        Graphics = entry.Graphics is { } graphics
            ? new TitleDocument.GraphicsDocument
            {
                RenderScale = graphics.RenderScale,
                MetalFx = NameOf(graphics.MetalFx, CompatNames.MetalFx),
                FrameRateCap = graphics.FrameRateCap,
            }
            : null,
        Environment = ToSortedMap(entry.Environment),
        HelperPolicy = entry.HelperPolicy is { } policy
            ? new TitleDocument.HelperPolicyDocument { BlockedExecutables = policy.BlockedExecutables?.ToList() }
            : null,
        Prefix = NameOf(entry.Prefix, CompatNames.Prefix),
        Rating = NameOf(entry.Rating, CompatNames.Rating),
        Reports = entry.Reports?.Select(ToReport).ToList(),
    };

    private static TitleEntryId? FromId(TitleDocument.IdDocument document, List<CompatProblem> problems)
    {
        uint? appId = null;
        if (document.SteamAppId is { } value)
        {
            if (IntRange.SteamAppId.Contains(value))
            {
                appId = (uint)value;
            }
            else
            {
                problems.Add(Invalid("$.id.steamAppId", IntRange.SteamAppId.Message));
            }
        }

        if (document.Slug is null)
        {
            problems.Add(Missing("$.id.slug"));
            return null;
        }

        return new TitleEntryId { SteamAppId = appId, ExeSha256 = document.ExeSha256, Slug = document.Slug };
    }

    private static List<TestReport>? FromReports(
        List<TitleDocument.ReportDocument>? documents, List<CompatProblem> problems)
    {
        if (documents is null)
        {
            return null;
        }

        var reports = new List<TestReport>(documents.Count);
        for (var i = 0; i < documents.Count; i++)
        {
            var document = documents[i];
            var path = CompatPath.Index("$.reports", i);
            var count = problems.Count;
            var date = Required(document.Date, path + ".date", problems);
            var device = Required(document.Device, path + ".device", problems);
            var iosVersion = Required(document.IosVersion, path + ".iosVersion", problems);
            var paneportVersion = Required(document.PaneportVersion, path + ".paneportVersion", problems);
            var gameBuild = Required(document.GameBuild, path + ".gameBuild", problems);
            var rating = FromName(
                Required(document.Rating, path + ".rating", problems), CompatNames.Rating, path + ".rating", problems);
            var evidence = Required(document.Evidence, path + ".evidence", problems);
            var peakMemory = FromInteger(
                document.PeakMemoryMiB, IntRange.PeakMemoryMiB, path + ".peakMemoryMiB", problems);
            var thermalState = FromName(
                document.ThermalState, CompatNames.ThermalState, path + ".thermalState", problems);

            DateOnly day = default;
            if (date is not null && !(DatePattern().IsMatch(date)
                && DateOnly.TryParseExact(
                    date, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out day)))
            {
                problems.Add(Invalid(path + ".date", "The date is a calendar date, written YYYY-MM-DD."));
            }

            if (problems.Count == count)
            {
                reports.Add(new TestReport
                {
                    Date = day,
                    Device = device!,
                    IosVersion = iosVersion!,
                    PaneportVersion = paneportVersion!,
                    GameBuild = gameBuild!,
                    Rating = rating!.Value,
                    Evidence = evidence!,
                    FpsMedian = document.FpsMedian,
                    FpsLow1 = document.FpsLow1,
                    PeakMemoryMiB = peakMemory,
                    ThermalState = thermalState,
                    Notes = document.Notes,
                });
            }
        }

        return reports;
    }

    private static TitleDocument.ReportDocument ToReport(TestReport report) => new()
    {
        Date = report.Date.ToString(DateFormat, CultureInfo.InvariantCulture),
        Device = report.Device,
        IosVersion = report.IosVersion,
        PaneportVersion = report.PaneportVersion,
        GameBuild = report.GameBuild,
        Rating = CompatNames.Rating.NameOf(report.Rating),
        Evidence = report.Evidence,
        FpsMedian = report.FpsMedian,
        FpsLow1 = report.FpsLow1,
        PeakMemoryMiB = report.PeakMemoryMiB,
        ThermalState = report.ThermalState is { } state ? CompatNames.ThermalState.NameOf(state) : null,
        Notes = report.Notes,
    };

    private static string? Required(string? value, string path, List<CompatProblem> problems)
    {
        if (value is null)
        {
            problems.Add(Missing(path));
        }

        return value;
    }

    private static TEnum? FromName<TEnum>(
        string? name, JsonNames<TEnum> names, string path, List<CompatProblem> problems)
        where TEnum : struct, Enum
    {
        if (name is null)
        {
            return null;
        }

        if (names.TryParse(name, out var value))
        {
            return value;
        }

        problems.Add(Invalid(path, names.Expected));
        return null;
    }

    private static List<TEnum>? FromNames<TEnum>(
        List<string>? items, JsonNames<TEnum> names, string path, List<CompatProblem> problems)
        where TEnum : struct, Enum
    {
        if (items is null)
        {
            return null;
        }

        var values = new List<TEnum>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            if (FromName(items[i], names, CompatPath.Index(path, i), problems) is { } value)
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static int? FromInteger(long? value, IntRange range, string path, List<CompatProblem> problems)
    {
        if (value is not { } number)
        {
            return null;
        }

        if (!range.Contains(number))
        {
            problems.Add(Invalid(path, range.Message));
            return null;
        }

        return (int)number;
    }

    private static string? NameOf<TEnum>(TEnum? value, JsonNames<TEnum> names)
        where TEnum : struct, Enum => value is { } named ? names.NameOf(named) : null;

    // Keys are written in ordinal order, so that the same entry always gives the same file.
    private static Dictionary<string, string>? ToSortedMap(IReadOnlyDictionary<string, string>? map)
    {
        if (map is null)
        {
            return null;
        }

        var sorted = new Dictionary<string, string>(map.Count, StringComparer.Ordinal);
        foreach (var key in map.Keys.Order(StringComparer.Ordinal))
        {
            sorted.Add(key, map[key]);
        }

        return sorted;
    }

    private static CompatProblem Invalid(string path, string message) => new(CompatRule.InvalidValue, path, message);

    private static CompatProblem Missing(string path) => new(CompatRule.MissingField, path, "The field is required.");

    [GeneratedRegex("^[0-9]{4}-[0-9]{2}-[0-9]{2}\\z")]
    private static partial Regex DatePattern();
}
