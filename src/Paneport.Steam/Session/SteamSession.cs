// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Ports;

namespace Paneport.Steam.Session;

// The session with Steam: the connection to a connection manager server, the logon with a
// refresh token, and keeping both up (steam-service.md section 4). After a connection ends or an
// attempt fails it connects again with backoff, and gives up at the policy's limit; when Steam
// asks, it moves to another server; after a return to the foreground it connects and logs on
// again, because iOS may have closed the socket of the suspended app; and it signs off on request
// without connecting again. It drops the saved sign-in when Steam refuses its token.
//
// Requests come from any thread and callbacks from the adapter's thread. Every change of state
// runs on the serial queue, so the fields below need no lock; Status is published for any thread.
internal sealed class SteamSession : IConnectionListener
{
    private const string LogSource = "SteamSession";

    private readonly ISteamConnection _connection;
    private readonly TokenKeeper _tokens;
    private readonly IDelays _delays;
    private readonly ReconnectPolicy _policy;
    private readonly ILog _log;
    private readonly SerialQueue _queue;
    private readonly List<TaskCompletionSource> _connectWaiters = [];
    private readonly List<TaskCompletionSource<SteamResult>> _logOnWaiters = [];
    private readonly List<TaskCompletionSource> _signOffWaiters = [];

    // The state, touched only on the queue.
    private Goal _goal;
    private SavedSignIn? _signIn;
    private int _signIns;
    private bool _connecting;
    private bool _connected;
    private bool _loggingOn;
    private bool _loggedOn;
    private bool _reconnecting;
    private bool _offline;
    private bool _signingOut;
    private bool _reconnectOnDisconnect;
    private TimeSpan? _restartDelay;
    private bool _disconnectWhenLoggedOff;
    private int _failedAttempts;
    private int _logOffs;
    private int _serverSwitches;
    private bool? _signInSaved;
    private string? _problem;
    private CancellationTokenSource? _wait;

    // Published for any thread.
    private SteamSessionStatus _status = SteamSessionStatus.Initial;
    private TaskCompletionSource<IReadOnlyList<uint>> _licenses = NewLicenses();

    public SteamSession(ISteamConnection connection, TokenKeeper tokens, IDelays delays, ReconnectPolicy policy, ILog log)
    {
        ArgumentNullException.ThrowIfNull(connection);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(delays);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(log);
        _connection = connection;
        _tokens = tokens;
        _delays = delays;
        _policy = policy;
        _log = log;
        _queue = new SerialQueue(exception =>
            _log.Write(LogLevel.Error, LogSource, $"A session step failed with {exception.GetType().Name}."));
        connection.Attach(this);
    }

    public event EventHandler? StatusChanged;

    // What the session works towards.
    private enum Goal
    {
        // Nothing: the connection may end.
        None,

        // A connection, for a sign-in that is running.
        Connected,

        // A logon with _signIn, kept up until sign-off.
        LoggedOn,
    }

    public SteamSessionStatus Status => Volatile.Read(ref _status);

    public bool IsLoggedOn => Status.State == SteamSessionState.LoggedOn;

    // Connects for a sign-in, which needs a connected client. Completes when connected, and fails
    // with SteamStepException when the policy gives up.
    public Task ConnectAsync(CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Post(() =>
        {
            // SteamKit2 marks the client as not connected before the drop reaches this queue, so a
            // connection counts only while the client still has it. Otherwise the waiter waits: the
            // drop that SteamKit2 has posted starts the reconnect, and the next connection answers it.
            if (_connected && _connection.IsConnected)
            {
                waiter.TrySetResult();
                return;
            }

            if (_connected)
            {
                _log.Write(LogLevel.Information, LogSource, "The client is no longer connected, so the sign-in waits for the next connection.");
            }

            _connectWaiters.Add(waiter);
            if (_goal == Goal.None)
            {
                _goal = Goal.Connected;
            }

            _offline = false;
            ConnectIfIdle();
            Publish();
        });
        return waiter.Task.WaitAsync(cancellationToken);
    }

