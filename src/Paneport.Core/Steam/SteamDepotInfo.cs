// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// A Windows depot of an app, as the Steam gate lists it.
/// </summary>
/// <param name="DepotId">The depot's ID.</param>
/// <param name="Name">The depot's name, or null when the app's information gives none.</param>
/// <param name="Size">The size of its public manifest's files in bytes, or the depot's maximum size when no manifest size is given; null when neither is.</param>
/// <param name="Architecture">The architecture that the depot is limited to, such as <c>64</c>; null when it is not limited.</param>
/// <param name="HasPublicManifest">Whether the app's information names a public manifest, which the manifest step needs.</param>
public sealed record SteamDepotInfo(
    uint DepotId,
    string? Name,
    ulong? Size,
    string? Architecture,
    bool HasPublicManifest);
