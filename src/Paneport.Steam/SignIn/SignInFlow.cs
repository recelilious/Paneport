// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using Paneport.Steam.Session;

namespace Paneport.Steam.SignIn;

// One sign-in (steam-service.md section 5, C-0007 section 3): with an account name and a password,
// confirmed by an approval in the Steam mobile app or by a Steam Guard code, or with a QR code. It
// connects, runs Steam's authentication through the authenticator port, logs the session on with
// the new refresh token and saves it. A code that Steam refuses is asked for again; a wait for an
// approval that ends without one, and the user's own choice, fall back to a code; a new QR
// challenge is shown at once. The password is dropped when the attempt ends, whatever the end, and
// is never logged.
internal sealed class SignInFlow : ISignInAttempt, ICodeSource, IDisposable
{
    private const string LogSource = "SteamSignIn";

    private readonly SteamSession _session;
    private readonly ISteamAuthenticator _authenticator;
    private readonly TokenKeeper _tokens;
    private readonly TimeProvider _time;
    private readonly RedactingLog _log;
    private readonly bool _withQrCode;
    private readonly CancellationTokenSource _cancel = new();
    private readonly Lock _lock = new();
    private readonly SignInConfirmation _confirmation;

    private string? _accountName;
    private string? _password;
    private AuthTokens? _registeredTokens;
    private SignInProgress _progress = new(SignInStage.Connecting);
    private TaskCompletionSource<string>? _codeRequest;
    private CancellationTokenSource? _round;
    private bool _acceptApproval;
    private bool _switchToCode;
    private long _userWaitStarted;
    private TimeSpan _waitedForUser;
    private Task<SignInResult>? _completion;

    private SignInFlow(
        SteamSession session,
        ISteamAuthenticator authenticator,
        TokenKeeper tokens,
        TimeProvider time,
        RedactingLog log,
        bool withQrCode,
        string? accountName,
        string? password,
        SignInConfirmation confirmation)
    {
        _session = session;
        _authenticator = authenticator;
        _tokens = tokens;
        _time = time;
        _log = log;
        _withQrCode = withQrCode;
        _accountName = accountName;
        _password = password;
        _confirmation = confirmation;

        // The attempt's own registrations, which it takes out when it drops the values.
        _log.Protect(accountName);
        _log.Protect(password);
    }

    public event EventHandler? ProgressChanged;

    public SignInProgress Progress
    {
        get
        {
            lock (_lock)
            {
                return _progress;
            }
        }
    }

    public Task<SignInResult> Completion => _completion ?? throw new InvalidOperationException("The sign-in has not started.");

    // Whether the attempt still holds the password; false once it has ended.
    internal bool HoldsPassword => Volatile.Read(ref _password) is not null;

    public static SignInFlow StartWithPassword(
        SteamSession session,
        ISteamAuthenticator authenticator,
        TokenKeeper tokens,
        TimeProvider time,
        RedactingLog log,
        string accountName,
        string password,
        SignInConfirmation confirmation)
    {
        ArgumentException.ThrowIfNullOrEmpty(accountName);
        ArgumentException.ThrowIfNullOrEmpty(password);
        var flow = new SignInFlow(session, authenticator, tokens, time, log, withQrCode: false, accountName, password, confirmation);
        flow.Start();
        return flow;
    }

    public static SignInFlow StartWithQrCode(
        SteamSession session,
        ISteamAuthenticator authenticator,
        TokenKeeper tokens,
        TimeProvider time,
        RedactingLog log)
    {
        var flow = new SignInFlow(session, authenticator, tokens, time, log, withQrCode: true, null, null, SignInConfirmation.ApproveInSteamApp);
        flow.Start();
        return flow;
    }

    public void SubmitCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        var trimmed = code.Trim();
        TaskCompletionSource<string>? request;
        lock (_lock)
        {
            if (_progress.Stage != SignInStage.WaitingForCode || trimmed.Length == 0)
            {
                return;
            }

            request = _codeRequest;
            _codeRequest = null;
        }

