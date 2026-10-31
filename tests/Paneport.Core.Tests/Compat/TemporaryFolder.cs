// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Tests.Compat;

// A folder of its own under the system's temporary folder, deleted with everything in it.
internal sealed class TemporaryFolder : IDisposable
{
    public TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paneport-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string FileOf(string name) => System.IO.Path.Combine(Path, name);

    // Copies the fixtures of one folder into this one.
    public TemporaryFolder WithFixtures(params string[] folder)
    {
        foreach (var file in Directory.EnumerateFiles(CompatTestFiles.Fixture(folder), "*.json"))
        {
            File.Copy(file, FileOf(System.IO.Path.GetFileName(file)));
        }

        return this;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file another process still holds stays behind in the temporary folder.
        }
    }
}
