// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

// Fails on personal data in docs/evidence/, compat/ and tests/, and on tracked files that
// C-0002 section 6 forbids anywhere in the repository (C-0011 section 2).
//
//   dotnet run tools/ScanPersonalData.cs                 scan the repository
//   dotnet run tools/ScanPersonalData.cs -- --self-test  check every rule against its fixtures
//
// A finding names the file, the line and the rule, never the matched value, because CI logs
// are as public as the repository. Exit codes: 0 clean, 1 findings, 2 usage or configuration
// error. docs/design/repository-checks.md describes every rule and its known false positives.

#:property PublishAot=false

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

try
{
    return args switch
    {
        [] => Scanner.ScanRepository(),
        ["--self-test"] => SelfTest.Run(),
        _ => Cli.Usage(),
    };
}
catch (Exception exception) when (
    exception is InvalidDataException or RegexMatchTimeoutException or IOException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"error: {exception.Message}");
    return 2;
}

internal static class Cli
{
    public static int Usage()
    {
        Console.Error.WriteLine("usage: dotnet run tools/ScanPersonalData.cs [-- --self-test]");
        return 2;
    }
}

// A finding in a file's text has its line; a finding about a file has none. InName marks a
// value in a file or folder name, whose path then ends in Report.NameNotShown.
internal sealed record Finding(string Path, int Line, string RuleId, bool InName = false);

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

internal static class Rules
{
    // The data that C-0011 section 2 lists. Labels matter more than shapes: 40 hex digits are a
    // commit hash unless a UDID label stands before them, and 64 are a SHA-256 value unless
    // they are labeled as a depot key.
    public static readonly ContentRule[] Content =
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

    public static string DescriptionOf(string ruleId) => ruleId switch
    {
        ForbiddenFiles.ExtensionRule => "file type that C-0002 section 6 forbids",
        ForbiddenFiles.BuildOutputRule => "build output, which C-0002 section 6 forbids",
        _ => Content.Single(rule => rule.Id == ruleId).Description,
    };
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

internal static class ForbiddenFiles
{
    public const string ExtensionRule = "forbidden-extension";
    public const string BuildOutputRule = "forbidden-build-output";

    // Signing material, pairing files and packaged apps: the file types that C-0002 section 6
    // and .gitignore list, and .pfx, which holds the same PKCS #12 data as .p12.
    private static readonly string[] s_extensions =
        [".p12", ".pfx", ".cer", ".mobileprovision", ".provisionprofile", ".mobiledevicepairing", ".ipa"];

    // Build output folders (C-0002 section 6).
    private static readonly string[] s_buildOutputFolders = ["bin", "obj", "DerivedData"];

    public static string? RuleFor(string path)
    {
        if (s_extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
        {
            return ExtensionRule;
        }

        var folders = path.Split('/')[..^1];
        return folders.Any(folder => s_buildOutputFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            ? BuildOutputRule
            : null;
    }
}

internal static class ContentScanner
{
    public static List<Finding> Scan(string path, string text)
    {
        var findings = new List<Finding>();
        var seen = new HashSet<(int Line, string RuleId)>();
        foreach (var (number, line) in Lines(text))
        {
            foreach (var rule in Rules.Content)
            {
                if (rule.FiresOn(line, path) && seen.Add((number, rule.Id)))
                {
                    findings.Add(new Finding(path, number, rule.Id));
                }
            }
        }

        return findings;
    }

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
    // bytes is binary, such as a screenshot: null, and it is listed instead.
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

    // A file or folder name can hold personal data too, as an attachment named after a UDID
    // does. The finding names the folder above it, followed by Report.NameNotShown.
    public static Finding? NameFinding(string path) =>
        NameMatch(path) is { } match ? new Finding(match.Shown, 0, match.RuleId, InName: true) : null;

    // The path to print for a file: the path itself, unless a name in it holds personal data.
    public static string ShownPath(string path) => NameMatch(path)?.Shown ?? path;

    private static (string Shown, string RuleId)? NameMatch(string path)
    {
        var segments = path.Split('/');
        for (var index = 0; index < segments.Length; index++)
        {
            var rule = Rules.Content.FirstOrDefault(candidate => candidate.FiresOn(segments[index], path));
            if (rule is not null)
            {
                var folder = string.Concat(segments[..index].Select(segment => $"{segment}/"));
                return (folder + Report.NameNotShown, rule.Id);
            }
        }

        return null;
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

internal sealed record AllowEntry(string Path, string RuleId, string Reason);

// eng/personal-data-allow-list.json. Each entry excuses one rule in one file and states why.
internal sealed class AllowList(List<AllowEntry> entries)
{
    public const string FilePath = "eng/personal-data-allow-list.json";

    public static AllowList Parse(string json)
    {
        var parsed = new List<AllowEntry>();
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("entries", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"{FilePath} needs an object with an \"entries\" array.");
            }

            foreach (var element in array.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"{FilePath}: every entry must be an object.");
                }

                var entry = new AllowEntry(Text(element, "path"), Text(element, "rule"), Text(element, "reason"));
                if (!Rules.Content.Any(rule => rule.Id == entry.RuleId))
                {
                    throw new InvalidDataException(
                        $"{FilePath}: \"{entry.RuleId}\" is not a content rule. Forbidden files are never allowed.");
                }

                if (parsed.Any(other => other.Path == entry.Path && other.RuleId == entry.RuleId))
                {
                    throw new InvalidDataException($"{FilePath}: {entry.Path} and {entry.RuleId} are listed twice.");
                }

                parsed.Add(entry);
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{FilePath} is not valid JSON: {exception.Message}");
        }

        return new AllowList(parsed);
    }

    // Returns the findings that no entry excuses, and the entries that excuse nothing. Such an
    // entry is stale and fails the scan, so that the list cannot rot. A finding whose file or
    // folder name is not shown is never excused: the file or folder is renamed instead.
    public (List<Finding> Kept, List<AllowEntry> Unused) Apply(IEnumerable<Finding> findings)
    {
        var used = new HashSet<AllowEntry>();
        var kept = new List<Finding>();
        foreach (var finding in findings)
        {
            var match = Report.IsNameNotShown(finding)
                ? null
                : entries.FirstOrDefault(
                    candidate => candidate.Path == finding.Path && candidate.RuleId == finding.RuleId);
            if (match is null)
            {
                kept.Add(finding);
            }
            else
            {
                used.Add(match);
            }
        }

        return (kept, entries.Where(candidate => !used.Contains(candidate)).ToList());
    }

    private static string Text(JsonElement element, string name)
    {
        var value = element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidDataException($"{FilePath}: every entry needs a non-empty \"{name}\".")
            : value;
    }
}

internal static class Report
{
    // Stands in for a file or folder name that holds personal data.
    public const string NameNotShown = "<name not shown>";

