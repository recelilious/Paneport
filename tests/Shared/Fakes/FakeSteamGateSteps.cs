// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Tests.Fakes;

// The steps of the Steam gate with answers that a test sets, recording each call. A step throws
// Failure, once, when a test sets it.
internal sealed class FakeSteamGateSteps : ISteamGateSteps
{
    private readonly List<string> _calls = [];

    public int Licenses { get; set; } = 42;

    public SteamAppInfo AppInfo { get; set; } = new(
        4_100_000_100,
        "Lantern Test App",
        [
            new SteamDepotInfo(4_100_000_111, "Lantern Test App Content", 1_048_576, "64", HasPublicManifest: true),
            new SteamDepotInfo(4_100_000_113, Name: null, 2_048, Architecture: null, HasPublicManifest: true),
        ],
        []);

    public SteamGrant DepotKey { get; set; } = SteamGrant.Obtained;

    public SteamGrant ManifestCode { get; set; } = SteamGrant.Obtained;

    public ManifestSummary Manifest { get; set; } = new(4_100_000_113, 12, 30, 2_048, TimeSpan.FromMilliseconds(850));

    public ChunkRunResult Chunks { get; set; } = new(10, 10, 0, 0, 5_120, 10_240, TimeSpan.FromMilliseconds(1_200));

    public Exception? Failure { get; set; }

    public IReadOnlyList<string> Calls => [.. _calls];

    public Task<int> CountLicensesAsync(CancellationToken cancellationToken) => Answer("licenses", Licenses);

    public Task<SteamAppInfo> ReadAppInfoAsync(uint appId, CancellationToken cancellationToken) => Answer($"app info {appId}", AppInfo);

    public Task<SteamGrant> GetDepotKeyAsync(uint depotId, CancellationToken cancellationToken) => Answer($"depot key {depotId}", DepotKey);

    public Task<SteamGrant> GetManifestRequestCodeAsync(uint depotId, CancellationToken cancellationToken) =>
        Answer($"manifest code {depotId}", ManifestCode);

    public Task<ManifestSummary> DownloadManifestAsync(uint depotId, CancellationToken cancellationToken) =>
        Answer($"manifest {depotId}", Manifest);

    public Task<ChunkRunResult> DownloadChunksAsync(int count, CancellationToken cancellationToken) => Answer($"chunks {count}", Chunks);

    public Task DeleteTestDataAsync(CancellationToken cancellationToken) => Answer("delete", true);

    private Task<T> Answer<T>(string call, T answer)
    {
        _calls.Add(call);
        if (Failure is { } failure)
        {
            Failure = null;
            return Task.FromException<T>(failure);
        }

        return Task.FromResult(answer);
    }
}
