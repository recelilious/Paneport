// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Compat;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// S6.4.3: the precedence rules of C-0008 section 1, one test for each rule and each exception,
// and how objects, lists and maps merge (docs/design/compat-db.md section 4).
public sealed class CompatPrecedenceTests
{
    [Theory]
    [InlineData(TitleSource.Steam)]
    [InlineData(TitleSource.Local)]
    public void TitleWithNoEntryRunsWithTheSchemaDefaultsAndShowsUntested(TitleSource source)
    {
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;

        var settings = CompatPrecedence.Merge(id, null, null);

        Assert.Equal(id, settings.Id);
        Assert.Equal(CompatRating.Untested, settings.Rating);
        Assert.Equal(PrefixMode.Shared, settings.Prefix);
        Assert.Equal(1.0, settings.RenderScale);
        Assert.Equal(MetalFxMode.Off, settings.MetalFx);
        Assert.Null(settings.Drm);
        Assert.Equal(LaunchDecision.Unclassified, settings.Launch);
        Assert.Null(settings.Name);
        Assert.Null(settings.Executable);
        Assert.Null(settings.JitPoolMiB);
        Assert.Null(settings.FrameRateCap);
    }

    [Fact]
    public void BundledEntryOverridesTheDefaults()
    {
        var bundled = TestEntries.Steam() with
        {
            Prefix = PrefixMode.Isolated,
            Rating = CompatRating.Playable,
            Graphics = new GraphicsSettings { RenderScale = 0.75, MetalFx = MetalFxMode.Spatial },
            JitPoolMiB = 640,
        };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, null);