    public static bool IsNameNotShown(Finding finding) =>
        finding.Path.EndsWith(NameNotShown, StringComparison.Ordinal);

    // Neither form ever contains the matched value.
    public static string Format(Finding finding)
    {
        var rule = $"{finding.RuleId}: {Rules.DescriptionOf(finding.RuleId)}";
        return finding switch
        {
            { InName: true } => $"{finding.Path}: {rule}, in this file or folder name",
            { Line: > 0 } => $"{finding.Path}:{finding.Line}: {rule}",
            _ => $"{finding.Path}: {rule}",
        };
    }

    // A GitHub Actions annotation, which marks the file and line in the pull request. A file
    // whose name is not shown cannot be marked, so its annotation names the folder instead.
    public static string Annotation(Finding finding)
    {
        var rule = $"{Rules.DescriptionOf(finding.RuleId)} (rule {finding.RuleId})";
        if (IsNameNotShown(finding))
        {
            var where = finding switch
            {
                { InName: true } => "in a file or folder name",
                { Line: > 0 } => $"on line {finding.Line}",
                _ => "in a file",
            };
            return "::error title=Personal data and forbidden files::"
                + Escape($"{finding.Path}: {rule}, {where}. The name and the value are not shown.", false);
        }

        var line = finding.Line > 0 ? $",line={finding.Line}" : string.Empty;
        return $"::error file={Escape(finding.Path, true)}{line},title=Personal data and forbidden files::"
            + Escape($"{rule}. The value is not shown.", false);
    }

    private static string Escape(string value, bool property)
    {
        var escaped = value.Replace("%", "%25", StringComparison.Ordinal)
            .Replace("\r", "%0D", StringComparison.Ordinal)
            .Replace("\n", "%0A", StringComparison.Ordinal);
        return property
            ? escaped.Replace(":", "%3A", StringComparison.Ordinal).Replace(",", "%2C", StringComparison.Ordinal)
            : escaped;
    }
}

internal static class Scanner
{
    // The folders that C-0011 section 2 names. A folder that does not exist yet is skipped.
    public static readonly string[] ScannedFolders = ["docs/evidence/", "compat/", "tests/"];

