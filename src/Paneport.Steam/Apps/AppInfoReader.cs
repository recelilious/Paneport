// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Steam;

namespace Paneport.Steam.Apps;

// Reads what the Steam gate needs from an app's information (S2.3.4, steam-service.md section 6):
// its name from the common section, and its Windows depots from the depots section, each with the
// size of its public manifest, its architecture and whether a public manifest is named. A section
// that is missing, or a value that cannot be read, becomes a problem in the result, never an
// exception, so the page shows what it found next to what it did not.
//
// The layout is the one that Steam's app information shows publicly, for example on SteamDB:
// common/name; depots/<id>/config/oslist with a comma-separated list such as "windows,macos",
// config/osarch, name, maxsize, and manifests/public, which is either the manifest ID itself or a
// section with gid, size and download.
internal static class AppInfoReader
{
    public static SteamAppInfo Read(uint appId, KeyValueNode? info)
    {
        var problems = new List<string>();
        if (info is null)
        {
            problems.Add("Steam returned no information for the app.");
            return new SteamAppInfo(appId, Name: null, [], problems);
        }

        var name = info["common"]?["name"]?.Value;
        if (info["common"] is null)
        {
            problems.Add("The app's information has no common section.");
        }
        else if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("The app's information names no app name.");
            name = null;
        }

        var depots = new List<SteamDepotInfo>();
        if (info["depots"] is not { } depotSection)
        {
            problems.Add("The app's information has no depots section.");
        }
        else
        {
            foreach (var depot in depotSection.Children)
            {
                if (ReadDepot(depot, problems) is { } read)
                {
                    depots.Add(read);
                }
            }

            if (depots.Count == 0)
            {
                problems.Add("The app's information lists no Windows depot.");
            }
        }

        return new SteamAppInfo(appId, name, depots, problems);
    }

    // A depot with Windows content, or null for an entry that is no depot or holds no Windows
    // content. Entries of the depots section that are not numbered, such as branches, are no depots.
    private static SteamDepotInfo? ReadDepot(KeyValueNode depot, List<string> problems)
    {
        if (!uint.TryParse(depot.Name, NumberStyles.None, CultureInfo.InvariantCulture, out var depotId) || depotId == 0)
        {
            return null;
        }

        if (!HoldsWindowsContent(depot))
        {
            return null;
        }

        var manifests = depot["manifests"];
        var publicManifest = manifests?["public"];
        var hasPublicManifest = ReadManifestId(publicManifest) is not null;
        if (!hasPublicManifest)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Depot {depotId} names no public manifest."));
        }

        var size = ReadNumber(publicManifest?["size"]) ?? ReadNumber(depot["maxsize"]);
        if (size is null)
        {
            problems.Add(string.Create(CultureInfo.InvariantCulture, $"Depot {depotId} gives no size."));
        }

        var architecture = depot["config"]?["osarch"]?.Value;
        return new SteamDepotInfo(
            depotId,
            string.IsNullOrWhiteSpace(depot["name"]?.Value) ? null : depot["name"]!.Value,
            size,
            string.IsNullOrWhiteSpace(architecture) ? null : architecture,
            hasPublicManifest);
    }

    // The public manifest's ID, from its older form, a value, or its newer one, a section with gid.
    public static ulong? ReadManifestId(KeyValueNode? publicManifest) =>
        publicManifest is null ? null : ReadNumber(publicManifest.Value is not null ? publicManifest : publicManifest["gid"]);

    // A depot holds Windows content when its oslist names windows, or when it has no oslist at all,
    // which means every platform.
    private static bool HoldsWindowsContent(KeyValueNode depot)
    {
        var osList = depot["config"]?["oslist"]?.Value;
        if (string.IsNullOrWhiteSpace(osList))
        {
            return true;
        }

        return osList.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Contains("windows", StringComparer.OrdinalIgnoreCase);
    }

    private static ulong? ReadNumber(KeyValueNode? node) =>
        node?.Value is { } text
            && ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            && number > 0
            ? number
            : null;
}
