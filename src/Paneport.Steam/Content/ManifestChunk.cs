// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Content;

// A chunk's entry in a manifest: its ID, which is the SHA-1 hash of its content after decryption
// and decompression (steam-service.md section 7), its Adler-32 checksum, where it sits in its file
// and its two lengths.
internal sealed class ManifestChunk
{
    public const int IdLength = 20;

    public ManifestChunk(byte[] id, uint checksum, ulong offset, uint compressedLength, uint uncompressedLength)
    {
        ArgumentNullException.ThrowIfNull(id);
        if (id.Length != IdLength)
        {
            throw new ArgumentException($"A chunk ID has {IdLength} bytes.", nameof(id));
        }

        Id = id;
        Checksum = checksum;
        Offset = offset;
        CompressedLength = compressedLength;
        UncompressedLength = uncompressedLength;
    }

    public byte[] Id { get; }

    public uint Checksum { get; }

    public ulong Offset { get; }

    public uint CompressedLength { get; }

    public uint UncompressedLength { get; }

    // The ID in lowercase hexadecimal, which names the chunk's file in the test folder.
    public string IdText => Convert.ToHexStringLower(Id);
}
