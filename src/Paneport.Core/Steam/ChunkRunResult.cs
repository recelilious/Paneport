// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// What the Steam gate shows of a chunk download (L2 step 26).
/// </summary>
/// <param name="Requested">How many distinct chunks were asked for.</param>
/// <param name="Verified">How many chunks matched their manifest entry and were written to the test folder.</param>
/// <param name="Rejected">How many chunks arrived but did not match their manifest entry; none of them was written.</param>
/// <param name="Failed">How many chunks could not be downloaded from any content server.</param>
/// <param name="DownloadedBytes">The bytes that arrived from the content servers.</param>
/// <param name="VerifiedBytes">The bytes of the verified chunks after decryption and decompression.</param>
/// <param name="Time">The time from the first request to the last chunk.</param>
public sealed record ChunkRunResult(
    int Requested,
    int Verified,
    int Rejected,
    int Failed,
    ulong DownloadedBytes,
    ulong VerifiedBytes,
    TimeSpan Time);
