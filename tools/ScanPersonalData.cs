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
catch (Exception exception) when (exception is InvalidDataException or RegexMatchTimeoutException)
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

internal sealed record Finding(string Path, int Line, string RuleId);

// One content rule. It matches a line when one of its patterns matches there and its excuse,
// if it has one, does not apply to that match.
internal sealed class ContentRule(
    string id, string description, Regex[] patterns, Func<Match, string, string, bool>? excuse = null)
{
    public string Id { get; } = id;

    public string Description { get; } = description;

    public IEnumerable<string> MatchesOn(string line, string path)
    {
        foreach (var pattern in patterns)
        {
            for (var match = pattern.Match(line); match.Success; match = match.NextMatch())
            {
                if (excuse is null || !excuse(match, line, path))
                {
                    yield return match.Value;
                }
            }
        }
    }

    public bool FiresOn(string line, string path) => MatchesOn(line, path).Any();
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
            [Patterns.SteamId64(), Patterns.SteamId2(), Patterns.SteamId3(), Patterns.LabeledSteamId()]),
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
                Patterns.MacUserPath(),
                Patterns.LinuxHomePath(),
                Patterns.AppleDevicePath(),
                Patterns.UncPath(),
            ],
            Excuses.LocalPath),
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

    // Devices from 2018 on: an 8-digit chip ID, a hyphen and the 16-digit ECID.
    [GeneratedRegex(@"(?<![0-9A-Fa-f-])[0-9A-Fa-f]{8}-[0-9A-Fa-f]{16}(?![0-9A-Fa-f-])", Plain, Timeout)]
    public static partial Regex Udid();

    // Older devices: 40 hex digits, the shape of a commit hash, so only with a label.
    [GeneratedRegex(
        @"\b(?:udid|unique[\s_-]*device[\s_-]*id(?:entifier)?)\b[^0-9a-z\r\n]{0,5}[0-9a-f]{40}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledLegacyUdid();

    [GeneratedRegex(
        @"\b(?:ecid|unique[\s_-]*chip[\s_-]*id)\b[^0-9a-z\r\n]{0,5}(?:0x)?[0-9a-f]{5,20}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex Ecid();

    // Older serial numbers have 11 or 12 characters; randomized ones, since 2021, 8 to 14.
    [GeneratedRegex(
        @"\bserial(?:[\s_-]*(?:number|no\.?))?\b[^0-9a-z\r\n]{0,5}"
            + @"(?-i:(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{8,14})(?![A-Za-z0-9])",
        Caseless,
        Timeout)]
    public static partial Regex SerialNumber();

    [GeneratedRegex(
        @"\b(?:team[\s_-]*id(?:entifier)?|development_team|com\.apple\.developer\.team-identifier)\b"
            + @"[^0-9a-z\r\n]{0,5}(?-i:(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{10})(?![A-Za-z0-9])",
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
        @"\b(?:application-identifier|app[\s_-]*id(?:entifier)?[\s_-]*prefix)\b[^0-9a-z\r\n]{0,5}"
            + @"(?-i:(?=[A-Z]*[0-9])(?=[0-9]*[A-Z])[A-Z0-9]{10})(?![A-Za-z0-9])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledAppIdPrefix();

    [GeneratedRegex(
        @"(?<![A-Za-z0-9._%+-])[A-Za-z0-9._%+-]+@[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?"
            + @"(?:\.[A-Za-z0-9](?:[A-Za-z0-9-]*[A-Za-z0-9])?)*\.[A-Za-z]{2,63}(?![A-Za-z0-9-])",
        Plain,
        Timeout)]
    public static partial Regex Email();

    // Individual accounts: 76561197960265728 plus the 32-bit account number.
    [GeneratedRegex(@"(?<![0-9])7656119[0-9]{10}(?![0-9])", Plain, Timeout)]
    public static partial Regex SteamId64();

    [GeneratedRegex(@"\bSTEAM_[0-5]:[01]:[0-9]{1,10}\b", Plain, Timeout)]
    public static partial Regex SteamId2();

    [GeneratedRegex(@"\[U:1:[0-9]{1,10}\]", Plain, Timeout)]
    public static partial Regex SteamId3();

    [GeneratedRegex(
        @"\bsteam[\s_-]*(?:id(?:64)?|account[\s_-]*id)\b[^0-9a-z\r\n]{0,5}[0-9]{5,20}(?![0-9])",
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

    [GeneratedRegex(
        @"(?<![A-Za-z0-9_-])eyJ[A-Za-z0-9_-]{8,}\.eyJ[A-Za-z0-9_-]{8,}\.[A-Za-z0-9_-]*", Plain, Timeout)]
    public static partial Regex Jwt();

    [GeneratedRegex(
        @"\bbearer\s+(?=[A-Za-z0-9._~+/-]*[0-9])(?=[A-Za-z0-9._~+/-]*[A-Za-z])[A-Za-z0-9._~+/-]{16,}",
        Caseless,
        Timeout)]
    public static partial Regex Bearer();

    [GeneratedRegex(
        @"\b(?:access|refresh|auth|session|id|api|client|bearer|guard)[\s_-]?(?:token|secret|key|data)s?\b"
            + @"[""']?\s*[:=]\s*[""']?(?=[A-Za-z0-9._~+/=-]*[0-9])(?=[A-Za-z0-9._~+/=-]*[A-Za-z])"
            + @"[A-Za-z0-9._~+/=-]{16,}",
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
        @"\b(?:depot[\s_-]*(?:decryption[\s_-]*)?key|decryption[\s_-]*key)s?\b[^0-9a-z\r\n]{0,5}"
            + @"(?:0x)?[0-9a-f]{64}(?![0-9a-z])",
        Caseless,
        Timeout)]
    public static partial Regex LabeledDepotKey();

    // The "depot ID;key" lines of a depot key file.
    [GeneratedRegex(@"^\s*[0-9]{1,10}\s*;\s*[0-9A-Fa-f]{64}\s*$", Plain, Timeout)]
    public static partial Regex DepotKeyFileLine();

    [GeneratedRegex(
        @"\b(?:manifest[\s_-]*)?request[\s_-]*code\b[^0-9a-z\r\n]{0,5}[0-9]{5,20}(?![0-9])",
        Caseless,
        Timeout)]
    public static partial Regex ManifestRequestCode();

    // A Windows user profile. Public and Default identify nobody.
    [GeneratedRegex(
        @"(?<![a-z0-9])[a-z]:(?:\\{1,2}|/)(?:users|documents and settings)(?:\\{1,2}|/)"
            + @"(?!(?:public|default|default user|all users)(?:\\|/|""|'|\s|$))[^\\/\s""'<>|:*?]+",
        Caseless,
        Timeout)]
    public static partial Regex WindowsProfilePath();

    [GeneratedRegex(@"(?<![A-Za-z0-9_.~-])/Users/(?!Shared(?:/|\b))[^/\s""'<>]+", Plain, Timeout)]
    public static partial Regex MacUserPath();

    [GeneratedRegex(@"(?<![A-Za-z0-9_.~-])/home/[^/\s""'<>]+", Plain, Timeout)]
    public static partial Regex LinuxHomePath();

    // App containers and per-user temporary folders on iOS and macOS.
    [GeneratedRegex(@"(?<![A-Za-z0-9_.~-])(?:/private)?/var/(?:mobile|folders)/", Plain, Timeout)]
    public static partial Regex AppleDevicePath();

    // \\host\share, also with the backslashes doubled as in a C# string.
    [GeneratedRegex(
        @"(?<![A-Za-z0-9_\\])\\{2}(?:\\{2})?[A-Za-z][A-Za-z0-9._-]*\\{1,2}[A-Za-z0-9$._-]+", Plain, Timeout)]
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

    [GeneratedRegex(@"^\s*<(string|integer)>([^<]*)</\1>\s*$", Plain, Timeout)]
    public static partial Regex PlistValue();

    // Retina image names such as icon@2x.png.
    [GeneratedRegex(@"^[0-9]+x\.[A-Za-z]+$", Plain, Timeout)]
    public static partial Regex ImageScaleSuffix();
}

// Matches that look like personal data but are not, judged from their context.
internal static class Excuses
{
    // git@host:owner/repo is an SSH remote, and icon@2x.png is an image name.
    public static bool Email(Match match, string line, string path)
    {
        var at = match.Value.IndexOf('@', StringComparison.Ordinal);
        var end = match.Index + match.Length;
        var sshRemote = match.Value[..at] == "git" && end < line.Length && line[end] == ':';
        return sshRemote || Patterns.ImageScaleSuffix().IsMatch(match.Value[(at + 1)..]);
    }

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

    // Signing material, pairing files and packaged apps (C-0002 section 6).
    private static readonly string[] s_extensions =
        [".p12", ".cer", ".mobileprovision", ".mobiledevicepairing", ".ipa"];

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

    // Every line with its number. A property list puts a key and its value on two lines, so
    // such a pair also comes as one "key: value" line under the value's number, which lets the
    // labeled rules see both.
    public static IEnumerable<(int Number, string Text)> Lines(string text)
    {
        var lines = text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        for (var index = 0; index < lines.Length; index++)
        {
            yield return (index + 1, lines[index]);
            if (index + 1 < lines.Length)
            {
                var key = Patterns.PlistKey().Match(lines[index]);
                var value = Patterns.PlistValue().Match(lines[index + 1]);
                if (key.Success && value.Success)
                {
                    yield return (index + 2, $"{key.Groups[1].Value}: {value.Groups[2].Value}");
                }
            }
        }
    }

    // Text files hold no NUL byte. Binary files, such as screenshots, are listed instead.
    public static bool IsBinary(byte[] bytes) => Array.IndexOf(bytes, (byte)0, 0, Math.Min(bytes.Length, 8000)) >= 0;

    public static string Decode(byte[] bytes) => new UTF8Encoding(false).GetString(bytes).TrimStart('\uFEFF');
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
            if (!document.RootElement.TryGetProperty("entries", out var array)
                || array.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException($"{FilePath} needs an \"entries\" array.");
            }

            foreach (var element in array.EnumerateArray())
            {
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
    // entry is stale and fails the scan, so that the list cannot rot.
    public (List<Finding> Kept, List<AllowEntry> Unused) Apply(IEnumerable<Finding> findings)
    {
        var used = new HashSet<AllowEntry>();
        var kept = new List<Finding>();
        foreach (var finding in findings)
        {
            var match = entries.FirstOrDefault(
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
    // Neither form ever contains the matched value.
    public static string Format(Finding finding) => finding.Line > 0
        ? $"{finding.Path}:{finding.Line}: {finding.RuleId}: {Rules.DescriptionOf(finding.RuleId)}"
        : $"{finding.Path}: {finding.RuleId}: {Rules.DescriptionOf(finding.RuleId)}";

    // A GitHub Actions annotation, which marks the file and line in the pull request.
    public static string Annotation(Finding finding)
    {
        var line = finding.Line > 0 ? $",line={finding.Line}" : string.Empty;
        var message = $"{Rules.DescriptionOf(finding.RuleId)} (rule {finding.RuleId}). The value is not shown.";
        return $"::error file={Escape(finding.Path, true)}{line},title=Personal data and forbidden files::"
            + Escape(message, false);
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
                findings.Add(new Finding(file, 0, rule));
            }
        }

        Console.WriteLine($"Forbidden files: checked the names of all {files.Count} files.");
        foreach (var folder in ScannedFolders)
        {
            findings.AddRange(ScanFolder(root, folder, files));
        }

        var (kept, unused) = allowList.Apply(findings);
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

        Console.WriteLine(
            $"{kept.Count} findings, {findings.Count - kept.Count} excused by {AllowList.FilePath}, "
                + $"{unused.Count} stale entries. A finding names the file, the line and the rule, never the value.");
        return kept.Count == 0 && unused.Count == 0 ? 0 : 1;
    }

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
            var bytes = File.ReadAllBytes(Path.Combine(root, file));
            if (ContentScanner.IsBinary(bytes))
            {
                binary.Add(file);
            }
            else
            {
                findings.AddRange(ContentScanner.Scan(file, ContentScanner.Decode(bytes)));
            }
        }

        Console.WriteLine($"Personal data: scanned {inFolder.Count - binary.Count} text files in {folder}.");
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

        using var process = Process.Start(start) ?? throw new InvalidDataException("git could not be started.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode == 0
            ? output
            : throw new InvalidDataException($"git {string.Join(' ', arguments)} failed: {error.Result.Trim()}");
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
        ("signing/development.cer", ForbiddenFiles.ExtensionRule),
        ("signing/Paneport.mobileprovision", ForbiddenFiles.ExtensionRule),
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
            new("udid", true, "older UDID format with its label", $"UDID = {hex40}"),
            new("udid", false, "commit hash in an evidence record", $"Build: commit {hex40}"),
            new("udid", false, "GUID", "Project GUID 3F2504E0-4F89-11D3-9A0C-0305E82C3301"),

            new("ecid", true, "ECID in hex", "ECID: 0x0123456789ABCD"),
            new("ecid", true, "UniqueChipID in decimal", "UniqueChipID: 98765432101"),
            new("ecid", false, "prose that names ECIDs", "The record never holds an ECID or a UDID."),

            new("serial-number", true, "serial number with its label", "Serial Number: AB12CD34EF"),
            new("serial-number", true, "SerialNumber key", "\"SerialNumber\": \"X1Y2Z3W4V5U6\""),
            new("serial-number", true, "randomized serial number", "serial: X9Y8Z7W6"),
            new("serial-number", false, "prose about serial numbers", "Serial numbers have 8 to 14 characters."),
            new("serial-number", false, "serial number that was not recorded", "Serial number: REDACTED"),

            new("team-identifier", true, "team identifier with its label", "Team ID: ABCDE12345"),
            new(
                "team-identifier",
                true,
                "team identifier in a property list",
                "<key>com.apple.developer.team-identifier</key>\n\t<string>ABCDE12345</string>"),
            new("team-identifier", false, "team identifier that was not recorded", "Team ID: not recorded"),

            new("app-identifier-prefix", true, "application-identifier value", "ABCDE12345.com.example.paneport"),
            new("app-identifier-prefix", true, "re-signed bundle identifier", "com.example.paneport.ABCDE12345"),
            new("app-identifier-prefix", false, "plain bundle identifier", "Bundle: com.example.paneport"),
            new("app-identifier-prefix", false, "C# namespace", "namespace Paneport.Core.Tests;"),

            new("email", true, "e-mail address", "Contact: tester@example.com"),
            new("email", false, "SSH remote", "git clone git@example.com:owner/repo.git"),
            new("email", false, "package directive with a version", "#:package Example.Package@1.2.3"),
            new("email", false, "image at twice the scale", "![screen](images/screen@2x.png)"),

            new("steam-id", true, "SteamID64", "\"owner\": 76561197960265728"),
            new("steam-id", true, "Steam2 ID", "STEAM_0:0:0"),
            new("steam-id", true, "Steam3 ID", "[U:1:0]"),
            new("steam-id", true, "labeled account ID", "steam_id: 00000000"),
            new("steam-id", false, "Steam app ID", "\"steamAppId\": 440"),
            new("steam-id", false, "Steam build ID", "\"gameBuild\": 14567890"),

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
            new("token", true, "bearer token", $"Authorization: Bearer {Synthetic.Alnum(32, 3)}"),
            new("token", true, "labeled refresh token", $"refresh_token={Synthetic.Alnum(24, 4)}"),
            new("token", true, "token in a URL", $"https://api.example.com/v1?access_token={Synthetic.Alnum(20, 5)}"),
            new("token", false, "placeholder", "refresh_token=<your refresh token>"),
            new("token", false, "redacted value", "access_token: REDACTED"),
            new("token", false, "content hash in a lock file", $"\"contentHash\": \"{Synthetic.Alnum(86, 6)}==\""),

            new("depot-key", true, "labeled depot key", $"Depot key: {hex64}"),
            new("depot-key", true, "line of a depot key file", $"1000;{hex64}"),
            new("depot-key", false, "SHA-256 of a program file", $"\"exeSha256\": [\"{hex64}\"]"),

            new("manifest-request-code", true, "manifest request code", "manifest request code: 1234567890123456789"),
            new("manifest-request-code", false, "prose", "The manifest request code is fetched again after 5 minutes."),

            new("local-path", true, "Windows user profile", @"Log: C:\Users\tester\AppData\Local\Paneport"),
            new("local-path", true, "Windows profile in a C# string", "var path = \"C:\\\\Users\\\\tester\\\\x\";"),
            new("local-path", true, "macOS home folder", "/Users/tester/Library/Logs"),
            new("local-path", true, "Linux home folder", "/home/tester/src/paneport"),
            new("local-path", true, "file address", "file:///Users/tester/report.txt"),
            new("local-path", true, "iOS app container", "/var/mobile/Containers/Data/Application/"),
            new("local-path", true, "network share", @"\\buildhost\share\logs"),
            new("local-path", false, "web address with a home folder", "https://example.com/home/tester/page"),
            new("local-path", false, "web address with a Users folder", "https://example.com/Users/tester"),
            new("local-path", false, "public Windows profile", @"C:\Users\Public\Documents"),
            new("local-path", false, "shared macOS folder", "/Users/Shared/Paneport"),
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

    // An unsigned token with "alg":"none", which no service accepts.
    public static string Jwt() =>
        $"{Base64Url("{\"alg\":\"none\",\"typ\":\"JWT\"}")}.{Base64Url("{\"sub\":\"paneport-self-test\"}")}.";

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