        StopUserWait();
        SetProgress(new SignInProgress(SignInStage.Starting));
        request?.TrySetResult(trimmed);
    }

    public void UseCodeInstead()
    {
        CancellationTokenSource? round;
        lock (_lock)
        {
            if (_progress.Stage != SignInStage.WaitingForApproval)
            {
                return;
            }

            _switchToCode = true;
            round = _round;
        }

        try
        {
            round?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The round ended after it was read above.
        }
    }

    // A no-op once the attempt has ended: the service disposes an attempt that another sign-in or a
    // sign-out replaced once it has ended, and a page may still hold it.
    public void Cancel()
    {
        TaskCompletionSource<string>? request;
        lock (_lock)
        {
            if (_progress.HasEnded)
            {
                return;
            }

            request = _codeRequest;
            _codeRequest = null;
        }

        try
        {
            _cancel.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The attempt ended and was disposed after the check above.
        }

        request?.TrySetCanceled();
    }

    public void Dispose() => _cancel.Dispose();

    Task<bool> ICodeSource.AcceptApprovalAsync()
    {
        bool accept;
        lock (_lock)
        {
            accept = _acceptApproval;
        }

        if (accept)
        {
            StartUserWait();
            SetProgress(new SignInProgress(SignInStage.WaitingForApproval));
        }

        return Task.FromResult(accept);
    }

    Task<string> ICodeSource.GetCodeAsync(SteamCodeKind kind, bool previousWasWrong)
    {
        var request = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenSource? round;
        lock (_lock)
        {
            _codeRequest = request;
            round = _round;
        }

        // SteamKit2 asks without a cancellation token, so the round's token ends the question.
        _ = round?.Token.Register(() => request.TrySetCanceled());
        if (_cancel.IsCancellationRequested)
        {
            request.TrySetCanceled();
        }

        StartUserWait();
        SetProgress(new SignInProgress(SignInStage.WaitingForCode, kind, previousWasWrong));
        return request.Task;
    }

    private void Start()
    {
        _session.BeginSignIn();
        _completion = RunAsync();
    }

    private async Task<SignInResult> RunAsync()
    {
        var started = _time.GetTimestamp();
        var token = _cancel.Token;
        SavedSignIn? loggingOnWith = null;

        // Whether the session holds a logon with the attempt's tokens that no saved sign-in covers,
        // as when the save failed: the tokens then keep the attempt's registration with the log
        // filter, which is otherwise taken out when the attempt ends.
        var keepTokens = false;
        try
        {
            SetProgress(new SignInProgress(SignInStage.Connecting));
            await _session.ConnectAsync(token).ConfigureAwait(false);
            SetProgress(new SignInProgress(SignInStage.Starting));
            var tokens = _withQrCode
                ? await SignInWithQrCodeAsync(token).ConfigureAwait(false)
                : await SignInWithPasswordAsync(token).ConfigureAwait(false);
            StopUserWait();

            // The tokens are registered before the account name that the attempt began with is
            // taken out, so that the name is never out of the filter in between.
            RegisterTokens(tokens);
            DropPassword();

            SetProgress(new SignInProgress(SignInStage.LoggingOn));
            var signIn = new SavedSignIn(tokens.AccountName, tokens.RefreshToken);
            loggingOnWith = signIn;
            var result = await _session.LogOnAsync(signIn, token).ConfigureAwait(false);
            if (result != SteamResult.OK)
            {
                return End(started, SignInStage.Failed, SteamResults.DescribeLogOnFailure(result));
            }

            // A cancellation, such as a sign-out, can land just after the logon. The save waits for
            // the store with the attempt's token, so a cancelled attempt never writes its token
            // after the sign-out deleted the saved sign-in.
            keepTokens = true;
            await _tokens.SaveAsync(signIn, token).ConfigureAwait(false);

            // The saved sign-in has the store's own registration from now on.
            keepTokens = false;
            _session.SetSignInSaved(true);
            return End(started, SignInStage.LoggedOn, problem: null);
        }
        catch (OperationCanceledException) when (_cancel.IsCancellationRequested)
        {
            if (loggingOnWith is not null)
            {
                // The logon may be on its way or already there: the session gives it up.
                _session.AbandonLogOn(loggingOnWith);
            }

            keepTokens = false;
            return End(started, SignInStage.Cancelled, problem: null);
        }
        catch (SteamAuthException exception)
        {
            return End(started, SignInStage.Failed, Explain(exception.Result));
        }
        catch (SteamStepException exception)
        {
            return End(started, SignInStage.Failed, exception.Message);
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Error, LogSource, $"The sign-in failed with {exception.GetType().Name}.");
            return End(started, SignInStage.Failed, $"The sign-in failed ({exception.GetType().Name}).");
        }
        finally
        {
            DropPassword();
            if (!keepTokens)
            {
                DropTokens();
            }

            _session.EndSignIn();
        }
    }

    // Runs rounds of Steam's authentication until one gives tokens. The first round waits for an
    // approval when the user chose that; a round that the user switches to a code, or whose wait for
    // an approval fails, is followed by one that asks for a code.
    private async Task<AuthTokens> SignInWithPasswordAsync(CancellationToken cancellationToken)
    {
        var acceptApproval = _confirmation == SignInConfirmation.ApproveInSteamApp;
        var first = true;
        while (true)
        {
            if (!first)
            {
                // The round before may have ended with the connection, as when iOS closes the
                // socket while the user approves in the Steam mobile app; a code round needs one.
                await _session.ConnectAsync(cancellationToken).ConfigureAwait(false);
            }

            first = false;
            using var round = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            lock (_lock)
            {
                _round = round;
                _acceptApproval = acceptApproval;
                _switchToCode = false;
            }

            var accountName = _accountName ?? throw new InvalidOperationException("The account name is gone.");
            var password = _password ?? throw new InvalidOperationException("The password is gone.");
            try
            {
                var session = await _authenticator.BeginWithCredentialsAsync(accountName, password, this, round.Token).ConfigureAwait(false);
                return await session.WaitForTokensAsync(round.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested && SwitchRequested())
            {
                _log.Write(LogLevel.Information, LogSource, "The user chose to enter a code instead of approving.");
            }
            catch (SteamAuthException exception) when (!cancellationToken.IsCancellationRequested && acceptApproval && Progress.Stage == SignInStage.WaitingForApproval)
            {
                _log.Write(LogLevel.Warning, LogSource, $"The approval did not come ({SteamResults.Describe(exception.Result)}), so a code is asked for instead.");
            }
            finally
            {
                lock (_lock)
                {
                    _round = null;
                }
            }

            StopUserWait();
            acceptApproval = false;
            SetProgress(new SignInProgress(SignInStage.Starting));
        }
    }

    private async Task<AuthTokens> SignInWithQrCodeAsync(CancellationToken cancellationToken)
    {
        var session = await _authenticator.BeginWithQrCodeAsync(cancellationToken).ConfigureAwait(false);
        var revision = 0;
        EventHandler showChallenge = (_, _) =>
        {
            if (Progress.Stage == SignInStage.ShowingQrCode)
            {
                SetProgress(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: session.Challenge, QrRevision: Interlocked.Increment(ref revision)));
            }
        };

        session.ChallengeChanged += showChallenge;
        try
        {
            StartUserWait();
            SetProgress(new SignInProgress(SignInStage.ShowingQrCode, QrChallenge: session.Challenge, QrRevision: 0));
            return await session.WaitForTokensAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            session.ChallengeChanged -= showChallenge;
        }
    }

    private bool SwitchRequested()
    {
        lock (_lock)
        {
            return _switchToCode;
        }
    }

    private SignInResult End(long started, SignInStage stage, string? problem)
    {
        StopUserWait();
        var outcome = stage switch
        {
            SignInStage.LoggedOn => SignInOutcome.LoggedOn,
            SignInStage.Cancelled => SignInOutcome.Cancelled,
            _ => SignInOutcome.Failed,
        };
        TimeSpan waited;
        lock (_lock)
        {
            waited = _waitedForUser;
        }

        var result = new SignInResult(outcome, _time.GetElapsedTime(started), waited, problem);
        _log.Write(
            outcome == SignInOutcome.Failed ? LogLevel.Warning : LogLevel.Information,
            LogSource,
            problem is null ? $"The sign-in ended: {outcome}." : $"The sign-in ended: {outcome}. {problem}");
        SetProgress(new SignInProgress(stage, Problem: problem));
        return result;
    }

    // Drops the password and the account name that the attempt began with, and takes them out of the
    // log filter, which would otherwise keep them for the service's life.
    private void DropPassword()
    {
        if (Interlocked.Exchange(ref _password, null) is { } password)
        {
            _log.Unprotect(password);
        }

        if (Interlocked.Exchange(ref _accountName, null) is { } accountName)
        {
            _log.Unprotect(accountName);
        }
    }

    private void RegisterTokens(AuthTokens tokens)
    {
        _log.Protect(tokens.AccountName);
        _log.Protect(tokens.RefreshToken);
        Volatile.Write(ref _registeredTokens, tokens);
    }

    // Takes the tokens' registration out when the attempt ends: a saved sign-in has the store's.
    private void DropTokens()
    {
        if (Interlocked.Exchange(ref _registeredTokens, null) is { } tokens)
        {
            _log.Unprotect(tokens.AccountName);
            _log.Unprotect(tokens.RefreshToken);
        }
    }

    private void StartUserWait()
    {
        lock (_lock)
        {
            if (_userWaitStarted == 0)
            {
                _userWaitStarted = _time.GetTimestamp();
            }
        }
    }

    private void StopUserWait()
    {
        lock (_lock)
        {
            if (_userWaitStarted != 0)
            {
                _waitedForUser += _time.GetElapsedTime(_userWaitStarted);
                _userWaitStarted = 0;
            }
        }
    }

    private void SetProgress(SignInProgress progress)
    {
        lock (_lock)
        {
            if (_progress.HasEnded || _progress == progress)
            {
                return;
            }

            _progress = progress;
        }

        ProgressChanged?.Invoke(this, EventArgs.Empty);
    }

    // What the page says about a sign-in that Steam refused. None of it names a secret.
    private static string Explain(SteamResult result) => result switch
    {
        SteamResult.InvalidPassword => "The account name or the password is wrong.",
        SteamResult.RateLimitExceeded or SteamResult.AccountLoginDeniedThrottle =>
            "Steam refused the sign-in after too many attempts. Wait a while, then try again.",
        SteamResult.Expired or SteamResult.FileNotFound =>
            "The sign-in took too long and expired. Start it again.",
        SteamResult.TwoFactorCodeMismatch or SteamResult.InvalidLoginAuthCode =>
            "Steam refused the code.",
        SteamResult.NoConnection =>
            "The connection to Steam ended during the sign-in. Start it again.",
        SteamResult.Fail =>
            "Steam could not complete the sign-in. Start it again.",
        _ => $"Steam refused the sign-in: {SteamResults.Describe(result)}.",
    };
}
