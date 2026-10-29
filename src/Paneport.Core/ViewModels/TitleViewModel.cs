// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using System.Globalization;
using Paneport.Core.Library;

namespace Paneport.Core.ViewModels;

/// <summary>
/// A title as the shell shows it: its name, identity, install state and launch path as text.
/// </summary>
public sealed class TitleViewModel
{
    /// <summary>Wraps a title.</summary>
    /// <param name="title">The title to show.</param>
    public TitleViewModel(Title title)
    {
        ArgumentNullException.ThrowIfNull(title);
        Title = title;
    }

    /// <summary>Gets the title.</summary>
    public Title Title { get; }

    /// <summary>Gets the title's display name.</summary>
    public string Name => Title.Name;

    /// <summary>Gets the title's identity, such as <c>steam-480</c> or <c>local-my-game</c>.</summary>
    public string Identity => Title.Id.ToString();

    /// <summary>Gets where the title comes from, such as <c>Steam app 480</c>.</summary>
    public string SourceText => Title.Id.Source switch
    {
        TitleSource.Steam => string.Create(CultureInfo.InvariantCulture, $"Steam app {Title.Id.SteamAppId}"),
        TitleSource.Local => "Brought in without Steam",
        _ => throw new UnreachableException(),
    };

    /// <summary>Gets the install state, such as <c>Installed</c> or <c>Downloading, 42%</c>.</summary>
    public string InstallStateText => Title.InstallState.Status switch
    {
        InstallStatus.NotInstalled => "Not installed",
        InstallStatus.Downloading => string.Create(
            CultureInfo.InvariantCulture,
            $"Downloading, {DownloadPercent(Title.InstallState.DownloadProgress.GetValueOrDefault())}%"),
        InstallStatus.Installed => "Installed",
        InstallStatus.UpdatePending => "Update pending",
        _ => throw new UnreachableException(),
    };

    /// <summary>Gets the launch path: <c>Direct</c>, <c>Dock</c> or <c>Not classified yet</c>.</summary>
    public string LaunchPathText => Title.LaunchPath switch
    {
        LaunchPath.Unclassified => "Not classified yet",
        LaunchPath.Direct => "Direct",
        LaunchPath.Dock => "Dock",
        _ => throw new UnreachableException(),
    };

    /// <summary>Gets one sentence on what the launch path means.</summary>
    public string LaunchPathDescription => Title.LaunchPath switch
    {
        LaunchPath.Unclassified => "Paneport classifies the title before its first launch.",
        LaunchPath.Direct => "The game's program starts directly in the Wine session.",
        LaunchPath.Dock => "The Dock host starts first and launches the game through the Steam client.",
        _ => throw new UnreachableException(),
    };

    // Rounding down keeps a download that has not finished from showing 100%.
    private static int DownloadPercent(double progress) => (int)Math.Floor(progress * 100);
}
