// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Storage;

/// <summary>
/// The file operations that the stores on the device need. <see cref="PhysicalFileSystem"/> does
/// them with System.IO; tests use a fake that can fail at any step.
/// </summary>
/// <remarks>
/// The exceptions of an implementation can name a local path in their message, so callers log
/// only their type.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Checks whether a folder exists.</summary>
    /// <param name="path">The folder's full path.</param>
    /// <returns>True when the folder exists.</returns>
    bool DirectoryExists(string path);

    /// <summary>Creates a folder and the folders above it, if they do not exist yet.</summary>
    /// <param name="path">The folder's full path.</param>
    void CreateDirectory(string path);

    /// <summary>Checks whether a file exists.</summary>
    /// <param name="path">The file's full path.</param>
    /// <returns>True when the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Reads a whole file.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file's bytes.</returns>
    Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken);

    /// <summary>Writes a whole file, replacing one that exists, and flushes it to storage.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="bytes">The content.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    /// <returns>A task that completes when the file is written.</returns>
    Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken);

    /// <summary>
    /// Moves a file over another in one step, so that a reader sees either the old or the new
    /// file, never a part of one.
    /// </summary>
    /// <param name="sourcePath">The file to move, in the same folder as the destination.</param>
    /// <param name="destinationPath">The file to replace, which may not exist yet.</param>
    void ReplaceFile(string sourcePath, string destinationPath);

    /// <summary>Deletes a file; a file that does not exist is not an error.</summary>
    /// <param name="path">The file's full path.</param>
    void DeleteFile(string path);
}