    // Logs on with a sign-in and keeps the logon up. Completes with Steam's result: OK, the result
    // that ended the logon, NoConnection when the policy gave up, or Cancelled when another sign-in
    // or a sign-off replaced it. A logon with another sign-in than the current one, logged on or
    // still on its way, connects again first, so that the other one's result cannot answer it.
    public Task<SteamResult> LogOnAsync(SavedSignIn signIn, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        var waiter = new TaskCompletionSource<SteamResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Post(() =>
        {
            var same = TokenKeeper.Same(_signIn, signIn);
            if (_loggedOn && same)
            {
                waiter.TrySetResult(SteamResult.OK);
                return;
            }

            var switching = _loggedOn || (_loggingOn && !same);
            if (switching)
            {
                CompleteLogOns(SteamResult.Cancelled);
            }

            _goal = Goal.LoggedOn;
            _signIn = signIn;
            _offline = false;
            _problem = null;
            _failedAttempts = 0;
            _logOffs = 0;
            _serverSwitches = 0;
            _logOnWaiters.Add(waiter);
            if (switching)
            {
                Restart();
            }
            else if (_connected)
            {
                if (!_loggingOn)
                {
                    SendLogOn();
                }
            }
            else
            {
                ConnectIfIdle();
            }

            Publish();
        });
        return waiter.Task.WaitAsync(cancellationToken);
    }

    // Signs off: logs off, disconnects and does not connect again. The saved sign-in stays; the
    // service deletes it at sign-out, and keeps it when it signs off for a Dock session (M8.2).
    public Task SignOffAsync(CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _queue.Post(() =>
        {
            _goal = Goal.None;
            _signIn = null;
            _reconnecting = false;
            _offline = false;
            _restartDelay = null;
            _logOffs = 0;
            CancelWait();
            EndWaiters(SteamResult.Cancelled, new OperationCanceledException("The session signed off."));
            if (!_connected && !_connecting)
            {
                waiter.TrySetResult();
                Publish();
                return;
            }

            _signingOut = true;
            _signOffWaiters.Add(waiter);
            if (_loggedOn)
            {
                // The logoff goes out before the connection ends; the timeout ends the wait for its
                // confirmation.
                _disconnectWhenLoggedOff = true;
                _connection.LogOff();
                StartLogOffTimeout();
            }
            else
            {
                _connection.Disconnect();
            }

            Publish();
        });
        return waiter.Task.WaitAsync(cancellationToken);
    }

    // The app returned to the foreground. A session that should be logged on connects and logs on
    // again, with a fresh count of attempts.
    public void OnForegroundReturned() => _queue.Post(() =>
    {
        if (_goal != Goal.LoggedOn || _signingOut)
        {
            return;
        }

        _log.Write(LogLevel.Information, LogSource, "The app returned to the foreground, so the session connects and logs on again.");
        _failedAttempts = 0;
        _logOffs = 0;
        _serverSwitches = 0;
        _offline = false;
        _reconnecting = true;
        Restart();
        Publish();
    });

    // Counts a sign-in as running, so that the session keeps a connection for it. Each call is
    // matched by one EndSignIn; a cancelled attempt that ends after the next one began leaves the
    // next one counted.
    public void BeginSignIn() => _queue.Post(() =>
    {
        _signIns++;
        _problem = null;
        if (_goal == Goal.None)
        {
            _goal = Goal.Connected;
        }

        Publish();
    });

    public void EndSignIn() => _queue.Post(() =>
    {
        _signIns--;
        if (_signIns == 0 && _goal == Goal.Connected)
        {
            // No sign-in needs the connection any more, so a wait to connect again waits for
            // nobody; left, it would hold off a sign-in that starts during it for up to 16 s.
            _goal = Goal.None;
            CancelWait();
        }

        Publish();
    });

