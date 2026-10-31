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

    // The serializer writes a name that holds a special character in brackets, as in $['a b'].
    // After TitleEntryJson's own checks only known names reach the serializer, so this only guards.
    public static string FromSerializer(string? path) =>
        path is null ? Root : BracketedName().Replace(path, "." + NameNotShown);

    private static bool IsPlainName(string name) =>
        name.Length is > 0 and <= 64 && !name.AsSpan().ContainsAnyExcept(s_plainNameCharacters);

    [GeneratedRegex(@"\['(?:[^'\\]|\\.)*'\]")]
    private static partial Regex BracketedName();
}
