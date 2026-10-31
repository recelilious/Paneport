// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.Json;
using Paneport.Core.Compat;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// S6.4.2 and S6.4.5: reading and writing title files. Every valid fixture loads, every invalid
// fixture is rejected for the one rule it breaks, and loading, writing and loading again keeps every
// value. The fixtures hold only made-up titles and values (C-0008 section 5).
public sealed class TitleEntryJsonTests
{
    // Each invalid fixture with its layer, the rule it breaks and the field that breaks it.
    public static TheoryData<string, CompatLayer, CompatRule, string> InvalidFixtures { get; } = new()
    {
        { "not-json.json", CompatLayer.Bundled, CompatRule.Json, "$" },
        { "not-an-object.json", CompatLayer.Bundled, CompatRule.Json, "$" },
        { "comment.json", CompatLayer.Bundled, CompatRule.Json, "$" },
        { "trailing-comma.json", CompatLayer.Bundled, CompatRule.Json, "$" },
        { "null-value.json", CompatLayer.Bundled, CompatRule.Type, "$.jitPoolMiB" },
        { "wrong-type.json", CompatLayer.Bundled, CompatRule.Type, "$.jitPoolMiB" },
        { "fraction-integer.json", CompatLayer.Bundled, CompatRule.Type, "$.jitPoolMiB" },
        { "environment-not-string.json", CompatLayer.Bundled, CompatRule.Type, "$.environment.WINEDEBUG" },
        { "duplicate-field.json", CompatLayer.Bundled, CompatRule.DuplicateField, "$.drm.ceg" },
        { "duplicate-schema-version.json", CompatLayer.Bundled, CompatRule.DuplicateField, "$.schemaVersion" },
        { "unknown-field.json", CompatLayer.Bundled, CompatRule.UnknownField, "$.colour" },
        { "unknown-nested-field.json", CompatLayer.Bundled, CompatRule.UnknownField, "$.graphics.brightness" },
        { "unknown-report-field.json", CompatLayer.Bundled, CompatRule.UnknownField, "$.reports[0].tester" },
        { "wrong-case-field.json", CompatLayer.Bundled, CompatRule.UnknownField, "$.jitPoolMib" },
        { "newer-schema-version.json", CompatLayer.Bundled, CompatRule.NewerSchemaVersion, "$.schemaVersion" },
        { "schema-version-zero.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.schemaVersion" },
        { "missing-schema-version.json", CompatLayer.Bundled, CompatRule.MissingField, "$.schemaVersion" },
        { "missing-id.json", CompatLayer.Bundled, CompatRule.MissingField, "$.id" },
        { "missing-slug.json", CompatLayer.Bundled, CompatRule.MissingField, "$.id.slug" },
        { "missing-name.json", CompatLayer.Bundled, CompatRule.MissingField, "$.name" },
        { "blank-name.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.name" },
        { "both-ids.json", CompatLayer.Bundled, CompatRule.Identity, "$.id" },
        { "neither-id.json", CompatLayer.Bundled, CompatRule.Identity, "$.id" },
        { "steam-app-id-zero.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.id.steamAppId" },
        { "bad-slug.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.id.slug" },
        { "uppercase-hash.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.id.exeSha256[0]" },
        { "duplicate-hash.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.id.exeSha256[1]" },
        { "absolute-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.path" },
        { "parent-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.path" },
        { "backslash-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.path" },
        { "drive-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.path" },
        { "trailing-dot-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.path" },
        { "bad-working-directory.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.workingDirectory" },
        { "nul-argument.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.executable.arguments[0]" },
        { "missing-executable-path.json", CompatLayer.Bundled, CompatRule.MissingField, "$.executable.path" },
        { "bad-architecture.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.architecture" },
        { "enum-wrong-case.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.prefix" },
        { "bad-ceg.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.drm.ceg" },
        { "bad-graphics-api.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphicsApi[1]" },
        { "duplicate-graphics-api.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphicsApi[1]" },
        { "unknown-runtime.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.runtimes[0]" },
        { "memory-estimate-zero.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.memoryEstimateMiB" },
        { "jit-pool-too-small.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.jitPoolMiB" },
        { "jit-pool-too-large.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.jitPoolMiB" },
        { "translator-key.json", CompatLayer.Bundled, CompatRule.NotAllowed, "$.translator.Multiblock" },
        { "environment-steam-key.json", CompatLayer.Bundled, CompatRule.NotAllowed, "$.environment.SteamAppId" },
        { "render-scale-zero.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphics.renderScale" },
        { "render-scale-above-one.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphics.renderScale" },
        { "frame-rate-cap-zero.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphics.frameRateCap" },
        { "bad-metal-fx.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.graphics.metalFx" },
        {
            "blocked-parent-path.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.helperPolicy.blockedExecutables[0]"
        },
        { "bad-rating.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.rating" },
        { "launch-unclassified.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.launch" },
        { "direct-without-ceg.json", CompatLayer.Bundled, CompatRule.Launch, "$.launch" },
        { "dock-without-blocker.json", CompatLayer.Bundled, CompatRule.Launch, "$.launch" },
        { "local-steam-api.json", CompatLayer.Bundled, CompatRule.Launch, "$.drm.steamApi" },
        { "local-steam-stub.json", CompatLayer.Bundled, CompatRule.Launch, "$.drm.steamStub" },
        { "local-ceg-yes.json", CompatLayer.Bundled, CompatRule.Launch, "$.drm.ceg" },
        { "local-dock.json", CompatLayer.Bundled, CompatRule.Launch, "$.launch" },
        { "local-without-executable.json", CompatLayer.Bundled, CompatRule.MissingField, "$.executable" },
        { "report-missing-evidence.json", CompatLayer.Bundled, CompatRule.MissingField, "$.reports[0].evidence" },
        { "report-bad-date.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].date" },
        { "report-bad-device.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].device" },
        { "report-bad-ios-version.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].iosVersion" },
        { "report-bad-version.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].paneportVersion" },
        { "report-steam-build-hash.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].gameBuild" },
        { "report-local-build-number.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].gameBuild" },
        { "report-untested.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].rating" },
        { "report-runs-without-low.json", CompatLayer.Bundled, CompatRule.MissingField, "$.reports[0].fpsLow1" },
        { "report-bad-evidence.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].evidence" },
        { "report-zero-fps.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].fpsMedian" },
        { "report-bad-thermal.json", CompatLayer.Bundled, CompatRule.InvalidValue, "$.reports[0].thermalState" },
        { "launch.json", CompatLayer.Device, CompatRule.DeviceLayer, "$.launch" },
        { "rating.json", CompatLayer.Device, CompatRule.DeviceLayer, "$.rating" },
        { "reports.json", CompatLayer.Device, CompatRule.DeviceLayer, "$.reports" },
        { "ceg-no.json", CompatLayer.Device, CompatRule.DeviceLayer, "$.drm.ceg" },
        { "ceg-yes.json", CompatLayer.Device, CompatRule.DeviceLayer, "$.drm.ceg" },
        { "jit-pool-too-large.json", CompatLayer.Device, CompatRule.InvalidValue, "$.jitPoolMiB" },
    };

