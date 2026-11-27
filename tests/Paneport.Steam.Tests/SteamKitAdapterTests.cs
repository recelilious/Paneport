// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections;
using System.Reflection;
using Paneport.Core.Security;
using Paneport.Core.Steam;
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
// Steam ID's registration with the log filter, and the server pick after Steam asked for another
// server. And how a sign-in ends when its connection has ended, over SteamKit2's own sessions on
// a client that never connects: a wait that the drop cancels, a poll or a code that starts after
// it, and a begin without a connection; and a wait that ends at once on a cancel that SteamKit2's
// poll ignores. None of it connects to Steam.
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

    // A known answer, computed apart from this code: the first four bytes, little-endian, of
    // HMAC-SHA256 under the identity 0, 1, ..., 31 of the ASCII text "login-id".
    [Fact]
    public void TheLoginIdOfAFixedIdentityIsTheKnownAnswer()
    {
        var identity = new byte[MachineIdentity.Length];
        for (var i = 0; i < identity.Length; i++)
        {
            identity[i] = (byte)i;
        }

        Assert.Equal(3_172_526_703u, new InstallIdentityMachineInfo(identity).LoginId());
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

    // The adapter's answer to an accepted logon, reached through SteamKit2's own callback queue on a
    // client that never connects: it registers the account's Steam ID with the log filter, and
    // forgets the servers that Steam asked to leave.
    [Fact]
    public async Task AnAcceptedLogonRegistersTheSteamIdAndForgetsTheServersToAvoid()
    {
        var samples = new SteamSamples();
        var log = new RedactingLog(new RecordingLog());
        using var connection = new SteamKitConnection(SteamConfiguration.Create(builder => builder.WithDirectoryFetch(false)), DeviceName, 41_000_123, log);
        connection.Avoid(ServerRecord.CreateWebSocketServer("cm1.example.invalid:443").EndPoint);
        Assert.Equal(1, connection.AvoidedServers);

        connection.Client.PostCallback(LoggedOn(EResult.OK, samples.SteamId));

        await SteamTestKit.Until(() => connection.AvoidedServers == 0 && log.Holds(samples.SteamId64), "the logon's callback");
        Assert.True(log.Holds(samples.AccountNumberText));
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

        // Also when the list has another server before it that is not to be avoided either.
        var other = ServerRecord.CreateWebSocketServer("cm2.example.invalid:443");
        Assert.Same(candidate, SteamKitConnection.PickAvoiding(candidate, [first, other, candidate], [first.EndPoint], ProtocolTypes.WebSocket));
    }

    // SteamKit2 cancels every pending job when the connection ends, which also ends a wait for an
    // approval or a QR scan; that is no cancellation by the caller.
    [Fact]
    public async Task AWaitThatTheConnectionEndedReportsNoConnection()
    {
        var ended = await Assert.ThrowsAsync<SteamAuthException>(() =>
            SteamKitAuthenticator.TranslateAsync(() => Task.FromCanceled<int>(new CancellationToken(canceled: true)), () => false, CancellationToken.None));
        Assert.Equal(SteamResult.NoConnection, ended.Result);

        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            SteamKitAuthenticator.TranslateAsync(() => Task.FromCanceled<int>(caller.Token), () => false, caller.Token));
    }

    // A job that starts after the connection ended, such as the next status poll of a wait, fails at
    // once with AsyncJobFailedException. While the client is not connected, that is the end of the
    // connection; while it is connected, Steam failed the job itself.
    [Fact]
    public async Task AJobThatStartsAfterTheConnectionEndedReportsNoConnection()
    {
        var client = ClientThatNeverConnects();
        var authentication = client.GetHandler<SteamUnifiedMessages>()!.CreateService<SteamKit2.Internal.Authentication>();
        async Task<object> poll() =>
            await authentication.PollAuthSessionStatus(new CAuthentication_PollAuthSessionStatus_Request { client_id = 1, request_id = new byte[16] });

        await Assert.ThrowsAsync<AsyncJobFailedException>(poll);
        var ended = await Assert.ThrowsAsync<SteamAuthException>(() => SteamKitAuthenticator.TranslateAsync(poll, () => client.IsConnected, CancellationToken.None));
        var failed = await Assert.ThrowsAsync<SteamAuthException>(() => SteamKitAuthenticator.TranslateAsync(poll, () => true, CancellationToken.None));

        Assert.Equal(SteamResult.NoConnection, ended.Result);
        Assert.Equal(SteamResult.Fail, failed.Result);

        // The caller's own cancel stays a cancel, whatever the job did meanwhile.
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SteamKitAuthenticator.TranslateAsync(poll, () => false, caller.Token));
    }

    [Fact]
    public async Task AQrWaitWhoseConnectionEndedReportsNoConnection()
    {
        var client = ClientThatNeverConnects();
        var session = new SteamKitAuthenticator.QrSession(SteamKit2QrSession(client), () => client.IsConnected);

        var ended = await Assert.ThrowsAsync<SteamAuthException>(() => session.WaitForTokensAsync(CancellationToken.None));

        Assert.Equal(SteamResult.NoConnection, ended.Result);
    }

    // The SteamKit2 jobs of the gate steps, each through its adapter.
    public static TheoryData<string> StepJobs => ["access token", "app info", "depot key", "manifest code", "servers", "server token"];

    // A gate step's job that SteamKit2 ends because the connection ended says so in plain words, not
    // with SteamKit2's type name or as a cancel: a job that starts after the end fails at once, and a
    // job that is out at the end is cancelled, as SteamKit2 cancels every pending job then.
    [Theory]
    [MemberData(nameof(StepJobs))]
    public async Task AStepWhoseConnectionEndedSaysSo(string job)
    {
        var after = await Assert.ThrowsAsync<SteamStepException>(() => StartStep(job, ClientThatNeverConnects()));

        var client = ClientMarkedConnected();
        var pending = StartStep(job, client);
        await SteamTestKit.Until(() => PendingJobs(client).Count == 1, "the job to be out");
        EndConnection(client);
        var cancelled = await Assert.ThrowsAsync<SteamStepException>(() => pending.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));

        Assert.Equal(SteamKitJobs.ConnectionEnded, after.Message);
        Assert.Equal(SteamKitJobs.ConnectionEnded, cancelled.Message);
    }

    // SteamKit2 cancels a job that Steam leaves unanswered for its timeout while the connection
    // stays up; the step says that Steam did not answer.
    [Theory]
    [MemberData(nameof(StepJobs))]
    public async Task AStepJobThatSteamLeavesUnansweredSaysSo(string job)
    {
        var client = ClientMarkedConnected();
        var step = StartStep(job, client);
        await SteamTestKit.Until(() => PendingJobs(client).Count == 1, "the job to be out");

        TimeOutPendingJobs(client);

        var timedOut = await Assert.ThrowsAsync<SteamStepException>(() => step.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
        Assert.Equal(SteamKitJobs.NoAnswer, timedOut.Message);
    }

    [Fact]
    public async Task AStepJobThatSteamFailsWhileConnectedSaysSo()
    {
        var failed = await Assert.ThrowsAsync<SteamStepException>(() => SteamKitJobs.StepAsync(
            () => Task.FromException<int>(new AsyncJobFailedException()),
            () => true,
            CancellationToken.None));

        Assert.Equal(SteamKitJobs.SteamFailed, failed.Message);
    }

    // The caller's own cancel stays a cancel, whatever SteamKit2 did with the job meanwhile, so that
    // the page says the step was cancelled.
    [Fact]
    public async Task AStepThatTheCallerCancelsStaysCancelled()
    {
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SteamKitJobs.StepAsync(
            () => Task.FromCanceled<int>(caller.Token),
            () => false,
            caller.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SteamKitJobs.StepAsync(
            () => Task.FromException<int>(new AsyncJobFailedException()),
            () => false,
            caller.Token));
    }

    // The poll's own refusals, such as an account without a confirmation that SteamKit2 supports,
    // are no end of the connection.
    [Fact]
    public async Task APollsOwnRefusalIsNoEndOfTheConnection()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => SteamKitAuthenticator.WaitAsync(
            _ => Task.FromException<AuthPollResult>(new InvalidOperationException("There are no allowed confirmations")),
            () => false,
            CancellationToken.None));
    }

    // SteamKit2 refuses to begin a sign-in on a client that is not connected; the adapter says why.
    [Fact]
    public async Task ASignInThatBeginsWithoutAConnectionReportsNoConnection()
    {
        var samples = new SteamSamples();
        var authenticator = new SteamKitAuthenticator(ClientThatNeverConnects(), DeviceName);

        var credentials = await Assert.ThrowsAsync<SteamAuthException>(() =>
            authenticator.BeginWithCredentialsAsync(samples.AccountName, samples.Password, new NoCodes(), CancellationToken.None));
        var qr = await Assert.ThrowsAsync<SteamAuthException>(() => authenticator.BeginWithQrCodeAsync(CancellationToken.None));

        Assert.Equal(SteamResult.NoConnection, credentials.Result);
        Assert.Equal(SteamResult.NoConnection, qr.Result);
    }

    [Fact]
    public async Task ABeginWithoutAConnectionNeverReachesSteamKit2()
    {
        var reached = false;

        var refused = await Assert.ThrowsAsync<SteamAuthException>(() => SteamKitAuthenticator.BeginAsync(
            () =>
            {
                reached = true;
                return Task.FromResult(0);
            },
            () => false,
            CancellationToken.None));

        Assert.Equal(SteamResult.NoConnection, refused.Result);
        Assert.False(reached);
    }

    // A sign-in cancelled before its begin never reaches SteamKit2, whose begin takes no token.
    [Fact]
    public async Task ABeginWithACancelledTokenNeverReachesSteamKit2()
    {
        var reached = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SteamKitAuthenticator.BeginAsync(
            () =>
            {
                reached = true;
                return Task.FromResult(0);
            },
            () => true,
            new CancellationToken(canceled: true)));

        Assert.False(reached);
    }

    // The connection can end between the adapter's check and SteamKit2's own. SteamKit2's refusal
    // to begin then means NoConnection, and only then.
    [Fact]
    public async Task SteamKit2sRefusalToBeginMeansNoConnectionOnlyWithoutAConnection()
    {
        var checks = 0;
        var ended = await Assert.ThrowsAsync<SteamAuthException>(() => SteamKitAuthenticator.BeginAsync(
            () => Task.FromException<int>(new InvalidOperationException("The SteamClient instance must be connected.")),
            () => checks++ == 0,
            CancellationToken.None));
        Assert.Equal(SteamResult.NoConnection, ended.Result);

        await Assert.ThrowsAsync<InvalidOperationException>(() => SteamKitAuthenticator.BeginAsync(
            () => Task.FromException<int>(new InvalidOperationException("Another refusal.")),
            () => true,
            CancellationToken.None));
    }

    // SteamKit2's poll ignores the token while its status request is out, until Steam answers or the
    // job times out after 10 seconds; the wait still ends at once on a cancel.
    [Fact]
    public async Task AWaitEndsAtOnceOnACancelThatSteamKit2sPollIgnores()
    {
        using var cancel = new CancellationTokenSource();
        var poll = new TaskCompletionSource<AuthPollResult>();
        var wait = SteamKitAuthenticator.WaitAsync(_ => poll.Task, () => true, cancel.Token);

        await cancel.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => wait.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken));
    }

    // The flow over the adapter's sessions and SteamKit2's own, on a client whose connection ended
    // before the next poll: the wait for an approval falls back to a code on a new connection.
    [Fact]
    public async Task AnApprovalWhoseNextPollFindsTheConnectionEndedFallsBackToACode()
    {
        var client = ClientThatNeverConnects();
        SteamKit2Sessions? sessions = null;
        using var kit = new SteamTestKit(samples => sessions = new SteamKit2Sessions(
            client,
            samples,
            () => client.IsConnected,
            EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation,
            EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode));
        var attempt = kit.Service.StartPasswordSignIn(kit.Samples.AccountName, kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();

        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);

        Assert.Equal(2, sessions!.CredentialSessions);
        attempt.Cancel();
        await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task AQrSignInWhoseNextPollFindsTheConnectionEndedSaysSo()
    {
        var client = ClientThatNeverConnects();
        using var kit = new SteamTestKit(samples => new SteamKit2Sessions(client, samples, () => client.IsConnected));
        var attempt = kit.Service.StartQrSignIn();
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();

        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("The connection to Steam ended during the sign-in. Start it again.", result.Problem);
    }

    // A job that Steam itself fails while the connection is up is no drop; the page says so in
    // plain words. The client here only claims to be connected, so its job fails as Steam's would.
    [Fact]
    public async Task AQrSignInThatSteamFailsWhileConnectedSaysSoInPlainWords()
    {
        var client = ClientThatNeverConnects();
        using var kit = new SteamTestKit(samples => new SteamKit2Sessions(client, samples, () => true));
        var attempt = kit.Service.StartQrSignIn();
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();

        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("Steam could not complete the sign-in. Start it again.", result.Problem);
    }

    // A code that the user submits after the connection ended goes to Steam as a job that fails at
    // once, as when the user reads the code in the Steam mobile app on the same phone.
    [Fact]
    public async Task ACodeSubmittedAfterTheConnectionEndedSaysSo()
    {
        var client = ClientThatNeverConnects();
        using var kit = new SteamTestKit(samples => new SteamKit2Sessions(client, samples, () => client.IsConnected, EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode));
        var attempt = kit.Service.StartPasswordSignIn(kit.Samples.AccountName, kit.Samples.Password, SignInConfirmation.EnterCode);
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForCode);

        attempt.SubmitCode("R7K2Q");
        var result = await attempt.Completion.WaitAsync(SteamTestKit.Patience, TestContext.Current.CancellationToken);

        Assert.Equal(SignInOutcome.Failed, result.Outcome);
        Assert.Equal("The connection to Steam ended during the sign-in. Start it again.", result.Problem);
    }

    // A sign-out waits for the running attempt; an approval wait whose poll ignores the cancel
    // keeps it waiting no longer than the cancel.
    [Fact]
    public async Task ASignOutDoesNotWaitForAPollThatIgnoresTheCancel()
    {
        var token = TestContext.Current.CancellationToken;
        var stuck = new TaskCompletionSource<AuthPollResult>();
        using var kit = new SteamTestKit(_ => new StuckSessions(stuck.Task));
        kit.Store.SignIn = kit.SampleSignIn();
        var resume = kit.Service.ResumeAsync(token);
        await kit.UntilRequested(FakeConnection.Connect, 1);
        kit.Connection.Listener.OnConnected();
        await kit.UntilRequested(FakeConnection.LogOn, 1);
        kit.Connection.Listener.OnLoggedOn(SteamResult.OK);
        await resume.WaitAsync(SteamTestKit.Patience, token);
        var attempt = kit.Service.StartPasswordSignIn(kit.Samples.AccountName, kit.Samples.Password, SignInConfirmation.ApproveInSteamApp);
        await SteamTestKit.UntilStage(attempt, SignInStage.WaitingForApproval);

        var signOut = kit.Service.SignOutAsync(token);
        await SteamTestKit.Until(() => kit.Service.Status.State == SteamSessionState.SigningOut, "the sign-off");
        kit.Connection.Listener.OnLoggedOff(SteamResult.OK);
        kit.Connection.Listener.OnDisconnected(userInitiated: true);
        await signOut.WaitAsync(SteamTestKit.Patience, token);

        Assert.Equal(SignInOutcome.Cancelled, (await attempt.Completion.WaitAsync(SteamTestKit.Patience, token)).Outcome);
        Assert.Null(kit.Store.SignIn);
    }

    // SteamKit2's callback for a logon answer, built from a result through its non-public
    // constructor, with the account's Steam ID as Steam's answer would carry it.
    private static SteamUser.LoggedOnCallback LoggedOn(EResult result, SteamID steamId)
    {
        var constructor = typeof(SteamUser.LoggedOnCallback).GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(EResult)])
            ?? throw new InvalidOperationException("SteamKit2's LoggedOnCallback has no constructor from a result.");
        var callback = (SteamUser.LoggedOnCallback)constructor.Invoke([result]);
        typeof(SteamUser.LoggedOnCallback).GetProperty(nameof(SteamUser.LoggedOnCallback.ClientSteamID))!.SetValue(callback, steamId);
        return callback;
    }

    private static SteamClient ClientThatNeverConnects() =>
        new(SteamConfiguration.Create(builder => builder.WithDirectoryFetch(false)), "Paneport tests");

    // SteamKit2 4.0.0-Beta.2 (commit 082594a) gives CMClient.IsConnected a private setter and keeps a
    // client's jobs in its internal AsyncJobManager; the tests reach both through reflection, to play
    // what SteamKit2 does when a connection ends or a job times out. A client marked connected has no
    // connection, so nothing that it sends leaves the process.
    private static SteamClient ClientMarkedConnected()
    {
        var client = ClientThatNeverConnects();
        SetConnected(client, connected: true);
        return client;
    }

    private static void SetConnected(SteamClient client, bool connected) =>
        typeof(CMClient).GetProperty(nameof(CMClient.IsConnected))!.SetValue(client, connected);

    private static object JobManager(SteamClient client) =>
        typeof(SteamClient).GetField("jobManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(client)!;

    private static List<AsyncJob> PendingJobs(SteamClient client)
    {
        var manager = JobManager(client);
        var jobs = (IDictionary)manager.GetType().GetField("asyncJobs", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
        return [.. jobs.Values.Cast<AsyncJob>()];
    }

    private static void RunJobManager(SteamClient client, string method)
    {
        var manager = JobManager(client);
        manager.GetType().GetMethod(method, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(manager, null);
    }

    // What SteamKit2 does when the connection ends: it marks the client as not connected, then
    // cancels every pending job.
    private static void EndConnection(SteamClient client)
    {
        SetConnected(client, connected: false);
        RunJobManager(client, "CancelPendingJobs");
    }

    // What SteamKit2's timer does once a job has waited for an answer as long as its timeout allows:
    // it cancels the job, while the connection stays up.
    private static void TimeOutPendingJobs(SteamClient client)
    {
        foreach (var job in PendingJobs(client))
        {
            job.Timeout = TimeSpan.Zero;
        }

        RunJobManager(client, "CancelTimedoutJobs");
    }

    // Starts a gate step's job through its adapter.
    private static Task StartStep(string job, SteamClient client)
    {
        var token = TestContext.Current.CancellationToken;
        var apps = new SteamKitApps(client);
        var content = new SteamKitContent(client);
        Task step = job switch
        {
            "access token" => apps.GetAppAccessTokenAsync(4_100_000_100, token),
            "app info" => apps.GetAppInfoAsync(4_100_000_100, 0, token),
            "depot key" => apps.GetDepotKeyAsync(4_100_000_101, 4_100_000_100, token),
            "manifest code" => content.GetManifestRequestCodeAsync(4_100_000_101, 4_100_000_100, 1, token),
            "servers" => content.GetServersAsync(token),
            "server token" => content.GetServerTokenAsync(4_100_000_100, 4_100_000_101, new ContentServer("cdn.example.invalid", new object()), token),
            _ => throw new ArgumentOutOfRangeException(nameof(job), job, "No such step job."),
        };

        // The CDN client goes once the job has ended; only the downloads use it.
        _ = step.ContinueWith(static (_, state) => ((SteamKitContent)state!).Dispose(), content, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return step;
    }

    // SteamKit2 makes its sessions only from Steam's answer to a begin request, so the tests build
    // them from such an answer through their non-public constructors.
    private static QrAuthSession SteamKit2QrSession(SteamClient client)
    {
        var answer = new CAuthentication_BeginAuthSessionViaQR_Response
        {
            client_id = 1,
            request_id = new byte[16],
            interval = 0.05f,
            challenge_url = "https://example.invalid/q",
        };
        answer.allowed_confirmations.Add(new CAuthentication_AllowedConfirmation
        {
            confirmation_type = EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceConfirmation,
        });
        return (QrAuthSession)Construct(typeof(QrAuthSession), client.Authentication, null, answer);
    }

    private static CredentialsAuthSession SteamKit2CredentialsSession(
        SteamClient client,
        IAuthenticator authenticator,
        SteamSamples samples,
        EAuthSessionGuardType[] confirmations)
    {
        var answer = new CAuthentication_BeginAuthSessionViaCredentials_Response
        {
            client_id = 1,
            request_id = new byte[16],
            interval = 0.05f,
            steamid = samples.SteamId.ConvertToUInt64(),
        };
        foreach (var confirmation in confirmations)
        {
            answer.allowed_confirmations.Add(new CAuthentication_AllowedConfirmation { confirmation_type = confirmation });
        }

        return (CredentialsAuthSession)Construct(typeof(CredentialsAuthSession), client.Authentication, authenticator, answer);
    }

    private static object Construct(Type type, SteamAuthentication authentication, IAuthenticator? authenticator, object answer)
    {
        var constructor = type.GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(SteamAuthentication), typeof(IAuthenticator), answer.GetType()])
            ?? throw new InvalidOperationException($"SteamKit2's {type.Name} has no constructor from Steam's answer.");
        return constructor.Invoke([authentication, authenticator, answer]);
    }

    private sealed class NoCodes : ICodeSource
    {
        public Task<bool> AcceptApprovalAsync() => Task.FromResult(false);

        public Task<string> GetCodeAsync(SteamCodeKind kind, bool previousWasWrong) => Task.FromResult("R7K2Q");
    }

    // The adapter's sessions over SteamKit2's own, on a client that never connects. A later password
    // sign-in, the code round after an approval that did not come, gets a session that asks for a
    // code and then gives the sample tokens, as Steam would on a new connection.
    private sealed class SteamKit2Sessions(SteamClient client, SteamSamples samples, Func<bool> connected, params EAuthSessionGuardType[] confirmations) : ISteamAuthenticator
    {
        private int _credentialSessions;

        public int CredentialSessions => Volatile.Read(ref _credentialSessions);

        public Task<IAuthSession> BeginWithCredentialsAsync(string accountName, string password, ICodeSource codes, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _credentialSessions) > 1)
            {
                return Task.FromResult<IAuthSession>(new CodeThenTokens(codes, samples));
            }

            var session = SteamKit2CredentialsSession(client, new SteamKitAuthenticator.CodeSourceAuthenticator(codes), samples, confirmations);
            return Task.FromResult<IAuthSession>(new SteamKitAuthenticator.Session(session.PollingWaitForResultAsync, connected));
        }

        public Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IQrAuthSession>(new SteamKitAuthenticator.QrSession(SteamKit2QrSession(client), connected));
    }

    private sealed class CodeThenTokens(ICodeSource codes, SteamSamples samples) : IAuthSession
    {
        public async Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken)
        {
            _ = await codes.GetCodeAsync(SteamCodeKind.Authenticator, previousWasWrong: false);
            return new AuthTokens(samples.AccountName, samples.RefreshTokenSecret);
        }
    }

    // A password sign-in whose poll never ends and ignores the token, as SteamKit2's does while its
    // status request is out.
    private sealed class StuckSessions(Task<AuthPollResult> stuck) : ISteamAuthenticator
    {
        public Task<IAuthSession> BeginWithCredentialsAsync(string accountName, string password, ICodeSource codes, CancellationToken cancellationToken) =>
            Task.FromResult<IAuthSession>(new SteamKitAuthenticator.Session(
                async ignoredToken =>
                {
                    _ = await codes.AcceptApprovalAsync();
                    return await stuck;
                },
                () => true));

        public Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
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
