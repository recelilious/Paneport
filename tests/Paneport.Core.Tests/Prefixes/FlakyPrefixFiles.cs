// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Prefixes;

namespace Paneport.Core.Tests.Prefixes;

// The physical files, counting each copy and failing after a number of copies, as a copy that the
// system interrupts would.
internal sealed class FlakyPrefixFiles(int? failAfterCopies = null) : IPrefixFiles
{
    private readonly PhysicalPrefixFiles _inner = new();

    public int Copies { get; private set; }

    public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

    public bool FileExists(string path) => _inner.FileExists(path);

    public IReadOnlyList<PrefixEntry> List(string root) => _inner.List(root);

    public void CreateDirectory(string path) => _inner.CreateDirectory(path);

    public void CopyFile(string sourcePath, string destinationPath)
    {
        if (Copies >= failAfterCopies)
        {
            throw new IOException("The copy was interrupted.");
        }

        Copies++;
        _inner.CopyFile(sourcePath, destinationPath);
    }

    public void CreateSymbolicLink(string path, string target) => _inner.CreateSymbolicLink(path, target);

    public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

    public void WriteAllBytes(string path, ReadOnlySpan<byte> bytes) => _inner.WriteAllBytes(path, bytes);

    public void ReplaceFile(string sourcePath, string destinationPath) => _inner.ReplaceFile(sourcePath, destinationPath);

    public void MoveDirectory(string sourcePath, string destinationPath) => _inner.MoveDirectory(sourcePath, destinationPath);

    public void DeleteFile(string path) => _inner.DeleteFile(path);

    public void DeleteDirectoryTree(string path) => _inner.DeleteDirectoryTree(path);

    public bool DeleteDirectoryIfEmpty(string path) => _inner.DeleteDirectoryIfEmpty(path);
}