    public void SetSignInSaved(bool saved) => _queue.Post(() =>
    {
        _signInSaved = saved;
        Publish();
    });

    // A sign-in that was cancelled after it asked for its logon: the session stops working towards
    // that logon, and logs off when the logon is there or comes later, so that no logon outlives
    // its cancelled attempt without a saved sign-in.
    public void AbandonLogOn(SavedSignIn signIn)
    {
        ArgumentNullException.ThrowIfNull(signIn);
        _queue.Post(() =>
        {
            if (!ReferenceEquals(_signIn, signIn))
            {
                return;
            }

            _signIn = null;
            _goal = _signIns > 0 ? Goal.Connected : Goal.None;
            _reconnecting = false;
            _offline = false;

            // A wait to connect again stays: the abandoning attempt still counts in _signIns until
            // its EndSignIn, and a sign-in that replaced it may need the wait, since its own connect
            // returned early because the wait was there. A pending wait connects only while a goal
            // remains, and EndSignIn cancels it once no sign-in is left.
            CompleteLogOns(SteamResult.Cancelled);
            if (_loggedOn && !_signingOut)
            {
                _log.Write(LogLevel.Information, LogSource, "The sign-in was cancelled after its logon, so the session logs off.");
                _connection.LogOff();
            }

            Publish();
        });
    }

    // The package IDs of the account's licenses, which Steam sends after each logon.
    public Task<IReadOnlyList<uint>> GetLicensesAsync(CancellationToken cancellationToken) =>
        Volatile.Read(ref _licenses).Task.WaitAsync(cancellationToken);

    void IConnectionListener.OnConnected() => _queue.Post(() =>
    {
        _connecting = false;
        _connected = true;
        _offline = false;
        if (_signingOut)
        {
            // The sign-off came while the adapter was still choosing a server, when a disconnect
            // could not reach a connection yet; the new connection ends at once.
            _connection.Disconnect();
            Publish();
            return;
        }

        // A restart that was asked for while connecting has this connection as its result.
        _reconnectOnDisconnect = false;
        _restartDelay = null;
        if (_goal != Goal.LoggedOn)
        {
            // A connection is what a sign-in needs. A logon counts as reached only when Steam
            // accepts it, so that a server that drops each connection before the logon's result
            // still ends at the policy's limit.
            _failedAttempts = 0;
        }

        foreach (var waiter in _connectWaiters)
        {
            waiter.TrySetResult();
        }

        _connectWaiters.Clear();
        if (_goal == Goal.LoggedOn && _signIn is not null && !_signingOut)
        {
            SendLogOn();
        }

        Publish();
    });

    void IConnectionListener.OnDisconnected(bool userInitiated) => _queue.Post(() =>
    {
        var wasLoggedOn = _loggedOn;
        _connected = false;
        _connecting = false;
        _loggingOn = false;
        _loggedOn = false;
        _disconnectWhenLoggedOff = false;
        if (wasLoggedOn)
        {
            ResetLicenses();
        }

        if (_signingOut)
        {
            _signingOut = false;
            foreach (var waiter in _signOffWaiters)
            {
                waiter.TrySetResult();
            }

            _signOffWaiters.Clear();

            // A sign-in or a code round that began during the sign-off, once SteamKit2 had dropped
            // the client, waits for a connection that the sign-off held back; it gets one now.
            if (_goal != Goal.None && _connectWaiters.Count > 0)
            {
                ConnectIfIdle();
            }

            Publish();
            return;
        }

        if (_reconnectOnDisconnect)
        {
            // An end that the session asked for itself, to move to another server or to log on
            // again, at once or after the wait that a logoff by Steam asks for.
            _reconnectOnDisconnect = false;
            var restartDelay = _restartDelay;
            _restartDelay = null;
            if (_goal != Goal.None)
            {
                if (restartDelay is { } wait)
                {
                    WaitThenConnect(wait);
                }
                else
                {
                    ConnectIfIdle();
                }
            }

            Publish();
            return;
        }

        if (_goal == Goal.None || _offline)
        {
            // Nothing to keep up, or the policy has already given up.
            Publish();
            return;
        }

        // The connection ended on its own, or an attempt failed.
        _reconnecting |= wasLoggedOn || _goal == Goal.LoggedOn;
        _failedAttempts++;
        if (_failedAttempts > _policy.MaxAttempts)
        {
            GiveUp();
            return;
        }

        var delay = _policy.DelayBefore(_failedAttempts);
        var how = userInitiated ? " at the client's request" : string.Empty;
        var next = string.Create(
            CultureInfo.InvariantCulture,
            $"attempt {_failedAttempts} of {_policy.MaxAttempts} follows in {delay.TotalSeconds:0.#} s");
        _log.Write(LogLevel.Warning, LogSource, $"The connection to Steam ended{how}; {next}.");
        WaitThenConnect(delay);
        Publish();
    });

