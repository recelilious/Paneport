// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Paneport.Core.Steam;
using Paneport.Steam.Content;
using Paneport.Steam.Ports;
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

        // Whatever the random key makes of the changed byte, the chunk is rejected: a garbled
        // deflate stream may also end early, with EndOfStreamException.
        Assert.Throws<ChunkRejectedException>(() =>
            SteamKitContent.ProcessOrReject(() => DepotChunk.Process(entry, changed, new byte[content.Length], key)));
    }

    [Fact]
    public void AWrongKeyIsRejected()
    {
        var (_, content, entry, data) = Chunk(length: 3000);

        Assert.Throws<ChunkRejectedException>(() =>
            SteamKitContent.ProcessOrReject(() => DepotChunk.Process(entry, data, new byte[content.Length], RandomNumberGenerator.GetBytes(32))));
    }

    // The processing reads only what the server sent, so a stream that ends early is the chunk's
    // fault, not the server's: no other server is asked for it.
    [Fact]
    public void AnEarlyEndOfTheProcessedStreamIsARejection()
    {
        var rejection = Assert.Throws<ChunkRejectedException>(() => SteamKitContent.ProcessOrReject(() => throw new EndOfStreamException()));

        Assert.IsType<EndOfStreamException>(rejection.InnerException);
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
    [SuppressMessage(
        "Usage",
        "CA2201:Do not raise reserved exception types",
        Justification = "The test only hands an OutOfMemoryException to the mapping, which must leave it out; nothing throws it.")]
    public void FailuresOfTheDownloadAreDownloadFailures()
    {
        // SteamKit2's own length checks of the response throw InvalidDataException, and a chunked
        // response that is too long overruns its buffer with NotSupportedException: a server that
        // sends too much or too little, so the next server is asked. So is any other failure.
        var failures = new Exception[]
        {
            new HttpRequestException("refused"),
            new IOException("cut"),
            new InvalidDataException("length"),
            new NotSupportedException("Memory stream is not expandable."),
            new InvalidOperationException("other"),
            new OperationCanceledException(),
        };
        foreach (var failure in failures)
        {
            Assert.IsType<ContentDownloadException>(SteamKitContent.DownloadFailure(failure, CancellationToken.None));
        }

        Assert.Null(SteamKitContent.DownloadFailure(new OperationCanceledException(), new CancellationToken(canceled: true)));
        Assert.Null(SteamKitContent.DownloadFailure(new OutOfMemoryException(), CancellationToken.None));
    }

    // The content adapter over SteamKit2's CDN client and a server on the loopback interface that
    // answers one chunk request. A response of the wrong length is a download failure, so the next
    // server is asked: a chunked body that is too long or too short, and a Content-Length that is too
    // long. A response of the right length whose bytes are not the chunk is a rejected chunk. Nothing
    // leaves the loopback interface, and no proxy is used.
    [Theory]
    [InlineData(16, true, false)]
    [InlineData(-16, true, false)]
    [InlineData(16, false, false)]
    [InlineData(0, true, true)]
    public async Task TheContentAdapterTellsAFailedDownloadFromARejectedChunk(int extraBytes, bool chunked, bool rejected)
    {
        const int CompressedLength = 1000;
        var client = new SteamClient(
            SteamConfiguration.Create(builder => builder
                .WithDirectoryFetch(false)
                .WithHttpClientFactory(_ => new HttpClient(new SocketsHttpHandler { UseProxy = false }))),
            "Paneport tests");
        using var content = new SteamKitContent(client);
        var id = new byte[ManifestChunk.IdLength];
        id[0] = 1;
        var entry = new ManifestChunk(id, 1, 0, CompressedLength, 2000);
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var serve = ServeOneResponseAsync(listener, RandomNumberGenerator.GetBytes(CompressedLength + extraBytes), chunked);
            Server server = new IPEndPoint(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);

            var failure = await Record.ExceptionAsync(() => content
                .DownloadChunkAsync(4_100_000_101, entry, new ContentServer("127.0.0.1", server), new byte[32], new byte[2000], null, TestContext.Current.CancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));

            Assert.IsType(rejected ? typeof(ChunkRejectedException) : typeof(ContentDownloadException), failure);
            await serve.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
        finally
        {
            listener.Stop();
        }
    }

    // Reads one request and answers it with the body, chunked or with its length. The client may
    // stop reading early, which ends the connection.
    private static async Task ServeOneResponseAsync(TcpListener listener, byte[] body, bool chunked)
    {
        using var connection = await listener.AcceptTcpClientAsync();
        var stream = connection.GetStream();
        var request = new StringBuilder();
        var buffer = new byte[4096];
        while (!request.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
        {
            var read = await stream.ReadAsync(buffer);
            if (read == 0)
            {
                return;
            }

            request.Append(Encoding.ASCII.GetString(buffer, 0, read));
        }

        var header = chunked
            ? "HTTP/1.1 200 OK\r\nTransfer-Encoding: chunked\r\nConnection: close\r\n\r\n" + body.Length.ToString("x", CultureInfo.InvariantCulture) + "\r\n"
            : string.Create(CultureInfo.InvariantCulture, $"HTTP/1.1 200 OK\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        try
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes(header));
            await stream.WriteAsync(body);
            if (chunked)
            {
                await stream.WriteAsync("\r\n0\r\n\r\n"u8.ToArray());
            }

            await stream.FlushAsync();
        }
        catch (IOException)
        {
            // The client ended the connection before the whole body.
        }
    }

    // A chunk entry without a valid ID could be neither downloaded nor checked: the manifest is
    // refused rather than read with a gap.
    [Fact]
    public void AChunkEntryWithoutAValidIdIsRefused()
    {
        var valid = new DepotManifest.ChunkData(new byte[ManifestChunk.IdLength], 1, 0, 10, 20);
        var invalid = new DepotManifest.ChunkData(new byte[ManifestChunk.IdLength - 1], 1, 20, 10, 20);

        Assert.Single(SteamKitContent.ChunksOf([valid]));
        var refusal = Assert.Throws<SteamStepException>(() => SteamKitContent.ChunksOf([valid, invalid]));
        Assert.Equal("The manifest names a chunk without a valid ID.", refusal.Message);
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
