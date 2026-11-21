// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Tests.Fakes;
using Paneport.Tests.Fakes;
using Xunit;
using static Paneport.Steam.Tests.Fakes.SyntheticContent;

namespace Paneport.Steam.Tests;

// S2.3.11: a full fake run through a capturing log shows no password, token, depot key, manifest
// request code, Steam ID or account name (C-0004 section 4, C-0010 section 2,
// steam-service.md section 8).
public sealed class RedactingLogTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public void RemovesAProtectedValueInAnyCase()
    {
        var log = new RedactingLog(new RecordingLog());
        log.Protect("SampleName");

        Assert.Equal("signed in as [redacted] and [redacted].", log.Scrub("signed in as SampleName and samplename."));
    }

    [Fact]
    public void LeavesValuesTooShortToProtect()
    {
        var log = new RedactingLog(new RecordingLog());
        log.Protect("ab");
        log.Protect("  ");
        log.Protect((string?)null);

        Assert.Equal("ab is fine", log.Scrub("ab is fine"));
    }

    [Fact]
    public void RemovesAKeyInHexadecimalAndInBase64()
    {
        var samples = new SteamSamples();
        var log = new RedactingLog(new RecordingLog());
        log.Protect(samples.DepotKey);

        foreach (var form in new[] { Convert.ToHexString(samples.DepotKey), Convert.ToBase64String(samples.DepotKey) })
        {
            Assert.Equal("key [redacted].", log.Scrub($"key {form}."));
        }
    }

    [Fact]
    public void RemovesWhatHasTheShapeOfASecretWithoutBeingTold()
    {
        var samples = new SteamSamples();
        var log = new RedactingLog(new RecordingLog());

        foreach (var secret in new[] { samples.RefreshToken, samples.SteamId64, samples.SteamId3, samples.SteamId2, Convert.ToHexString(samples.DepotKey), Convert.ToBase64String(samples.DepotKey) })
        {
            var scrubbed = log.Scrub($"before {secret} after");
            Assert.DoesNotContain(secret, scrubbed, StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith("before ", scrubbed, StringComparison.Ordinal);
            Assert.EndsWith(" after", scrubbed, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KeepsWhatIsNoSecret()
    {
        var log = new RedactingLog(new RecordingLog());
        var text = string.Create(CultureInfo.InvariantCulture, $"Depot {WindowsDepot} of app {AppId}: 12 files, 30 chunks, 2048 bytes, HTTP 403, result InvalidPassword (5).");

        Assert.Equal(text, log.Scrub(text));
    }

    [Fact]
    public void WritesTheScrubbedLineWithItsLevelAndSource()
    {
        var inner = new RecordingLog();
        var log = new RedactingLog(inner);
        var secret = new Secret("a-secret-value");
        log.Protect(secret);

        log.Write(LogLevel.Warning, "Source", "value a-secret-value");

        Assert.Equal([new LogLine(LogLevel.Warning, "Source", "value [redacted]")], inner.Lines);
    }

    // The whole gate run of L2 steps 17 to 28, with Steam's side played by fakes, and with lines of
    // the kind that SteamKit2's debug output could write, each holding one of the values, written
    // through the same log after the service learned them.
    [Fact]
    public async Task AFullFakeRunLogsNoSecret()
    {
        using var kit = new SteamTestKit();
        var samples = kit.Samples;
        kit.Authenticator.Guard = AccountGuard.AuthenticatorCode;
        kit.Apps.Infos[AppId] = AppInfo();
        kit.Apps.DepotKey = new DepotKeyResult(SteamResult.OK, (byte[])samples.DepotKey.Clone());
        kit.Content.ManifestCode = samples.ManifestCode;
        kit.Content.RequiredServerToken = samples.ServerToken;
        var (manifest, chunks) = Manifest();
        kit.Content.Manifest = manifest;
        foreach (var (entry, content) in chunks)
        {
            kit.Content.Chunks[entry.IdText] = content;
        }

        var attempt = kit.Service.StartPasswordSignIn(samples.AccountName, samples.Password, SignInConfirmation.EnterCode);
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);
        attempt.SubmitCode("WRONG");
        await SteamTestKit.Until(() => attempt.Progress.CodeWasWrong, "the code to be asked for again");
        attempt.SubmitCode(kit.Authenticator.Code);
        await kit.UntilRequested(FakeConnection.LogOn, 1);
        kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SignInOutcome.LoggedOn, (await attempt.Completion.WaitAsync(SteamTestKit.Patience, Token)).Outcome);
        kit.Connection.Listener.OnLicenseList(SteamResult.OK, [1, 2]);

        await kit.Gate.CountLicensesAsync(Token);
        await kit.Gate.ReadAppInfoAsync(AppId, Token);
        await kit.Gate.GetDepotKeyAsync(WindowsDepot, Token);
        await kit.Gate.GetManifestRequestCodeAsync(WindowsDepot, Token);
        await kit.Gate.DownloadManifestAsync(WindowsDepot, Token);
        await kit.Gate.DownloadChunksAsync(10, Token);
        foreach (var value in samples.Forbidden)
        {
            kit.Log.Write(LogLevel.Debug, "SteamKit2", $"debug line with {value} in it");
        }

        await kit.Gate.DeleteTestDataAsync(Token);
        var signOut = kit.Service.SignOutAsync(Token);
        await kit.UntilRequested(FakeConnection.LogOff, 1);
        kit.Connection.Listener.OnLoggedOff(SteamResult.OK);
        kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOut.WaitAsync(SteamTestKit.Patience, Token);

        var lines = kit.AppLog.Lines;
        Assert.True(lines.Count > samples.Forbidden.Count, "The run wrote lines of its own.");
        foreach (var line in lines)
        {
            foreach (var value in samples.Forbidden)
            {
                Assert.DoesNotContain(value, line.Message, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(value, line.Source, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
