// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Paneport.Core.Compat;

// Builds the JSON paths that problems name, such as $.reports[0].date. A field name that is not a
// plain name, such as an unknown key that might hold a local path, shows as <name not shown>, as the
// personal-data scanner prints such names (docs/design/repository-checks.md section 3).
internal static partial class CompatPath
{
    public const string Root = "$";

    private const string NameNotShown = "<name not shown>";

    private static readonly SearchValues<char> s_plainNameCharacters =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789_-");

    public static string Property(string parent, string name) =>
        $"{parent}.{(IsPlainName(name) ? name : NameNotShown)}";

    public static string Index(string parent, int index) =>
        string.Create(CultureInfo.InvariantCulture, $"{parent}[{index}]");

    // The serializer writes a name that holds a special character in brackets, as in $['a b'], and any
    // other name after a dot, whatever it holds. The path shows its plain names and indices up to the
    // first other segment, and <name not shown> in place of the rest, because where a bracketed name
    // ends cannot be told for sure. After TitleEntryJson's own checks only known names reach the
    // serializer, so this only guards.
    public static string FromSerializer(string? path)
    {
        if (path is null)
        {
            return Root;
        }

        var plain = PlainSerializerPath().Match(path);
        if (!plain.Success)
        {
            return Root;
        }

        return plain.Length == path.Length ? path : $"{plain.Value}.{NameNotShown}";
    }

    private static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 64 && !name.AsSpan().ContainsAnyExcept(s_plainNameCharacters);

    // The root, then plain names as IsPlainName defines them, each ending where the next segment starts
    // or the path ends, and indices.
    [GeneratedRegex(@"\A\$(?:\.[A-Za-z0-9_-]{1,64}(?=[.\[]|\z)|\[[0-9]+\])*")]
    private static partial Regex PlainSerializerPath();
}