    public static int ScanRepository()
    {
        var root = Git.Run(Directory.GetCurrentDirectory(), "rev-parse", "--show-toplevel").Trim();

        // Tracked files and new files that git does not ignore, so that a local run before a
        // commit also sees the files that are about to be added.
        var files = Git.Run(root, "ls-files", "-z", "--cached", "--others", "--exclude-standard")
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .Where(file => File.Exists(Path.Combine(root, file)))
            .Order(StringComparer.Ordinal)
            .ToList();

        var allowListPath = Path.Combine(root, AllowList.FilePath);
        var allowList = File.Exists(allowListPath)
            ? AllowList.Parse(File.ReadAllText(allowListPath))
            : new AllowList([]);

        Console.WriteLine("Personal data and forbidden files (C-0011 section 2)");
        var findings = new List<Finding>();
        foreach (var file in files)
        {
            if (ForbiddenFiles.RuleFor(file) is { } rule)
            {
                findings.Add(new Finding(ContentScanner.ShownPath(file), 0, rule));
            }
        }

        Console.WriteLine($"Forbidden files: checked the names of all {files.Count} files.");
        foreach (var folder in ScannedFolders)
        {
            findings.AddRange(ScanFolder(root, folder, files));
        }

        var (kept, unused) = allowList.Apply(findings.Distinct());
        var annotate = Environment.GetEnvironmentVariable("GITHUB_ACTIONS") == "true";
        var ordered = kept.OrderBy(finding => finding.Path, StringComparer.Ordinal).ThenBy(finding => finding.Line);
        foreach (var finding in ordered)
        {
            if (annotate)
            {
                Console.WriteLine(Report.Annotation(finding));
            }

            Console.WriteLine(Report.Format(finding));
        }

        foreach (var entry in unused)
        {
            Console.WriteLine($"{AllowList.FilePath}: the entry for {entry.Path} and {entry.RuleId} excuses nothing.");
        }

        var excused = findings.Distinct().Count() - kept.Count;
        Console.WriteLine(
            $"{kept.Count} findings, {excused} excused by {AllowList.FilePath}, {unused.Count} stale entries. "
                + "A finding names the file, the line and the rule, never the value.");
        return kept.Count == 0 && unused.Count == 0 ? 0 : 1;
    }

    // The names and the text of every file in the folder. A file below a name that holds
    // personal data is reported under the folder above that name, so that the name never shows.
    private static List<Finding> ScanFolder(string root, string folder, List<string> files)
    {
        var findings = new List<Finding>();
        var inFolder = files.Where(file => file.StartsWith(folder, StringComparison.Ordinal)).ToList();
        if (inFolder.Count == 0)
        {
            Console.WriteLine($"Personal data: {folder} does not exist yet, skipped.");
            return findings;
        }

        var binary = new List<string>();
        foreach (var file in inFolder)
        {
            if (ContentScanner.NameFinding(file) is { } name)
            {
                findings.Add(name);
            }

            var shown = ContentScanner.ShownPath(file);
            var text = ContentScanner.Decode(File.ReadAllBytes(Path.Combine(root, file)));
            if (text is null)
            {
                binary.Add(shown);
            }
            else
            {
                findings.AddRange(ContentScanner.Scan(file, text).Select(finding => finding with { Path = shown }));
            }
        }

        Console.WriteLine(
            $"Personal data: scanned the names of {inFolder.Count} files and the text of "
                + $"{inFolder.Count - binary.Count} in {folder}.");
        foreach (var file in binary)
        {
            Console.WriteLine($"  Binary, not scanned; review it by eye: {file}");
        }

        return findings;
    }
}

internal static class Git
{
    public static string Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("core.quotepath=off");
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Start(start);
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output
            : throw new InvalidDataException($"git {string.Join(' ', arguments)} failed: {error.Result.Trim()}");
    }

    private static Process Start(ProcessStartInfo start)
    {
        try
        {
            return Process.Start(start) ?? throw new InvalidDataException("git could not be started.");
        }
        catch (Win32Exception exception)
        {
            throw new InvalidDataException($"git could not be started: {exception.Message}");
        }
    }
}

internal sealed class Tally
{
    public int Total { get; private set; }

    public int Failed { get; private set; }

