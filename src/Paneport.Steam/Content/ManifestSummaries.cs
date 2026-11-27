// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;

namespace Paneport.Steam.Content;

// What the Steam gate shows of a manifest (S2.3.5), and which chunks it downloads (S2.3.6).
internal static class ManifestSummaries
{
    // Files without folders; distinct chunks, since files can share one; and the files' sizes.
    public static ManifestSummary Summarize(DepotManifestData manifest, TimeSpan downloadTime)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var files = manifest.Files.Where(file => !file.IsDirectory).ToList();
        var size = 0UL;
        foreach (var file in files)
        {
            size += file.Size;
        }

        return new ManifestSummary(manifest.DepotId, files.Count, DistinctChunks(manifest).Count, size, downloadTime);
    }

    // The distinct chunks of the manifest, smallest first and then by ID, so that a test download
    // is quick and always picks the same chunks.
    public static IReadOnlyList<ManifestChunk> DistinctChunks(DepotManifestData manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var chunks = new List<ManifestChunk>();
        foreach (var chunk in manifest.Files.SelectMany(file => file.Chunks))
        {
            if (seen.Add(chunk.IdText))
            {
                chunks.Add(chunk);
            }
        }

        return [.. chunks.OrderBy(chunk => chunk.CompressedLength).ThenBy(chunk => chunk.IdText, StringComparer.Ordinal)];
    }
}
