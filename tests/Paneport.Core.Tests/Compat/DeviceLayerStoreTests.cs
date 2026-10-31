// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Compat;
using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Compat;

// S6.4.4: the device layer store in a temporary folder. One file per title, named after its id,
// replaced atomically.
public sealed class DeviceLayerStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly DeviceLayerStore _store;

    public DeviceLayerStoreTests()
    {
        _store = new DeviceLayerStore(_folder.Path);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SavedEntryLoadsWithEveryValue()
    {
        var entry = TestEntries.Local() with
        {
            Drm = TestEntries.Clean,
            Architecture = TitleArchitecture.X64,
            Graphics = new GraphicsSettings { RenderScale = 0.5 },
            JitPoolMiB = 768,
        };

        await _store.SaveAsync(entry, Token);
        var loaded = await _store.LoadAsync(TestEntries.LocalId, Token);

        Assert.Equal(TitleEntryStatus.Loaded, loaded.Status);
        Assert.Equal(
            TitleEntryJson.Write(entry, CompatLayer.Device), TitleEntryJson.Write(loaded.Entry!, CompatLayer.Device));
    }

    [Fact]
    public async Task EachTitleHasOneFileNamedAfterItsId()
    {
        await _store.SaveAsync(TestEntries.Steam(), Token);
        await _store.SaveAsync(TestEntries.Local(), Token);
        await _store.SaveAsync(TestEntries.Steam(name: "Copper Orchard, renamed"), Token);

        Assert.Equal(
            ["local-copper-orchard.json", "steam-4100000310.json"],
            Directory.EnumerateFiles(_folder.Path)
                .Select(file => Path.GetFileName(file))
                .Order(StringComparer.Ordinal));
        Assert.Equal("Copper Orchard, renamed", (await _store.LoadAsync(TestEntries.SteamId, Token)).Entry!.Name);
    }

    [Fact]
    public async Task WrittenFileIsWhatTheWriterGives()
    {
        await _store.SaveAsync(TestEntries.Steam(), Token);

        Assert.Equal(
            TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Device),
            File.ReadAllBytes(_folder.FileOf("steam-4100000310.json")));
    }

    [Fact]
    public async Task FailedWriteLeavesTheOldFile()
    {
        await _store.SaveAsync(TestEntries.Steam(name: "Old name"), Token);
        var file = _folder.FileOf("steam-4100000310.json");
        var before = File.ReadAllBytes(file);

        // A folder where the store writes its temporary file makes the write fail.
        Directory.CreateDirectory(file + ".tmp");
        var failure = await Record.ExceptionAsync(() => _store.SaveAsync(TestEntries.Steam(name: "New name"), Token));

        Assert.True(failure is IOException or UnauthorizedAccessException, failure?.GetType().Name);
        Assert.Equal(before, File.ReadAllBytes(file));
        Assert.Equal("Old name", (await _store.LoadAsync(TestEntries.SteamId, Token)).Entry!.Name);
    }

    [Fact]
    public async Task CancelledSaveLeavesTheOldFile()
    {
        await _store.SaveAsync(TestEntries.Steam(name: "Old name"), Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _store.SaveAsync(TestEntries.Steam(name: "New name"), new CancellationToken(canceled: true)));

        Assert.Equal("Old name", (await _store.LoadAsync(TestEntries.SteamId, Token)).Entry!.Name);
        Assert.Single(Directory.EnumerateFiles(_folder.Path));
    }

    [Fact]
    public async Task EntryThatBreaksADeviceRuleIsNotSaved()
    {
        var reviewedOnly = TestEntries.Steam() with { Rating = CompatRating.Playable };

        await Assert.ThrowsAsync<ArgumentException>(() => _store.SaveAsync(reviewedOnly, Token));

        Assert.Empty(Directory.EnumerateFiles(_folder.Path));
    }

    [Fact]
    public async Task RejectedFileStaysUntilASaveReplacesIt()
    {
        // A newer build wrote this file; this build cannot read it and leaves it alone.
        var newer = Encoding.UTF8.GetBytes(
            """{ "schemaVersion": 2, "id": { "steamAppId": 4100000310, "slug": "copper-orchard" } }""");
        var file = _folder.FileOf("steam-4100000310.json");
        File.WriteAllBytes(file, newer);

        var rejected = await _store.LoadAsync(TestEntries.SteamId, Token);

        Assert.Equal(TitleEntryStatus.Rejected, rejected.Status);
        Assert.Equal(CompatRule.NewerSchemaVersion, Assert.Single(rejected.Problems).Rule);
        Assert.Equal(newer, File.ReadAllBytes(file));

        await _store.SaveAsync(TestEntries.Steam(), Token);
        Assert.Equal(TitleEntryStatus.Loaded, (await _store.LoadAsync(TestEntries.SteamId, Token)).Status);
    }

    [Fact]
    public async Task FileWhoseIdIsAnotherTitlesIsRejected()
    {
        var body = TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Device);
        File.WriteAllBytes(_folder.FileOf("steam-4100000311.json"), body);

        var result = await _store.LoadAsync(TitleId.Steam(4_100_000_311), Token);

        Assert.Equal(CompatRule.Identity, Assert.Single(result.Problems).Rule);
    }

    [Fact]
    public async Task DeleteRemovesTheTitlesFile()
    {
        await _store.SaveAsync(TestEntries.Steam(), Token);
        await _store.SaveAsync(TestEntries.Local(), Token);

        Assert.True(await _store.DeleteAsync(TestEntries.SteamId, Token));
        Assert.False(await _store.DeleteAsync(TestEntries.SteamId, Token));

        Assert.Equal(TitleEntryStatus.Missing, (await _store.LoadAsync(TestEntries.SteamId, Token)).Status);
        Assert.Equal(TitleEntryStatus.Loaded, (await _store.LoadAsync(TestEntries.LocalId, Token)).Status);
    }

    [Fact]
    public async Task MissingFolderHoldsNoEntries()
    {
        using var store = new DeviceLayerStore(_folder.FileOf("not-created-yet"));

        Assert.Equal(TitleEntryStatus.Missing, (await store.LoadAsync(TestEntries.SteamId, Token)).Status);
        Assert.Empty(await store.LoadAllAsync(Token));
        Assert.False(await store.DeleteAsync(TestEntries.SteamId, Token));

        await store.SaveAsync(TestEntries.Steam(), Token);
        Assert.Equal(TitleEntryStatus.Loaded, (await store.LoadAsync(TestEntries.SteamId, Token)).Status);
    }

    [Fact]
    public async Task LoadAllReadsTitleFilesAndRejectsOtherNames()
    {
        await _store.SaveAsync(TestEntries.Steam(), Token);
        await _store.SaveAsync(TestEntries.Local(), Token);
        var body = TitleEntryJson.Write(TestEntries.Steam(), CompatLayer.Device);

        // Names that differ from each other in more than letter case, because the file systems of
        // Windows and macOS ignore it.
        string[] badNames =
            ["Steam-4100000313.json", "steam-04100000310.json", "local-Copper.json", "notes.json", "steam-4100000312.JSON"];
        foreach (var name in badNames)
        {
            File.WriteAllBytes(_folder.FileOf(name), body);
        }

        // Other files are not title files: a leftover temporary file and a text file.
        File.WriteAllBytes(_folder.FileOf("steam-4100000310.json.tmp"), body);
        File.WriteAllText(_folder.FileOf("README.md"), "Not a title file.");

        var files = await _store.LoadAllAsync(Token);

        Assert.Equal(
            [
                "Steam-4100000313.json", "local-Copper.json", "local-copper-orchard.json", "notes.json",
                "steam-04100000310.json", "steam-4100000310.json", "steam-4100000312.JSON",
            ],
            files.Select(file => file.FileName));
        foreach (var file in files)
        {
            if (file.FileName is "local-copper-orchard.json" or "steam-4100000310.json")
            {
                Assert.Equal(TitleEntryStatus.Loaded, file.Result.Status);
                Assert.Equal(file.FileName, file.Id + ".json");
            }
            else
            {
                Assert.Null(file.Id);
                Assert.Equal(CompatRule.Identity, Assert.Single(file.Result.Problems).Rule);
            }
        }
    }

    public void Dispose()
    {
        _store.Dispose();
        _folder.Dispose();
    }
}
