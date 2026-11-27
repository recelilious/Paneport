// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Content;

namespace Paneport.Steam.Ports;

// Manifest request codes and the content servers, which the SteamKit2 adapter implements over
// SteamContent and SteamKit2's CDN client. A download that fails at the server or on the way
// throws ContentDownloadException; a chunk that arrives but fails SteamKit2's own processing
// throws ChunkRejectedException.
internal interface ISteamContent
{
    // The request code of a manifest, or 0 when Steam grants none. It is never logged.
    Task<ulong> GetManifestRequestCodeAsync(uint depotId, uint appId, ulong manifestId, CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentServer>> GetServersAsync(CancellationToken cancellationToken);

    // A token for a content server that refused a request without one, or null when Steam grants
    // none. It is never logged.
    Task<string?> GetServerTokenAsync(uint appId, uint depotId, ContentServer server, CancellationToken cancellationToken);

    // Downloads, decrypts and reads a manifest; the file names come back decrypted.
    Task<DepotManifestData> DownloadManifestAsync(
        uint depotId,
        ulong manifestId,
        ulong requestCode,
        ContentServer server,
        byte[] depotKey,
        string? serverToken,
        CancellationToken cancellationToken);

    // Downloads a chunk, and decrypts and decompresses it into the destination with SteamKit2's
    // chunk processing, which also checks its length and Adler-32 checksum. Returns the bytes
    // written.
    Task<int> DownloadChunkAsync(
        uint depotId,
        ManifestChunk chunk,
        ContentServer server,
        byte[] depotKey,
        byte[] destination,
        string? serverToken,
        CancellationToken cancellationToken);
}
