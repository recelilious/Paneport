// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Content;

// The folder that the Steam gate's test downloads go to: steam-gate-test below the downloads
// folder of C-0004 section 4 on the device, and below the desktop head's own folder on Windows.
// Each verified chunk is written as one file named by its chunk ID; the page deletes the folder.
internal sealed class TestDownloadFolder
{
    public const string FolderName = "steam-gate-test";

    public TestDownloadFolder(string downloadsFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(downloadsFolder);
        Path = System.IO.Path.Combine(downloadsFolder, FolderName);
    }

    public string Path { get; }

    // Writes a chunk that passed its check; nothing calls it for one that did not.
    public async Task WriteChunkAsync(uint depotId, ManifestChunk chunk, ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        var folder = System.IO.Path.Combine(Path, depotId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(System.IO.Path.Combine(folder, chunk.IdText), content, cancellationToken).ConfigureAwait(false);
    }

    // The files that the folder holds, for the tests and the page.
    public IReadOnlyList<string> Files() =>
        Directory.Exists(Path) ? Directory.GetFiles(Path, "*", SearchOption.AllDirectories) : [];

    public void Delete()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
