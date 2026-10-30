// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

// C-0010 section 2: a token must not reach a log line by accident.
public sealed class SecretTests
{
    // Made up for this test; it only has to be recognizable in a string.
    private const string Hidden = "hidden-value-4711";

    [Fact]
    public void RevealReturnsTheValue()
    {
        Assert.Equal(Hidden, new Secret(Hidden).Reveal());
    }

    [Fact]
    public void TextFormNeverContainsTheSecret()
    {
        var secret = new Secret(Hidden);

        string[] texts =
        [
            secret.ToString(),
            $"{secret}",
            $"{secret,40}",
            $"{secret:X}",
            string.Format(CultureInfo.InvariantCulture, "{0}", secret),
            string.Concat("value ", secret),
            new StringBuilder().Append(secret).ToString(),
            Convert.ToString(secret, CultureInfo.InvariantCulture) ?? string.Empty,
        ];

        Assert.All(texts, text =>
        {
            Assert.DoesNotContain(Hidden, text, StringComparison.Ordinal);
            Assert.Contains(Secret.Redacted, text, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ALogLineWithTheSecretShowsItRedacted()
    {
        var log = new RecordingLog();

        log.Write(LogLevel.Information, nameof(SecretTests), $"Received {new Secret(Hidden)}.");

        var line = Assert.Single(log.Lines);
        Assert.Equal($"Received {Secret.Redacted}.", line.Message);
    }

    [Fact]
    public void EmptyValueIsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Secret(string.Empty));
    }
}