    void IConnectionListener.OnLoggedOn(SteamResult result) => _queue.Post(() =>
    {
        _loggingOn = false;
        if (_reconnectOnDisconnect)
        {
            // The result of a logon on a connection that the session is ending, such as one that
            // another sign-in replaced; it must not answer the logon that follows.
            _log.Write(LogLevel.Information, LogSource, $"A logon result came on a connection that is ending ({SteamResults.Describe(result)}); it is ignored.");
            return;
        }

        if (result == SteamResult.NoConnection && !_connection.IsConnected)
        {
            // SteamKit2's answer to a logon on a client that is no longer connected: that is the
            // drop, whose own notice starts the reconnect, and not the logon's result.
            _log.Write(LogLevel.Information, LogSource, "The logon met a connection that had ended; the logon waits for the next one.");
            Publish();
            return;
        }

        if (result == SteamResult.OK)
        {
            if (_goal != Goal.LoggedOn || _signIn is null)
            {
                // The sign-in that this logon was for was abandoned or signed off on its way.
                if (!_signingOut)
                {
                    _log.Write(LogLevel.Information, LogSource, "A logon came for a sign-in that is no longer wanted, so the session logs off.");
                    _connection.LogOff();
                }

                Publish();
                return;
            }

            _loggedOn = true;
            _reconnecting = false;
            _offline = false;
            _problem = null;
            _failedAttempts = 0;
            _serverSwitches = 0;
            CompleteLogOns(SteamResult.OK);
            _log.Write(LogLevel.Information, LogSource, "Logged on.");
            Publish();
            return;
        }

        if (SteamResults.MeansTryAnotherServer(result) && _serverSwitches < _policy.MaxServerSwitches && _goal == Goal.LoggedOn)
        {
            _serverSwitches++;
            _log.Write(LogLevel.Warning, LogSource, $"Steam asked for another server ({SteamResults.Describe(result)}).");
            _connection.AvoidCurrentServer();
            Restart();
            Publish();
            return;
        }

        _problem = SteamResults.DescribeLogOnFailure(result);
        _log.Write(LogLevel.Warning, LogSource, _problem);
        if (SteamResults.MeansTokenInvalid(result) && _signIn is { } refused)
        {
            // Only the saved sign-in's own token is dropped: a fresh token that Steam refuses leaves
            // another sign-in in the store alone.
            _ = DropRefusedAsync(refused);
        }

        _goal = _signIns > 0 ? Goal.Connected : Goal.None;
        _signIn = null;
        _reconnecting = false;
        CompleteLogOns(result);
        Publish();
    });

