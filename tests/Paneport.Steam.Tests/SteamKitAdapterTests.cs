// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.SteamKit;
using Paneport.Steam.Tests.Fakes;
using Paneport.Tests.Fakes;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Discovery;
using SteamKit2.Internal;
using Xunit;

namespace Paneport.Steam.Tests;

// What the SteamKit2 adapters send to Steam and how they read its answers, which no fake reaches:
// the sign-in's details, the logon's machine name and login ID, the machine identity's wiring, the
// Steam ID's registration with the log filter, the server pick after Steam asked for another
// server, and the end of a wait that a dropped connection cancels. None of it connects to Steam.
public sealed class SteamKitAdapterTests
{
    private const string DeviceName = "Paneport (test)";

    [Fact]
    public void EverySignInAsksForASteamClientTokenUnderTheFixedName()
    {
        var details = SteamKitAuthenticator.Details(DeviceName);

        Assert.Equal(DeviceName, details.DeviceFriendlyName);
        Assert.Equal(EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient, details.PlatformType);
        Assert.True(details.IsPersistentSession);
    }

    [Fact]
    public void ALogonSendsTheFixedMachineNameAndTheLoginId()
    {
        var samples = new SteamSamples();

        var details = SteamKitConnection.LogOnDetails(samples.AccountName, samples.RefreshTokenSecret, DeviceName, 41_000_123);

        Assert.Equal(samples.AccountName, details.Username);
        Assert.Equal(samples.RefreshToken, details.AccessToken);
        Assert.Null(details.Password);
        Assert.True(details.ShouldRememberPassword);
        Assert.Equal(DeviceName, details.MachineName);
        Assert.Equal(41_000_123u, details.LoginID);
    }

    // The login ID replaces the device's private IP address, which SteamKit2 would send without one:
    // the same for each logon of an installation, another for another installation, never 0.
    [Fact]
    public void TheLoginIdComesFromTheInstallIdentityAlone()
    {
        var identity = new byte[MachineIdentity.Length];
        identity[0] = 7;
        var other = new byte[MachineIdentity.Length];
        other[0] = 8;

        var loginId = new InstallIdentityMachineInfo(identity).LoginId();

        Assert.Equal(loginId, new InstallIdentityMachineInfo([.. identity]).LoginId());
        Assert.NotEqual(loginId, new InstallIdentityMachineInfo(other).LoginId());
        Assert.NotEqual(0u, loginId);
        Assert.NotEqual(0u, InstallIdentityMachineInfo.RandomLoginId());
    }

    [Fact]
    public void TheServicesUseTheInstallIdentityForTheMachineAndTheLoginId()
    {
        var identity = new byte[MachineIdentity.Length];
        identity[5] = 3;
        using var services = SteamServices.Create(Options(identity));

        Assert.IsType<InstallIdentityMachineInfo>(services.Connection.Client.Configuration.MachineInfoProvider);
        Assert.Equal(new InstallIdentityMachineInfo(identity).LoginId(), services.Connection.LoginId);
    }

    [Fact]
    public void WithoutAnInstallIdentityEachRunHasALoginIdOfItsOwn()
    {
        using var first = SteamServices.Create(Options(identity: null));
        using var second = SteamServices.Create(Options(identity: null));

        Assert.IsNotType<InstallIdentityMachineInfo>(first.Connection.Client.Configuration.MachineInfoProvider);
        Assert.NotEqual(0u, first.Connection.LoginId);
        Assert.NotEqual(first.Connection.LoginId, second.Connection.LoginId);
    }

    // The bare account number has no shape that a rule could remove, so only the registration
    // keeps it out of the log.
    [Fact]
    public void TheSteamIdIsRegisteredInEveryFormALineCouldHold()
    {
        var samples = new SteamSamples();
        var log = new RedactingLog(new RecordingLog());

        SteamKitConnection.ProtectSteamId(log, samples.SteamId);

        foreach (var form in new[] { samples.SteamId64, samples.SteamId3, samples.AccountNumberText })
        {
            Assert.Equal($"before {Secret.Redacted} after", log.Scrub($"before {form} after"));
        }
    }

    // SteamKit2 gives its pick with the chosen protocol alone and its list with all of a server's
    // protocols, so the server to avoid is found by its endpoint.
    [Fact]
    public void AMoveAvoidsTheServerWhateverProtocolsItIsListedWith()
    {
        var avoided = ServerRecord.CreateServer("cm1.example.invalid", 27017, ProtocolTypes.Tcp);
        var listed = ServerRecord.CreateServer("cm1.example.invalid", 27017, ProtocolTypes.Tcp | ProtocolTypes.Udp);
        var other = ServerRecord.CreateServer("cm2.example.invalid", 27017, ProtocolTypes.Tcp | ProtocolTypes.Udp);

        var pick = SteamKitConnection.PickAvoiding(avoided, [listed, other], [avoided.EndPoint], ProtocolTypes.Tcp);

        Assert.Same(other, pick);
    }

    [Fact]
    public void SteamKit2sOwnPickStandsWhenItIsNoServerToAvoid()
    {
        var candidate = ServerRecord.CreateWebSocketServer("cm3.example.invalid:443");
        var first = ServerRecord.CreateWebSocketServer("cm1.example.invalid:443");

        Assert.Same(candidate, SteamKitConnection.PickAvoiding(candidate, [first, candidate], [first.EndPoint], ProtocolTypes.WebSocket));
        Assert.Same(candidate, SteamKitConnection.PickAvoiding(candidate, [first, candidate], [], ProtocolTypes.WebSocket));
    }

    // SteamKit2 cancels every pending job when the connection ends, which also ends a wait for an
    // approval or a QR scan; that is no cancellation by the caller.
    [Fact]
    public async Task AWaitThatTheConnectionEndedReportsNoConnection()
    {
        var ended = await Assert.ThrowsAsync<SteamAuthException>(() =>
            SteamKitAuthenticator.TranslateAsync(() => Task.FromCanceled<int>(new CancellationToken(canceled: true)), CancellationToken.None));
        Assert.Equal(SteamResult.NoConnection, ended.Result);

        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SteamKitAuthenticator.TranslateAsync(() => Task.FromCanceled<int>(caller.Token), caller.Token));
    }

    private static SteamServiceOptions Options(byte[]? identity) => new()
    {
        Log = new RecordingLog(),
        Tokens = new FakeTokenStore(),
        DeviceName = DeviceName,
        DownloadsFolder = Path.Combine(Path.GetTempPath(), "paneport-adapter-tests"),
        MachineIdentity = identity,
    };
}
