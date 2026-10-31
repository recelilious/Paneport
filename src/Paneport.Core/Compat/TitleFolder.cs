// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Core.Compat;

// A folder of title files, one per title, named after the title's identity (C-0008 section 1).
// Both layers keep their files this way.
internal sealed class TitleFolder(string path, CompatLayer layer)
{
    private const string Extension = ".json";

    public string Path { get; } = path;

    public string FileOf(TitleId id) => System.IO.Path.Combine(Path, id + Extension);

    public async Task<TitleEntryResult> LoadAsync(TitleId id, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(FileOf(id), cancellationToken).ConfigureAwait(false);
        return bytes is null ? TitleEntryResult.Missing : TitleEntryJson.Read(bytes, layer, id);
    }

    // Every *.json file, in ordinal order of the names. The extension is matched in any letter case,
    // so that a name the case-sensitive file system of iOS would not find is reported on Windows too.
    public async Task<IReadOnlyList<CompatFile>> LoadAllAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(Path))
        {
            return [];
        }

        var files = new List<CompatFile>();
        var names = Directory.EnumerateFiles(Path)
            .Select(file => System.IO.Path.GetFileName(file))
            .Where(name => name.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal);
        foreach (var name in names)
        {
            if (!name.EndsWith(Extension, StringComparison.Ordinal)
                || !TitleId.TryParse(name[..^Extension.Length], out var id))
            {
                var problem = new CompatProblem(
                    CompatRule.Identity,
                    CompatPath.Root,
                    "The file name is not steam-<appid>.json or local-<slug>.json.");
                files.Add(new CompatFile(name, null, TitleEntryResult.Rejected([problem])));
                continue;
            }

            var bytes = await ReadAsync(FileOf(id), cancellationToken).ConfigureAwait(false);
            var result = bytes is null ? TitleEntryResult.Missing : TitleEntryJson.Read(bytes, layer, id);
            files.Add(new CompatFile(name, id, result));
        }

        return files;
    }

    // Reads at most one byte more than a title file may hold, so that a larger file is rejected
    // without being read whole. Null when the file does not exist.
    private static async Task<byte[]?> ReadAsync(string file, CancellationToken cancellationToken)
    {
        try
        {
            var stream = new FileStream(
                file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                // A file that shrinks while it is read gives the bytes that were there.
                var buffer = new byte[(int)Math.Min(stream.Length, TitleEntryJson.MaxFileBytes + 1L)];
                var read = await stream.ReadAtLeastAsync(
                    buffer, buffer.Length, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);
                return read == buffer.Length ? buffer : buffer[..read];
            }
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }
}