    public static TheoryData<CompatLayer, string> ValidFixtures { get; } = new(
        CompatTestFiles.FixtureNames("valid", "bundled").Select(name => (CompatLayer.Bundled, name))
            .Concat(CompatTestFiles.FixtureNames("valid", "device").Select(name => (CompatLayer.Device, name))));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryValidBundledFixtureLoads()
    {
        var files = await new BundledSnapshot(CompatTestFiles.Fixture("valid", "bundled")).LoadAllAsync(Token);

        Assert.Equal(5, files.Count);
        Assert.All(files, file => Assert.True(
            file.Result.Status == TitleEntryStatus.Loaded,
            $"{file.FileName}: {string.Join("; ", file.Result.Problems)}"));
    }

    [Fact]
    public async Task EveryValidDeviceFixtureLoads()
    {
        using var store = new DeviceLayerStore(CompatTestFiles.Fixture("valid", "device"));
        var files = await store.LoadAllAsync(Token);

        Assert.Equal(3, files.Count);
        Assert.All(files, file => Assert.True(
            file.Result.Status == TitleEntryStatus.Loaded,
            $"{file.FileName}: {string.Join("; ", file.Result.Problems)}"));
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void InvalidFixtureIsRejectedForTheRuleItBreaks(
        string fixture, CompatLayer layer, CompatRule rule, string path)
    {
        var result = TitleEntryJson.Read(FixtureBytes("invalid", layer, fixture), layer);

        Assert.Equal(TitleEntryStatus.Rejected, result.Status);
        Assert.Null(result.Entry);
        var problem = Assert.Single(result.Problems);
        Assert.Equal(rule, problem.Rule);
        Assert.Equal(path, problem.Path);
        Assert.False(string.IsNullOrWhiteSpace(problem.Message));
    }

    [Fact]
    public void EveryInvalidFixtureIsListedWithItsRule()
    {
        var listed = InvalidFixtures.Select(row => $"{FolderOf(row.Data.Item2)}/{row.Data.Item1}");
        var present = CompatTestFiles.FixtureNames("invalid", "bundled").Select(name => "bundled/" + name)
            .Concat(CompatTestFiles.FixtureNames("invalid", "device").Select(name => "device/" + name));

        Assert.Equal(present.Order(StringComparer.Ordinal), listed.Order(StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ValidFixtures))]
    public void LoadingWritingAndLoadingAgainKeepsEveryValue(CompatLayer layer, string fixture)
    {
        var original = FixtureBytes("valid", layer, fixture);
        var entry = TitleEntryJson.Read(original, layer).Entry!;

        var written = TitleEntryJson.Write(entry, layer);
        var again = TitleEntryJson.Read(written, layer);

        // Every value of the file survives, and writing the entry read back gives the same bytes.
        using var originalDocument = JsonDocument.Parse(original);
        using var writtenDocument = JsonDocument.Parse(written);
        Assert.True(JsonElement.DeepEquals(originalDocument.RootElement, writtenDocument.RootElement));
        Assert.Equal(TitleEntryStatus.Loaded, again.Status);
        Assert.Equal(written, TitleEntryJson.Write(again.Entry!, layer));
    }

    [Fact]
    public void WrittenFileUsesTwoSpaceIndentsAndLineFeeds()
    {
        var text = Encoding.UTF8.GetString(TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Bundled));

        Assert.Equal(
            "{\n  \"schemaVersion\": 1,\n  \"id\": {\n    \"steamAppId\": 4100000310,\n"
            + "    \"slug\": \"copper-orchard\"\n  },\n  \"name\": \"Copper Orchard\"\n}\n",
            text);
    }

