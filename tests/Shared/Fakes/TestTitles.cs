// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Tests.Fakes;

// Made-up titles that cover every install state and launch path, a local title and a long name.
// Their app IDs lie far above any that Steam has assigned.
internal static class TestTitles
{
    public static Title InstalledDirect { get; } =
        new(TitleId.Steam(4_100_000_010), "Installed Direct Sample", InstallState.Installed, LaunchPath.Direct);

    public static Title DownloadingDock { get; } =
        new(TitleId.Steam(4_100_000_020), "Downloading Dock Sample", InstallState.Downloading(0.25), LaunchPath.Dock);

    public static Title UpdatePendingDock { get; } =
        new(TitleId.Steam(4_100_000_030), "Update Pending Dock Sample", InstallState.UpdatePending, LaunchPath.Dock);

    public static Title NotInstalledUnclassified { get; } =
        new(TitleId.Steam(4_100_000_040), "Not Installed Unclassified Sample", InstallState.NotInstalled, LaunchPath.Unclassified);

    public static Title Local { get; } =
        new(TitleId.Local("local-sample"), "Local Sample", InstallState.Installed, LaunchPath.Direct);

    public static Title LongName { get; } = new(
        TitleId.Steam(4_100_000_050),
        "A Deliberately Long Sample Name That Keeps Going To Check That The Library Wraps It Instead Of Cutting It Off",
        InstallState.Installed,
        LaunchPath.Unclassified);

    public static IReadOnlyList<Title> All { get; } =
        [InstalledDirect, DownloadingDock, UpdatePendingDock, NotInstalledUnclassified, Local, LongName];
}
