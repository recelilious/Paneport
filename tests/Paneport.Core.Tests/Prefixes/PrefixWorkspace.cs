// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Prefixes;
using Paneport.Core.Storage;
using Paneport.Core.Tests.Compat;
using Paneport.Tests.Fakes;

namespace Paneport.Core.Tests.Prefixes;

// A container and a bundled template in a temporary folder, with helpers to build a template, to
// run the installer over it and to read a tree back as text, so that a test compares whole trees.
internal sealed class PrefixWorkspace : IDisposable
{
    public const string Windows = "drive_c/windows/";

    private readonly TemporaryFolder _folder = new();

    public PrefixWorkspace()
    {
        Container = _folder.FileOf("container");
        Template = _folder.FileOf("bundle/wine/prefix-template");
        Directory.CreateDirectory(Container);
    }

    public string Container { get; }

    public string Template { get; }

    public string PrefixesFolder => StorageLayout.PathOf(Container, StorageLayout.Prefixes);

    public string Prefix => Path.Combine(PrefixesFolder, PrefixNames.Shared);

    public FakeBackupMarks Marks { get; } = new();

    public RecordingLog Log { get; } = new();

    // Writes a template: the files by relative path, and its marker, replacing an earlier template.
    public void MakeTemplate(int version, IReadOnlyDictionary<string, string> files, params string[] replaceOnUpdate)
    {
        if (Directory.Exists(Template))
        {
            Directory.Delete(Template, recursive: true);
        }

        Directory.CreateDirectory(Template);
        foreach (var (path, content) in files)
        {
            Write(Template, path, content);
        }

        var replace = string.Join(",", replaceOnUpdate.Select(folder => $"\"{folder}\""));
        Write(
            Template,
            TemplateMarker.FileName,
            $"{{ \"schema\": 1, \"version\": {version}, \"wineRevision\": \"rev{version}\", \"replaceOnUpdate\": [{replace}] }}");
    }

    public PrefixInstaller Installer(IPrefixFiles? files = null) =>
        new(Container, Template, files ?? new PhysicalPrefixFiles(), Marks, Log);

    public Task<PrefixInstallResult> Install(IPrefixFiles? files = null) =>
        Installer(files).InstallAsync(PrefixNames.Shared, CancellationToken.None);

    public static void Write(string root, string relative, string content)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    // Every entry of a tree as "path" for a folder and "path=content" for a file, in path order.
    public static IReadOnlyList<string> Tree(string root) =>
    [
        .. new PhysicalPrefixFiles().List(root).Select(entry => entry.Kind switch
        {
            PrefixEntryKind.File => $"{entry.Path}={File.ReadAllText(Path.Combine(root, entry.Path.Replace('/', Path.DirectorySeparatorChar)))}",
            PrefixEntryKind.Symlink => $"{entry.Path}->{entry.LinkTarget}",
            _ => entry.Path + "/",
        }),
    ];

    public void Dispose() => _folder.Dispose();
}
