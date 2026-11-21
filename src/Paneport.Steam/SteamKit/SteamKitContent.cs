// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Content;
using Paneport.Steam.Ports;
using SteamKit2;
using SteamKit2.CDN;

namespace Paneport.Steam.SteamKit;

// The content port over SteamKit2's SteamContent and its CDN client (S2.3.5 and S2.3.6). Downloads
// go through SteamKit2's client, whose chunk processing decrypts, decompresses and checks each
// chunk's length and Adler-32 checksum; the service then checks its SHA-1 hash. Failures become the
// port's own exceptions, whose messages hold no URL, since SteamKit2's carry the server token.
internal sealed class SteamKitContent : ISteamContent, IDisposable
{
    private readonly SteamContent _content;
    private readonly Client _cdn;

    public SteamKitContent(SteamClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _content = client.GetRequiredHandler<SteamContent>();
        _cdn = new Client(client);
    }

    public Task<ulong> GetManifestRequestCodeAsync(uint depotId, uint appId, ulong manifestId, CancellationToken cancellationToken) =>
        _content.GetManifestRequestCode(depotId, appId, manifestId, "public").WaitAsync(cancellationToken);

    // Content servers and caches that serve every app, the least loaded first.
    public async Task<IReadOnlyList<ContentServer>> GetServersAsync(CancellationToken cancellationToken)
    {
        var servers = await _content.GetServersForSteamPipe().WaitAsync(cancellationToken).ConfigureAwait(false);
        return
        [
            .. servers
                .Where(server => server.Type is "CDN" or "SteamCache" && server.AllowedAppIds.Length == 0)
                .OrderBy(server => server.WeightedLoad)
                .Select(server => new ContentServer(server.Host ?? server.VHost ?? "content server", server)),
        ];
    }

    public async Task<string?> GetServerTokenAsync(uint appId, uint depotId, ContentServer server, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        var token = await _content.GetCDNAuthToken(appId, depotId, server.Host).WaitAsync(cancellationToken).ConfigureAwait(false);
        return token.Result == EResult.OK && !string.IsNullOrEmpty(token.Token) ? token.Token : null;
    }

    public async Task<DepotManifestData> DownloadManifestAsync(
        uint depotId,
        ulong manifestId,
        ulong requestCode,
        ContentServer server,
        byte[] depotKey,
        string? serverToken,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(server);
        try
        {
            var manifest = await _cdn.DownloadManifestAsync(depotId, manifestId, requestCode, (Server)server.Handle, depotKey, null, serverToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return Copy(depotId, manifestId, manifest);
        }
        catch (Exception exception) when (DownloadFailure(exception, cancellationToken) is { } failure)
        {
            throw failure;
        }
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
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(server);
        var entry = new DepotManifest.ChunkData(chunk.Id, chunk.Checksum, chunk.Offset, chunk.CompressedLength, chunk.UncompressedLength);
        try
        {
            return await _cdn.DownloadDepotChunkAsync(depotId, entry, (Server)server.Handle, destination, depotKey, null, serverToken)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException exception)
        {
            throw new ChunkRejectedException("SteamKit2 rejected the chunk.", exception);
        }
        catch (Exception exception) when (DownloadFailure(exception, cancellationToken) is { } failure)
        {
            throw failure;
        }
    }

    public void Dispose() => _cdn.Dispose();

    // A failure at the server or on the way, or null for one that is no download failure, such as
    // the caller's cancellation.
    private static ContentDownloadException? DownloadFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        SteamKitWebRequestException web => new ContentDownloadException(web.StatusCode, web),
        HttpRequestException http => new ContentDownloadException(http.StatusCode, http),
        OperationCanceledException or IOException or InvalidDataException => new ContentDownloadException((System.Net.HttpStatusCode?)null, exception),
        _ => null,
    };

    private static DepotManifestData Copy(uint depotId, ulong manifestId, DepotManifest manifest)
    {
        var files = new List<ManifestFile>();
        foreach (var file in manifest.Files ?? [])
        {
            var chunks = file.Chunks
                .Where(chunk => chunk.ChunkID is { Length: ManifestChunk.IdLength })
                .Select(chunk => new ManifestChunk(chunk.ChunkID!, chunk.Checksum, chunk.Offset, chunk.CompressedLength, chunk.UncompressedLength))
                .ToList();
            files.Add(new ManifestFile(file.FileName ?? string.Empty, file.TotalSize, file.Flags.HasFlag(EDepotFileFlag.Directory), chunks));
        }

        return new DepotManifestData(depotId, manifestId, files);
    }
}
