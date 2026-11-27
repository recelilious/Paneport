// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// A chunk that arrived but did not decrypt, decompress or match its length and Adler-32 checksum
// in SteamKit2's chunk processing, which throws InvalidDataException for it.
internal sealed class ChunkRejectedException : Exception
{
    public ChunkRejectedException()
        : base("The chunk did not match its manifest entry.")
    {
    }

    public ChunkRejectedException(string message)
        : base(message)
    {
    }

    public ChunkRejectedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
