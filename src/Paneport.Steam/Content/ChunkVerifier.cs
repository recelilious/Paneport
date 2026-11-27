// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace Paneport.Steam.Content;

// Checks a chunk against its manifest entry before anything uses it (C-0007 section 4,
// steam-service.md section 7). SteamKit2's chunk processing has already decrypted and decompressed
// it and checked its length and Adler-32 checksum; this check adds the entry's chunk ID, which is
// the SHA-1 hash of the processed content, so a chunk is accepted only with a cryptographic match.
internal static class ChunkVerifier
{
    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Steam's manifests name each chunk by the SHA-1 hash of its content, so the check must compute SHA-1; it protects no secret.")]
    public static ChunkCheck Check(ManifestChunk entry, ReadOnlySpan<byte> content)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if ((ulong)content.Length != entry.UncompressedLength)
        {
            return ChunkCheck.WrongLength;
        }

        Span<byte> hash = stackalloc byte[ManifestChunk.IdLength];
        SHA1.HashData(content, hash);
        return CryptographicOperations.FixedTimeEquals(hash, entry.Id) ? ChunkCheck.Matches : ChunkCheck.WrongHash;
    }
}
