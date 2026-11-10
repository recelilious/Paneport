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
    // A reviewed classification that allows the Direct path on its own.
    private static readonly DrmClassification s_allowsDirect = new()
    {
        SteamApi = false,
        SteamStub = false,
        Ceg = CegStatus.No,
    };

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
        // The reviewed entry sets only ceg by hand (C-0007 section 5) and names the program that the
        // Direct path starts; the device computes the rest.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
        var bundled = Reviewed(id, new DrmClassification { Ceg = CegStatus.No });
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
    public void DirectPathNeedsAProgramToStart(TitleSource source, LaunchDecision closed)
    {
        // The Direct path starts the program that an entry names (C-0007 section 5). Where no layer
        // names one, a classification that allows the Direct path gives the closed path instead. A
        // local entry without a program breaks its layer's rules; the merge does not rely on them.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
        var unnamed = Reviewed(id, s_allowsDirect) with { Executable = null };
        var device = TestEntries.ForId(id) with { Executable = null, Drm = TestEntries.Clean };

        var settings = CompatPrecedence.Merge(id, unnamed, device);

        Assert.Null(settings.Executable);
        Assert.Equal(ReviewedClassificationUse.Used, settings.ReviewedClassification);
        Assert.Equal(LaunchDecision.Direct, LaunchRule.Decide(source, settings.Drm));
        Assert.Equal(closed, settings.Launch);

        // The same classification with the reviewed program gives the Direct path.
        Assert.Equal(LaunchDecision.Direct, CompatPrecedence.Merge(id, Reviewed(id, s_allowsDirect), device).Launch);
    }

    [Fact]
    public void ReviewedSteamProgramNeedsNoDeviceEntry()
    {
        // A device layer without an entry names no other program, so a reviewed entry that names the
        // program and sets every member that the launch rule reads gives the Direct path on its own.
        var settings = CompatPrecedence.Merge(TestEntries.SteamId, Reviewed(TestEntries.SteamId, s_allowsDirect), null);

        Assert.Equal(ReviewedClassificationUse.Used, settings.ReviewedClassification);
        Assert.Equal("Orchard.exe", settings.Executable!.Path);
        Assert.Equal(LaunchDecision.Direct, settings.Launch);
    }

    [Fact]
    public void ReviewedDrmCoversOnlyTheReviewedProgram()
    {
        // The reviewed drm counts only while the device runs the reviewed program (C-0008 section 1).
        // A device executable with another path, here a launcher with SteamStub, is another program,
        // and only its computed classification counts.
        var bundled = TestEntries.Steam() with
        {
            Executable = new ExecutableSpec { Path = "Orchard.exe" },
            Drm = new DrmClassification { SteamApi = false, SteamStub = false, Ceg = CegStatus.No },
        };
        var computed = TestEntries.Clean with { SteamStub = true };
        var launcher = TestEntries.Steam() with
        {
            Executable = new ExecutableSpec { Path = "Launcher.exe" },
            Drm = computed,
        };

        var settings = CompatPrecedence.Merge(TestEntries.SteamId, bundled, launcher);

        Assert.Equal(computed, settings.Drm);
        Assert.Equal(ReviewedClassificationUse.OtherProgram, settings.ReviewedClassification);
        Assert.Equal(LaunchDecision.Dock, settings.Launch);

        // The same path with other arguments is the reviewed program, and so is no device executable.
        // The reviewed values then win over the computed ones.
        var samePath = launcher with { Executable = new ExecutableSpec { Path = "Orchard.exe", Arguments = ["-x"] } };
        var noExecutable = launcher with { Executable = null };
        foreach (var device in new[] { samePath, noExecutable })
        {
            var reviewed = CompatPrecedence.Merge(TestEntries.SteamId, bundled, device);
            Assert.Equal(ReviewedClassificationUse.Used, reviewed.ReviewedClassification);
            Assert.Equal(LaunchDecision.Direct, reviewed.Launch);
        }

        // Another letter case is another path, and a reviewed entry that names no program covers none
        // that the device names.
        var otherCase = launcher with { Executable = new ExecutableSpec { Path = "orchard.exe" } };
        var unnamed = bundled with { Executable = null };
        foreach (var (reviewedEntry, device) in new[] { (bundled, otherCase), (unnamed, samePath) })
        {
            var other = CompatPrecedence.Merge(TestEntries.SteamId, reviewedEntry, device);
            Assert.Equal(ReviewedClassificationUse.OtherProgram, other.ReviewedClassification);
            Assert.Equal(LaunchDecision.Dock, other.Launch);
        }

        // Without a reviewed drm there is nothing to use.
        foreach (var reviewedEntry in new[] { bundled with { Drm = null }, null })
        {
            var none = CompatPrecedence.Merge(TestEntries.SteamId, reviewedEntry, launcher);
            Assert.Equal(ReviewedClassificationUse.None, none.ReviewedClassification);
        }
    }

    [Fact]
    public void ReviewedDrmCoversOnlyTheReviewedBuildsOfALocalTitle()
    {
        // A local title's device entry pairs with the bundled entry by its slug; the reviewed drm counts
        // only for builds whose hash the bundled entry lists.
        var otherBuild = new string('c', 64);
        var secondBuild = new string('d', 64);
        var bundled = TestEntries.Local() with
        {
            Id = TestEntries.Local().Id with { ExeSha256 = [TestEntries.LocalHash, secondBuild] },
            Drm = new DrmClassification { SteamApi = false, SteamStub = false, Ceg = CegStatus.No },
        };
        var device = TestEntries.Local() with { Drm = TestEntries.Clean };
        var moved = new ExecutableSpec { Path = "bin/Orchard.exe" };

        // A reviewed build keeps the reviewed classification, with or without a program of its own:
        // its hash, not its path, shows that it is the reviewed program.
        foreach (var reviewedBuild in new[] { device, device with { Executable = moved } })
        {
            var settings = CompatPrecedence.Merge(TestEntries.LocalId, bundled, reviewedBuild);
            Assert.Equal(ReviewedClassificationUse.Used, settings.ReviewedClassification);
            Assert.Equal(LaunchDecision.Direct, settings.Launch);
        }

        // A build the entry does not list is refused, alone, next to a reviewed one, or with a program
        // of its own.
        var unreviewedBuild = device with { Id = device.Id with { ExeSha256 = [otherBuild] } };
        foreach (var unreviewed in new[]
        {
            unreviewedBuild,
            device with { Id = device.Id with { ExeSha256 = [TestEntries.LocalHash, otherBuild] } },
            unreviewedBuild with { Executable = moved },
        })
        {
            var settings = CompatPrecedence.Merge(TestEntries.LocalId, bundled, unreviewed);
            Assert.Equal(TestEntries.Clean, settings.Drm);
            Assert.Equal(ReviewedClassificationUse.UnreviewedBuild, settings.ReviewedClassification);
            Assert.Equal(LaunchDecision.Refused, settings.Launch);
        }

        // Without a device entry nothing shows which build the device runs, so the title waits for the
        // classifier.
        var unclassified = CompatPrecedence.Merge(TestEntries.LocalId, bundled, null);
        Assert.Null(unclassified.Drm);
        Assert.Equal(ReviewedClassificationUse.UnreviewedBuild, unclassified.ReviewedClassification);
        Assert.Equal(LaunchDecision.Unclassified, unclassified.Launch);
    }

    [Fact]
    public void LocalDeviceEntryThatListsNoBuildRunsNoReviewedBuild()
    {
        // A local device entry shows the build that the device runs by its exeSha256 values. An empty
        // list, or none, breaks the device layer's rules, and the merge still finds no reviewed build.
        var bundled = Reviewed(TestEntries.LocalId, s_allowsDirect);
        var device = TestEntries.Local() with { Drm = TestEntries.Clean };
        foreach (var builds in new IReadOnlyList<string>?[] { [], null })
        {
            var unlisted = device with { Id = device.Id with { ExeSha256 = builds } };
            Assert.NotEmpty(TitleEntryJson.Validate(unlisted, CompatLayer.Device));

            var settings = CompatPrecedence.Merge(TestEntries.LocalId, bundled, unlisted);

            Assert.Equal(TestEntries.Clean, settings.Drm);
            Assert.Equal(ReviewedClassificationUse.UnreviewedBuild, settings.ReviewedClassification);
            Assert.Equal(LaunchDecision.Refused, settings.Launch);
        }
    }

    [Theory]
    [InlineData(TitleSource.Steam)]
    [InlineData(TitleSource.Local)]
    public void AnotherProgramWithoutAComputedClassificationWaitsForTheClassifier(TitleSource source)
    {
        // A device entry for another program or an unreviewed build, saved before the classifier has
        // run, holds no drm. The reviewed drm still does not count, so the title is Unclassified, not
        // Direct.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;

        var settings = CompatPrecedence.Merge(id, Reviewed(id, s_allowsDirect), AnotherProgram(source));

        Assert.Null(settings.Drm);
        Assert.Equal(NotTheReviewedProgram(source), settings.ReviewedClassification);
        Assert.Equal(LaunchDecision.Unclassified, settings.Launch);
    }

    [Theory]
    [InlineData(TitleSource.Steam, LaunchDecision.Dock)]
    [InlineData(TitleSource.Local, LaunchDecision.Refused)]
    public void ReviewedBlockersKeepTheDirectPathClosedForAnotherProgram(TitleSource source, LaunchDecision closed)
    {
        // Where the reviewed drm does not count, only the computed classification does, and its ceg is
        // never no. A title whose reviewed classification blocks the Direct path therefore stays
        // blocked, even with an unchecked device entry that claims ceg no.
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
        foreach (var blocker in new[]
        {
            new DrmClassification { SteamApi = true, Ceg = CegStatus.No },
            new DrmClassification { SteamStub = true, Ceg = CegStatus.No },
            new DrmClassification { Ceg = CegStatus.Unknown },
            new DrmClassification { Ceg = CegStatus.Yes },
        })
        {
            foreach (var computed in new[] { TestEntries.Clean, TestEntries.Clean with { Ceg = CegStatus.No } })
            {
                var device = AnotherProgram(source) with { Drm = computed };

                var settings = CompatPrecedence.Merge(id, Reviewed(id, blocker), device);

                Assert.Equal(NotTheReviewedProgram(source), settings.ReviewedClassification);
                Assert.Equal(closed, settings.Launch);
            }
        }
    }

    [Theory]
    [InlineData(TitleSource.Steam)]
    [InlineData(TitleSource.Local)]
    public void OnlyDrmDependsOnTheReviewedProgram(TitleSource source)
    {
        // Where the device runs another program or an unreviewed build, every reviewed setting but drm
        // still applies (docs/design/compat-db.md section 10).
        var id = source == TitleSource.Steam ? TestEntries.SteamId : TestEntries.LocalId;
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
        var bundled = Reviewed(id, s_allowsDirect) with
        {
            Prefix = PrefixMode.Isolated,
            Rating = CompatRating.Boots,
            Reports = [report],
        };
        var device = AnotherProgram(source) with { Drm = TestEntries.Clean };

        var settings = CompatPrecedence.Merge(id, bundled, device);

        Assert.Equal(NotTheReviewedProgram(source), settings.ReviewedClassification);
        Assert.Equal(TestEntries.Clean, settings.Drm);
        Assert.Equal("Copper Orchard", settings.Name);
        Assert.Equal(PrefixMode.Isolated, settings.Prefix);
        Assert.Equal(CompatRating.Boots, settings.Rating);
        Assert.Equal([report], settings.Reports);

        // A device executable replaces the reviewed one as a whole; a device entry without one keeps it.
        Assert.Equal(source == TitleSource.Steam ? "Launcher.exe" : "Orchard.exe", settings.Executable!.Path);
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

    // A reviewed entry that names the program Orchard.exe, as TestEntries.Local() does, with its drm.
    private static TitleEntry Reviewed(TitleId id, DrmClassification? drm) =>
        TestEntries.ForId(id) with { Executable = new ExecutableSpec { Path = "Orchard.exe" }, Drm = drm };

    // A device entry with no classification yet for another program than the reviewed one: a launcher
    // for the Steam title, and a build that the reviewed entry does not list for the local title.
    private static TitleEntry AnotherProgram(TitleSource source) => source == TitleSource.Steam
        ? TestEntries.Steam(name: null) with { Executable = new ExecutableSpec { Path = "Launcher.exe" } }
        : TestEntries.Local(name: null) with
        {
            Id = TestEntries.Local().Id with { ExeSha256 = [new string('c', 64)] },
            Executable = null,
        };

    private static ReviewedClassificationUse NotTheReviewedProgram(TitleSource source) => source == TitleSource.Steam
        ? ReviewedClassificationUse.OtherProgram
        : ReviewedClassificationUse.UnreviewedBuild;
}
