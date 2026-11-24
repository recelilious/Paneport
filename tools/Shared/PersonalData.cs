// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// The detectors of personal data that tools/ScanPersonalData.cs and tools/ScanPackage.cs share:
// the content rules of C-0011 section 2, their patterns, the excuses that keep their known false
// positives out, and the helpers that read a file as text and give its lines. Both tools include
// this file with #:include; a shared file carries no #: directive of its own
// (docs/design/repository-checks.md section 2).

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;

// One content rule. It matches a line when one of its patterns matches there and its excuse,
// if it has one, does not apply to that match. A rule may first normalize the line.
internal sealed class ContentRule(
    string id,
    string description,
    Regex[] patterns,
    Func<Match, string, string, bool>? excuse = null,
    Func<string, string>? normalize = null)
{
    public string Id { get; } = id;

    public string Description { get; } = description;

    public int PatternCount => patterns.Length;

    // Each match that no excuse applies to, with the index of the pattern that found it.
    public IEnumerable<(int Pattern, string Value)> Matches(string line, string path)
    {
        var text = normalize is null ? line : normalize(line);
        for (var index = 0; index < patterns.Length; index++)
        {
            for (var match = patterns[index].Match(text); match.Success; match = match.NextMatch())
            {
                if (excuse is null || !excuse(match, text, path))
                {
                    yield return (index, match.Value);
                }
            }
        }
    }

    public IEnumerable<string> MatchesOn(string line, string path) => Matches(line, path).Select(match => match.Value);

    public bool FiresOn(string line, string path) => Matches(line, path).Any();
}

// The content rules, and the helpers that read a file as text and give its lines.
internal static class PersonalData
{
    // The data that C-0011 section 2 lists. Labels matter more than shapes: 40 hex digits are a
    // commit hash unless a UDID label stands before them, and 64 are a SHA-256 value unless
    // they are labeled as a depot key.
    public static readonly ContentRule[] Rules =
    [
        new("udid", "UDID", [Patterns.Udid(), Patterns.LabeledLegacyUdid()]),
        new("ecid", "ECID", [Patterns.Ecid()]),
        new("serial-number", "serial number", [Patterns.SerialNumber()]),
        new("team-identifier", "team identifier", [Patterns.TeamIdentifier()]),
        new(
            "app-identifier-prefix",
            "application-identifier prefix or re-signed bundle identifier",
            [Patterns.TeamPrefixedBundleId(), Patterns.TeamSuffixedBundleId(), Patterns.LabeledAppIdPrefix()]),
        new("email", "e-mail address", [Patterns.Email()], Excuses.Email),
        new(
            "steam-id",
            "Steam ID",
            [Patterns.SteamId64(), Patterns.SteamId2(), Patterns.SteamId3(), Patterns.LabeledSteamId()],
            Excuses.SteamId),
        new("ip-address", "IP address", [Patterns.IPv4(), Patterns.IPv6Candidate()], Excuses.IpAddress),
        new(
            "token",
            "access token, refresh token or API key",
            [Patterns.Jwt(), Patterns.Bearer(), Patterns.LabeledToken(), Patterns.TokenInUrl()]),
        new("depot-key", "depot key", [Patterns.LabeledDepotKey(), Patterns.DepotKeyFileLine()]),
        new("manifest-request-code", "manifest request code", [Patterns.ManifestRequestCode()]),
        new(
            "local-path",
            "local path",
            [
                Patterns.WindowsProfilePath(),
                Patterns.UsersPath(),
                Patterns.LinuxHomePath(),
                Patterns.AppleDevicePath(),
                Patterns.UncPath(),
            ],
            Excuses.LocalPath,
            // JSON writers may escape every slash, as in "\/Users\/name".
            line => line.Replace(@"\/", "/", StringComparison.Ordinal)),
    ];