    public void Expect(bool passed, string description)
    {
        Total++;
        Failed += passed ? 0 : 1;
        Console.WriteLine($"  {(passed ? "ok  " : "FAIL")} {description}");
    }
}

// A fixture expects its rule to be the only one that fires, or no rule at all to fire.
internal sealed record Fixture(string Rule, bool Fires, string Name, string Text, string Path = "tests/Fixture.cs");

// Every fixture is synthetic (C-0011 section 1): addresses at example.com, IP addresses from the
// documentation ranges of RFC 5737 and RFC 3849, and made-up identifiers. Values shaped like
// credentials are built at run time, so that no secret scanner mistakes this file for a leak.
internal static class SelfTest
{
    public static int Run()
    {
        Console.WriteLine("Self-test of tools/ScanPersonalData.cs, with synthetic fixtures only.");
        var tally = new Tally();
        var fixtures = Fixtures();
        foreach (var group in fixtures.GroupBy(fixture => fixture.Rule))
        {
            Console.WriteLine($"Rule {group.Key}:");
            foreach (var fixture in group)
            {
                var fired = ContentScanner.Scan(fixture.Path, fixture.Text)
                    .Select(finding => finding.RuleId)
                    .ToHashSet();
                var passed = fixture.Fires ? fired.SetEquals([fixture.Rule]) : fired.Count == 0;
                var detail = passed
                    ? string.Empty
                    : $" (fired: {(fired.Count == 0 ? "nothing" : string.Join(", ", fired))})";
                tally.Expect(passed, $"{(fixture.Fires ? "flags" : "ignores")} {fixture.Name}{detail}");
            }
        }

        Console.WriteLine("Coverage:");
        foreach (var rule in Rules.Content)
        {
            var flagged = fixtures.Any(fixture => fixture.Rule == rule.Id && fixture.Fires);
            var ignored = fixtures.Any(fixture => fixture.Rule == rule.Id && !fixture.Fires);
            tally.Expect(flagged && ignored, $"{rule.Id} has fixtures that it flags and fixtures that it ignores");
        }

        Console.WriteLine("Every pattern is the only one of its rule to fire on some fixture:");
        foreach (var rule in Rules.Content)
        {
            var alone = fixtures
                .Where(fixture => fixture.Rule == rule.Id && fixture.Fires)
                .Select(fixture => ContentScanner.Lines(fixture.Text)
                    .SelectMany(line => rule.Matches(line.Text, fixture.Path))
                    .Select(match => match.Pattern)
                    .ToHashSet())
                .Where(patterns => patterns.Count == 1)
                .Select(patterns => patterns.Single())
                .ToHashSet();
            var missing = Enumerable.Range(0, rule.PatternCount).Where(pattern => !alone.Contains(pattern)).ToList();
            var detail = missing.Count == 0 ? string.Empty : $" (never alone: pattern {string.Join(", ", missing)})";
            tally.Expect(missing.Count == 0, $"{rule.Id}, {rule.PatternCount} patterns{detail}");
        }

        Console.WriteLine("Reports never contain the matched value:");
        foreach (var fixture in fixtures.Where(fixture => fixture.Fires))
        {
            var values = ContentScanner.Lines(fixture.Text)
                .SelectMany(line => Rules.Content.SelectMany(rule => rule.MatchesOn(line.Text, fixture.Path)))
                .ToList();
            var output = ContentScanner.Scan(fixture.Path, fixture.Text)
                .SelectMany(finding => new[] { Report.Format(finding), Report.Annotation(finding) })
                .ToList();
            var leaked = output.Any(line => values.Any(value => line.Contains(value, StringComparison.Ordinal)));
            tally.Expect(values.Count > 0 && !leaked, $"{fixture.Rule}: {fixture.Name}");
        }

        Console.WriteLine("Encodings:");
        CheckEncodings(tally);

        Console.WriteLine("File and folder names:");
        CheckNames(tally);

        Console.WriteLine("Forbidden files:");
        foreach (var (path, expected) in ForbiddenFixtures())
        {
            tally.Expect(ForbiddenFiles.RuleFor(path) == expected, $"{expected ?? "allowed"}: {path}");
        }

        Console.WriteLine("Allow list:");
        CheckAllowList(tally);

        Console.WriteLine(tally.Failed == 0
            ? $"Self-test passed: {tally.Total} of {tally.Total} checks as expected."
            : $"Self-test FAILED: {tally.Failed} of {tally.Total} checks not as expected.");
        return tally.Failed == 0 ? 0 : 1;
    }

    private static void CheckEncodings(Tally tally)
    {
        const string Text = "Contact: tester@example.com";
        List<(string Name, byte[] Bytes)> texts =
        [
            ("UTF-8 with a byte-order mark", [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(Text)]),
            (
                "UTF-16 with a byte-order mark, as Windows PowerShell 5.1 writes it",
                [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(Text)]),
            (
                "big-endian UTF-16 with a byte-order mark",
                [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(Text)]),
            ("UTF-32 with a byte-order mark", [.. Encoding.UTF32.GetPreamble(), .. Encoding.UTF32.GetBytes(Text)]),
            ("UTF-16 without a byte-order mark", Encoding.Unicode.GetBytes(Text)),
            ("UTF-16 without a mark, with an ideographic space", Encoding.Unicode.GetBytes($"{Text}　")),
        ];
        foreach (var (name, bytes) in texts)
        {
            var decoded = ContentScanner.Decode(bytes);
            var fired = decoded is null ? [] : ContentScanner.Scan("docs/evidence/log.txt", decoded);
            tally.Expect(fired.Any(finding => finding.RuleId == "email"), $"reads {name}");
        }

        byte[] image = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];
        tally.Expect(ContentScanner.Decode(image) is null, "counts a PNG image as binary");
    }

