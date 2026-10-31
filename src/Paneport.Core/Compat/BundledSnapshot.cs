// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The snapshot of <c>compat/titles/</c> that the app bundles (C-0008 section 1,
/// C-0009 section 4). It is read only, and its entries follow the bundled layer's rules.
/// </summary>
public sealed class BundledSnapshot
{
    private readonly TitleFolder _folder;

    /// <summary>Opens the snapshot.</summary>
    /// <param name="titlesFolder">
    /// The snapshot's folder of title files, <c>compat/titles</c> in the app bundle. It may not exist,
    /// and the snapshot is then empty.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="titlesFolder"/> is empty.</exception>
    public BundledSnapshot(string titlesFolder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titlesFolder);
        _folder = new TitleFolder(titlesFolder, CompatLayer.Bundled);
    }

    /// <summary>Reads the bundled entry of a title.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, the rules its file breaks, or <see cref="TitleEntryStatus.Missing"/>.</returns>
    public Task<TitleEntryResult> LoadAsync(TitleId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _folder.LoadAsync(id, cancellationToken);
    }

    /// <summary>Reads every title file of the snapshot, such as for matching a local title by its hash.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each <c>*.json</c> file with what reading it gave, in ordinal order of the names.</returns>
    public Task<IReadOnlyList<CompatFile>> LoadAllAsync(CancellationToken cancellationToken) =>
        _folder.LoadAllAsync(cancellationToken);
}
