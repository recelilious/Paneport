// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using System.Text.RegularExpressions;
using Paneport.Core.Logging;
using Paneport.Core.Security;

namespace Paneport.Steam.Logging;

// The log of the Steam service (S2.3.11, steam-service.md section 8). Every line of the service,
// and of SteamKit2's own debug output when that is on, passes through it on its way to the app's
// log, which must never hold a password, token, depot key, manifest request code, Steam ID or
// account name (C-0004 section 4, C-0010 section 2). It removes two kinds of text: each value that
// the service registered with Protect as it learned it, wherever it appears and in any case; and
// whatever has the shape of a secret, whoever wrote it: a JSON web token, a Steam ID in any of its
// three forms, a long run of hexadecimal or base64 characters, and a long number.
internal sealed partial class RedactingLog : ILog
{
    // Shorter values are not registered: they would remove ordinary words from every line.
    public const int MinProtectedLength = 3;

    private readonly ILog _inner;
    private readonly Lock _lock = new();
    private string[] _values = [];

    public RedactingLog(ILog inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    // Registers a value that no line may hold from now on.
    public void Protect(string? value)
    {
        if (value is null || value.Trim().Length < MinProtectedLength)
        {
            return;
        }

        lock (_lock)
        {
            if (_values.Contains(value, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            // Longest first, so that a value inside a longer one cannot leave the rest behind.
            _values = [.. _values.Append(value).OrderByDescending(v => v.Length)];
        }
    }

    public void Protect(Secret secret)
    {
        ArgumentNullException.ThrowIfNull(secret);
        Protect(secret.Reveal());
    }

    // Registers a key in the forms a line could hold it: hexadecimal and base64.
    public void Protect(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            return;
        }

        Protect(Convert.ToHexString(key));
        Protect(Convert.ToBase64String(key));
    }

    // Takes a value out again once the service has dropped it, such as a password when its attempt
    // ends, so that the filter does not keep it in memory for the service's life. The shape rules
    // stay.
    public void Unprotect(string? value)
    {
        if (value is null)
        {
            return;
        }

        lock (_lock)
        {
            _values = [.. _values.Where(v => !string.Equals(v, value, StringComparison.OrdinalIgnoreCase))];
        }
    }

    // Takes out both forms of a key that the service has wiped.
    public void Unprotect(ReadOnlySpan<byte> key)
    {
        if (key.IsEmpty)
        {
            return;
        }

        Unprotect(Convert.ToHexString(key));
        Unprotect(Convert.ToBase64String(key));
    }

    // Whether the filter holds a value; for the tests.
    public bool Holds(string value)
    {
        lock (_lock)
        {
            return _values.Contains(value, StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Write(LogLevel level, string source, string message) => _inner.Write(level, source, Scrub(message));

    public string Scrub(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        string[] values;
        lock (_lock)
        {
            values = _values;
        }

        var builder = new StringBuilder(text);
        foreach (var value in values)
        {
            ReplaceIgnoringCase(builder, value, Secret.Redacted);
        }

        var scrubbed = builder.ToString();
        scrubbed = WebToken().Replace(scrubbed, Secret.Redacted);
        scrubbed = SteamIdText().Replace(scrubbed, Secret.Redacted);
        scrubbed = LongHex().Replace(scrubbed, Secret.Redacted);
        scrubbed = LongBase64().Replace(scrubbed, Secret.Redacted);
        return LongNumber().Replace(scrubbed, Secret.Redacted);
    }

    private static void ReplaceIgnoringCase(StringBuilder builder, string value, string replacement)
    {
        var text = builder.ToString();
        var index = text.IndexOf(value, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return;
        }

        var result = new StringBuilder(text.Length);
        var start = 0;
        while (index >= 0)
        {
            result.Append(text, start, index - start).Append(replacement);
            start = index + value.Length;
            index = text.IndexOf(value, start, StringComparison.OrdinalIgnoreCase);
        }

        result.Append(text, start, text.Length - start);
        builder.Clear().Append(result);
    }

    // Three base64url parts, the first of which starts a JSON object: a JSON web token, the form of
    // Steam's refresh and access tokens.
    [GeneratedRegex(@"eyJ[A-Za-z0-9_-]{2,}\.[A-Za-z0-9_-]{2,}\.[A-Za-z0-9_-]*", RegexOptions.CultureInvariant)]
    private static partial Regex WebToken();

    // A Steam ID as STEAM_X:Y:Z or as [U:1:Z] and its kin. The 64-bit form is a long number.
    [GeneratedRegex(@"STEAM_[0-5]:[01]:\d+|\[[IUMGAPCgTLca]:\d:\d+(:\d+)?\]", RegexOptions.CultureInvariant)]
    private static partial Regex SteamIdText();

    // A depot key in hexadecimal has 64 characters; a chunk or file hash has 40, which this
    // removes too, at no loss for a log.
    [GeneratedRegex(@"(?<![0-9A-Za-z])[0-9A-Fa-f]{32,}(?![0-9A-Za-z])", RegexOptions.CultureInvariant)]
    private static partial Regex LongHex();

    // A depot key in base64 has 44 characters.
    [GeneratedRegex(@"(?<![0-9A-Za-z+/])[0-9A-Za-z+/]{40,}={0,2}", RegexOptions.CultureInvariant)]
    private static partial Regex LongBase64();

    // A 64-bit Steam ID has 17 digits, and a manifest request code is a 64-bit number. IDs of apps
    // and depots have at most 10 digits and stay.
    [GeneratedRegex(@"(?<!\d)\d{15,}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex LongNumber();
}
