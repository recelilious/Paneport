// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// A library without titles: the iOS head's library until M7.1 builds it from Steam and M6.5
/// adds imported titles.
/// </summary>
public sealed class EmptyLibraryService : ILibraryService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Title>> GetTitlesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<Title>>([]);
    }
}
