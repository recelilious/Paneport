// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Net;
using Paneport.Steam.Content;
using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// Manifest request codes and content servers that a test sets up. A chunk's content comes from
// Chunks by its ID text; a chunk without an entry fails at every server.
internal sealed class FakeContent : ISteamContent
{
    private readonly Lock _lock = new();
    private readonly List<string?> _tokensUsed = [];

    public ulong ManifestCode { get; set; }

    public List<ContentServer> Servers { get; } = [new ContentServer("cache1.example.invalid", "first")];

    public DepotManifestData? Manifest { get; set; }

    // The content that each chunk download writes, by chunk ID text.
    public Dictionary<string, byte[]> Chunks { get; } = new(StringComparer.Ordinal);

    // Chunks that SteamKit2's processing rejects, by chunk ID text.
    public HashSet<string> Unprocessable { get; } = new(StringComparer.Ordinal);

    // When set, every server refuses a download without this token with HTTP 403.
    public string? RequiredServerToken { get; set; }

    // The server token of each download, in order; null for none.
    public IReadOnlyList<string?> TokensUsed
    {
        get
        {
            lock (_lock)
            {
                return [.. _tokensUsed];
            }
        }
    }

    public int ManifestDownloads { get; private set; }

    // Runs before each chunk is returned, with the depot key that the download was given; a test can
    // hold a download there.
    public Func<byte[], Task>? BeforeChunk { get; set; }

    // A copy of the depot key that each chunk download held when it returned.
    public List<byte[]> KeysAtReturn { get; } = [];

    public Task<ulong> GetManifestRequestCodeAsync(uint depotId, uint appId, ulong manifestId, CancellationToken cancellationToken) =>
        Task.FromResult(ManifestCode);

    public Task<IReadOnlyList<ContentServer>> GetServersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ContentServer>>([.. Servers]);

    public Task<string?> GetServerTokenAsync(uint appId, uint depotId, ContentServer server, CancellationToken cancellationToken) =>
        Task.FromResult(RequiredServerToken);

    public Task<DepotManifestData> DownloadManifestAsync(
        uint depotId,
        ulong manifestId,
        ulong requestCode,
        ContentServer server,
        byte[] depotKey,
        string? serverToken,
        CancellationToken cancellationToken)
    {
        ManifestDownloads++;
        Use(serverToken);
        return Task.FromResult(Manifest ?? throw new ContentDownloadException(HttpStatusCode.NotFound, null));
    }

    public async Task<int> DownloadChunkAsync(
        uint depotId,
        ManifestChunk chunk,
        ContentServer server,
        byte[] depotKey,
        byte[] destination,
        string? serverToken,
        CancellationToken cancellationToken)
    {
        Use(serverToken);
        if (Unprocessable.Contains(chunk.IdText))
        {
            throw new ChunkRejectedException();
        }

        if (!Chunks.TryGetValue(chunk.IdText, out var content))
        {
            throw new ContentDownloadException(HttpStatusCode.NotFound, null);
        }

        if (BeforeChunk is { } before)
        {
            await before(depotKey);
        }

        lock (_lock)
        {
            KeysAtReturn.Add([.. depotKey]);
        }

        var length = Math.Min(content.Length, destination.Length);
        content.AsSpan(0, length).CopyTo(destination);
        return length;
    }

    private void Use(string? serverToken)
    {
        lock (_lock)
        {
            _tokensUsed.Add(serverToken);
        }

        if (RequiredServerToken is not null && serverToken != RequiredServerToken)
        {
            throw new ContentDownloadException(HttpStatusCode.Forbidden, null);
        }
    }
}
