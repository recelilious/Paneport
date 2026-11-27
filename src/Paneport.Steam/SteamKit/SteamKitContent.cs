// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers;
using Paneport.Core.Steam;
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
        var raw = ArrayPool<byte>.Shared.Rent(checked((int)Math.Max(1, chunk.CompressedLength)));
        try
        {
            int length;
            try
            {
                // The chunk as the server sent it, still encrypted and compressed. A failure here,
                // SteamKit2's own length checks included, is the server's or the way's.
                length = await _cdn.DownloadDepotChunkAsync(depotId, entry, (Server)server.Handle, raw, depotKey: null, proxyServer: null, serverToken)
                    .WaitAsync(cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (DownloadFailure(exception, cancellationToken) is { } failure)
            {
                throw failure;
            }

            return ProcessOrReject(() => DepotChunk.Process(entry, raw.AsSpan(0, length), destination, depotKey));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(raw);
        }
    }

    public void Dispose() => _cdn.Dispose();

    // Runs SteamKit2's processing of a downloaded chunk: decryption, decompression, and the length
    // and Adler-32 checks. It reads only the bytes that the server sent, so any failure of it means
    // that the chunk arrived but is not the one its entry names: a failed check, bad compressed data
    // or a stream that ends early, a padding that does not decrypt, as with a wrong key, or a zip
    // entry that claims more than the entry's length. None is worth asking another server for.
    internal static int ProcessOrReject(Func<int> process)
    {
        ArgumentNullException.ThrowIfNull(process);
        try
        {
            return process();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new ChunkRejectedException("SteamKit2 rejected the chunk.", exception);
        }
    }

    // A failure at the server or on the way, which every exception of a download is, whatever its
    // type: SteamKit2's own length checks throw InvalidDataException, and a chunked response with
    // more bytes than the entry's length overruns SteamKit2's fixed buffer with
    // NotSupportedException. Null only for the caller's cancellation and a lack of memory, as in
    // ProcessOrReject, which are no download failures.
    internal static ContentDownloadException? DownloadFailure(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        OperationCanceledException when cancellationToken.IsCancellationRequested => null,
        OutOfMemoryException => null,
        SteamKitWebRequestException web => new ContentDownloadException(web.StatusCode, web),
        HttpRequestException http => new ContentDownloadException(http.StatusCode, http),
        _ => new ContentDownloadException((System.Net.HttpStatusCode?)null, exception),
    };

    private static DepotManifestData Copy(uint depotId, ulong manifestId, DepotManifest manifest)
    {
        var files = new List<ManifestFile>();
        foreach (var file in manifest.Files ?? [])
        {
            files.Add(new ManifestFile(file.FileName ?? string.Empty, file.TotalSize, file.Flags.HasFlag(EDepotFileFlag.Directory), ChunksOf(file.Chunks)));
        }

        return new DepotManifestData(depotId, manifestId, files);
    }

    // A file's chunk entries. Every one is part of the file, and one without a valid ID could be
    // neither downloaded nor checked, so such a manifest is refused rather than read with gaps
    // (C-0007 section 4).
    internal static List<ManifestChunk> ChunksOf(IEnumerable<DepotManifest.ChunkData> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var chunks = new List<ManifestChunk>();
        foreach (var entry in entries)
        {
            if (entry.ChunkID is not { Length: ManifestChunk.IdLength } id)
            {
                throw new SteamStepException("The manifest names a chunk without a valid ID.");
            }

            chunks.Add(new ManifestChunk(id, entry.Checksum, entry.Offset, entry.CompressedLength, entry.UncompressedLength));
        }

        return chunks;
    }
}
