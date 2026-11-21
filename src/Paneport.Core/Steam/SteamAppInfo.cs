// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// What the Steam gate shows of an app's information (L2 step 23).
/// </summary>
/// <param name="AppId">The app's ID.</param>
/// <param name="Name">The app's name, or null when its information has none.</param>
/// <param name="WindowsDepots">The depots that hold Windows content, in the order of the app's information.</param>
/// <param name="Problems">The sections that were missing or could not be read, in words; empty when none was.</param>
public sealed record SteamAppInfo(
    uint AppId,
    string? Name,
    IReadOnlyList<SteamDepotInfo> WindowsDepots,
    IReadOnlyList<string> Problems);
