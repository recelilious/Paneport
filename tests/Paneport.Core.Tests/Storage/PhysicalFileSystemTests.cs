// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Storage;
using Paneport.Core.Tests.Compat;
using Xunit;

namespace Paneport.Core.Tests.Storage;

// The file system of the device through System.IO, in a temporary folder, as IFileSystem promises
// it to the settings file and the crash record store. Whether a write reached storage and whether
// a replace was one rename cannot be seen from here; what each call leaves behind can.
public sealed class PhysicalFileSystemTests
{
    private static readonly byte[] s_first = [1, 2, 3];
    private static readonly byte[] s_second = [4, 5];

    [Fact]
    public async Task AWrittenFileReadsBackAndASecondWriteReplacesIt()
    {
        using var folder = new TemporaryFolder();
        var files = new PhysicalFileSystem();
        var path = folder.FileOf("settings.json");

        await files.WriteAllBytesAsync(path, s_first, CancellationToken.None);
        await files.WriteAllBytesAsync(path, s_second, CancellationToken.None);

        Assert.True(files.FileExists(path));
        Assert.Equal(s_second, await files.ReadAllBytesAsync(path, CancellationToken.None));
    }

    [Fact]
    public async Task AReplaceMovesTheFileOverAnExistingOneOrToANewPlace()
    {
        using var folder = new TemporaryFolder();
        var files = new PhysicalFileSystem();
        var source = folder.FileOf("settings.json.tmp");
        var destination = folder.FileOf("settings.json");
        await files.WriteAllBytesAsync(destination, s_first, CancellationToken.None);
        await files.WriteAllBytesAsync(source, s_second, CancellationToken.None);

        files.ReplaceFile(source, destination);

        Assert.False(files.FileExists(source));
        Assert.Equal(s_second, await files.ReadAllBytesAsync(destination, CancellationToken.None));

        var other = folder.FileOf("other.json");
        await files.WriteAllBytesAsync(source, s_first, CancellationToken.None);
        files.ReplaceFile(source, other);

        Assert.False(files.FileExists(source));
        Assert.Equal(s_first, await files.ReadAllBytesAsync(other, CancellationToken.None));
    }

    [Fact]
    public async Task DeletingAMissingFileIsNotAnError()
    {
        using var folder = new TemporaryFolder();
        var files = new PhysicalFileSystem();
        var path = folder.FileOf("crash-record.bin");
        await files.WriteAllBytesAsync(path, s_first, CancellationToken.None);

        files.DeleteFile(path);
        files.DeleteFile(path);

        Assert.False(files.FileExists(path));
    }

    [Fact]
    public void CreatingAFolderTwiceIsNotAnError()
    {
        using var folder = new TemporaryFolder();
        var files = new PhysicalFileSystem();
        var path = Path.Combine(folder.Path, "Library", "Caches", "Paneport");

        Assert.False(files.DirectoryExists(path));
        files.CreateDirectory(path);
        files.CreateDirectory(path);

        Assert.True(files.DirectoryExists(path));
    }

    [Fact]
    public async Task ACancelledReadEndsAsCancelled()
    {
        using var folder = new TemporaryFolder();
        var files = new PhysicalFileSystem();
        var path = folder.FileOf("settings.json");
        await files.WriteAllBytesAsync(path, s_first, CancellationToken.None);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => files.ReadAllBytesAsync(path, new CancellationToken(canceled: true)));
    }
}
