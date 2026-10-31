// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Paneport.Core.Compat;
using Paneport.Core.Hosting;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// S6.4.1: compat/schema/title.schema.json read as plain JSON. It implements C-0008 section 2 and
// agrees with the lists, defaults and names that the code checks.
public sealed partial class TitleSchemaTests
{
    private static readonly JsonNode s_schema = JsonNode.Parse(
        File.ReadAllText(CompatTestFiles.Repository("compat", "schema", "title.schema.json")))!;

    private static JsonNode Properties => s_schema["properties"]!;

    [Fact]
    public void SchemaIsJsonOfDraft202012()
    {
        Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)s_schema["$schema"]);
        Assert.Equal("object", (string?)s_schema["type"]);
    }

    [Fact]
    public void SchemaDefinesTheFieldsOfC0008AndNoOthers()
    {
        // C-0008 section 2, in its order, and section 4 for the reports.
        Assert.Equal(
            [
                "schemaVersion", "id", "name", "executable", "architecture", "drm", "launch", "graphicsApi",
                "runtimes", "memoryEstimateMiB", "jitPoolMiB", "translator", "graphics", "environment",
                "helperPolicy", "prefix", "rating", "reports",
            ],
            PropertyNames(s_schema));
        Assert.Equal(["steamAppId", "exeSha256", "slug"], PropertyNames(Definition("id")));
        Assert.Equal(["path", "workingDirectory", "arguments"], PropertyNames(Definition("executable")));
        Assert.Equal(["steamApi", "steamStub", "steamAppIdFile", "ceg"], PropertyNames(Definition("drm")));
        Assert.Equal(["renderScale", "metalFx", "frameRateCap"], PropertyNames(Definition("graphics")));
        Assert.Equal(["blockedExecutables"], PropertyNames(Definition("helperPolicy")));
        Assert.Equal(
            [
                "date", "device", "iosVersion", "paneportVersion", "gameBuild", "rating", "evidence", "fpsMedian",
                "fpsLow1", "peakMemoryMiB", "thermalState", "notes",
            ],
            PropertyNames(Definition("report")));
        Assert.Equal(
            ["date", "device", "iosVersion", "paneportVersion", "gameBuild", "rating", "evidence"],
            Strings(Definition("report")["required"]));

        string[] objects = ["id", "executable", "drm", "graphics", "helperPolicy", "report"];
        Assert.False((bool)s_schema["additionalProperties"]!);
        Assert.All(objects, name => Assert.False((bool)Definition(name)["additionalProperties"]!));
    }

    [Fact]
    public void SchemaNamesTheFieldsTheCodeWrites()
    {
        // An entry that sets every field, written by the code, has the schema's names at each level.
        // A Steam title's id and a local title's id together name every member of id.
        var written = JsonNode.Parse(TitleEntryJson.Write(FullEntry(), CompatLayer.Bundled))!;
        var localId = JsonNode.Parse(TitleEntryJson.Write(TestEntries.Local(), CompatLayer.Bundled))!["id"]!;

        Assert.Equal(PropertyNames(s_schema), PropertyNames(written));
        Assert.Equal(
            PropertyNames(Definition("id")).Order(StringComparer.Ordinal),
            PropertyNames(written["id"]!).Union(PropertyNames(localId)).Order(StringComparer.Ordinal));
        Assert.Equal(PropertyNames(Definition("executable")), PropertyNames(written["executable"]!));
        Assert.Equal(PropertyNames(Definition("drm")), PropertyNames(written["drm"]!));
        Assert.Equal(PropertyNames(Definition("graphics")), PropertyNames(written["graphics"]!));
        Assert.Equal(PropertyNames(Definition("helperPolicy")), PropertyNames(written["helperPolicy"]!));
        Assert.Equal(PropertyNames(Definition("report")), PropertyNames(written["reports"]![0]!));
    }

    [Fact]
    public void EnvironmentAllowListHoldsNoSteamVariable()
    {
        var environment = Properties["environment"]!;

        // Only the listed names are allowed, and none of them is one that steam_api reads
        // (C-0008 section 5). Windows reads environment names in any letter case.
        Assert.False((bool)environment["additionalProperties"]!);
        Assert.DoesNotContain(
            PropertyNames(environment), name => name.StartsWith("steam", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ListsMatchTheCode()
    {
        Assert.False((bool)Properties["translator"]!["additionalProperties"]!);
        Assert.Equal(Sorted(CompatLists.EnvironmentKeys), Sorted(PropertyNames(Properties["environment"]!)));
        Assert.Equal(Sorted(CompatLists.TranslatorKeys), Sorted(PropertyNames(Properties["translator"]!)));
        Assert.Equal(Sorted(CompatLists.RuntimeIds), Sorted(Strings(Definition("runtime")["enum"])));
    }

    [Fact]
    public void TargetDevicesMatchTheDesignDocument()
    {
        // The rows of the target device table in docs/design/compat-db.md, up to the next heading.
        var document = File.ReadAllLines(CompatTestFiles.Repository("docs", "design", "compat-db.md"));
        var start = Array.FindIndex(
            document, line => line.StartsWith('#') && line.EndsWith("Target devices", StringComparison.Ordinal));
        Assert.True(start >= 0, "The design document has no Target devices section.");
        var documented = document
            .Skip(start + 1)
            .TakeWhile(line => !line.StartsWith('#'))
            .Where(line => line.StartsWith('|'))
            .SelectMany(line => ModelIdentifier().Matches(line).Select(match => match.Groups[1].Value))
            .ToArray();

        Assert.NotEmpty(documented);
        Assert.Equal(Sorted(documented), Sorted(Strings(Definition("targetDevices")["enum"])));
    }

    [Fact]
    public void DefaultsMatchWhatTheMergeGivesWithoutEntries()
    {
        var defaults = CompatPrecedence.Merge(TestEntries.SteamId, null, null);
        var graphics = Definition("graphics")["properties"]!;

        Assert.Equal("shared", (string?)Properties["prefix"]!["default"]);
        Assert.Equal(PrefixMode.Shared, defaults.Prefix);
        Assert.Equal("untested", (string?)Properties["rating"]!["default"]);
        Assert.Equal(CompatRating.Untested, defaults.Rating);
        Assert.Equal(1.0, (double)graphics["renderScale"]!["default"]!);
        Assert.Equal(1.0, defaults.RenderScale);
        Assert.Equal("off", (string?)graphics["metalFx"]!["default"]);
        Assert.Equal(MetalFxMode.Off, defaults.MetalFx);

        JsonNode[] lists =
        [
            Properties["graphicsApi"]!,
            Properties["runtimes"]!,
            Properties["reports"]!,
            Definition("executable")["properties"]!["arguments"]!,
            Definition("helperPolicy")["properties"]!["blockedExecutables"]!,
        ];
        Assert.All(lists, list => Assert.Empty(list["default"]!.AsArray()));
        Assert.Empty(Properties["translator"]!["default"]!.AsObject());
        Assert.Empty(Properties["environment"]!["default"]!.AsObject());
        Assert.Empty(defaults.GraphicsApi);
        Assert.Empty(defaults.Runtimes);
        Assert.Empty(defaults.Reports);
        Assert.Empty(defaults.BlockedExecutables);
        Assert.Empty(defaults.Translator);
        Assert.Empty(defaults.Environment);
        Assert.Empty(CompatPrecedence.Merge(TestEntries.LocalId, TestEntries.Local(), null).Executable!.Arguments!);
    }

    [Fact]
    public void NothingThatAllowsALaunchHasADefault()
    {
        // drm has no default that would allow a launch, and launch follows from drm (C-0008 section 1).
        Assert.Null(Properties["launch"]!["default"]);
        Assert.Null(Definition("drm")["default"]);
        Assert.All(
            PropertyNames(Definition("drm")),
            member => Assert.Null(Definition("drm")["properties"]![member]!["default"]));

        var defaults = CompatPrecedence.Merge(TestEntries.SteamId, null, null);
        Assert.Null(defaults.Drm);
        Assert.Equal(LaunchDecision.Unclassified, defaults.Launch);
    }

    // Each value the schema lists reads, and each value of the model that the code writes is one the
    // schema lists. A value of the model without a name, such as LaunchPath.Unclassified, fails the
    // check of an entry instead.
    [Fact]
    public void ArchitecturesAgree() => AssertAgree(
        Strings(Properties["architecture"]!["enum"]),
        value => $"\"architecture\": \"{value}\"",
        Enum.GetValues<TitleArchitecture>().Select(value => TestEntries.Steam() with { Architecture = value }),
        written => (string?)written["architecture"]);

    [Fact]
    public void CegValuesAgree() => AssertAgree(
        Strings(Definition("drm")["properties"]!["ceg"]!["enum"]),
        value => $"\"drm\": {{ \"ceg\": \"{value}\" }}",
        Enum.GetValues<CegStatus>().Select(value => TestEntries.Steam() with { Drm = new() { Ceg = value } }),
        written => (string?)written["drm"]!["ceg"]);

    [Fact]
    public void LaunchPathsAgree() => AssertAgree(
        Strings(Properties["launch"]!["enum"]),
        value => $$"""
            "launch": "{{value}}",
            "drm": { "steamApi": {{(value == "dock" ? "true" : "false")}}, "steamStub": false, "ceg": "no" }
            """,
        Enum.GetValues<LaunchPath>().Select(value => TestEntries.Steam() with
        {
            Launch = value,
            Drm = new() { SteamApi = value == LaunchPath.Dock, SteamStub = false, Ceg = CegStatus.No },
        }),
        written => (string?)written["launch"]);

    [Fact]
    public void GraphicsApisAgree() => AssertAgree(
        Strings(Properties["graphicsApi"]!["items"]!["enum"]),
        value => $"\"graphicsApi\": [\"{value}\"]",
        Enum.GetValues<GraphicsApi>().Select(value => TestEntries.Steam() with { GraphicsApi = [value] }),
        written => (string?)written["graphicsApi"]![0]);

    [Fact]
    public void MetalFxModesAgree() => AssertAgree(
        Strings(Definition("graphics")["properties"]!["metalFx"]!["enum"]),
        value => $"\"graphics\": {{ \"metalFx\": \"{value}\" }}",
        Enum.GetValues<MetalFxMode>().Select(value => TestEntries.Steam() with { Graphics = new() { MetalFx = value } }),
        written => (string?)written["graphics"]!["metalFx"]);

    [Fact]
    public void PrefixModesAgree() => AssertAgree(
        Strings(Properties["prefix"]!["enum"]),
        value => $"\"prefix\": \"{value}\"",
        Enum.GetValues<PrefixMode>().Select(value => TestEntries.Steam() with { Prefix = value }),
        written => (string?)written["prefix"]);

    [Fact]
    public void RatingsAgree() => AssertAgree(
        Strings(Definition("rating")["enum"]),
        value => $"\"rating\": \"{value}\"",
        Enum.GetValues<CompatRating>().Select(value => TestEntries.Steam() with { Rating = value }),
        written => (string?)written["rating"]);

    [Fact]
    public void ObservedRatingsAgree() => AssertAgree(
        Strings(Definition("report")["properties"]!["rating"]!["enum"]),
        value => $"\"reports\": [{ReportJson(value, thermalState: null)}]",
        Enum.GetValues<CompatRating>().Select(value => TestEntries.Steam() with { Reports = [Report(value, null)] }),
        written => (string?)written["reports"]![0]!["rating"]);

    [Fact]
    public void ThermalStatesAgree() => AssertAgree(
        Strings(Definition("report")["properties"]!["thermalState"]!["enum"]),
        value => $"\"reports\": [{ReportJson("boots", value)}]",
        Enum.GetValues<ThermalState>().Select(value => TestEntries.Steam() with
        {
            Reports = [Report(CompatRating.Boots, value)],
        }),
        written => (string?)written["reports"]![0]!["thermalState"]);

    private static void AssertAgree(
        string[] schemaValues,
        Func<string, string> field,
        IEnumerable<TitleEntry> entries,
        Func<JsonNode, string?> writtenValue)
    {
        foreach (var value in schemaValues)
        {
            var json = $$"""
                { "schemaVersion": 1, "id": { "steamAppId": 4100000310, "slug": "copper-orchard" },
                  "name": "Copper Orchard", {{field(value)}} }
                """;
            var result = TitleEntryJson.Read(Encoding.UTF8.GetBytes(json), CompatLayer.Bundled);
            Assert.True(
                result.Status == TitleEntryStatus.Loaded, $"'{value}' did not read: {string.Join("; ", result.Problems)}");
        }

        var written = new List<string>();
        foreach (var entry in entries)
        {
            var problems = TitleEntryJson.Validate(entry, CompatLayer.Bundled);
            if (problems.Count > 0)
            {
                Assert.All(problems, problem => Assert.Equal(CompatRule.InvalidValue, problem.Rule));
                continue;
            }

            written.Add(writtenValue(JsonNode.Parse(TitleEntryJson.Write(entry, CompatLayer.Bundled))!)!);
        }

        Assert.Equal(Sorted(schemaValues), Sorted(written));
    }

    private static TestReport Report(CompatRating rating, ThermalState? thermalState) => new()
    {
        Date = new DateOnly(2026, 11, 2),
        Device = "iPhone18,1",
        IosVersion = "27.2 (24B119)",
        PaneportVersion = "v0.1.0",
        GameBuild = "12345678",
        Rating = rating,
        Evidence = "docs/evidence/2026-11-02-copper-orchard.md",
        FpsMedian = 30,
        FpsLow1 = 25,
        PeakMemoryMiB = 2000,
        ThermalState = thermalState,
    };

    private static string ReportJson(string rating, string? thermalState)
    {
        var thermal = thermalState is null ? string.Empty : $", \"thermalState\": \"{thermalState}\"";
        return $$"""
            { "date": "2026-11-02", "device": "iPhone18,1", "iosVersion": "27.2 (24B119)", "paneportVersion": "v0.1.0",
              "gameBuild": "12345678", "rating": "{{rating}}", "evidence": "docs/evidence/2026-11-02-copper-orchard.md",
              "fpsMedian": 30, "fpsLow1": 25, "peakMemoryMiB": 2000{{thermal}} }
            """;
    }

    // An entry with every field set, to compare the code's names with the schema's.
    private static TitleEntry FullEntry() => TestEntries.Steam() with
    {
        Executable = new ExecutableSpec { Path = "Orchard.exe", WorkingDirectory = ".", Arguments = ["-windowed"] },
        Architecture = TitleArchitecture.X64,
        Drm = new DrmClassification { SteamApi = true, SteamStub = false, SteamAppIdFile = false, Ceg = CegStatus.No },
        Launch = LaunchPath.Dock,
        GraphicsApi = [GraphicsApi.Direct3D11],
        Runtimes = ["vcredist-v14-x64"],
        MemoryEstimateMiB = 1000,
        JitPoolMiB = 512,
        Translator = new Dictionary<string, string>(),
        Graphics = new GraphicsSettings { RenderScale = 0.5, MetalFx = MetalFxMode.Spatial, FrameRateCap = 30 },
        Environment = new Dictionary<string, string>(),
        HelperPolicy = new HelperPolicy { BlockedExecutables = ["Reporter.exe"] },
        Prefix = PrefixMode.Isolated,
        Rating = CompatRating.Runs,
        Reports = [Report(CompatRating.Runs, ThermalState.Fair) with { Notes = "Fictional." }],
    };

    private static JsonNode Definition(string name) => s_schema["$defs"]![name]!;

    // The names under "properties" of a schema, or the names of a written object.
    private static string[] PropertyNames(JsonNode node)
    {
        var properties = node["properties"] as JsonObject ?? node.AsObject();
        return [.. properties.Select(property => property.Key)];
    }

    private static string[] Strings(JsonNode? array) => [.. array!.AsArray().Select(item => (string)item!)];

    private static string[] Sorted(IEnumerable<string> values) => [.. values.Order(StringComparer.Ordinal)];

    [GeneratedRegex(@"`(iPhone[0-9]+,[0-9]+)`")]
    private static partial Regex ModelIdentifier();
}
