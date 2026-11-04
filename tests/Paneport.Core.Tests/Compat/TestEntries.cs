// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Compat;
using Paneport.Core.Library;

namespace Paneport.Core.Tests.Compat;

// Entries made in code for the tests. The titles are made up, and their app IDs, from
// 4,100,000,000, are as far from real ones as those of the shared fakes.
internal static class TestEntries
{
    public const uint SteamAppId = 4_100_000_310;

    public const string LocalSlug = "copper-orchard";

    public const string LocalHash = "ae89c8af8eb034705d71f639ffa2196787f761d5ed4a0f856b09aba9b0d050b6";

    public static TitleId SteamId { get; } = TitleId.Steam(SteamAppId);

    public static TitleId LocalId { get; } = TitleId.Local(LocalSlug);

    public static TitleEntry Steam(string? name = "Copper Orchard") => new()
    {
        Id = new TitleEntryId { SteamAppId = SteamAppId, Slug = LocalSlug },
        Name = name,
    };

    public static TitleEntry Local(string? name = "Copper Orchard") => new()
    {
        Id = new TitleEntryId { ExeSha256 = [LocalHash], Slug = LocalSlug },
        Name = name,
        Executable = new ExecutableSpec { Path = "Orchard.exe" },
    };

    public static TitleEntry ForId(TitleId id) => id.Source == TitleSource.Steam ? Steam() : Local();

    // The classification the device computes for a title with no Steam marker: CEG stays unknown.
    public static DrmClassification Clean { get; } = new()
    {
        SteamApi = false,
        SteamStub = false,
        SteamAppIdFile = false,
        Ceg = CegStatus.Unknown,
    };
}
