// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Steam.Apps;
using Paneport.Steam.Ports;
using SteamKit2;

namespace Paneport.Steam.SteamKit;

// The app information port over SteamKit2's SteamApps (S2.3.4 and S2.3.5): PICS access tokens,
// product information, which it copies into the service's own key-value tree, and depot keys.
internal sealed class SteamKitApps(SteamClient client) : ISteamApps
{
    private readonly SteamApps _apps = client.GetRequiredHandler<SteamApps>();

    public async Task<ulong> GetAppAccessTokenAsync(uint appId, CancellationToken cancellationToken)
    {
        var tokens = await _apps.PICSGetAccessTokens(appId, null).ToTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        return tokens.AppTokens.TryGetValue(appId, out var token) ? token : 0;
    }

    public async Task<KeyValueNode?> GetAppInfoAsync(uint appId, ulong accessToken, CancellationToken cancellationToken)
    {
        var results = await _apps.PICSGetProductInfo(new SteamApps.PICSRequest(appId, accessToken), null)
            .ToTask()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        foreach (var callback in results.Results ?? [])
        {
            if (callback.Apps.TryGetValue(appId, out var info) && info.KeyValues is { } keyValues)
            {
                return Copy(keyValues);
            }
        }

        return null;
    }

    public async Task<DepotKeyResult> GetDepotKeyAsync(uint depotId, uint appId, CancellationToken cancellationToken)
    {
        var callback = await _apps.GetDepotDecryptionKey(depotId, appId).ToTask().WaitAsync(cancellationToken).ConfigureAwait(false);
        return new DepotKeyResult((SteamResult)(int)callback.Result, callback.DepotKey);
    }

    // A node with children keeps them; any other node keeps its value.
    private static KeyValueNode Copy(KeyValue node) =>
        node.Children.Count > 0
            ? new KeyValueNode(node.Name ?? string.Empty, [.. node.Children.Select(Copy)])
            : new KeyValueNode(node.Name ?? string.Empty, node.Value);
}