    void IConnectionListener.OnLoggedOff(SteamResult result) => _queue.Post(() =>
    {
        var wasLoggedOn = _loggedOn;
        _loggedOn = false;
        if (wasLoggedOn)
        {
            ResetLicenses();
        }

        if (_disconnectWhenLoggedOff)
        {
            _disconnectWhenLoggedOff = false;
            _connection.Disconnect();
            Publish();
            return;
        }

        if (result is SteamResult.LoggedInElsewhere or SteamResult.LogonSessionReplaced)
        {
            // Another client took the account's session: Valve's client in a Dock session, or
            // another session with the same login ID. The session does not take it back
            // (C-0007 section 4), which would only replace the other one in turn, again and again.
            // The saved sign-in stays.
            _problem = result == SteamResult.LoggedInElsewhere
                ? "Steam logged this session off because the account logged on elsewhere."
                : "Steam logged this session off because another session of the account replaced it.";
            _log.Write(LogLevel.Warning, LogSource, _problem);
            _goal = Goal.None;
            _signIn = null;
            _reconnecting = false;
            _connection.Disconnect();
            Publish();
            return;
        }

        if (_goal == Goal.LoggedOn)
        {
            // Each logoff counts, with the policy's wait before the next logon, so that a session
            // that Steam logs off after every logon ends at the limit. Only a new sign-in, a resume
            // or a return to the foreground starts the count again.
            _reconnecting = true;
            _logOffs++;
            if (_logOffs > _policy.MaxAttempts)
            {
                GiveUp($"Steam kept logging the session off ({SteamResults.Describe(result)}), so Paneport stopped logging on again.");
                _connection.Disconnect();
                return;
            }

            var delay = _policy.DelayBefore(_logOffs);
            _log.Write(
                LogLevel.Warning,
                LogSource,
                string.Create(CultureInfo.InvariantCulture, $"Steam logged the session off ({SteamResults.Describe(result)}); logon {_logOffs} of {_policy.MaxAttempts} follows in {delay.TotalSeconds:0.#} s."));
            Restart(delay);
        }

        Publish();
    });

    void IConnectionListener.OnLicenseList(SteamResult result, IReadOnlyList<uint> packageIds) => _queue.Post(() =>
    {
        if (result != SteamResult.OK)
        {
            _log.Write(LogLevel.Warning, LogSource, $"The license list came with {SteamResults.Describe(result)}.");
            return;
        }

        var licenses = Volatile.Read(ref _licenses);
        if (!licenses.TrySetResult(packageIds))
        {
            var fresh = NewLicenses();
            fresh.TrySetResult(packageIds);
            Volatile.Write(ref _licenses, fresh);
        }
    });

    private static TaskCompletionSource<IReadOnlyList<uint>> NewLicenses() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private void ConnectIfIdle()
    {
        if (_connected || _connecting || _wait is not null || _signingOut)
        {
            return;
        }

        _connecting = true;
        _connection.Connect();
    }

    private void SendLogOn()
    {
        var signIn = _signIn ?? throw new InvalidOperationException("No sign-in to log on with.");
        if (!_connection.IsConnected)
        {
            // SteamKit2 would answer a logon on a client that is no longer connected with
            // NoConnection at once. The drop that it has seen is on its way here, and the logon goes
            // out on the next connection.
            _log.Write(LogLevel.Information, LogSource, "The client is no longer connected, so the logon waits for the next connection.");
            return;
        }

        _loggingOn = true;
        _connection.LogOn(signIn.AccountName, signIn.RefreshToken);
    }

    // Ends the current connection to connect again, at once or after a wait, or connects when there
    // is none.
    private void Restart(TimeSpan? delay = null)
    {
        CancelWait();
        if (_connected || _connecting)
        {
            _reconnectOnDisconnect = true;
            _restartDelay = delay;
            _connection.Disconnect();
        }
        else if (delay is { } wait)
        {
            WaitThenConnect(wait);
        }
        else
        {
            ConnectIfIdle();
        }
    }

    // Drops the saved sign-in when it is the one that Steam refused, and says so in the status.
    private async Task DropRefusedAsync(SavedSignIn refused)
    {
        if (await _tokens.DropAsync(refused).ConfigureAwait(false))
        {
            SetSignInSaved(false);
        }
    }

    private void WaitThenConnect(TimeSpan delay)
    {
        var wait = new CancellationTokenSource();
        _wait = wait;
        _ = ConnectAfterAsync(delay, wait);
    }

