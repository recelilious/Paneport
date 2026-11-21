// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Content;
using Xunit;
using static Paneport.Steam.Tests.Fakes.SyntheticContent;

namespace Paneport.Steam.Tests;

// S2.3.6: a chunk passes only with the length and the SHA-1 hash of its manifest entry.
public sealed class ChunkVerifierTests
{
    [Fact]
    public void AChunkWithItsEntrysLengthAndHashMatches()
    {
        var (entry, content) = Chunk(100, 7);

        Assert.Equal(ChunkCheck.Matches, ChunkVerifier.Check(entry, content));
    }

    [Fact]
    public void AChunkWithAnotherLengthDoesNotMatch()
    {
        var (entry, content) = Chunk(100, 7);

        Assert.Equal(ChunkCheck.WrongLength, ChunkVerifier.Check(entry, content.AsSpan(1)));
        Assert.Equal(ChunkCheck.WrongLength, ChunkVerifier.Check(entry, [.. content, 0]));
    }

    [Fact]
    public void AChunkWithOneChangedBitDoesNotMatch()
    {
        var (entry, content) = Chunk(100, 7);
        content[50] ^= 0x01;

        Assert.Equal(ChunkCheck.WrongHash, ChunkVerifier.Check(entry, content));
    }

    [Fact]
    public void AChunkIdHasTwentyBytes()
    {
        Assert.Throws<ArgumentException>(() => new ManifestChunk(new byte[19], 0, 0, 1, 1));
    }
}
