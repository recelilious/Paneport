// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// The device's own compatibility layer: one file per title in
/// <c>Library/Application Support/Paneport/compat/</c>, named like the repository's files
/// (C-0008 section 1, C-0004 section 4). It holds the computed classification and the user's
/// overrides, and it never leaves the device.
/// </summary>
/// <remarks>
/// <para>
/// A save replaces a title's file atomically: the entry goes to a temporary file in the same folder,
/// which then takes the file's place, so a save that fails leaves the old file. The store runs one
/// operation at a time, so a load waits for a pending save. One store serves a folder.
/// </para>
/// <para>
/// iOS backs the folder up (C-0004 section 4), so nothing here excludes it. Only the classifier
/// writes <c>drm</c>; the user's overrides are the other fields.
/// </para>
/// </remarks>
public sealed class DeviceLayerStore : IDisposable
{
    private const string TemporarySuffix = ".tmp";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TitleFolder _folder;

    /// <summary>Opens the store.</summary>
    /// <param name="folder">The folder of the device layer. A save creates it when it does not exist.</param>
    /// <exception cref="ArgumentException"><paramref name="folder"/> is empty.</exception>
    public DeviceLayerStore(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        _folder = new TitleFolder(folder, CompatLayer.Device);
    }

    /// <summary>Reads the device entry of a title.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The entry, the rules its file breaks, or <see cref="TitleEntryStatus.Missing"/>.</returns>
    /// <remarks>A rejected file stays in place until a save replaces it.</remarks>
    public async Task<TitleEntryResult> LoadAsync(TitleId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _folder.LoadAsync(id, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Reads every title file of the device layer.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>Each <c>*.json</c> file with what reading it gave, in ordinal order of the names.</returns>
    public async Task<IReadOnlyList<CompatFile>> LoadAllAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _folder.LoadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Saves a title's device entry in place of its file, atomically.</summary>
    /// <param name="entry">The entry, which must follow the device layer's rules.</param>
    /// <param name="cancellationToken">
    /// Cancels the save. A save cancelled before its file is replaced leaves the old file.
    /// </param>
    /// <returns>A task that completes once the file is replaced.</returns>
    /// <exception cref="ArgumentException">The entry breaks a rule of the device layer.</exception>
    public async Task SaveAsync(TitleEntry entry, CancellationToken cancellationToken)
    {
        var bytes = TitleEntryJson.Write(entry, CompatLayer.Device);
        var file = _folder.FileOf(entry.Id.ToTitleId());
        var temporary = file + TemporarySuffix;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_folder.Path);
            var stream = new FileStream(
                temporary, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

                // The bytes reach the disk before the rename, so a crash leaves the old file or the new one.
                stream.Flush(flushToDisk: true);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, file, overwrite: true);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Deletes a title's device entry, as removing the title does.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="cancellationToken">Cancels the deletion before it starts.</param>
    /// <returns>True when a file was deleted; false when the layer had none for the title.</returns>
    public async Task<bool> DeleteAsync(TitleId id, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(id);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var file = _folder.FileOf(id);
            TryDelete(file + TemporarySuffix);
            if (!File.Exists(file))
            {
                return false;
            }

            File.Delete(file);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Releases the store's lock.</summary>
    public void Dispose() => _gate.Dispose();

    // Cleans up after a failed save. The save's own exception is the one that matters.
    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
