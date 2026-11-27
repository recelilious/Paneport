// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Content;

// How a processed chunk compares with its manifest entry.
internal enum ChunkCheck
{
    // Its length and its SHA-1 hash match the entry.
    Matches,

    // It is longer or shorter than the entry says.
    WrongLength,

    // Its SHA-1 hash is not the entry's chunk ID.
    WrongHash,
}
