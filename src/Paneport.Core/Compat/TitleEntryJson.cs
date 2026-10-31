// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.Json;
using System.Text.Unicode;
using Paneport.Core.Library;

namespace Paneport.Core.Compat;

/// <summary>
/// Reads, checks and writes title files (C-0008 sections 1 and 2) with source-generated
/// <c>System.Text.Json</c> code. The app checks in code the rules it relies on, with no schema
/// library; CI validates the repository's files against the schema as well.
/// </summary>
/// <remarks>
/// A file that breaks a rule is rejected as a whole, and none of its values are used.
/// <c>docs/design/compat-db.md</c> lists the rules and how a newer schema version and unknown
/// fields are handled.
/// </remarks>
public static class TitleEntryJson
{
    /// <summary>The schema version that this build reads and writes.</summary>
    public const int SchemaVersion = 1;

    /// <summary>The largest title file that is read, 1 MiB.</summary>
    public const int MaxFileBytes = 1_048_576;

    /// <summary>Reads a title file.</summary>
    /// <param name="utf8Json">The file's bytes, UTF-8 with or without a byte-order mark.</param>
    /// <param name="layer">The layer the file belongs to, whose rules apply.</param>
    /// <param name="expectedId">
    /// The identity that the file's name gives, which the entry's <c>id</c> must match; null to
    /// skip that check.
    /// </param>
    /// <returns>The entry, or the rules the file breaks.</returns>
    public static TitleEntryResult Read(ReadOnlySpan<byte> utf8Json, CompatLayer layer, TitleId? expectedId = null)
    {
        CheckLayer(layer);
        if (utf8Json.Length > MaxFileBytes)
        {
            return Reject(CompatRule.Json, CompatPath.Root, "The file is larger than 1 MiB.");
        }

        if (utf8Json.StartsWith("﻿"u8))
        {
            utf8Json = utf8Json[3..];
        }

        if (!Utf8.IsValid(utf8Json))
        {
            return Reject(CompatRule.Json, CompatPath.Root, "The file is not valid UTF-8.");
        }

        var scan = StrictJsonScan.Scan(utf8Json);
        if (scan.Fatal is { } fatal)
        {
            return TitleEntryResult.Rejected([fatal]);
        }

        // A newer version may add or rename fields, so its other problems mean nothing here.
        if (scan.SchemaVersion > SchemaVersion && !scan.Problems.Any(problem => problem.Path == "$.schemaVersion"))
        {
            return Reject(
                CompatRule.NewerSchemaVersion,
                "$.schemaVersion",
                "The file follows a newer schema version than this build reads.");
        }

        if (scan.Problems.Count > 0)
        {
            return TitleEntryResult.Rejected(scan.Problems);
        }

        TitleDocument? document;
        try
        {
            document = JsonSerializer.Deserialize(utf8Json, CompatJsonContext.Default.TitleDocument);
        }
        catch (JsonException exception)
        {
            return Reject(
                CompatRule.Type,
                CompatPath.FromSerializer(exception.Path),
                "The value does not have the field's type. Integers are written without a fraction or an exponent.");
        }

        var problems = new List<CompatProblem>();
        var entry = TitleDocumentMapper.FromDocument(document!, problems);
        if (entry is null)
        {
            return TitleEntryResult.Rejected(problems);
        }

        TitleEntryRules.Check(entry, layer, problems);
        if (problems.Count == 0 && expectedId is not null && entry.Id.ToTitleId() != expectedId)
        {
            problems.Add(new CompatProblem(
                CompatRule.Identity,
                "$.id",
                "The id does not match the file name, steam-<appid>.json or local-<slug>.json."));
        }

        return problems.Count == 0 ? TitleEntryResult.Loaded(entry) : TitleEntryResult.Rejected(problems);
    }

    /// <summary>Checks an entry against the rules of a layer, as reading its file would.</summary>
    /// <param name="entry">The entry to check.</param>
    /// <param name="layer">The layer whose rules apply.</param>
    /// <returns>The rules the entry breaks; empty when it follows all of them.</returns>
    public static IReadOnlyList<CompatProblem> Validate(TitleEntry entry, CompatLayer layer)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CheckLayer(layer);
        var problems = new List<CompatProblem>();
        if (entry.Id is null)
        {
            problems.Add(new CompatProblem(CompatRule.MissingField, "$.id", "The field is required."));
            return problems;
        }

        TitleEntryRules.Check(entry, layer, problems);
        return problems;
    }

    /// <summary>Writes an entry as a title file.</summary>
    /// <param name="entry">The entry to write.</param>
    /// <param name="layer">The layer whose rules the entry must follow.</param>
    /// <returns>The file's UTF-8 bytes, with LF line endings and a final line break.</returns>
    /// <exception cref="ArgumentException">The entry breaks a rule of the layer.</exception>
    public static byte[] Write(TitleEntry entry, CompatLayer layer)
    {
        var problems = Validate(entry, layer);
        if (problems.Count > 0)
        {
            throw new ArgumentException($"The entry breaks a rule of the {layer} layer: {problems[0]}", nameof(entry));
        }

        var json = JsonSerializer.SerializeToUtf8Bytes(
            TitleDocumentMapper.ToDocument(entry), CompatJsonContext.Default.TitleDocument);
        return [.. json, (byte)'\n'];
    }

    private static void CheckLayer(CompatLayer layer)
    {
        if (!Enum.IsDefined(layer))
        {
            throw new ArgumentOutOfRangeException(nameof(layer), layer, "The layer is not defined.");
        }
    }

    private static TitleEntryResult Reject(CompatRule rule, string path, string message) =>
        TitleEntryResult.Rejected([new CompatProblem(rule, path, message)]);
}