    private static void CheckNames(Tally tally)
    {
        List<(string Path, string? Rule, string? Shown, string? Value)> names =
        [
            ("tests/0000FFFF-0123456789ABCDEF.plist", "udid", "tests/", "0000FFFF-0123456789ABCDEF"),
            ("docs/evidence/2026-10-30-tester@example.com.md", "email", "docs/evidence/", "tester@example.com"),
            ("tests/fixtures/192.0.2.10/log.txt", "ip-address", "tests/fixtures/", "192.0.2.10"),
            ("tests/Paneport.Core.Tests/packages.lock.json", null, null, null),
            ("compat/steam/440.json", null, null, null),
            ("docs/evidence/2026-10-30-device-pairing.md", null, null, null),
        ];
        foreach (var (path, rule, shown, value) in names)
        {
            var finding = ContentScanner.NameFinding(path);
            if (rule is null)
            {
                tally.Expect(finding is null && ContentScanner.ShownPath(path) == path, $"passes the name {path}");
                continue;
            }

            var expected = $"{shown}{Report.NameNotShown}";
            var output = finding is null ? [] : new[] { Report.Format(finding), Report.Annotation(finding) };
            tally.Expect(
                finding == new Finding(expected, 0, rule, InName: true)
                    && ContentScanner.ShownPath(path) == expected
                    && !output.Any(line => line.Contains(value!, StringComparison.Ordinal)),
                $"flags {rule} in a name and reports {expected} without the name");
        }

        var folder = ContentScanner.ShownPath("tests/0000FFFF-0123456789ABCDEF/log.txt");
        var below = ContentScanner.Scan("tests/x/log.txt", "Contact: tester@example.com").Single() with
        {
            Path = folder,
        };
        tally.Expect(
            Report.Format(below) == $"tests/{Report.NameNotShown}:1: email: e-mail address"
                && !Report.Annotation(below).Contains("0000FFFF", StringComparison.Ordinal),
            "reports a finding below such a name under the folder above it, with its line");
    }

    private static void CheckAllowList(Tally tally)
    {
        var findings = new List<Finding>
        {
            new("tests/A.cs", 3, "email"),
            new("tests/A.cs", 4, "ip-address"),
            new("tests/B.cs", 1, "email"),
        };
        var list = AllowList.Parse(
            """
            { "entries": [
              { "path": "tests/A.cs", "rule": "email", "reason": "Synthetic: an example address." },
              { "path": "tests/C.cs", "rule": "email", "reason": "Synthetic: an entry that excuses nothing." }
            ] }
            """);
        var (kept, unused) = list.Apply(findings);
        tally.Expect(!kept.Contains(findings[0]), "an entry excuses its own rule in its own file");
        tally.Expect(
            kept.Contains(findings[1]) && kept.Contains(findings[2]), "it excuses no other rule and no other file");
        tally.Expect(unused.Count == 1 && unused[0].Path == "tests/C.cs", "an entry that excuses nothing is reported");
        tally.Expect(
            Rejects("""{ "entries": [ { "path": "tests/A.cs", "rule": "email", "reason": " " } ] }"""),
            "an entry without a reason is rejected");
        tally.Expect(
            Rejects("""{ "entries": [ { "path": "a.p12", "rule": "forbidden-extension", "reason": "Test." } ] }"""),
            "a forbidden file cannot be allowed");
        tally.Expect(
            Rejects("""{ "entries": [ { "path": "tests/A.cs", "rule": "no-such-rule", "reason": "Synthetic." } ] }"""),
            "an unknown rule is rejected");
        tally.Expect(Rejects("""{ "entries": [ 1 ] }"""), "an entry that is not an object is rejected");
        tally.Expect(Rejects("[]"), "a list that is not an object is rejected");

        var hidden = new Finding($"tests/{Report.NameNotShown}", 0, "udid", InName: true);
        var (keptHidden, _) = AllowList.Parse(
                $$"""{ "entries": [ { "path": "{{hidden.Path}}", "rule": "udid", "reason": "Synthetic." } ] }""")
            .Apply([hidden]);
        tally.Expect(keptHidden.Contains(hidden), "a finding whose name is not shown is never excused");
    }

    private static bool Rejects(string json)
    {
        try
        {
            AllowList.Parse(json);
            return false;
        }
        catch (InvalidDataException)
        {
            return true;
        }
    }

