// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;

namespace Paneport.IOS;

// The app container's root folder, as iOS names it through the Library folder of the user domain.
// The storage layout of C-0004 section 4 lies below it.
internal static class ContainerFolders
{
    public static string FindRoot()
    {
        var library = NSFileManager.DefaultManager.GetUrls(NSSearchPathDirectory.LibraryDirectory, NSSearchPathDomain.User);
        var path = library.Length > 0 ? library[0].Path : null;
        return Path.GetDirectoryName(path?.TrimEnd('/'))
            ?? throw new InvalidOperationException("iOS named no Library folder for the app container.");
    }
}
