// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Session;
using Paneport.Steam.SignIn;

namespace Paneport.Steam;

/// <summary>
/// The Steam service on SteamKit2 (C-0007 sections 2 and 3, <c>docs/design/steam-service.md</c>):
/// the session with Steam, sign-in by approval, by code and by QR code, and the saved sign-in in
/// the token store.
/// </summary>
/// <remarks>
/// One sign-in runs at a time; starting another cancels the one before. Every log line goes
/// through a filter that removes passwords, tokens, keys, codes, Steam IDs and the account name
/// (C-0004 section 4, C-0010 section 2).
/// </remarks>
public sealed class SteamService : ISteamService, IDisposable
{
    private readonly SteamSession _session;
    private readonly ISteamAuthenticator _authenticator;
    private readonly TokenKeeper _tokens;
    private readonly TimeProvider _time;
    private readonly RedactingLog _log;
    private readonly Lock _lock = new();
    private SignInFlow? _current;

    internal SteamService(
        ISteamConnection connection,
        ISteamAuthenticator authenticator,
        ITokenStore tokens,
        IDelays delays,
        ReconnectPolicy policy,
        TimeProvider time,
        RedactingLog log)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(authenticator);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _authenticator = authenticator;
        _time = time;
        _log = log;
        _tokens = new TokenKeeper(new ProtectingTokenStore(tokens, log), log);
        _session = new SteamSession(connection, _tokens, delays, policy, log);
        _session.StatusChanged += (_, _) => StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? StatusChanged;

    /// <inheritdoc />
    public SteamSignInState SignInState =>
        Status.State == SteamSessionState.LoggedOn ? SteamSignInState.SignedIn : SteamSignInState.SignedOut;

    /// <inheritdoc />
    public SteamSessionStatus Status => _session.Status;

    internal SteamSession Session => _session;

    /// <inheritdoc />
    public async Task<SignInResult> ResumeAsync(CancellationToken cancellationToken)
    {
        if (_session.IsLoggedOn)
        {
            return new SignInResult(SignInOutcome.LoggedOn, TimeSpan.Zero, TimeSpan.Zero, Problem: null);
        }

        var started = _time.GetTimestamp();
        var saved = await _tokens.ReadAsync(cancellationToken).ConfigureAwait(false);
        _session.SetSignInSaved(saved is not null);
        if (saved is null)
        {
            return SignInResult.NothingSaved;
        }

        var result = await _session.LogOnAsync(saved, cancellationToken).ConfigureAwait(false);
        var elapsed = _time.GetElapsedTime(started);
        return result == SteamResult.OK
            ? new SignInResult(SignInOutcome.LoggedOn, elapsed, TimeSpan.Zero, Problem: null)
            : new SignInResult(SignInOutcome.Failed, elapsed, TimeSpan.Zero, $"Steam refused the logon: {SteamResults.Describe(result)}.");
    }

    /// <inheritdoc />
    public ISignInAttempt StartPasswordSignIn(string accountName, string password, SignInConfirmation confirmation)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        _log.Protect(accountName);
        _log.Protect(password);
        var flow = SignInFlow.StartWithPassword(_session, _authenticator, _tokens, _time, _log, accountName, password, confirmation);
        Replace(flow);
        return flow;
    }

    /// <inheritdoc />
    public ISignInAttempt StartQrSignIn()
    {
        var flow = SignInFlow.StartWithQrCode(_session, _authenticator, _tokens, _time, _log);
        Replace(flow);
        return flow;
    }

    /// <inheritdoc />
    public async Task SignOutAsync(CancellationToken cancellationToken)
    {
        Replace(null);
        await _tokens.DeleteAsync(CancellationToken.None).ConfigureAwait(false);
        _session.SetSignInSaved(false);
        await _session.SignOffAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Tells the service that the app returned to the foreground. A session that should be logged
    /// on connects and logs on again, because iOS may have closed the socket of the suspended app.
    /// </summary>
    public void NotifyForegroundReturned() => _session.OnForegroundReturned();

    /// <summary>Cancels a running sign-in and releases the service's resources.</summary>
    public void Dispose()
    {
        Replace(null);
        _tokens.Dispose();
    }

    // Makes the flow the current one and cancels the one before. The old flow ends on its own and
    // drops its password then; it is disposed once it has ended.
    private void Replace(SignInFlow? flow)
    {
        SignInFlow? previous;
        lock (_lock)
        {
            previous = _current;
            _current = flow;
        }

        if (previous is not null)
        {
            previous.Cancel();
            _ = previous.Completion.ContinueWith(
                static (_, state) => ((SignInFlow)state!).Dispose(),
                previous,
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }
}
