// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Storage;

namespace Paneport.Tests.Fakes;

// A file system in memory, with forward-slash paths as the tests build them. A test can make the
// next write fail halfway, after it wrote part of the file, as a crash or a full disk would; can
// hold a write open until it releases it; and can make reads or folder creation fail.
internal sealed class FakeFileSystem : IFileSystem
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);
    private readonly HashSet<string> _folders = new(StringComparer.Ordinal);
    private readonly Lock _lock = new();

    public bool FailNextWrite { get; set; }

    public bool FailReads { get; set; }

    public HashSet<string> FoldersThatFail { get; } = new(StringComparer.Ordinal);

    public TaskCompletionSource? HoldWrites { get; set; }

    public List<string> Replacements { get; } = [];

    public IReadOnlyCollection<string> Folders
    {
        get
        {
            lock (_lock)
            {
                return [.. _folders];
            }
        }
    }

    public bool DirectoryExists(string path)
    {
        lock (_lock)
        {
            return _folders.Contains(Normalize(path));
        }
    }

    public void CreateDirectory(string path)
    {
        var normalized = Normalize(path);
        if (FoldersThatFail.Contains(normalized))
        {
            throw new IOException("The test fails this folder.");
        }

        lock (_lock)
        {
            _folders.Add(normalized);
        }
    }

    public bool FileExists(string path)
    {
        lock (_lock)
        {
            return _files.ContainsKey(Normalize(path));
        }
    }

    public Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (FailReads)
        {
            throw new IOException("The test fails reads.");
        }

        lock (_lock)
        {
            return _files.TryGetValue(Normalize(path), out var bytes)
                ? Task.FromResult(bytes.ToArray())
                : throw new FileNotFoundException("The test has no such file.");
        }
    }

    public async Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken)
    {
        if (HoldWrites is { } hold)
        {
            await hold.Task.WaitAsync(cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var normalized = Normalize(path);
        if (FailNextWrite)
        {
            FailNextWrite = false;
            lock (_lock)
            {
                _files[normalized] = bytes[..(bytes.Length / 2)].ToArray();
            }

            throw new IOException("The test interrupts this write.");
        }

        lock (_lock)
        {
            _files[normalized] = bytes.ToArray();
        }
    }

    public void ReplaceFile(string sourcePath, string destinationPath)
    {
        lock (_lock)
        {
            var source = Normalize(sourcePath);
            var bytes = _files[source];
            _files.Remove(source);
            _files[Normalize(destinationPath)] = bytes;
            Replacements.Add(Normalize(destinationPath));
        }
    }

    public void DeleteFile(string path)
    {
        lock (_lock)
        {
            _files.Remove(Normalize(path));
        }
    }

    public byte[]? Content(string path)
    {
        lock (_lock)
        {
            return _files.TryGetValue(Normalize(path), out var bytes) ? bytes : null;
        }
    }

    public void SetContent(string path, byte[] bytes)
    {
        lock (_lock)
        {
            _files[Normalize(path)] = bytes;
        }
    }

    private static string Normalize(string path) => path.Replace('\\', '/');
}
