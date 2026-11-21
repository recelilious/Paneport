// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// What the Steam gate shows of a downloaded manifest (L2 step 25).
/// </summary>
/// <param name="DepotId">The depot.</param>
/// <param name="FileCount">The number of files, not counting folders.</param>
/// <param name="ChunkCount">The number of distinct chunks that the files are made of.</param>
/// <param name="TotalSize">The size of all files in bytes.</param>
/// <param name="DownloadTime">The time from the request to the parsed manifest.</param>
public sealed record ManifestSummary(
    uint DepotId,
    int FileCount,
    int ChunkCount,
    ulong TotalSize,
    TimeSpan DownloadTime);
