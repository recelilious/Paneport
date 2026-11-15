// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>The file system of the device or the workstation, through System.IO.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken) =>
        File.ReadAllBytesAsync(path, cancellationToken);

    /// <inheritdoc />
    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);

            // A file that replaces another must be on storage before the rename, or a crash right
            // after the rename could leave an empty file in its place.
            stream.Flush(flushToDisk: true);
        }
    }

    /// <inheritdoc />
    public void ReplaceFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: true);

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);
}
