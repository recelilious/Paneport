// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Ports;
using SteamKit2;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace Paneport.Steam.SteamKit;

// The authentication port over SteamKit2's SteamAuthentication (S2.3.2, steam-service.md
// section 5). Every sign-in asks for a refresh token of the SteamClient platform type, the one
// that Valve's client accepts (C-0007 section 3), under a fixed device name that is never the
// device's own; a password sign-in also asks for a persistent session. Steam's refusals arrive as
// SteamAuthException with Steam's result, and so does the end of the connection that a sign-in
// uses, as NoConnection.
internal sealed class SteamKitAuthenticator(SteamClient client, string deviceName) : ISteamAuthenticator
{
    public async Task<IAuthSession> BeginWithCredentialsAsync(
        string accountName,
        string password,
        ICodeSource codes,
        CancellationToken cancellationToken)
    {
        var details = Details(deviceName);
        details.Username = accountName;
        details.Password = password;
        details.Authenticator = new CodeSourceAuthenticator(codes);
        var session = await BeginAsync(() => client.Authentication.BeginAuthSessionViaCredentialsAsync(details), Connected, cancellationToken)
            .ConfigureAwait(false);
        return new Session(session.PollingWaitForResultAsync, Connected);
    }

    public async Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken)
    {
        var session = await BeginAsync(() => client.Authentication.BeginAuthSessionViaQRAsync(Details(deviceName)), Connected, cancellationToken)
            .ConfigureAwait(false);
        return new QrSession(session, Connected);
    }

    // What every sign-in asks Steam for. SteamKit2 sends the persistence only with a password
    // sign-in: a QR sign-in's request has no such field, so its token's lifetime is Steam's choice.
    internal static AuthSessionDetails Details(string deviceName) => new()
    {
        DeviceFriendlyName = deviceName,
        PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
        IsPersistentSession = true,
    };

    // Begins a sign-in on the connection, which must be up. SteamKit2 refuses to begin on a client
    // that is not connected with InvalidOperationException; the check before the call keeps that
    // case apart, and the exception counts as the end of the connection only while the client is
    // not connected, since the connection can end between the check and SteamKit2's own.
    internal static Task<T> BeginAsync<T>(Func<Task<T>> begin, Func<bool> connected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(begin);
        ArgumentNullException.ThrowIfNull(connected);
        return TranslateAsync(
            async () =>
            {
                if (!connected())
                {
                    throw new SteamAuthException(SteamResult.NoConnection);
                }

                try
                {
                    return await SteamKitJobs.WaitAsync(begin(), cancellationToken).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception) when (!connected())
                {
                    throw new SteamAuthException(SteamResult.NoConnection, exception);
                }
            },
            connected,
            cancellationToken);
    }

    // Runs one of SteamKit2's authentication calls and gives its failures in Steam's terms: a refusal
    // as SteamAuthException with Steam's result, and the end of the connection as NoConnection.
    // SteamKit2 ends a call in three ways that are not refusals. When the connection ends, it cancels
    // every pending job, which also ends a wait for an approval or a QR scan; that is no cancellation
    // by the caller. A job that starts after the connection ended, such as the next status poll of a
    // wait or a code that the user submits, fails at once with AsyncJobFailedException. And Steam
    // itself can fail a job while the connection is up, with the same exception, which is then a
    // failure of Steam's: Fail, which the page explains in plain words. Whether the client is
    // connected tells the last two apart: the session connects again only after it has seen the
    // drop and waited at least a second (ReconnectPolicy), so the client cannot be connected again
    // by the time the failure arrives here.
    internal static async Task<T> TranslateAsync<T>(Func<Task<T>> call, Func<bool> connected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(connected);
        try
        {
            return await call().ConfigureAwait(false);
        }
        catch (AuthenticationException exception)
        {
            throw new SteamAuthException((SteamResult)(int)exception.Result, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamAuthException(SteamResult.NoConnection, exception);
        }
        catch (AsyncJobFailedException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new SteamAuthException(connected() ? SteamResult.Fail : SteamResult.NoConnection, exception);
        }
    }

    // Waits for the tokens of a sign-in. SteamKit2's polling wait checks the token only between its
    // polls: a status request that has gone out ignores it until Steam answers or the job times out
    // after 10 seconds. So the wait ends at once on a cancel, as a sign-out that waits for the
    // attempt needs, and SteamKit2's own wait runs out in the background.
    internal static async Task<AuthTokens> WaitAsync(
        Func<CancellationToken, Task<AuthPollResult>> poll,
        Func<bool> connected,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(poll);
        var result = await TranslateAsync(() => SteamKitJobs.WaitAsync(poll(cancellationToken), cancellationToken), connected, cancellationToken)
            .ConfigureAwait(false);
        return new AuthTokens(result.AccountName, new Secret(result.RefreshToken));
    }

    private bool Connected() => client.IsConnected;

    // Passes SteamKit2's questions on to the sign-in flow. The email hint that SteamKit2 gives with
    // an email code is personal data and is not passed on.
    internal sealed class CodeSourceAuthenticator(ICodeSource codes) : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
            codes.GetCodeAsync(SteamCodeKind.Authenticator, previousCodeWasIncorrect);

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
            codes.GetCodeAsync(SteamCodeKind.Email, previousCodeWasIncorrect);

        public Task<bool> AcceptDeviceConfirmationAsync() => codes.AcceptApprovalAsync();
    }

    // A password sign-in's session: SteamKit2's polling wait, which the tests can stand in for.
    internal sealed class Session(Func<CancellationToken, Task<AuthPollResult>> poll, Func<bool> connected) : IAuthSession
    {
        public Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken) => WaitAsync(poll, connected, cancellationToken);
    }

    internal sealed class QrSession : IQrAuthSession
    {
        private readonly QrAuthSession _session;
        private readonly Func<bool> _connected;

        public QrSession(QrAuthSession session, Func<bool> connected)
        {
            ArgumentNullException.ThrowIfNull(session);
            ArgumentNullException.ThrowIfNull(connected);
            _session = session;
            _connected = connected;
            _session.ChallengeURLChanged = () => ChallengeChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? ChallengeChanged;

        public string Challenge => _session.ChallengeURL;

        public Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken) =>
            WaitAsync(_session.PollingWaitForResultAsync, _connected, cancellationToken);
    }
}
