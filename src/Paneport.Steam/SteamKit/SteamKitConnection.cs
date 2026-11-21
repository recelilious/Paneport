// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
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
    private readonly List<ServerRecord> _avoided = [];
    private IConnectionListener? _listener;
    private ServerRecord? _server;
    private SteamTransport _transport;
    private volatile bool _stopping;

    public SteamKitConnection(SteamConfiguration configuration, string machineName, RedactingLog log)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineName);
        ArgumentNullException.ThrowIfNull(log);
        _machineName = machineName;
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
            if (_server is { } server && !_avoided.Contains(server))
            {
                _avoided.Add(server);
            }
        }
    }

    public void LogOn(string accountName, Secret refreshToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        _user.LogOn(new SteamUser.LogOnDetails
        {
            Username = accountName,
            AccessToken = refreshToken.Reveal(),
            ShouldRememberPassword = true,

            // Fixed text, never the device's name (L2 brief, M2.3).
            MachineName = _machineName,
        });
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

    private async Task<ServerRecord?> PickServerAsync()
    {
        var protocols = _client.Configuration.ProtocolTypes;
        ServerRecord[] avoided;
        lock (_lock)
        {
            avoided = [.. _avoided];
        }

        if (avoided.Length > 0)
        {
            var other = _client.Servers.GetAllEndPoints()
                .FirstOrDefault(record => (record.ProtocolTypes & protocols) != 0 && !avoided.Contains(record));
            if (other is not null)
            {
                return other;
            }
        }

        return await _client.Servers.GetNextServerCandidateAsync(protocols).ConfigureAwait(false);
    }

    private void OnLoggedOn(SteamUser.LoggedOnCallback callback)
    {
        // The account's Steam ID in its three forms, so that no line can hold it. The public IP
        // address that Steam reports is never read.
        if (callback.ClientSteamID is { } steamId && steamId.IsValid)
        {
            _log.Protect(steamId.ConvertToUInt64().ToString(CultureInfo.InvariantCulture));
            _log.Protect(steamId.Render());
            _log.Protect(steamId.AccountID.ToString(CultureInfo.InvariantCulture));
        }

        Listener?.OnLoggedOn(Convert(callback.Result));
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
