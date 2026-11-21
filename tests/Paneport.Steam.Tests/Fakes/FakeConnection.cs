// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Security;
using Paneport.Core.Steam;
using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// A connection that records each request and answers none by itself: a test plays Steam's part
// through the attached listener, as SteamKit2's callbacks would.
internal sealed class FakeConnection : ISteamConnection
{
    public const string Connect = "connect";
    public const string Disconnect = "disconnect";
    public const string AvoidServer = "avoid server";
    public const string LogOn = "log on";
    public const string LogOff = "log off";

    private readonly Lock _lock = new();
    private readonly List<string> _requests = [];
    private IConnectionListener? _listener;
    private (string AccountName, string RefreshToken)? _lastLogOn;

    public SteamTransport Transport { get; set; } = SteamTransport.WebSocket;

    public IConnectionListener Listener => _listener ?? throw new InvalidOperationException("No listener is attached.");

    public IReadOnlyList<string> Requests
    {
        get
        {
            lock (_lock)
            {
                return [.. _requests];
            }
        }
    }

    public (string AccountName, string RefreshToken)? LastLogOn
    {
        get
        {
            lock (_lock)
            {
                return _lastLogOn;
            }
        }
    }

    public int Count(string request) => Requests.Count(r => r == request);

    public void Attach(IConnectionListener listener) => _listener = listener;

    void ISteamConnection.Connect() => Add(Connect);

    void ISteamConnection.Disconnect() => Add(Disconnect);

    void ISteamConnection.AvoidCurrentServer() => Add(AvoidServer);

    void ISteamConnection.LogOn(string accountName, Secret refreshToken)
    {
        lock (_lock)
        {
            _lastLogOn = (accountName, refreshToken.Reveal());
        }

        Add(LogOn);
    }

    void ISteamConnection.LogOff() => Add(LogOff);

    private void Add(string request)
    {
        lock (_lock)
        {
            _requests.Add(request);
        }
    }
}