    // Every line with its number. A property list puts a value on the line after its key, or
    // in an array below it, so each such value also comes as one "key: value" line under the
    // value's number, which lets the labeled rules see both. So does a key and its value on
    // one line.
    public static IEnumerable<(int Number, string Text)> Lines(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        string? key = null;
        var inArray = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            yield return (index + 1, line);
            foreach (Match pair in Patterns.PlistPairOnOneLine().Matches(line))
            {
                yield return (index + 1, $"{pair.Groups[1].Value}: {pair.Groups[3].Value}");
            }

            if (Patterns.PlistKey().Match(line) is { Success: true } keyLine)
            {
                (key, inArray) = (keyLine.Groups[1].Value, false);
            }
            else if (key is not null && !inArray && Patterns.PlistArrayStart().IsMatch(line))
            {
                inArray = true;
            }
            else if (key is not null && Patterns.PlistValue().Match(line) is { Success: true } value)
            {
                yield return (index + 1, $"{key}: {value.Groups[2].Value}");
                key = inArray ? key : null;
            }
            else
            {
                (key, inArray) = (null, false);
            }
        }
    }

    // Text in UTF-8, or in UTF-16 or UTF-32 with a byte-order mark, as Windows PowerShell 5.1
    // writes it, or in UTF-16 without one. Any other file with a NUL byte in its first 8,000
    // bytes is binary, such as a screenshot: null, and the repository scan lists it instead.
    public static string? Decode(byte[] bytes)
    {
        var sample = bytes.AsSpan(0, Math.Min(bytes.Length, 8000));
        Encoding? encoding = bytes switch
        {
            [0xFF, 0xFE, 0x00, 0x00, ..] => new UTF32Encoding(bigEndian: false, byteOrderMark: true),
            [0x00, 0x00, 0xFE, 0xFF, ..] => new UTF32Encoding(bigEndian: true, byteOrderMark: true),
            [0xFF, 0xFE, ..] => new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
            [0xFE, 0xFF, ..] => new UnicodeEncoding(bigEndian: true, byteOrderMark: true),
            _ when sample.Contains((byte)0) => Utf16WithoutMark(sample),
            _ => new UTF8Encoding(false),
        };
        return encoding?.GetString(bytes).TrimStart('\uFEFF');
    }

    // UTF-16 without a byte-order mark: one byte of at least half of the byte pairs is NUL, as
    // in mostly ASCII text, and the other byte at most an eighth as often, as for U+3000.
    private static UnicodeEncoding? Utf16WithoutMark(ReadOnlySpan<byte> sample)
    {
        var pairs = sample.Length / 2;
        var (nulFirst, nulSecond) = (0, 0);
        for (var pair = 0; pair < pairs; pair++)
        {
            nulFirst += sample[2 * pair] == 0 ? 1 : 0;
            nulSecond += sample[(2 * pair) + 1] == 0 ? 1 : 0;
        }

        return (nulFirst, nulSecond) switch
        {
            _ when pairs == 0 => null,
            var (first, second) when second * 2 >= pairs && first * 8 <= second =>
                new UnicodeEncoding(bigEndian: false, byteOrderMark: false),
            var (first, second) when first * 2 >= pairs && second * 8 <= first =>
                new UnicodeEncoding(bigEndian: true, byteOrderMark: false),
            _ => null,
        };
    }
}

internal static partial class Patterns
{
    private const RegexOptions Plain = RegexOptions.CultureInvariant;
    private const RegexOptions Caseless = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
    private const int Timeout = 2000;

    // A label starts a word, follows an underscore or a digit, or starts with a capital after a
    // lowercase letter or a digit, so that STEAM_API_KEY, steamApiKey and IOPlatformSerialNumber
    // hold labels too.
    private const string LabelStart = @"(?:(?<![A-Za-z])|(?-i:(?<=[a-z0-9])(?=[A-Z])))";

    // Between a label and its value: a short note in parentheses, as in "Serial Number (system):",
    // and up to five characters that are neither letters nor digits.
    private const string LabelGap = @"(?:\s*\([^()\r\n]{1,20}\))?[^0-9a-z\r\n]{0,5}";

    // 10 capital letters and digits, with at least one of each, as a team identifier has.
    private const string TeamIdValue = @"(?-i:(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{10})(?![A-Za-z0-9])";

    // After a name that identifies nobody, anything that cannot go on with the name, such as a
    // slash, a backtick or a bracket around a Markdown code span or link, or the end.
    private const string NameEnd = @"(?![A-Za-z0-9_-])";

    // Devices from 2018 on: an 8-digit chip ID, a hyphen and the 16-digit ECID. A hyphen may
    // join it to other text, as in syslog-<UDID>.txt; a GUID's grouping never matches.
    [GeneratedRegex(@"(?<![0-9A-Fa-f])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{16}(?![0-9A-Fa-f])", Plain, Timeout)]
    public static partial Regex Udid();

