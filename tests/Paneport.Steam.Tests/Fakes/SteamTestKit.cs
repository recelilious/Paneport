// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Content;
using Paneport.Steam.Gate;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Session;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Steam.Tests.Fakes;

// The Steam service over fakes of every port, as the composition root wires it over SteamKit2,
// with a log that records what reached the app's log and a test folder of its own.
internal sealed class SteamTestKit : IDisposable
{
    public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    public SteamTestKit()
        : this(authenticator: null)
    {
    }

    // With an authenticator of the test's own in the service's place of the fake one, such as the
    // SteamKit2 adapter's sessions.
    public SteamTestKit(Func<SteamSamples, ISteamAuthenticator>? authenticator)
    {
        Authenticator = new FakeAuthenticator(Samples);
        Log = new RedactingLog(AppLog);
        Service = new SteamService(Connection, authenticator?.Invoke(Samples) ?? Authenticator, Store, Delays, ReconnectPolicy.Default, Time, Log);
        DownloadsPath = Path.Combine(Path.GetTempPath(), "paneport-steam-tests-" + Guid.NewGuid().ToString("N"));
        Folder = new TestDownloadFolder(DownloadsPath);
        Gate = new GateSteps(Service.Session, Apps, Content, Folder, Time, Log);
    }

    public SteamSamples Samples { get; } = new();

    public FakeConnection Connection { get; } = new();

    public ManualDelays Delays { get; } = new();

    public FakeAuthenticator Authenticator { get; }

    public FakeTokenStore Store { get; } = new();

    public FakeTimeProvider Time { get; } = new();

    public RecordingLog AppLog { get; } = new();

    public RedactingLog Log { get; }

    public SteamService Service { get; }

    public SteamSession Session => Service.Session;

    public FakeApps Apps { get; } = new();

    public FakeContent Content { get; } = new();

    public string DownloadsPath { get; }

    public TestDownloadFolder Folder { get; }

    public GateSteps Gate { get; }

    public SavedSignIn SampleSignIn() => new(Samples.AccountName, Samples.RefreshTokenSecret);

    // Logs the session on with the sample sign-in, playing Steam's part.
    public async Task LogOnAsync()
    {
        var logOn = Session.LogOnAsync(SampleSignIn(), CancellationToken.None);
        Connection.Listener.OnConnected();
        Connection.Listener.OnLoggedOn(SteamResult.OK);
        Assert.Equal(SteamResult.OK, await logOn.WaitAsync(Patience));
        Assert.Equal(SteamSessionState.LoggedOn, Service.Status.State);
    }

    // Waits until the condition holds, for work that continues on another thread.
    public static async Task Until(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + Patience;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"Timed out waiting for {what}.");
            }

            await Task.Delay(5);
        }
    }

    public static Task UntilStage(ISignInAttempt attempt, SignInStage stage) =>
        Until(() => attempt.Progress.Stage == stage, $"the stage {stage} (now {attempt.Progress.Stage})");

    public Task UntilRequested(string request, int count) =>
        Until(() => Connection.Count(request) >= count, $"{count} requests to {request}");

    public void Dispose()
    {
        Service.Dispose();
        try
        {
            if (Directory.Exists(DownloadsPath))
            {
                Directory.Delete(DownloadsPath, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A file that another process holds stays behind in the temporary folder.
        }
    }
}
