// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Steam;

namespace Paneport.Core.ViewModels;

/// <summary>
/// A Windows depot as the Steam gate page lists it.
/// </summary>
/// <param name="Depot">The depot.</param>
public sealed record SteamDepotRow(SteamDepotInfo Depot)
{
    /// <summary>Gets the line that the page shows: the depot's ID, name, size and architecture.</summary>
    public string Text
    {
        get
        {
            var name = Depot.Name ?? "no name";
            var size = Depot.Size is { } bytes ? SteamGateTexts.Bytes(bytes) : "size unknown";
            var architecture = Depot.Architecture is { } arch ? $", {arch}-bit" : string.Empty;
            var manifest = Depot.HasPublicManifest ? string.Empty : ", no public manifest";
            return string.Create(CultureInfo.InvariantCulture, $"{Depot.DepotId}: {name}, {size}{architecture}{manifest}");
        }
    }
}