    private static List<(string Path, string? Rule)> ForbiddenFixtures() =>
    [
        ("signing/development.p12", ForbiddenFiles.ExtensionRule),
        ("signing/development.PFX", ForbiddenFiles.ExtensionRule),
        ("signing/development.cer", ForbiddenFiles.ExtensionRule),
        ("signing/Paneport.mobileprovision", ForbiddenFiles.ExtensionRule),
        ("signing/Paneport.provisionprofile", ForbiddenFiles.ExtensionRule),
        ("device/phone.mobiledevicepairing", ForbiddenFiles.ExtensionRule),
        ("artifacts/Paneport.IPA", ForbiddenFiles.ExtensionRule),
        ("src/Paneport.Core/bin/Release/Paneport.Core.dll", ForbiddenFiles.BuildOutputRule),
        ("tests/Paneport.Core.Tests/obj/project.assets.json", ForbiddenFiles.BuildOutputRule),
        ("src/Paneport.iOS/DerivedData/Build/main.o", ForbiddenFiles.BuildOutputRule),
        ("docs/design/binary-notes.md", null),
        ("docs/certificates.cer.md", null),
        ("tests/objects/README.md", null),
    ];

    private static List<Fixture> Fixtures()
    {
        var hex40 = Synthetic.Hex(40, 1);
        var hex64 = Synthetic.Hex(64, 2);
        const string LockFile = "tests/Paneport.UI.Tests/packages.lock.json";
        return
        [
            new("udid", true, "current UDID format", "Device: 0000FFFF-0123456789ABCDEF"),
            new("udid", true, "UDID joined to a name by hyphens", "Attached syslog-0000FFFF-0123456789ABCDEF.txt"),
            new("udid", true, "UDID with a hyphen after it", "Backup 0000FFFF-0123456789ABCDEF-20261030"),
            new("udid", true, "older UDID format with its label", $"UDID = {hex40}"),
            new("udid", true, "older UDID after a camel-case label", $"deviceUDID: {hex40}"),
            new("udid", false, "commit hash in an evidence record", $"Build: commit {hex40}"),
            new("udid", false, "GUID", "Project GUID 3F2504E0-4F89-11D3-9A0C-0305E82C3301"),

            new("ecid", true, "ECID in hex", "ECID: 0x0123456789ABCD"),
            new("ecid", true, "UniqueChipID in decimal", "UniqueChipID: 98765432101"),
            new("ecid", true, "ApECID, a label joined in camel case", "ApECID: 0x0123456789ABCD"),
            new("ecid", false, "prose that names ECIDs", "The record never holds an ECID or a UDID."),

            new("serial-number", true, "serial number with its label", "Serial Number: AB12CD34EF"),
            new("serial-number", true, "SerialNumber key", "\"SerialNumber\": \"X1Y2Z3W4V5U6\""),
            new("serial-number", true, "randomized serial number", "serial: X9Y8Z7W6"),
            new("serial-number", true, "ioreg's IOPlatformSerialNumber", "\"IOPlatformSerialNumber\" = \"AB12CD34EF\""),
            new("serial-number", true, "label with a note in parentheses", "Serial Number (system): AB12CD34EF"),
            new(
                "serial-number",
                true,
                "key and value on one line of a property list",
                "<key>SerialNumber</key><string>AB12CD34EF</string>"),
            new("serial-number", false, "prose about serial numbers", "Serial numbers have 8 to 14 characters."),
            new("serial-number", false, "serial number that was not recorded", "Serial number: REDACTED"),
            new("serial-number", false, "type name that contains Serial", "var serializer = new JsonSerializer();"),

            new("team-identifier", true, "team identifier with its label", "Team ID: ABCDE12345"),
            new(
                "team-identifier",
                true,
                "team identifier in a property list",
                "<key>com.apple.developer.team-identifier</key>\n\t<string>ABCDE12345</string>"),
            new(
                "team-identifier",
                true,
                "TeamIdentifier array of a provisioning profile",
                "<key>TeamIdentifier</key>\n<array>\n\t<string>ABCDE12345</string>\n</array>"),
            new("team-identifier", true, "label joined in camel case", "appleTeamId: ABCDE12345"),
            new("team-identifier", false, "team identifier that was not recorded", "Team ID: not recorded"),

            new("app-identifier-prefix", true, "application-identifier value", "ABCDE12345.com.example.paneport"),
            new("app-identifier-prefix", true, "re-signed bundle identifier", "com.example.paneport.ABCDE12345"),
            new("app-identifier-prefix", true, "AppIdentifierPrefix", "AppIdentifierPrefix: ABCDE12345"),
            new(
                "app-identifier-prefix",
                true,
                "ApplicationIdentifierPrefix array of a provisioning profile",
                "<key>ApplicationIdentifierPrefix</key>\n<array>\n<string>ABCDE12345</string>\n</array>"),
            new("app-identifier-prefix", false, "plain bundle identifier", "Bundle: com.example.paneport"),
            new("app-identifier-prefix", false, "C# namespace", "namespace Paneport.Core.Tests;"),

            new("email", true, "e-mail address", "Contact: tester@example.com"),
            new("email", false, "SSH remote", "git clone git@example.com:owner/repo.git"),
            new("email", false, "package directive with a version", "#:package Example.Package@1.2.3"),
            new("email", false, "image at twice the scale", "![screen](images/screen@2x.png)"),

            new("steam-id", true, "SteamID64", "\"owner\": 76561197960265728"),
            new("steam-id", true, "SteamID64 at the top of the individual range", "\"owner\": 76561202255233023"),
            new("steam-id", true, "Steam2 ID", "STEAM_0:0:0"),
            new("steam-id", true, "Steam3 ID", "[U:1:0]"),
            new("steam-id", true, "labeled account ID", "steam_id: 00000000"),
            new("steam-id", true, "SteamID32", "SteamID32: 12345678"),
            new("steam-id", true, "label joined in camel case", "userSteamId: 12345678"),
            new("steam-id", false, "Steam app ID", "\"steamAppId\": 440"),
            new("steam-id", false, "Steam build ID", "\"gameBuild\": 14567890"),
            new("steam-id", false, "number above the individual range", "\"total\": 76561202255233024"),

            new("ip-address", true, "IPv4 address", "Server at 192.0.2.10 answered."),
            new("ip-address", true, "IPv4 address with a port", "peer 198.51.100.7:27015"),
            new("ip-address", true, "IPv4 address in a C# string", "var host = \"203.0.113.42\";"),
            new("ip-address", true, "IPv6 address", "Server at 2001:db8::1 answered."),
            new("ip-address", true, "IPv6 address with a port", "[2001:db8:0:0:0:0:0:1]:443"),
            new("ip-address", false, "assembly version", "\"assemblyVersion\": \"10.0.14.0\","),
            new("ip-address", false, "dependency line of a lock file", "    \"HarfBuzzSharp\": \"8.3.1.3\",", LockFile),
            new("ip-address", false, "resolved version in a lock file", "    \"resolved\": \"8.3.1.3\",", LockFile),
            new("ip-address", false, "version range in a lock file", "    \"requested\": \"[8.3.1.3, )\",", LockFile),
            new("ip-address", false, "version with a v", "Released as v1.2.3.4."),
            new("ip-address", false, "version after the word version", "Paneport version 1.2.3.4 started."),
            new("ip-address", false, "build number", "OS build 10.0.200.123"),
            new("ip-address", true, "address after a word that ends in build", "Rebuild at 192.0.2.20 failed."),
            new("ip-address", false, "version parsed in code", "var version = Version.Parse(\"1.2.3.4\");"),
            new("ip-address", false, "five-part version", "1.2.3.4.5"),
            new("ip-address", false, "version with a large part", "Avalonia.Angle.Windows.Natives 2.1.27548.20260419"),
            new("ip-address", false, "time of day", "Started at 12:34:56 UTC."),
            new("ip-address", false, "colon-separated hex that is no address", "Value: 00:1A:2B:3C:4D:5E"),

            new("token", true, "JSON Web Token", $"Token: {Synthetic.Jwt()}"),
            new("token", true, "JSON Web Token with spaces, as Steam's", $"Token: {Synthetic.Jwt(spaced: true)}"),
            new("token", true, "bearer token", $"Authorization: Bearer {Synthetic.Alnum(32, 3)}"),
            new("token", true, "labeled refresh token", $"refresh_token={Synthetic.Alnum(24, 4)}"),
            new("token", true, "refresh token after a space", $"--refresh-token {Synthetic.Alnum(24, 7)}"),
            new("token", true, "STEAM_API_KEY", $"STEAM_API_KEY={Synthetic.Hex(32, 8).ToUpperInvariant()}"),
            new("token", true, "label joined in camel case", $"steamApiKey: {Synthetic.Alnum(24, 9)}"),
            new("token", true, "SteamKit2's NewGuardData", $"\"NewGuardData\": \"{Synthetic.Alnum(40, 10)}\""),
            new("token", true, "token in a URL", $"https://api.example.com/v1?access_token={Synthetic.Alnum(20, 5)}"),
            new("token", true, "key in a URL", $"https://api.example.com/v1/items?key={Synthetic.Alnum(20, 11)}"),
            new("token", false, "placeholder", "refresh_token=<your refresh token>"),
            new("token", false, "redacted value", "access_token: REDACTED"),
            new("token", false, "content hash in a lock file", $"\"contentHash\": \"{Synthetic.Alnum(86, 6)}==\""),
            new("token", false, "prose about refresh tokens", "Sign in again when the refresh token expires."),
            new("token", false, "method call after a key label", "var authKey = Convert.ToBase64String(bytes);"),

            new("depot-key", true, "labeled depot key", $"Depot key: {hex64}"),
            new("depot-key", true, "line of a depot key file", $"1000;{hex64}"),
            new("depot-key", false, "SHA-256 of a program file", $"\"exeSha256\": [\"{hex64}\"]"),

            new("manifest-request-code", true, "manifest request code", "manifest request code: 1234567890123456789"),
            new("manifest-request-code", false, "prose", "The manifest request code is fetched again after 5 minutes."),

            new("local-path", true, "Windows user profile", @"Log: C:\Users\tester\AppData\Local\Paneport"),
            new("local-path", true, "Windows profile in a C# string", "var path = \"C:\\\\Users\\\\tester\\\\x\";"),
            new("local-path", true, "Windows profile escaped twice", @"""path"": ""C:\\\\Users\\\\tester"""),
            new("local-path", true, "macOS home folder", "/Users/tester/Library/Logs"),
            new("local-path", true, "macOS home folder on the data volume", "/System/Volumes/Data/Users/tester"),
            new("local-path", true, "macOS home folder on another volume", "/Volumes/Backup/Users/tester/Logs"),
            new("local-path", true, "JSON-escaped macOS path", @"""path"": ""\/Users\/tester\/Library"""),
            new("local-path", true, "Windows profile in Git Bash", "/c/Users/tester/src/paneport"),
            new("local-path", true, "Windows profile in WSL", "/mnt/c/Users/tester/src"),
            new("local-path", true, "Windows profile in Cygwin", "/cygdrive/c/Users/tester/src"),
            new("local-path", true, "Linux home folder", "/home/tester/src/paneport"),
            new("local-path", true, "home folder under /var/home", "/var/home/tester/src"),
            new("local-path", true, "file address", "file:///Users/tester/report.txt"),
            new("local-path", true, "iOS app container", "/var/mobile/Containers/Data/Application/"),
            new("local-path", true, "JSON-escaped iOS app container", @"""path"": ""\/private\/var\/mobile\/Library"""),
            new("local-path", true, "network share", @"\\buildhost\share\logs"),
            new("local-path", true, "network share right after a label's colon", @"Source:\\nas01\backups\tester"),
            new("local-path", true, "network share with a one-letter name", @"\\buildhost\c\logs"),
            new("local-path", true, "WSL share", @"\\wsl$\Ubuntu\home\tester"),
            new("local-path", false, "web address with a home folder", "https://example.com/home/tester/page"),
            new("local-path", false, "web address with a Users folder", "https://example.com/Users/tester"),
            new("local-path", false, "public Windows profile", @"C:\Users\Public\Documents"),
            new("local-path", false, "default Windows profile", @"C:\Users\Default\AppData"),
            new("local-path", false, "placeholder for the user name", @"C:\Users\%USERNAME%\AppData"),
            new("local-path", false, "public Windows profile in Git Bash", "/c/Users/Public/Documents"),
            new("local-path", false, "shared macOS folder", "/Users/Shared/Paneport"),
            new("local-path", false, "shared macOS folder in a code span", "Logs go to `/Users/Shared`."),
            new("local-path", false, "shared macOS folder before a bracket", "[shared logs: /Users/Shared]"),
            new("local-path", false, "public Windows profile in a code span", "`/c/Users/Public`"),
            new("local-path", false, "drive path in a C# string", "var system = \"C:\\\\Windows\\\\System32\";"),
            new("local-path", false, "regular expression in a C# string", "new Regex(\"\\\\bPaneport\\\\b\")"),
            new("local-path", false, "relative path", "tests/Paneport.Core.Tests/README.md"),
            new("local-path", false, "device model identifier", "\"device\": \"iPhone18,1\""),
        ];
    }
}

internal static class Synthetic
{
    private const string Letters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    private const string HexDigits = "0123456789abcdef";

    public static string Alnum(int length, int seed) => Pick(Letters, length, seed);

    public static string Hex(int length, int seed) => Pick(HexDigits, length, seed);

    // An unsigned token with "alg":"none", which no service accepts. With spaces in its JSON,
    // as Steam writes its tokens, both parts start with eyAi instead of eyJ.
    public static string Jwt(bool spaced = false)
    {
        var (header, payload) = spaced
            ? ("{ \"alg\": \"none\", \"typ\": \"JWT\" }", "{ \"sub\": \"paneport-self-test\" }")
            : ("{\"alg\":\"none\",\"typ\":\"JWT\"}", "{\"sub\":\"paneport-self-test\"}");
        return $"{Base64Url(header)}.{Base64Url(payload)}.";
    }

    private static string Pick(string alphabet, int length, int seed)
    {
        var builder = new StringBuilder(length);
        var state = (uint)seed;
        for (var index = 0; index < length; index++)
        {
            state = (state * 1664525) + 1013904223;
            builder.Append(alphabet[(int)(state >> 24) % alphabet.Length]);
        }

        return builder.ToString();
    }

    private static string Base64Url(string text) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
