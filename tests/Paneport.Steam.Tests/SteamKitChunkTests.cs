// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Security.Cryptography;
using Paneport.Steam.Content;
using Paneport.Steam.SteamKit;
using SteamKit2;
using SteamKit2.CDN;
using Xunit;

namespace Paneport.Steam.Tests;

// S2.3.6 on SteamKit2's own chunk processing: a chunk that the test builds the way the depot
// format has it, compressed into a zip with one entry and encrypted with AES-256 behind an
// ECB-encrypted IV, passes DepotChunk.Process and the service's check; a changed byte or a wrong
// key makes the processing fail in a way that the content adapter counts as a rejected chunk; and a
// chunk whose Adler-32 checksum matches but whose content is another's is caught by the SHA-1
// check (steam-service.md section 7). Everything is made by the test (C-0011 section 1).
public sealed class SteamKitChunkTests
{
    [Fact]
    public void ARealSyntheticChunkPassesSteamKit2sProcessingAndTheCheck()
    {
        var (key, content, entry, data) = Chunk(length: 3000);
        var destination = new byte[content.Length];

        var written = DepotChunk.Process(entry, data, destination, key);

        Assert.Equal(content.Length, written);
        Assert.Equal(content, destination);
        Assert.Equal(ChunkCheck.Matches, ChunkVerifier.Check(Entry(entry), destination));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(-1)]
    public void AChangedByteIsRejected(int position)
    {
        var (key, content, entry, data) = Chunk(length: 3000);
        var changed = (byte[])data.Clone();
        changed[position < 0 ? changed.Length + position : position] ^= 0x5A;

        var exception = Record.Exception(() => DepotChunk.Process(entry, changed, new byte[content.Length], key));

        Assert.NotNull(exception);
        Assert.True(SteamKitContent.IsRejection(exception), exception.GetType().Name);
    }

    [Fact]
    public void AWrongKeyIsRejected()
    {
        var (_, content, entry, data) = Chunk(length: 3000);

        var exception = Record.Exception(() => DepotChunk.Process(entry, data, new byte[content.Length], RandomNumberGenerator.GetBytes(32)));

        Assert.NotNull(exception);
        Assert.True(SteamKitContent.IsRejection(exception), exception.GetType().Name);
    }

    [Fact]
    public void AnotherChunksContentUnderTheSameChecksumFailsTheShaCheck()
    {
        var (key, content, entry, data) = Chunk(length: 3000);
        var destination = new byte[content.Length];
        var other = (byte[])entry.ChunkID!.Clone();
        other[0] ^= 0xFF;
        var claimed = new DepotManifest.ChunkData(other, entry.Checksum, entry.Offset, entry.CompressedLength, entry.UncompressedLength);

        DepotChunk.Process(claimed, data, destination, key);

        Assert.Equal(ChunkCheck.WrongHash, ChunkVerifier.Check(Entry(claimed), destination));
    }

    [Fact]
    public void FailuresThatAreNoRejectionStayDownloadFailures()
    {
        Assert.False(SteamKitContent.IsRejection(new HttpRequestException("refused")));
        Assert.False(SteamKitContent.IsRejection(new IOException("cut")));
        Assert.False(SteamKitContent.IsRejection(new ArgumentNullException("server")));
        Assert.False(SteamKitContent.IsRejection(new OperationCanceledException()));
    }

    private static ManifestChunk Entry(DepotManifest.ChunkData chunk) =>
        new(chunk.ChunkID!, chunk.Checksum, chunk.Offset, chunk.CompressedLength, chunk.UncompressedLength);

    // A depot key, a chunk's content, its manifest entry and the bytes that a content server sends.
    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "A manifest names each chunk by the SHA-1 hash of its content; the test makes such an ID.")]
    private static (byte[] Key, byte[] Content, DepotManifest.ChunkData Entry, byte[] Data) Chunk(int length)
    {
        var content = new byte[length];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)((i * 7) ^ (i >> 3));
        }

        using var zipped = new MemoryStream();
        using (var archive = new ZipArchive(zipped, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var stream = archive.CreateEntry("chunk", CompressionLevel.Optimal).Open();
            stream.Write(content);
        }

        var key = RandomNumberGenerator.GetBytes(32);
        var iv = RandomNumberGenerator.GetBytes(16);
        using var aes = Aes.Create();
        aes.Key = key;
        var data = aes.EncryptEcb(iv, PaddingMode.None).Concat(aes.EncryptCbc(zipped.ToArray(), iv, PaddingMode.PKCS7)).ToArray();
        var entry = new DepotManifest.ChunkData(SHA1.HashData(content), DepotChunk.AdlerHash(content), 0, (uint)data.Length, (uint)content.Length);
        return (key, content, entry, data);
    }
}
