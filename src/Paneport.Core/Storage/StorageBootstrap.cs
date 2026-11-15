// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Storage;

/// <summary>
/// Sets up the storage layout of C-0004 section 4 at launch: it creates every folder and marks
/// the folders that say "No" as excluded from backup. <see cref="MarkAgain"/> renews a mark after
/// files in a folder were replaced, as the contract asks.
/// </summary>
/// <remarks>
/// A folder that cannot be created or marked is logged with the exception's type only, because the
/// message can hold a local path, and the other folders are still set up.
/// </remarks>
public sealed class StorageBootstrap
{
    private const string LogSource = nameof(StorageBootstrap);

    private readonly string _containerRoot;
    private readonly IFileSystem _files;
    private readonly IBackupMarks _marks;
    private readonly ILog _log;

    /// <summary>Creates the bootstrap for an app container.</summary>
    /// <param name="containerRoot">The app container's root folder.</param>
    /// <param name="files">The file system.</param>
    /// <param name="marks">The backup marks.</param>
    /// <param name="log">The log.</param>
    public StorageBootstrap(string containerRoot, IFileSystem files, IBackupMarks marks, ILog log)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerRoot);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(log);
        _containerRoot = containerRoot;
        _files = files;
        _marks = marks;
        _log = log;
    }

    /// <summary>Gets the full path of a folder of the layout.</summary>
    /// <param name="folder">The folder.</param>
    /// <returns>The folder's full path.</returns>
    public string PathOf(StorageFolder folder) => StorageLayout.PathOf(_containerRoot, folder);

    /// <summary>Creates every folder of the layout and marks the ones excluded from backup.</summary>
    /// <returns>What each folder looks like afterwards.</returns>
    public IReadOnlyList<FolderState> Run()
    {
        foreach (var folder in StorageLayout.Folders)
        {
            var path = PathOf(folder);
            try
            {
                _files.CreateDirectory(path);
            }
            catch (Exception exception)
            {
                _log.Write(LogLevel.Error, LogSource, $"Creating {folder.RelativePath} failed with {exception.GetType().Name}.");
                continue;
            }

            if (folder.Backup == BackupPolicy.Excluded)
            {
                Mark(folder, path);
            }
        }

        return Inspect();
    }

    /// <summary>
    /// Marks a folder that says "No" again, after files in it were replaced. A folder that goes
    /// into backups is left alone.
    /// </summary>
    /// <param name="folder">The folder.</param>
    public void MarkAgain(StorageFolder folder)
    {
        ArgumentNullException.ThrowIfNull(folder);
        if (folder.Backup == BackupPolicy.Excluded)
        {
            Mark(folder, PathOf(folder));
        }
    }

    /// <summary>Reads what each folder of the layout looks like now.</summary>
    /// <returns>One state per folder, in the order of the layout.</returns>
    public IReadOnlyList<FolderState> Inspect() =>
        [.. StorageLayout.Folders.Select(folder =>
        {
            var path = PathOf(folder);
            var exists = _files.DirectoryExists(path);
            return new FolderState(folder, exists, exists ? _marks.IsExcluded(path) : null);
        })];

    private void Mark(StorageFolder folder, string path)
    {
        try
        {
            _marks.SetExcluded(path);
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Error, LogSource, $"Marking {folder.RelativePath} failed with {exception.GetType().Name}.");
        }
    }
}
