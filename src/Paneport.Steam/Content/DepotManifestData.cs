// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Content;

// A depot manifest as the service uses it. The SteamKit2 adapter copies SteamKit2's
// DepotManifest into it, so that the summary, the chunk choice and their tests need no SteamKit2
// type and the tests build manifests of their own (C-0011 section 1).
internal sealed class DepotManifestData
{
    public DepotManifestData(uint depotId, ulong manifestId, IReadOnlyList<ManifestFile> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        DepotId = depotId;
        ManifestId = manifestId;
        Files = files;
    }

    public uint DepotId { get; }

    public ulong ManifestId { get; }

    public IReadOnlyList<ManifestFile> Files { get; }
}
