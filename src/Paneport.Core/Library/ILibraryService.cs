// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// The titles in the user's library (the library manager of C-0007 section 2).
/// </summary>
/// <remarks>
/// For now it only lists titles. M7.1 builds the library from Steam behind this interface, and
/// M6.5 adds imported titles.
/// </remarks>
public interface ILibraryService
{
    /// <summary>Lists the titles in the library.</summary>
    /// <param name="cancellationToken">Cancels the listing.</param>
    /// <returns>The titles, in no particular order.</returns>
    Task<IReadOnlyList<Title>> GetTitlesAsync(CancellationToken cancellationToken);
}
