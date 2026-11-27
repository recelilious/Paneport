// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Prefixes;

/// <summary>The prefix files through System.IO, which behaves the same on iOS and on the workstation.</summary>
public sealed class PhysicalPrefixFiles : IPrefixFiles
{
    /// <inheritdoc />
    public bool DirectoryExists(string path) => Directory.Exists(path);

    /// <inheritdoc />
    public bool FileExists(string path) => File.Exists(path);

    /// <inheritdoc />
    public IReadOnlyList<PrefixEntry> List(string root)
    {
        var entries = new List<PrefixEntry>();
        Walk(new DirectoryInfo(root), string.Empty, entries);
        entries.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return entries;
    }

    /// <inheritdoc />
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);

    /// <inheritdoc />
    public void CopyFile(string sourcePath, string destinationPath)
    {
        using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
        input.CopyTo(output);

        // The rename that follows a copy is the commit, so the content must be on storage first.
        output.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public void CreateSymbolicLink(string path, string target) => File.CreateSymbolicLink(path, target);

    /// <inheritdoc />
    public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);

    /// <inheritdoc />
    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        output.Write(bytes);
        output.Flush(flushToDisk: true);
    }

    /// <inheritdoc />
    public void ReplaceFile(string sourcePath, string destinationPath) =>
        File.Move(sourcePath, destinationPath, overwrite: true);

    /// <inheritdoc />
    public void MoveDirectory(string sourcePath, string destinationPath) =>
        Directory.Move(sourcePath, destinationPath);

    /// <inheritdoc />
    public void DeleteFile(string path) => File.Delete(path);

    /// <inheritdoc />
    public void DeleteDirectoryTree(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    /// <inheritdoc />
    public bool DeleteDirectoryIfEmpty(string path)
    {
        try
        {
            Directory.Delete(path, recursive: false);
            return true;
        }
        catch (IOException)
        {
            return !Directory.Exists(path);
        }
    }

    private static void Walk(DirectoryInfo folder, string parent, List<PrefixEntry> entries)
    {
        foreach (var info in folder.EnumerateFileSystemInfos())
        {
            var path = parent + info.Name;
            if (info.LinkTarget is { } target)
            {
                entries.Add(new PrefixEntry(path, PrefixEntryKind.Symlink, target));
            }
            else if (info is DirectoryInfo directory)
            {
                entries.Add(new PrefixEntry(path, PrefixEntryKind.Directory));
                Walk(directory, path + "/", entries);
            }
            else
            {
                entries.Add(new PrefixEntry(path, PrefixEntryKind.File));
            }
        }
    }
}