    // Older devices: 40 hex digits, the shape of a commit hash, so only with a label.
    [GeneratedRegex(
        LabelStart + @"(?:udid|unique[\s_-]*device[\s_-]*id(?:entifier)?)\b" + LabelGap + @"[0-9a-f]{40}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledLegacyUdid();

    [GeneratedRegex(
        LabelStart + @"(?:ecid|unique[\s_-]*chip[\s_-]*id)\b" + LabelGap + @"(?:0x)?[0-9a-f]{5,20}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex Ecid();

    // Older serial numbers have 11 or 12 characters; randomized ones, since 2021, 8 to 14.
    [GeneratedRegex(
        LabelStart + @"serial(?:[\s_-]*(?:number|no\.?))?\b" + LabelGap
            + @"(?-i:(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{8,14})(?![A-Za-z0-9])",
        Caseless,
        Timeout)]
    public static partial Regex SerialNumber();

    [GeneratedRegex(
        LabelStart + @"(?:team[\s_-]*id(?:entifier)?|development_team|com\.apple\.developer\.team-identifier)\b"
            + LabelGap + TeamIdValue,
        Caseless,
        Timeout)]
    public static partial Regex TeamIdentifier();

    // A team identifier in front of a bundle identifier, as in application-identifier values.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9.])(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{10}\.[a-z][a-z0-9-]*(?:\.[A-Za-z0-9-]+)+",
        Plain,
        Timeout)]
    public static partial Regex TeamPrefixedBundleId();

    // A team identifier appended to a bundle identifier, as sideloading tools re-sign apps.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9.])[a-z][a-z0-9-]*(?:\.[A-Za-z0-9-]+)+\.(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{10}"
            + @"(?![A-Za-z0-9.])",
        Plain,
        Timeout)]
    public static partial Regex TeamSuffixedBundleId();