    // Connects once the wait is over, unless a newer request replaced or cancelled the wait.
    private async Task ConnectAfterAsync(TimeSpan delay, CancellationTokenSource wait)
    {
        try
        {
            await _delays.DelayAsync(delay, wait.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        _queue.Post(() =>
        {
            if (!ReferenceEquals(_wait, wait))
            {
                return;
            }

            _wait = null;
            wait.Dispose();
            if (_goal != Goal.None)
            {
                ConnectIfIdle();
            }

            Publish();
        });
    }

    private void StartLogOffTimeout() => _ = DisconnectAfterLogOffTimeoutAsync();

    // Disconnects when the logoff was not confirmed in time.
    private async Task DisconnectAfterLogOffTimeoutAsync()
    {
        await _delays.DelayAsync(_policy.LogOffTimeout, CancellationToken.None).ConfigureAwait(false);
        _queue.Post(() =>
        {
            if (_disconnectWhenLoggedOff)
            {
                _disconnectWhenLoggedOff = false;
                _connection.Disconnect();
            }
        });
    }

    private void CancelWait()
    {
        if (_wait is { } wait)
        {
            _wait = null;
            wait.Cancel();
            wait.Dispose();
        }
    }

    private void GiveUp(string? problem = null)
    {
        _problem = problem ?? string.Create(
            CultureInfo.InvariantCulture,
            $"Paneport could not reach Steam after {_policy.MaxAttempts} attempts.");
        _log.Write(LogLevel.Warning, LogSource, _problem);
        _failedAttempts = 0;
        _logOffs = 0;
        _restartDelay = null;
        if (_goal == Goal.LoggedOn)
        {
            // The goal stays, so that a return to the foreground or a resume tries again.
            _offline = true;
        }
        else
        {
            _goal = Goal.None;
        }

        EndWaiters(SteamResult.NoConnection, new SteamStepException(_problem));
        Publish();
    }

    private void CompleteLogOns(SteamResult result)
    {
        foreach (var waiter in _logOnWaiters)
        {
            waiter.TrySetResult(result);
        }

        _logOnWaiters.Clear();
    }

    private void EndWaiters(SteamResult logOnResult, Exception connectFailure)
    {
        foreach (var waiter in _connectWaiters)
        {
            waiter.TrySetException(connectFailure);
        }

        _connectWaiters.Clear();
        CompleteLogOns(logOnResult);
    }

    private void ResetLicenses()
    {
        if (Volatile.Read(ref _licenses).Task.IsCompleted)
        {
            Volatile.Write(ref _licenses, NewLicenses());
        }
    }

    private SteamSessionState CurrentState()
    {
        if (_signingOut)
        {
            return SteamSessionState.SigningOut;
        }

        if (_loggedOn)
        {
            return SteamSessionState.LoggedOn;
        }

        if (_offline)
        {
            return SteamSessionState.Offline;
        }

        var busy = _connecting || _connected || _loggingOn || _wait is not null;
        if (_reconnecting && _goal == Goal.LoggedOn && busy)
        {
            return SteamSessionState.Reconnecting;
        }

        if (_loggingOn)
        {
            return SteamSessionState.LoggingOn;
        }

        if (_signIns > 0)
        {
            return SteamSessionState.SigningIn;
        }

        return (_connecting || _wait is not null) && _goal != Goal.None
            ? SteamSessionState.Connecting
            : SteamSessionState.SignedOut;
    }

    // Publishes the status after a change, and raises StatusChanged when it differs.
    private void Publish()
    {
        var transport = _connected || _loggedOn ? _connection.Transport : Volatile.Read(ref _status).Transport;
        var status = new SteamSessionStatus(CurrentState(), _signInSaved, transport, _problem);
        var previous = Volatile.Read(ref _status);
        if (status == previous)
        {
            return;
        }

        Volatile.Write(ref _status, status);
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
