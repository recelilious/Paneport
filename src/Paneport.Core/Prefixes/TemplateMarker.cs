// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;

namespace Paneport.Core.Prefixes;

/// <summary>
/// The version marker at the root of a template prefix, <c>paneport-template.json</c>
/// (docs/design/runtime-ios.md section 7). The installed prefix keeps a copy of the marker of the
/// template it came from.
/// </summary>
/// <param name="Schema">The marker's schema, 1 for now.</param>
/// <param name="Version">An integer that grows with each change of the template.</param>
/// <param name="WineRevision">The commit of the Wine fork that the template was made for.</param>
/// <param name="ReplaceOnUpdate">
/// The folders that a newer template replaces in an installed prefix, as relative paths with
/// forward slashes that end in a slash, such as <c>drive_c/windows/</c>.
/// </param>
public sealed record TemplateMarker(
    int Schema,
    int Version,
    string WineRevision,
    IReadOnlyList<string> ReplaceOnUpdate)
{
    /// <summary>The marker's file name, at the root of a template and of an installed prefix.</summary>
    public const string FileName = "paneport-template.json";

    /// <summary>The only schema that exists.</summary>
    public const int CurrentSchema = 1;

    /// <summary>Reads a marker, strictly: a marker with a problem is refused, never guessed at.</summary>
    /// <param name="json">The file's bytes, as UTF-8 JSON.</param>
    /// <param name="marker">The marker, when the file is valid.</param>
    /// <param name="problem">What is wrong, when it is not; it names no path.</param>
    /// <returns>True when the file is a valid marker.</returns>
    public static bool TryParse(ReadOnlySpan<byte> json, out TemplateMarker? marker, out string? problem)
    {
        marker = null;
        try
        {
            var reader = new Utf8JsonReader(json);
            using var document = JsonDocument.ParseValue(ref reader);
            problem = Read(document.RootElement, out marker);
        }
        catch (JsonException)
        {
            problem = "the marker is not valid JSON";
        }

        return problem is null;
    }

    private static string? Read(JsonElement root, out TemplateMarker? marker)
    {
        marker = null;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return "the marker is not an object";
        }

        if (!TryGetInteger(root, "schema", out var schema) || schema != CurrentSchema)
        {
            return "the marker's schema is not 1";
        }

        if (!TryGetInteger(root, "version", out var version) || version < 1)
        {
            return "the marker's version is not a positive integer";
        }

        if (!root.TryGetProperty("wineRevision", out var revision)
            || revision.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(revision.GetString()))
        {
            return "the marker has no wineRevision";
        }

        if (!root.TryGetProperty("replaceOnUpdate", out var replace) || replace.ValueKind != JsonValueKind.Array)
        {
            return "the marker has no replaceOnUpdate list";
        }

        var folders = new List<string>();
        foreach (var item in replace.EnumerateArray())
        {
            var folder = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (folder is null || !IsFolderPrefix(folder))
            {
                return "replaceOnUpdate holds an entry that is no relative folder ending in a slash";
            }

            folders.Add(folder);
        }

        marker = new TemplateMarker(schema, version, revision.GetString()!, folders);
        return null;
    }

    private static bool TryGetInteger(JsonElement root, string name, out int value)
    {
        value = 0;
        return root.TryGetProperty(name, out var element)
            && element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out value);
    }

    // A folder inside the prefix: forward slashes, a trailing slash, and no empty, "." or ".."
    // segment, so that no entry can reach outside the prefix.
    private static bool IsFolderPrefix(string folder)
    {
        if (folder.Length < 2
            || folder[^1] != '/'
            || folder.Contains('\\', StringComparison.Ordinal)
            || folder.Contains(':', StringComparison.Ordinal))
        {
            return false;
        }

        return folder[..^1].Split('/').All(segment => segment.Length > 0 && segment is not "." and not "..");
    }
}
