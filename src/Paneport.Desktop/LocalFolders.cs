// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Desktop;

// The desktop head's stand-ins for the folders that the iOS head takes from the app bundle and its
// container (C-0009 section 4, C-0004 section 4). The stages whose pages read the compatibility
// database open it over these folders here (docs/design/compat-db.md section 7).
internal static class LocalFolders
{
    // The snapshot of compat/ that the build copies next to the program, where the iOS build
    // bundles it.
    public static string BundledCompatTitles { get; } = Path.Combine(AppContext.BaseDirectory, "compat", "titles");

    // The device layer, in the user's local application data, which nothing syncs. The store creates
    // the folder with its first save.
    public static string DeviceCompat { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Paneport.Desktop", "compat");
}