    [Fact]
    public void ByteOrderMarkIsSkipped()
    {
        var withMark = Encoding.UTF8.GetPreamble()
            .Concat(TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Bundled))
            .ToArray();

        Assert.Equal(TitleEntryStatus.Loaded, TitleEntryJson.Read(withMark, CompatLayer.Bundled).Status);
    }

    [Fact]
    public void TextThatIsNotUtf8IsRejected()
    {
        var bytes = TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Bundled);
        var name = Array.IndexOf(bytes, (byte)'C');
        bytes[name] = 0xC3;

        var problem = Assert.Single(TitleEntryJson.Read(bytes, CompatLayer.Bundled).Problems);
        Assert.Equal(CompatRule.Json, problem.Rule);
    }

    [Fact]
    public void FileLargerThanOneMebibyteIsRejected()
    {
        var name = new string('x', TitleEntryJson.MaxFileBytes);
        var json = $$"""
            { "schemaVersion": 1, "id": { "steamAppId": 4100000310, "slug": "copper-orchard" }, "name": "{{name}}" }
            """;

        Assert.Equal(CompatRule.Json, Assert.Single(ReadText(json).Problems).Rule);
    }

    [Theory]
    [InlineData("{ \"schemaVersion\": 1 } {}")]
    [InlineData("{ \"schemaVersion\": 1 } x")]
    [InlineData("")]
    [InlineData("   ")]
    public void AnythingButOneObjectIsRejected(string json)
    {
        Assert.Equal(CompatRule.Json, Assert.Single(ReadText(json).Problems).Rule);
    }

    [Fact]
    public void NewerSchemaVersionIsAllThatIsReported()
    {
        // The fixture also adds a field that version 1 lacks; only the version is reported.
        var json = File.ReadAllText(CompatTestFiles.Fixture("invalid", "bundled", "newer-schema-version.json"));
        Assert.Contains("controllerLayout", json, StringComparison.Ordinal);

        Assert.Equal(CompatRule.NewerSchemaVersion, Assert.Single(ReadText(json, CompatLayer.Device).Problems).Rule);
    }

    [Fact]
    public void ProblemsNameTheFieldButNeverTheValue()
    {
        const string Marker = "marker-value-4711";
        const string BadPath = $"\"executable\": {{ \"path\": \"../{Marker}.exe\" }}";
        const string OddField = $"\"a b {Marker}\": 1";
        const string Start = """{ "schemaVersion": 1, "id": { "steamAppId": 4100000310, "slug": "copper-orchard" }""";

        var problem = Assert.Single(ReadText($"{Start}, \"name\": \"Copper Orchard\", {BadPath}, {OddField} }}").Problems);

        // The unknown field is found first, and its name is not shown, because it is no plain name.
        Assert.Equal(CompatRule.UnknownField, problem.Rule);
        Assert.Equal("$.<name not shown>", problem.Path);
        Assert.DoesNotContain(Marker, problem.ToString(), StringComparison.Ordinal);

        var pathProblem = Assert.Single(ReadText($"{Start}, \"name\": \"Copper Orchard\", {BadPath} }}").Problems);
        Assert.Equal("$.executable.path", pathProblem.Path);
        Assert.DoesNotContain(Marker, pathProblem.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FileOfAnotherTitleIsRejected()
    {
        var bytes = TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Bundled);

        Assert.Equal(
            TitleEntryStatus.Loaded, TitleEntryJson.Read(bytes, CompatLayer.Bundled, TestEntries.SteamId).Status);
        foreach (var other in new[] { TitleId.Steam(TestEntries.SteamAppId + 1), TestEntries.LocalId })
        {
            var problem = Assert.Single(TitleEntryJson.Read(bytes, CompatLayer.Bundled, other).Problems);
            Assert.Equal(CompatRule.Identity, problem.Rule);
        }
    }

    [Fact]
    public void WriteRefusesAnEntryThatBreaksARuleOfItsLayer()
    {
        // A device entry needs no name, and a bundled one does.
        var unnamed = TestEntries.Steam(name: null);
        Assert.Throws<ArgumentException>(() => TitleEntryJson.Write(unnamed, CompatLayer.Bundled));
        Assert.NotEmpty(TitleEntryJson.Write(unnamed, CompatLayer.Device));

        var dock = TestEntries.Steam() with { Drm = new() { SteamApi = true }, Launch = LaunchPath.Dock };
        Assert.NotEmpty(TitleEntryJson.Write(dock, CompatLayer.Bundled));
        Assert.Throws<ArgumentException>(() => TitleEntryJson.Write(dock, CompatLayer.Device));
    }

    [Fact]
    public void ValidateChecksAnEntryMadeInCode()
    {
        var entry = TestEntries.Steam() with
        {
            Architecture = (TitleArchitecture)42,
            Runtimes = ["vcredist-v14-x64", null!],
            Environment = new Dictionary<string, string> { ["WINEDEBUG"] = "-all" },
            Graphics = new GraphicsSettings { RenderScale = double.NaN },
            Executable = new ExecutableSpec { Path = "Orchard.exe", Arguments = [null!] },
        };

        Assert.Equal(
            [
                (CompatRule.InvalidValue, "$.executable.arguments[0]"),
                (CompatRule.InvalidValue, "$.architecture"),
                (CompatRule.InvalidValue, "$.runtimes[1]"),
                (CompatRule.InvalidValue, "$.graphics.renderScale"),
                (CompatRule.NotAllowed, "$.environment.WINEDEBUG"),
            ],
            TitleEntryJson.Validate(entry, CompatLayer.Device).Select(problem => (problem.Rule, problem.Path)));
    }

    private static string FolderOf(CompatLayer layer) => layer == CompatLayer.Bundled ? "bundled" : "device";

    private static byte[] FixtureBytes(string validity, CompatLayer layer, string fixture) =>
        File.ReadAllBytes(CompatTestFiles.Fixture(validity, FolderOf(layer), fixture));

    private static TitleEntryResult ReadText(string json, CompatLayer layer = CompatLayer.Bundled) =>
        TitleEntryJson.Read(Encoding.UTF8.GetBytes(json), layer);
}
