// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// Steam's answer to a depot key request: the result, and the key when it is OK. The key is
// secret; ToString never shows it.
internal sealed class DepotKeyResult
{
    public DepotKeyResult(SteamResult result, byte[]? key)
    {
        Result = result;
        Key = key;
    }

    public SteamResult Result { get; }

    public byte[]? Key { get; }

    // A key is granted only with an OK result and a key that is not empty.
    public bool IsGranted => Result == SteamResult.OK && Key is { Length: > 0 };

    public override string ToString() => $"{nameof(DepotKeyResult)} {SteamResults.Describe(Result)}";
}