    [GeneratedRegex(
        LabelStart + @"(?:application-identifier|app(?:lication)?[\s_-]*id(?:entifier)?[\s_-]*prefix)\b"
            + LabelGap + TeamIdValue,
        Caseless,
        Timeout)]
    public static partial Regex LabeledAppIdPrefix();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?"
            + @"(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,63}(?![A-Za-z0-9-])",
        Plain,
        Timeout)]
    public static partial Regex Email();

    // Individual accounts: 76561197960265728 plus the 32-bit account number, up to
    // 76561202255233023. Excuses.SteamId keeps only values in that range.
    [GeneratedRegex(@"(?<![0-9])7656(?:119|120)[0-9]{10}(?![0-9])", Plain, Timeout)]
    public static partial Regex SteamId64();

    [GeneratedRegex(@"\bSTEAM_[0-5]:[01]:[0-9]{1,10}\b", Plain, Timeout)]
    public static partial Regex SteamId2();

    [GeneratedRegex(@"\[U:1:[0-9]{1,10}\]", Plain, Timeout)]
    public static partial Regex SteamId3();

    [GeneratedRegex(
        LabelStart + @"steam[\s_-]*(?:id(?:64|32|s)?|account[\s_-]*id)\b" + LabelGap + @"[0-9]{5,20}(?![0-9])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledSteamId();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_.])(?:(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])\.){3}"
            + @"(?:25[0-5]|2[0-4][0-9]|1[0-9]{2}|[1-9]?[0-9])(?![A-Za-z0-9_]|\.[0-9])",
        Plain,
        Timeout)]
    public static partial Regex IPv4();

    // A run of hex groups and colons. Excuses.IpAddress keeps only valid IPv6 addresses.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_:.])(?=[0-9A-Fa-f:]*[0-9A-Fa-f])[0-9A-Fa-f]{0,4}(?::[0-9A-Fa-f]{0,4}){2,7}"
            + @"(?![A-Za-z0-9_:])",
        Plain,
        Timeout)]
    public static partial Regex IPv6Candidate();

    // Both first parts are base64url JSON that starts with a brace: eyJ for {", and eyA or ew
    // when a space or a line break follows the brace, as in Steam's tokens.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_-])e[wy][A-Za-z0-9_-]{8,}\.e[wy][A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*", Plain, Timeout)]
    public static partial Regex Jwt();

    [GeneratedRegex(
        @"\bbearer\s+(?=[A-Za-z0-9._~+/-]*[0-9])(?=[A-Za-z0-9._~+/-]*[A-Za-z])[A-Za-z0-9._~+/-]{16,}",
        Caseless,
        Timeout)]
    public static partial Regex Bearer();

    // The value follows a colon, an equals sign or a space. A method call after the label,
    // as in authKey = Convert.ToBase64String(...), is code, not a value.
    [GeneratedRegex(
        LabelStart + @"(?:access|refresh|auth|session|id|api|client|bearer|guard)[\s_-]?(?:token|secret|key|data)s?\b"
            + @"[""']?(?:\s*[:=]|\s)\s*[""']?(?=[A-Za-z0-9._~+/=-]*[0-9])(?=[A-Za-z0-9._~+/=-]*[A-Za-z])"
            + @"(?>[A-Za-z0-9._~+/=-]{16,})(?!\()",
        Caseless,
        Timeout)]
    public static partial Regex LabeledToken();

    [GeneratedRegex(
        @"[?&](?:access_token|refresh_token|token|key|api_key|apikey)="
            + @"(?=[A-Za-z0-9._~%+/-]*[0-9])(?=[A-Za-z0-9._~%+/-]*[A-Za-z])[A-Za-z0-9._~%+/-]{16,}",
        Caseless,
        Timeout)]
    public static partial Regex TokenInUrl();

    [GeneratedRegex(
        LabelStart + @"(?:depot[\s_-]*(?:decryption[\s_-]*)?key|decryption[\s_-]*key)s?\b" + LabelGap
            + @"(?:0x)?[0-9a-f]{64}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledDepotKey();

    // The "depot ID;key" lines of a depot key file.
    [GeneratedRegex(@"^\s*[0-9]{1,10}\s*;\s*[0-9A-Fa-f]{64}\s*$", Plain, Timeout)]
    public static partial Regex DepotKeyFileLine();

    [GeneratedRegex(
        LabelStart + @"(?:manifest[\s_-]*)?request[\s_-]*code\b" + LabelGap + @"[0-9]{5,20}(?![0-9])",
        Caseless,
        Timeout)]
    public static partial Regex ManifestRequestCode();

    // A Windows user profile, with single, doubled or more backslashes, or with forward slashes.
    // Public and Default identify nobody, and neither does a placeholder such as %USERNAME%.
    [GeneratedRegex(
        @"(?<![a-z0-9])[a-z]:(?:\\+|/)(?:users|documents and settings)(?:\\+|/)"
            + @"(?!(?:public|default|default user|all users)" + NameEnd + @")(?![%$<{])[^\\/\s""'<>|:*?]+",
        Caseless,
        Timeout)]
    public static partial Regex WindowsProfilePath();

    // A Users folder: on macOS, also on another volume or the data volume, and a Windows profile
    // as Git Bash (/c/Users), WSL (/mnt/c/Users) and Cygwin (/cygdrive/c/Users) show it.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_.~-])"
            + @"(?:/(?:[A-Za-z]|mnt/[A-Za-z]|cygdrive/[A-Za-z]|System/Volumes/Data|Volumes/[^/""'<>\r\n]{1,64}))?"
            + @"/Users/(?!(?:Shared|Public|Default|All Users|Default User)" + NameEnd + @")(?![%$<{])[^/\s""'<>]+",
        Plain,
        Timeout)]
    public static partial Regex UsersPath();

    // A home folder on Linux, also under /var/home, as on Fedora's atomic desktops.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.~-])(?:/var)?/home/(?![%$<{])[^/\s""'<>]+", Plain, Timeout)]
    public static partial Regex LinuxHomePath();

    // App containers and per-user temporary folders on iOS and macOS.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.~-])(?:/private)?/var/(?:mobile|folders)/", Plain, Timeout)]
    public static partial Regex AppleDevicePath();

    // \\host\share, also \\wsl$\distribution, and \\\\host\\share as in a C# or JSON string, also
    // right after a label's colon. A drive path in such a string ("C:\\Windows") is no share, and
    // neither is "\\bword\\b", whose separators are not escaped like its start.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_\\])(?<!(?<![A-Za-z0-9])[A-Za-z]:)"
            + @"(?:\\{2}[A-Za-z][A-Za-z0-9._$-]*\\(?!\\)|\\{4}[A-Za-z][A-Za-z0-9._$-]*\\{2})[A-Za-z0-9$._-]+",
        Plain,
        Timeout)]
    public static partial Regex UncPath();

    // The end of the text before a version: "version": ", Version=, version , build ,
    // "resolved": "[, Version.Parse(".
    [GeneratedRegex(
        @"(?:\b(?:resolved|requested|build)|version\w*)[""']?\s*[:=(,]?\s*[""']?[\[(]?\s*$"
            + @"|version[\w.]*\s*\(\s*[""']$",
        Caseless,
        Timeout)]
    public static partial Regex VersionLabelBefore();

    // A dependency line of a NuGet lock file maps a package ID to a version.
    [GeneratedRegex(@"^\s*""[A-Za-z0-9_.-]+""\s*:\s*""[\[(]?$", Plain, Timeout)]
    public static partial Regex LockFileDependencyBefore();

    [GeneratedRegex(@"\b[a-z][a-z0-9+.-]*://[^\s""'<>)\]]+", Caseless, Timeout)]
    public static partial Regex Url();

    [GeneratedRegex(@"^\s*<key>([^<]+)</key>\s*$", Plain, Timeout)]
    public static partial Regex PlistKey();

    // A value on a line of its own, also as an array of one value.
    [GeneratedRegex(@"^\s*(?:<array>\s*)?<(string|integer)>([^<]*)</\1>\s*(?:</array>\s*)?$", Plain, Timeout)]
    public static partial Regex PlistValue();

    [GeneratedRegex(@"^\s*<array>\s*$", Plain, Timeout)]
    public static partial Regex PlistArrayStart();

    // A key and its value on one line, the value also as the first value of an array.
    [GeneratedRegex(@"<key>([^<]+)</key>\s*(?:<array>\s*)?<(string|integer)>([^<]*)</\2>", Plain, Timeout)]
    public static partial Regex PlistPairOnOneLine();

    // Retina image names such as icon@2x.png.
    [GeneratedRegex(@"^[0-9]+x\.[A-Za-z]+$", Plain, Timeout)]
    public static partial Regex ImageScaleSuffix();
}

