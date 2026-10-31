// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Compat;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// The database over the bundled snapshot and the device layer, and the repository's own titles.
public sealed class CompatDatabaseTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task LoadsBothLayersAndMergesThem()
    {
        using var device = new TemporaryFolder().WithFixtures("valid", "device");
        using var store = new DeviceLayerStore(device.Path);
        var database = new CompatDatabase(new BundledSnapshot(CompatTestFiles.Fixture("valid", "bundled")), store);

        var title = await database.LoadAsync(TitleId.Steam(4_100_000_120), Token);

        Assert.Equal(TitleEntryStatus.Loaded, title.Bundled.Status);
        Assert.Equal(TitleEntryStatus.Loaded, title.Device.Status);
        var settings = title.Effective;
        Assert.Equal("Quarry of the Nine Kites", settings.Name);
        Assert.Equal(768, settings.JitPoolMiB);
        Assert.Equal(0.5, settings.RenderScale);
        Assert.Equal(MetalFxMode.Spatial, settings.MetalFx);
        Assert.Equal(30, settings.FrameRateCap);
        Assert.Equal(PrefixMode.Shared, settings.Prefix);
        Assert.Equal(CompatRating.Playable, settings.Rating);
        Assert.Equal(2, settings.Reports.Count);

        // The reviewed steamApi true wins over the computed false, so the title takes the Dock path.
        Assert.True(settings.Drm!.SteamApi);
        Assert.Equal(CegStatus.No, settings.Drm.Ceg);
        Assert.Equal(LaunchDecision.Dock, settings.Launch);
    }

    [Fact]
    public async Task LocalTitleTakesTheDirectPathWithTheReviewedClassification()
    {
        using var device = new TemporaryFolder().WithFixtures("valid", "device");
        using var store = new DeviceLayerStore(device.Path);
        var database = new CompatDatabase(new BundledSnapshot(CompatTestFiles.Fixture("valid", "bundled")), store);

        var settings = (await database.LoadAsync(TitleId.Local("lantern-garden"), Token)).Effective;

        Assert.Equal("Lantern Garden (imported)", settings.Name);
        Assert.Equal("LanternGarden.exe", settings.Executable!.Path);
        Assert.Equal(["--fullscreen"], settings.Executable.Arguments!);
        Assert.Empty(settings.Runtimes);
        Assert.Equal(LaunchDecision.Direct, settings.Launch);
    }

    [Fact]
    public async Task RejectedLayerAddsNothingAndIsReported()
    {
        using var device = new TemporaryFolder();
        File.Copy(CompatTestFiles.Fixture("invalid", "device", "ceg-no.json"), device.FileOf("steam-4100000210.json"));
        using var store = new DeviceLayerStore(device.Path);
        var database = new CompatDatabase(new BundledSnapshot(device.FileOf("no-snapshot")), store);

        var title = await database.LoadAsync(TitleId.Steam(4_100_000_210), Token);

        Assert.Equal(TitleEntryStatus.Missing, title.Bundled.Status);
        Assert.Equal(TitleEntryStatus.Rejected, title.Device.Status);
        Assert.Equal(CompatRule.DeviceLayer, Assert.Single(title.Device.Problems).Rule);
        Assert.Null(title.Effective.Drm);
        Assert.Equal(LaunchDecision.Unclassified, title.Effective.Launch);
    }

    [Fact]
    public async Task TitleWithNoEntryShowsAsUntested()
    {
        using var device = new TemporaryFolder();
        using var store = new DeviceLayerStore(device.Path);
        var database = new CompatDatabase(new BundledSnapshot(CompatTestFiles.Fixture("valid", "bundled")), store);

        var title = await database.LoadAsync(TitleId.Steam(4_100_000_999), Token);

        Assert.Equal(TitleEntryStatus.Missing, title.Bundled.Status);
        Assert.Equal(TitleEntryStatus.Missing, title.Device.Status);
        Assert.Equal(CompatRating.Untested, title.Effective.Rating);
    }

    [Fact]
    public async Task EveryFileInCompatTitlesLoads()
    {
        // The repository's own entries pass the loader's checks in the dotnet job until M8.3 checks
        // them against the schema as well. The README proves that the copy of the folder is in place.
        var folder = CompatTestFiles.Repository("compat", "titles");
        Assert.True(File.Exists(Path.Combine(folder, "README.md")));

        var files = await new BundledSnapshot(folder).LoadAllAsync(Token);

        Assert.All(files, file => Assert.True(
            file.Result.Status == TitleEntryStatus.Loaded,
            $"{file.FileName}: {string.Join("; ", file.Result.Problems)}"));
    }
}
