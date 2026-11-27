// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Prefixes;

/// <summary>
/// The file operations that the prefix installer needs. <see cref="PhysicalPrefixFiles"/> does them
/// with System.IO on every platform; tests wrap it to make a copy fail part way.
/// </summary>
/// <remarks>
/// Every call is synchronous, because the installer runs them on a worker thread. The exceptions
/// of an implementation can name a local path in their message, so callers log only their type.
/// </remarks>
public interface IPrefixFiles
{
    /// <summary>Checks whether a folder exists.</summary>
    /// <param name="path">The folder's full path.</param>
    /// <returns>True when the folder exists.</returns>
    bool DirectoryExists(string path);

    /// <summary>Checks whether a file exists.</summary>
    /// <param name="path">The file's full path.</param>
    /// <returns>True when the file exists.</returns>
    bool FileExists(string path);

    /// <summary>Lists every entry below a folder, at any depth, without following symbolic links.</summary>
    /// <param name="root">The folder's full path.</param>
    /// <returns>The entries, ordered by path, so that a folder comes before what it holds.</returns>
    IReadOnlyList<PrefixEntry> List(string root);

    /// <summary>Creates a folder and the folders above it, if they do not exist yet.</summary>
    /// <param name="path">The folder's full path.</param>
    void CreateDirectory(string path);

    /// <summary>Copies a file's content to a new or existing file and flushes it to storage.</summary>
    /// <param name="sourcePath">The file to copy.</param>
    /// <param name="destinationPath">The file to write, in a folder that exists.</param>
    void CopyFile(string sourcePath, string destinationPath);

    /// <summary>Creates a symbolic link.</summary>
    /// <param name="path">The link's full path.</param>
    /// <param name="target">Where the link points.</param>
    void CreateSymbolicLink(string path, string target);

    /// <summary>Reads a whole file.</summary>
    /// <param name="path">The file's full path.</param>
    /// <returns>The file's bytes.</returns>
    byte[] ReadAllBytes(string path);

    /// <summary>Writes a whole file, replacing one that exists, and flushes it to storage.</summary>
    /// <param name="path">The file's full path.</param>
    /// <param name="bytes">The content.</param>
    void WriteAllBytes(string path, ReadOnlySpan<byte> bytes);

    /// <summary>Moves a file over another in one step: a reader sees the old or the new file, never a part.</summary>
    /// <param name="sourcePath">The file to move, in the same folder as the destination.</param>
    /// <param name="destinationPath">The file to replace, which may not exist yet.</param>
    void ReplaceFile(string sourcePath, string destinationPath);

    /// <summary>Renames a folder in one step.</summary>
    /// <param name="sourcePath">The folder to rename.</param>
    /// <param name="destinationPath">The new path, in the same parent folder, which does not exist.</param>
    void MoveDirectory(string sourcePath, string destinationPath);

    /// <summary>Deletes a file or a symbolic link; one that does not exist is not an error.</summary>
    /// <param name="path">The full path.</param>
    void DeleteFile(string path);

    /// <summary>Deletes a folder with everything in it; one that does not exist is not an error.</summary>
    /// <param name="path">The folder's full path.</param>
    void DeleteDirectoryTree(string path);

    /// <summary>Deletes a folder only when it is empty.</summary>
    /// <param name="path">The folder's full path.</param>
    /// <returns>True when the folder is gone afterwards.</returns>
    bool DeleteDirectoryIfEmpty(string path);
}