// Matches that look like personal data but are not, judged from their context.
internal static class Excuses
{
    private const ulong FirstIndividualSteamId = 76561197960265728;
    private const ulong LastIndividualSteamId = 76561202255233023;

    // git@host:owner/repo is an SSH remote, and icon@2x.png is an image name.
    public static bool Email(Match match, string line, string path)
    {
        var at = match.Value.IndexOf('@', StringComparison.Ordinal);
        var end = match.Index + match.Length;
        var sshRemote = match.Value[..at] == "git" && end < line.Length && line[end] == ':';
        return sshRemote || Patterns.ImageScaleSuffix().IsMatch(match.Value[(at + 1)..]);
    }

    // A 17-digit number outside the range of individual accounts is no SteamID64.
    public static bool SteamId(Match match, string line, string path) =>
        match.Value.Length == 17
            && ulong.TryParse(match.Value, out var id)
            && id is < FirstIndividualSteamId or > LastIndividualSteamId;

    // An IPv6 candidate must parse as IPv6. Four-part version numbers have the shape of IPv4
    // addresses, so a version label, or a dependency line of a lock file, excuses them.
    public static bool IpAddress(Match match, string line, string path)
    {
        if (match.Value.Contains(':', StringComparison.Ordinal))
        {
            return !IPAddress.TryParse(match.Value, out var address)
                || address.AddressFamily != AddressFamily.InterNetworkV6;
        }

        var before = line[..match.Index];
        return Patterns.VersionLabelBefore().IsMatch(before)
            || (Path.GetFileName(path) == "packages.lock.json" && Patterns.LockFileDependencyBefore().IsMatch(before));
    }

    // A path inside a web address belongs to the address; one inside a file: address is local.
    public static bool LocalPath(Match match, string line, string path)
    {
        for (var url = Patterns.Url().Match(line); url.Success; url = url.NextMatch())
        {
            var inside = match.Index >= url.Index && match.Index < url.Index + url.Length;
            if (inside && !url.Value.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
