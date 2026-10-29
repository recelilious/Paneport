// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Desktop.Fakes;

// Fictional sample titles that cover every install state and launch path, one local title and
// one very long name. They are invented for Paneport and carry no game material (C-0008
// section 5). Their app IDs lie far above any ID that Steam has assigned, so none names a real
// app. docs/design/app-shell.md lists the same table.
internal sealed class SampleLibrary : ILibraryService
{
    private static readonly IReadOnlyList<Title> s_titles =
    [
        new(TitleId.Steam(4_000_000_010), "Brasswing Courier", InstallState.Installed, LaunchPath.Direct),
        new(TitleId.Steam(4_000_000_020), "Lanternfall Expedition", InstallState.Downloading(0.42), LaunchPath.Dock),
        new(TitleId.Steam(4_000_000_030), "Quarry of the Nine Kites", InstallState.UpdatePending, LaunchPath.Dock),
        new(TitleId.Steam(4_000_000_040), "Saltmarsh Signal Tower", InstallState.NotInstalled, LaunchPath.Unclassified),
        new(
            TitleId.Steam(4_000_000_050),
            "The Unabridged Annals of Captain Wobblethorpe and the Seventeen Clockwork Lighthouses of the Drifting Archipelago: Definitive Edition",
            InstallState.Installed,
            LaunchPath.Unclassified),
        new(TitleId.Local("pocket-orrery"), "Pocket Orrery", InstallState.Installed, LaunchPath.Direct),
    ];

    public Task<IReadOnlyList<Title>> GetTitlesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(s_titles);
    }
}
