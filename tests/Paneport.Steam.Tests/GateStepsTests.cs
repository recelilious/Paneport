// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using Paneport.Steam.Content;
using Paneport.Steam.Ports;
using Paneport.Steam.Tests.Fakes;
using Xunit;
using static Paneport.Steam.Tests.Fakes.SyntheticContent;

namespace Paneport.Steam.Tests;

// S2.3.5 and S2.3.6: a missing key or a code of 0 gives "not granted"; a synthetic manifest gives
// the right file count, chunk count and size; a chunk that does not match its manifest entry is
// rejected and never written, and a matching synthetic chunk passes (steam-service.md sections 6
// and 7).
public sealed class GateStepsTests : IDisposable
{
    private readonly SteamTestKit _kit = new();

    public GateStepsTests()
    {
        _kit.Apps.Infos[AppId] = AppInfo();
        _kit.Apps.DepotKey = new DepotKeyResult(SteamResult.OK, _kit.Samples.DepotKey);
        _kit.Content.ManifestCode = _kit.Samples.ManifestCode;
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _kit.Dispose();

    [Fact]
    public async Task EveryStepNeedsALogon()
    {
        var exception = await Assert.ThrowsAsync<SteamStepException>(() => _kit.Gate.CountLicensesAsync(Token));

        Assert.Equal("Sign in first.", exception.Message);
    }

    [Fact]
    public async Task CountsTheLicensesThatSteamSent()
    {
        await _kit.LogOnAsync();
        _kit.Connection.Listener.OnLicenseList(SteamResult.OK, [1, 2, 3, 4]);

        Assert.Equal(4, await _kit.Gate.CountLicensesAsync(Token));
    }

    [Fact]
    public async Task ReadsTheAppInformationWithItsAccessToken()
    {
        await _kit.LogOnAsync();

        var info = await _kit.Gate.ReadAppInfoAsync(AppId, Token);

        Assert.Equal(_kit.Apps.AccessToken, _kit.Apps.AccessTokenAsked);
        Assert.Equal([WindowsDepot, OlderWindowsDepot], info.WindowsDepots.Select(depot => depot.DepotId));
    }

    [Fact]
    public async Task AMissingDepotKeyIsNotGranted()
    {
        await ReadAppInfoAsync();

        _kit.Apps.DepotKey = new DepotKeyResult(SteamResult.AccessDenied, null);
        Assert.Equal(SteamGrant.NotGranted, await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token));
        _kit.Apps.DepotKey = new DepotKeyResult(SteamResult.OK, []);
        Assert.Equal(SteamGrant.NotGranted, await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token));
        _kit.Apps.DepotKey = new DepotKeyResult(SteamResult.OK, _kit.Samples.DepotKey);
        Assert.Equal(SteamGrant.Obtained, await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token));
    }

    [Fact]
    public async Task AManifestRequestCodeOfZeroIsNotGranted()
    {
        await ReadAppInfoAsync();

        _kit.Content.ManifestCode = 0;
        Assert.Equal(SteamGrant.NotGranted, await _kit.Gate.GetManifestRequestCodeAsync(WindowsDepot, Token));
        _kit.Content.ManifestCode = _kit.Samples.ManifestCode;
        Assert.Equal(SteamGrant.Obtained, await _kit.Gate.GetManifestRequestCodeAsync(WindowsDepot, Token));
    }

    [Fact]
    public async Task OnlyAWindowsDepotOfTheAppReadLastIsAccepted()
    {
        await ReadAppInfoAsync();

        var exception = await Assert.ThrowsAsync<SteamStepException>(() => _kit.Gate.GetDepotKeyAsync(MacDepot, Token));

        Assert.Contains("is not a Windows depot", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ASyntheticManifestGivesTheRightCounts()
    {
        var (manifest, _) = Manifest();

        var summary = ManifestSummaries.Summarize(manifest, TimeSpan.FromMilliseconds(250));

        Assert.Equal(WindowsDepot, summary.DepotId);
        Assert.Equal(3, summary.FileCount);
        Assert.Equal(3, summary.ChunkCount);
        Assert.Equal(176UL, summary.TotalSize);
        Assert.Equal(TimeSpan.FromMilliseconds(250), summary.DownloadTime);
    }

    [Fact]
    public async Task TheManifestNeedsTheKeyAndTheCodeOfItsDepot()
    {
        await ReadAppInfoAsync();

        var noKey = await Assert.ThrowsAsync<SteamStepException>(() => _kit.Gate.DownloadManifestAsync(WindowsDepot, Token));
        Assert.Equal("Get the depot key of this depot first.", noKey.Message);
        await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token);
        var noCode = await Assert.ThrowsAsync<SteamStepException>(() => _kit.Gate.DownloadManifestAsync(WindowsDepot, Token));
        Assert.Equal("Get the manifest request code of this depot first.", noCode.Message);
    }

    [Fact]
    public async Task DownloadsAndSummarizesTheManifest()
    {
        await GrantAsync();
        _kit.Content.Manifest = Manifest().Manifest;

        var summary = await _kit.Gate.DownloadManifestAsync(WindowsDepot, Token);

        Assert.Equal((3, 3, 176UL), (summary.FileCount, summary.ChunkCount, summary.TotalSize));
    }

    [Fact]
    public async Task MatchingChunksPassAndAreWritten()
    {
        var chunks = await DownloadManifestAsync();
        foreach (var (entry, content) in chunks)
        {
            _kit.Content.Chunks[entry.IdText] = content;
        }

        var result = await _kit.Gate.DownloadChunksAsync(10, Token);

        Assert.Equal((3, 3, 0, 0), (result.Requested, result.Verified, result.Rejected, result.Failed));
        Assert.Equal(144UL, result.VerifiedBytes);
        Assert.Equal(3, _kit.Folder.Files().Count);
        foreach (var (entry, content) in chunks)
        {
            var file = Path.Combine(_kit.Folder.Path, WindowsDepot.ToString(System.Globalization.CultureInfo.InvariantCulture), entry.IdText);
            Assert.Equal(content, await File.ReadAllBytesAsync(file, Token));
        }
    }

    [Fact]
    public async Task AChunkThatDoesNotMatchItsEntryIsRejectedAndNeverWritten()
    {
        var chunks = await DownloadManifestAsync();
        var (entry, content) = chunks[0];
        var altered = (byte[])content.Clone();
        altered[^1] ^= 0xFF;
        _kit.Content.Chunks[entry.IdText] = altered;

        var result = await _kit.Gate.DownloadChunksAsync(1, Token);

        Assert.Equal((1, 0, 1), (result.Requested, result.Verified, result.Rejected));
        Assert.Empty(_kit.Folder.Files());
    }

    [Fact]
    public async Task AChunkOfTheWrongLengthIsRejectedAndNeverWritten()
    {
        var chunks = await DownloadManifestAsync();
        var (entry, content) = chunks[0];
        _kit.Content.Chunks[entry.IdText] = content[..^1];

        var result = await _kit.Gate.DownloadChunksAsync(1, Token);

        Assert.Equal(1, result.Rejected);
        Assert.Empty(_kit.Folder.Files());
    }

    [Fact]
    public async Task AChunkThatSteamKit2RejectsIsNeverWritten()
    {
        var chunks = await DownloadManifestAsync();
        _kit.Content.Unprocessable.Add(chunks[0].Entry.IdText);

        var result = await _kit.Gate.DownloadChunksAsync(1, Token);

        Assert.Equal(1, result.Rejected);
        Assert.Empty(_kit.Folder.Files());
    }

    [Fact]
    public async Task AChunkThatNoServerGivesCountsAsFailed()
    {
        await DownloadManifestAsync();

        var result = await _kit.Gate.DownloadChunksAsync(2, Token);

        Assert.Equal((2, 0, 0, 2), (result.Requested, result.Verified, result.Rejected, result.Failed));
    }

    [Fact]
    public async Task AServerThatRefusesARequestWithoutATokenGetsOne()
    {
        _kit.Content.RequiredServerToken = _kit.Samples.ServerToken;
        await GrantAsync();
        _kit.Content.Manifest = Manifest().Manifest;

        await _kit.Gate.DownloadManifestAsync(WindowsDepot, Token);

        Assert.Equal([null, _kit.Samples.ServerToken], _kit.Content.TokensUsed);
    }

    [Fact]
    public async Task DeletingTheTestDataRemovesTheFolderAndForgetsTheKey()
    {
        var chunks = await DownloadManifestAsync();
        _kit.Content.Chunks[chunks[0].Entry.IdText] = chunks[0].Content;
        await _kit.Gate.DownloadChunksAsync(1, Token);
        Assert.Single(_kit.Folder.Files());

        await _kit.Gate.DeleteTestDataAsync(Token);

        Assert.False(Directory.Exists(_kit.Folder.Path));
        var exception = await Assert.ThrowsAsync<SteamStepException>(() => _kit.Gate.DownloadChunksAsync(1, Token));
        Assert.Equal("Download the manifest first.", exception.Message);
        Assert.All(_kit.Samples.DepotKey, b => Assert.Equal(0, b));
    }

    private async Task ReadAppInfoAsync()
    {
        await _kit.LogOnAsync();
        await _kit.Gate.ReadAppInfoAsync(AppId, Token);
    }

    private async Task GrantAsync()
    {
        await ReadAppInfoAsync();
        Assert.Equal(SteamGrant.Obtained, await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token));
        Assert.Equal(SteamGrant.Obtained, await _kit.Gate.GetManifestRequestCodeAsync(WindowsDepot, Token));
    }

    // The chunks of the synthetic manifest in the order in which a download takes them.
    private async Task<IReadOnlyList<(ManifestChunk Entry, byte[] Content)>> DownloadManifestAsync()
    {
        await GrantAsync();
        var (manifest, chunks) = Manifest();
        _kit.Content.Manifest = manifest;
        await _kit.Gate.DownloadManifestAsync(WindowsDepot, Token);
        return [.. ManifestSummaries.DistinctChunks(manifest).Select(entry => chunks.Single(chunk => chunk.Entry.IdText == entry.IdText))];
    }
    // A step keeps a copy of the depot key: fetching the key again while the chunks download wipes
    // the old key's bytes, but not the ones that the running step decrypts with.
    [Fact]
    public async Task FetchingTheKeyAgainDuringTheChunksKeepsTheirKey()
    {
        var key = (byte[])_kit.Samples.DepotKey.Clone();
        var chunks = await DownloadManifestAsync();
        foreach (var (entry, content) in chunks)
        {
            _kit.Content.Chunks[entry.IdText] = content;
        }

        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _kit.Content.BeforeChunk = _ =>
        {
            _kit.Content.BeforeChunk = null;
            reached.TrySetResult();
            return held.Task;
        };

        var run = _kit.Gate.DownloadChunksAsync(10, Token);
        await reached.Task.WaitAsync(SteamTestKit.Patience, Token);
        _kit.Apps.DepotKey = new DepotKeyResult(SteamResult.OK, [.. key]);
        Assert.Equal(SteamGrant.Obtained, await _kit.Gate.GetDepotKeyAsync(WindowsDepot, Token));
        held.SetResult();
        var result = await run.WaitAsync(SteamTestKit.Patience, Token);

        Assert.Equal(3, result.Verified);
        Assert.All(_kit.Content.KeysAtReturn, seen => Assert.Equal(key, seen));
    }

    // Forgetting the key takes its text out of the log filter, which would otherwise keep it for
    // the service's life; the hexadecimal and base64 rules still remove such text.
    [Fact]
    public async Task DeletingTheTestDataTakesTheKeyOutOfTheLogFilter()
    {
        await GrantAsync();
        var hex = Convert.ToHexString(_kit.Apps.DepotKey.Key!);
        var base64 = Convert.ToBase64String(_kit.Apps.DepotKey.Key!);
        Assert.True(_kit.Log.Holds(hex));
        Assert.True(_kit.Log.Holds(base64));

        await _kit.Gate.DeleteTestDataAsync(Token);

        Assert.False(_kit.Log.Holds(hex));
        Assert.False(_kit.Log.Holds(base64));
    }
}
