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
// section 5). Every sign-in asks for a persistent refresh token of the SteamClient platform type,
// the one that Valve's client accepts (C-0007 section 3), under a fixed device name that is never
// the device's own. Steam's refusals arrive as SteamAuthException with Steam's result.
internal sealed class SteamKitAuthenticator(SteamClient client, string deviceName) : ISteamAuthenticator
{
    public async Task<IAuthSession> BeginWithCredentialsAsync(
        string accountName,
        string password,
        ICodeSource codes,
        CancellationToken cancellationToken)
    {
        var details = Details();
        details.Username = accountName;
        details.Password = password;
        details.Authenticator = new CodeSourceAuthenticator(codes);
        try
        {
            var session = await client.Authentication.BeginAuthSessionViaCredentialsAsync(details)
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new Session(session);
        }
        catch (AuthenticationException exception)
        {
            throw new SteamAuthException((SteamResult)(int)exception.Result, exception);
        }
    }

    public async Task<IQrAuthSession> BeginWithQrCodeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var session = await client.Authentication.BeginAuthSessionViaQRAsync(Details())
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            return new QrSession(session);
        }
        catch (AuthenticationException exception)
        {
            throw new SteamAuthException((SteamResult)(int)exception.Result, exception);
        }
    }

    private AuthSessionDetails Details() => new()
    {
        DeviceFriendlyName = deviceName,
        PlatformType = EAuthTokenPlatformType.k_EAuthTokenPlatformType_SteamClient,
        IsPersistentSession = true,
    };

    private static async Task<AuthTokens> WaitAsync(AuthSession session, CancellationToken cancellationToken)
    {
        try
        {
            var result = await session.PollingWaitForResultAsync(cancellationToken).ConfigureAwait(false);
            return new AuthTokens(result.AccountName, new Secret(result.RefreshToken));
        }
        catch (AuthenticationException exception)
        {
            throw new SteamAuthException((SteamResult)(int)exception.Result, exception);
        }
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
