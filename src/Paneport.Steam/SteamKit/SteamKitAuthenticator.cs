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
// SteamAuthException with Steam's result.
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
        var session = await TranslateAsync(
            () => client.Authentication.BeginAuthSessionViaCredentialsAsync(details).WaitAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new Session(session);
    }

    public async Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken)
    {
        var session = await TranslateAsync(
            () => client.Authentication.BeginAuthSessionViaQRAsync(Details(deviceName)).WaitAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        return new QrSession(session);
    }

    // What every sign-in asks Steam for. SteamKit2 sends the persistence only with a password
    // sign-in: a QR sign-in's request has no such field, so its token's lifetime is Steam's choice.
    internal static AuthSessionDetails Details(string deviceName) => new()
    {
        DeviceFriendlyName = deviceName,
        PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
        IsPersistentSession = true,
    };

    // Runs one of SteamKit2's authentication calls and gives its failures in Steam's terms: a refusal
    // as SteamAuthException with Steam's result, and a cancellation that is not the caller's as
    // NoConnection, because SteamKit2 cancels every pending job when the connection ends, which
    // also ends a wait for an approval or a QR scan.
    internal static async Task<T> TranslateAsync<T>(Func<Task<T>> call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);
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
    }

    private static async Task<AuthTokens> WaitAsync(AuthSession session, CancellationToken cancellationToken)
    {
        var result = await TranslateAsync(() => session.PollingWaitForResultAsync(cancellationToken), cancellationToken).ConfigureAwait(false);
        return new AuthTokens(result.AccountName, new Secret(result.RefreshToken));
    }

    // Passes SteamKit2's questions on to the sign-in flow. The email hint that SteamKit2 gives with
    // an email code is personal data and is not passed on.
    private sealed class CodeSourceAuthenticator(ICodeSource codes) : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect) =>
            codes.GetCodeAsync(SteamCodeKind.Authenticator, previousCodeWasIncorrect);

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect) =>
            codes.GetCodeAsync(SteamCodeKind.Email, previousCodeWasIncorrect);

        public Task<bool> AcceptDeviceConfirmationAsync() => codes.AcceptApprovalAsync();
    }

    private sealed class Session(CredentialsAuthSession session) : IAuthSession
    {
        public Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken) => WaitAsync(session, cancellationToken);
    }

    private sealed class QrSession : IQrAuthSession
    {
        private readonly QrAuthSession _session;

        public QrSession(QrAuthSession session)
        {
            _session = session;
            _session.ChallengeURLChanged = () => ChallengeChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? ChallengeChanged;

        public string Challenge => _session.ChallengeURL;

        public Task<AuthTokens> WaitForTokensAsync(CancellationToken cancellationToken) => WaitAsync(_session, cancellationToken);
    }
}
