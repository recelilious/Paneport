// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Net;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Logging;
using Paneport.Steam.Ports;
using SteamKit2;
using SteamKit2.Discovery;

namespace Paneport.Steam.SteamKit;

// The connection port over SteamKit2's SteamClient (S2.3.1, steam-service.md section 3). It owns
// the client and runs its callback loop on a managed worker thread of its own (C-0004 section 1),
// which reports each callback to the session. It picks each server itself, so that it knows the
// transport, and keeps the servers that Steam asked it to leave out of its next picks.
internal sealed class SteamKitConnection : ISteamConnection, IDisposable
{
    private const string LogSource = "SteamConnection";

    private static readonly TimeSpan s_callbackWait = TimeSpan.FromMilliseconds(250);

    private readonly SteamClient _client;
    private readonly CallbackManager _callbacks;
    private readonly SteamUser _user;
    private readonly string _machineName;
    private readonly RedactingLog _log;
    private readonly Thread _thread;
    private readonly Lock _lock = new();
    private readonly List<EndPoint> _avoided = [];
    private IConnectionListener? _listener;
    private ServerRecord? _server;
    private SteamTransport _transport;
    private volatile bool _stopping;

    public SteamKitConnection(SteamConfiguration configuration, string machineName, uint loginId, RedactingLog log)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentNullException.ThrowIfNull(log);
        _machineName = machineName;
        LoginId = loginId;
        _log = log;
        _client = new SteamClient(configuration, "Paneport");
        _callbacks = new CallbackManager(_client);
        _user = _client.GetRequiredHandler<SteamUser>();
        _callbacks.Subscribe<SteamClient.ConnectedCallback>(_ => Listener?.OnConnected());
        _callbacks.Subscribe<SteamClient.DisconnectedCallback>(callback => Listener?.OnDisconnected(callback.UserInitiated));
        _callbacks.Subscribe<SteamUser.LoggedOnCallback>(OnLoggedOn);
        _callbacks.Subscribe<SteamUser.LoggedOffCallback>(callback => Listener?.OnLoggedOff(Convert(callback.Result)));
        _callbacks.Subscribe<SteamApps.LicenseListCallback>(callback =>
            Listener?.OnLicenseList(Convert(callback.Result), [.. callback.LicenseList.Select(license => license.PackageID)]));
        _thread = new Thread(RunCallbacks) { IsBackground = true, Name = "SteamKit2 callbacks" };
        _thread.Start();
    }

    public SteamClient Client => _client;

    public bool IsConnected => _client.IsConnected;

    // The login ID that every logon sends (steam-service.md section 5).
    public uint LoginId { get; }

    public SteamTransport Transport
    {
        get
        {
            lock (_lock)
            {
                return _transport;
            }
        }
    }

    private IConnectionListener? Listener => Volatile.Read(ref _listener);

    public void Attach(IConnectionListener listener) => Volatile.Write(ref _listener, listener);

    // Picks a server without blocking the caller, which may be the UI thread: the first pick can
    // fetch the server list from Steam's directory.
    public void Connect() => _ = ConnectAsync();

    public void Disconnect() => _client.Disconnect();

    public void AvoidCurrentServer()
    {
        lock (_lock)
        {
            if (_server is { } server)
            {
                AvoidUnderLock(server.EndPoint);
            }
        }
    }

    // Avoids a server in the next picks, until Steam accepts a logon.
    internal void Avoid(EndPoint endPoint)
    {
        ArgumentNullException.ThrowIfNull(endPoint);
        lock (_lock)
        {
            AvoidUnderLock(endPoint);
        }
    }

    // How many servers the next picks avoid; for the tests.
    internal int AvoidedServers
    {
        get
        {
            lock (_lock)
            {
                return _avoided.Count;
            }
        }
    }

    public void LogOn(string accountName, Secret refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        _user.LogOn(LogOnDetails(accountName, refreshToken, _machineName, LoginId));
    }

    // What a logon sends. The machine name is fixed text, never the device's name (L2 brief, M2.3).
    // The login ID is set, because without one SteamKit2 sends the device's private IP address on
    // TCP, merely XORed with a fixed mask, and the same value from every WebSocket client.
    internal static SteamUser.LogOnDetails LogOnDetails(string accountName, Secret refreshToken, string machineName, uint loginId)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        return new SteamUser.LogOnDetails
        {
            Username = accountName,
            AccessToken = refreshToken.Reveal(),
            ShouldRememberPassword = true,
            MachineName = machineName,
            LoginID = loginId,
        };
    }

    public void LogOff() => _user.LogOff();

    public void Dispose()
    {
        _stopping = true;
        _client.Disconnect();
        _thread.Join(TimeSpan.FromSeconds(2));
    }

    private static SteamResult Convert(EResult result) => (SteamResult)(int)result;

    private async Task ConnectAsync()
    {
        try
        {
            var server = await PickServerAsync().ConfigureAwait(false);
            if (server is null)
            {
                _log.Write(LogLevel.Warning, LogSource, "Steam's server list is empty.");
                Listener?.OnDisconnected(userInitiated: false);
                return;
            }

            lock (_lock)
            {
                _server = server;
                _transport = server.ProtocolTypes.HasFlag(ProtocolTypes.WebSocket) ? SteamTransport.WebSocket : SteamTransport.Tcp;
            }

            _client.Connect(server);
        }
        catch (Exception exception)
        {
            _log.Write(LogLevel.Warning, LogSource, $"Finding a server failed with {exception.GetType().Name}.");
            Listener?.OnDisconnected(userInitiated: false);
        }
    }

    // SteamKit2's own pick, which skips the servers that it marked bad, unless that is a server Steam
    // asked to leave; then the first other server of the list.
    private async Task<ServerRecord?> PickServerAsync()
    {
        var protocols = _client.Configuration.ProtocolTypes;
        var candidate = await _client.Servers.GetNextServerCandidateAsync(protocols).ConfigureAwait(false);
        EndPoint[] avoided;
        lock (_lock)
        {
            avoided = [.. _avoided];
        }

        return PickAvoiding(candidate, _client.Servers.GetAllEndPoints(), avoided, protocols);
    }

    // The candidate, unless its endpoint is one to avoid; then the first record of the list with one
    // of the protocols and an endpoint not to avoid, or the candidate when there is none.
    internal static ServerRecord? PickAvoiding(ServerRecord? candidate, IEnumerable<ServerRecord> all, IReadOnlyCollection<EndPoint> avoided, ProtocolTypes protocols)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(avoided);
        if (candidate is null || !avoided.Contains(candidate.EndPoint))
        {
            return candidate;
        }

        return all.FirstOrDefault(record => (record.ProtocolTypes & protocols) != 0 && !avoided.Contains(record.EndPoint)) ?? candidate;
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        // The public IP address that Steam reports is never read.
        if (callback.ClientSteamID is { } steamId)
        {
            ProtectSteamId(_log, steamId);
        }

        if (callback.Result == EResult.OK)
        {
            // The servers that Steam asked to leave were for this logon; later picks are SteamKit2's.
            lock (_lock)
            {
                _avoided.Clear();
            }
        }

        Listener?.OnLoggedOn(Convert(callback.Result));
    }

    // By endpoint only: SteamKit2 gives the pick with the chosen protocol alone, and its list the
    // same server with all of its protocols. Called under the lock.
    private void AvoidUnderLock(EndPoint endPoint)
    {
        if (!_avoided.Contains(endPoint))
        {
            _avoided.Add(endPoint);
        }
    }

    // Registers the account's Steam ID with the log filter in the forms a line could hold it: the
    // 64-bit number, the [U:1:N] text and the bare account number, which no shape rule removes.
    internal static void ProtectSteamId(RedactingLog log, SteamID steamId)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(steamId);
        if (!steamId.IsValid)
        {
            return;
        }

        log.Protect(steamId.ConvertToUInt64().ToString(CultureInfo.InvariantCulture));
        log.Protect(steamId.Render());
        log.Protect(steamId.AccountID.ToString(CultureInfo.InvariantCulture));
    }

    private void RunCallbacks()
    {
        while (!_stopping)
        {
            try
            {
                _callbacks.RunWaitCallbacks(s_callbackWait);
            }
            catch (Exception exception)
            {
                _log.Write(LogLevel.Error, LogSource, $"A SteamKit2 callback failed with {exception.GetType().Name}.");
            }
        }
    }
}
