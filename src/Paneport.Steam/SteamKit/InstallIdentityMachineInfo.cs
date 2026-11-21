// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Security.Cryptography;
using System.Text;
using SteamKit2;

namespace Paneport.Steam.SteamKit;

// The machine identity that SteamKit2 hashes into each logon (steam-service.md section 5). On iOS
// SteamKit2's own provider would hash the device's name; this one derives all three values from a
// random identity of the installation instead, so nothing about the device reaches Steam and the
// identity stays the same from one launch to the next.
internal sealed class InstallIdentityMachineInfo : IMachineInfoProvider
{
    private readonly byte[] _identity;

    public InstallIdentityMachineInfo(byte[] identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (identity.Length < MachineIdentity.Length)
        {
            throw new ArgumentException($"An install identity has at least {MachineIdentity.Length} bytes.", nameof(identity));
        }

        _identity = identity;
    }

    public byte[]? GetMachineGuid() => Derive("machine");

    public byte[]? GetMacAddress() => Derive("network");

    public byte[]? GetDiskId() => Derive("disk");

    private byte[] Derive(string purpose) => HMACSHA256.HashData(_identity, Encoding.ASCII.GetBytes(purpose));
}