        Assert.Equal("Copper Orchard", settings.Name);
        Assert.Equal(PrefixMode.Isolated, settings.Prefix);
        Assert.Equal(CompatRating.Playable, settings.Rating);
        Assert.Equal(0.75, settings.RenderScale);
        Assert.Equal(MetalFxMode.Spatial, settings.MetalFx);
        Assert.Equal(640, settings.JitPoolMiB);
    }

    [Fact]
    public void DeviceLayerOverridesTheBundledEntry()
    {
        var bundled = TestEntries.Steam() with
        {
            Architecture = TitleArchitecture.X64,
            Prefix = PrefixMode.Isolated,
            JitPoolMiB = 640,
            MemoryEstimateMiB = 2000,
        };
        var device = TestEntries.Steam(name: "Copper Orchard (mine)") with
        {
            Architecture = TitleArchitecture.X86,
            Prefix = PrefixMode.Shared,
            JitPoolMiB = 768,
        };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        Assert.Equal("Copper Orchard (mine)", settings.Name);
        Assert.Equal(TitleArchitecture.X86, settings.Architecture);
        Assert.Equal(PrefixMode.Shared, settings.Prefix);
        Assert.Equal(768, settings.JitPoolMiB);

        // What the device layer leaves out, the bundled entry still sets.
        Assert.Equal(2000, settings.MemoryEstimateMiB);
    }

    [Fact]
    public void ExecutableIsReplacedAsAWhole()
    {
        var bundled = TestEntries.Steam() with
        {
            Executable = new ExecutableSpec
            {
                Path = "Orchard.exe",
                WorkingDirectory = "bin",
                Arguments = ["-reviewed"],
            },
        };
        var device = TestEntries.Steam() with { Executable = new ExecutableSpec { Path = "Tools/Editor.exe" } };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        // The reviewed working directory and arguments belong to the reviewed program.
        Assert.Equal("Tools/Editor.exe", settings.Executable!.Path);
        Assert.Null(settings.Executable.WorkingDirectory);
        Assert.Empty(settings.Executable.Arguments!);
    }

    [Fact]
    public void GraphicsSettingsMergeMemberByMember()
    {
        var bundled = TestEntries.Steam() with { Graphics = new() { RenderScale = 0.75, FrameRateCap = 30 } };
        var device = TestEntries.Steam() with { Graphics = new() { RenderScale = 0.5, MetalFx = MetalFxMode.Spatial } };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        Assert.Equal(0.5, settings.RenderScale);
        Assert.Equal(MetalFxMode.Spatial, settings.MetalFx);
        Assert.Equal(30, settings.FrameRateCap);
    }

    [Fact]
    public void ListsAreReplacedWholeAndAnEmptyListReplacesToo()
    {
        var bundled = TestEntries.Steam() with
        {
            Runtimes = ["vcredist-v14-x64"],
            GraphicsApi = [GraphicsApi.Direct3D11, GraphicsApi.Direct3D9],
            HelperPolicy = new HelperPolicy { BlockedExecutables = ["Reporter.exe"] },
        };
        var device = TestEntries.Steam() with
        {
            Runtimes = [],
            GraphicsApi = [GraphicsApi.Vulkan],
        };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        Assert.Empty(settings.Runtimes);
        Assert.Equal([GraphicsApi.Vulkan], settings.GraphicsApi);
        Assert.Equal(["Reporter.exe"], settings.BlockedExecutables);
    }

    [Fact]
    public void TranslatorOptionsAndEnvironmentMergeKeyByKey()
    {
        // The allow-lists are empty for now, so these entries would fail their checks; the merge
        // itself does not check, and the keys show how it treats them.
        var bundled = TestEntries.Steam() with
        {
            Translator = new Dictionary<string, string> { ["OptionA"] = "1", ["OptionB"] = "1" },
            Environment = new Dictionary<string, string> { ["VARIABLE_A"] = "reviewed" },
        };
        var device = TestEntries.Steam() with
        {
            Translator = new Dictionary<string, string> { ["OptionB"] = "0" },
            Environment = new Dictionary<string, string> { ["VARIABLE_B"] = "mine" },
        };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        Assert.Equal(new Dictionary<string, string> { ["OptionA"] = "1", ["OptionB"] = "0" }, settings.Translator);
        Assert.Equal(
            new Dictionary<string, string> { ["VARIABLE_A"] = "reviewed", ["VARIABLE_B"] = "mine" }, settings.Environment);
    }

    [Fact]
    public void RatingAndReportsComeOnlyFromTheBundledEntry()
    {
        var report = new TestReport
        {
            Date = new DateOnly(2026, 11, 2),
            Device = "iPhone18,1",
            IosVersion = "27.2 (24B119)",
            PaneportVersion = "v0.1.0",
            GameBuild = "12345678",
            Rating = CompatRating.Boots,
            Evidence = "docs/evidence/2026-11-02-copper-orchard.md",
        };
        var bundled = TestEntries.Steam() with { Rating = CompatRating.Boots, Reports = [report] };

        // A device entry with a rating breaks its layer's rules; even unchecked, it adds nothing.
        var device = TestEntries.Steam() with { Rating = CompatRating.Perfect, Reports = [] };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);

        Assert.Equal(CompatRating.Boots, settings.Rating);
        Assert.Equal([report], settings.Reports);
        Assert.Equal(CompatRating.Untested, CompatPrecedence.Merge(TestEntries.SteamId, null, device).Rating);
    }

    [Fact]
    public void DrmValueOfTheBundledEntryWinsOverTheComputedOne()
    {
        // Exception 1 of C-0008 section 1, member by member.
        var bundled = TestEntries.Steam() with { Drm = new() { SteamApi = false, SteamAppIdFile = false } };
        var device = TestEntries.Steam() with
        {
            Drm = new() { SteamApi = true, SteamStub = false, SteamAppIdFile = true, Ceg = CegStatus.Unknown },
        };

        var drm = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device).Drm!;

        Assert.False(drm.SteamApi);
        Assert.False(drm.SteamAppIdFile);
        Assert.False(drm.SteamStub);
        Assert.Equal(CegStatus.Unknown, drm.Ceg);
    }

    [Theory]
    [InlineData(TitleSource.Steam)]
    [InlineData(TitleSource.Local)]
    public void ReviewedCegAndComputedMembersTogetherGiveTheDirectPath(TitleSource source)
    {
        // The reviewed entry sets only ceg by hand (C-0007 section 5); the device computes the rest.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
        var bundled = TestEntries.ForId(id) with { Drm = new DrmClassification { Ceg = CegStatus.No } };
        var device = TestEntries.ForId(id) with { Drm = TestEntries.Clean };

        Assert.Equal(LaunchDecision.Unclassified, CompatPrecedence.Merge(id, bundled, null).Launch);

        var settings = CompatPrecedence.Merge(id, bundled, device);

        Assert.Equal(
            new DrmClassification { SteamApi = false, SteamStub = false, SteamAppIdFile = false, Ceg = CegStatus.No },
            settings.Drm);
        Assert.Equal(LaunchDecision.Direct, settings.Launch);
    }

    [Theory]
    [InlineData(TitleSource.Steam, LaunchDecision.Dock)]
    [InlineData(TitleSource.Local, LaunchDecision.Refused)]
    public void DeviceLayerCannotTurnTheLaunchPathIntoDirect(TitleSource source, LaunchDecision closed)
    {
        // Exception 2 of C-0008 section 1. The bundled entry's classification requires the Dock path,
        // and a clean computed classification does not change that.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
        var clean = TestEntries.ForId(id) with { Drm = TestEntries.Clean with { Ceg = null } };
        foreach (var reviewed in new[]
        {
            new DrmClassification { SteamApi = true, Ceg = CegStatus.No },
            new DrmClassification { SteamStub = true, Ceg = CegStatus.No },
            new DrmClassification { Ceg = CegStatus.Unknown },
            new DrmClassification { Ceg = CegStatus.Yes },
        })
        {
            var bundled = TestEntries.ForId(id) with { Drm = reviewed };
            Assert.Equal(closed, CompatPrecedence.Merge(id, bundled, clean).Launch);
        }

        // Without a reviewed ceg, the device layer cannot set it: the merge takes only unknown from it,
        // even from an entry that skipped its checks.
        var claimsNoCeg = TestEntries.ForId(id) with { Drm = TestEntries.Clean with { Ceg = CegStatus.No } };
        Assert.NotEmpty(TitleEntryJson.Validate(claimsNoCeg, CompatLayer.Device));
        Assert.Equal(closed, CompatPrecedence.Merge(id, null, claimsNoCeg).Launch);
        Assert.Equal(closed, CompatPrecedence.Merge(id, TestEntries.ForId(id), claimsNoCeg).Launch);

        // A launch path that an unchecked device entry states is not read either.
        var claimsDirect = TestEntries.ForId(id) with { Drm = TestEntries.Clean, Launch = LaunchPath.Direct };
        Assert.NotEmpty(TitleEntryJson.Validate(claimsDirect, CompatLayer.Device));
        Assert.Equal(closed, CompatPrecedence.Merge(id, null, claimsDirect).Launch);
    }

    [Fact]
    public void EntryOfAnotherTitleIsRefused()
    {
        Assert.Throws<ArgumentException>(() => CompatPrecedence.Merge(TestEntries.LocalId, TestEntries.Steam(), null));
        Assert.Throws<ArgumentException>(() => CompatPrecedence.Merge(TestEntries.SteamId, null, TestEntries.Local()));
    }
}
