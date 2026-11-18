// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The register of C-0003 section 6, as tools/CheckLicenses.cs and tools/GenerateNotices.cs read
// it. Both include this file with #:include; a shared file carries no #: directive of its own
// (docs/design/repository-checks.md section 2).

using System.Text.RegularExpressions;

internal sealed record RegisterRow(string Component, string License, string Category, int Line);

// The register table of C-0003 section 6. If the table changes shape, parsing fails with a
// message that says what changed, rather than guessing.
internal static partial class Register
{
    public const string FilePath = "docs/contracts/C-0003-licensing.md";
    public const string Heading = "## 6. Component register";

    public static readonly string[] Categories = ["Allowed", "Conditional", "Forbidden", "Under review", "Tool"];

    private static readonly string[] s_columns = ["Component", "Purpose", "License", "Category"];

    public static List<RegisterRow> Parse(string markdown)
    {
        var lines = markdown.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var heading = Array.FindIndex(lines, line => line.Trim() == Heading);
        if (heading < 0)
        {
            throw new InvalidDataException($"{FilePath} has no heading \"{Heading}\".");
        }

        var header = heading + 1;
        while (header < lines.Length
            && !IsTableLine(lines[header])
            && !lines[header].StartsWith("## ", StringComparison.Ordinal))
        {
            header++;
        }

        if (header >= lines.Length || !IsTableLine(lines[header]))
        {
            throw new InvalidDataException($"{FilePath}: section 6 holds no table.");
        }

        var columns = Cells(lines[header]);
        if (!columns.SequenceEqual(s_columns, StringComparer.Ordinal))
        {
            throw new InvalidDataException(
                $"{FilePath} line {header + 1}: the register's columns are \"{string.Join(" | ", columns)}\", "
                    + $"not \"{string.Join(" | ", s_columns)}\". Update tools/CheckLicenses.cs to the new shape.");
        }

        var rows = new List<RegisterRow>();
        for (var index = header + 2; index < lines.Length && IsTableLine(lines[index]); index++)
        {
            var cells = Cells(lines[index]);
            if (cells.Length != s_columns.Length)
            {
                throw new InvalidDataException(
                    $"{FilePath} line {index + 1}: a register row has {cells.Length} cells, not {s_columns.Length}.");
            }

            var category = CategoryOf(cells[3]) ?? throw new InvalidDataException(
                $"{FilePath} line {index + 1}: the category \"{cells[3]}\" starts with none of "
                    + $"{string.Join(", ", Categories)}.");
            if (rows.Any(row => row.Component == cells[0]))
            {
                throw new InvalidDataException($"{FilePath} line {index + 1}: \"{cells[0]}\" is registered twice.");
            }

            rows.Add(new RegisterRow(cells[0], cells[2], category, index + 1));
        }

        return rows.Count > 0
            ? rows
            : throw new InvalidDataException($"{FilePath}: the register table has no rows.");
    }

    // A category cell starts with its category, and may go on after a comma, semicolon or colon,
    // as in "Allowed, under FTL" or "Under review: collect the notices".
    public static string? CategoryOf(string cell) => Categories.FirstOrDefault(
        category => cell == category
            || (cell.StartsWith(category, StringComparison.Ordinal) && ",;:".Contains(cell[category.Length])));

    private static bool IsTableLine(string line) => line.TrimStart().StartsWith('|');

    private static string[] Cells(string line)
    {
        var trimmed = line.Trim();
        trimmed = trimmed.StartsWith('|') ? trimmed[1..] : trimmed;
        trimmed = trimmed.EndsWith('|') ? trimmed[..^1] : trimmed;
        return CellSeparator()
            .Split(trimmed)
            .Select(cell => cell.Trim().Replace(@"\|", "|", StringComparison.Ordinal))
            .ToArray();
    }

    [GeneratedRegex(@"(?<!\\)\|")]
    private static partial Regex CellSeparator();
}
