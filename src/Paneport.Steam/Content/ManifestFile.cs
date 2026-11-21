// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Content;

// A file or a folder of a manifest, with the chunks that its content is made of.
internal sealed class ManifestFile
{
    public ManifestFile(string name, ulong size, bool isDirectory, IReadOnlyList<ManifestChunk> chunks)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(chunks);
        Name = name;
        Size = size;
        IsDirectory = isDirectory;
        Chunks = chunks;
    }

    public string Name { get; }

    public ulong Size { get; }

    public bool IsDirectory { get; }

    public IReadOnlyList<ManifestChunk> Chunks { get; }
}
