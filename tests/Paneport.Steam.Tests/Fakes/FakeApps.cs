// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Apps;
using Paneport.Steam.Ports;

namespace Paneport.Steam.Tests.Fakes;

// App information and depot keys that a test sets up.
internal sealed class FakeApps : ISteamApps
{
    public ulong AccessToken { get; set; } = 7;

    public Dictionary<uint, KeyValueNode> Infos { get; } = [];

    public DepotKeyResult DepotKey { get; set; } = new(SteamResult.AccessDenied, null);

    public ulong? AccessTokenAsked { get; private set; }

    public Task<ulong> GetAppAccessTokenAsync(uint appId, CancellationToken cancellationToken) => Task.FromResult(AccessToken);

    public Task<KeyValueNode?> GetAppInfoAsync(uint appId, ulong accessToken, CancellationToken cancellationToken)
    {
        AccessTokenAsked = accessToken;
        return Task.FromResult(Infos.TryGetValue(appId, out var info) ? info : null);
    }

    public Task<DepotKeyResult> GetDepotKeyAsync(uint depotId, uint appId, CancellationToken cancellationToken) =>
        Task.FromResult(DepotKey);
}
