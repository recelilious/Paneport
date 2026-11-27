// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using Paneport.Core.Security;

namespace Paneport.Steam.Tests.Fakes;

// Values of the kinds that must never reach a log or a page, made at run time, so that no test
// source holds one and the personal-data check finds nothing in tests/ (C-0011 sections 1 and 2).
// None of them is real: the names and passwords are random, the tokens only have a token's shape,
// and the Steam IDs are random accounts of an individual's ID range.
internal sealed class SteamSamples
{
    // The first individual Steam ID, in hexadecimal so that it is no Steam ID in the source.
    private const ulong IndividualBase = 0x0110_0001_0000_0000;

    public SteamSamples()
    {
        AccountName = "user" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(5));
        Password = "pw-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(8));
        RefreshToken = WebToken();
        AccountNumber = (uint)RandomNumberGenerator.GetInt32(1_000_000, int.MaxValue);
        DepotKey = RandomNumberGenerator.GetBytes(32);
        // Ten to twelve digits: short enough that only its registration with the log filter, not the
        // filter's rule for long numbers, keeps it out of the log.
        ManifestCode = 1_000_000_000UL * (ulong)RandomNumberGenerator.GetInt32(1, 1000) + (ulong)RandomNumberGenerator.GetInt32(0, 1_000_000_000);
        ServerToken = "server-" + Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
    }

    public string AccountName { get; }

    public string Password { get; }

    public string RefreshToken { get; }

    public Secret RefreshTokenSecret => new(RefreshToken);

    public uint AccountNumber { get; }

    public byte[] DepotKey { get; }

    public ulong ManifestCode { get; }

    public string ServerToken { get; }

    public string SteamId64 => (IndividualBase + AccountNumber).ToString(CultureInfo.InvariantCulture);

    public string SteamId3 => "[" + "U:1:" + AccountNumber.ToString(CultureInfo.InvariantCulture) + "]";

    public string SteamId2 => "STEAM" + "_0:" + (AccountNumber & 1).ToString(CultureInfo.InvariantCulture) + ":" + (AccountNumber >> 1).ToString(CultureInfo.InvariantCulture);

    public string ManifestCodeText => ManifestCode.ToString(CultureInfo.InvariantCulture);

    public string AccountNumberText => AccountNumber.ToString(CultureInfo.InvariantCulture);

    // The account's Steam ID as SteamKit2 gives it with a logon.
    public SteamKit2.SteamID SteamId => new(AccountNumber, SteamKit2.EUniverse.Public, SteamKit2.EAccountType.Individual);

    // Every value that no log line may hold, in the forms a line could hold it.
    public IReadOnlyList<string> Forbidden =>
    [
        AccountName,
        Password,
        RefreshToken,
        Convert.ToHexString(DepotKey),
        Convert.ToHexStringLower(DepotKey),
        Convert.ToBase64String(DepotKey),
        ManifestCodeText,
        ServerToken,
        SteamId64,
        SteamId3,
        SteamId2,
        AccountNumberText,
    ];

    // A token with the shape of Steam's: three base64url parts, the first two of which encode a
    // JSON object.
    public static string WebToken() =>
        string.Concat("e", "yJ", Base64UrlPart(12), ".", "e", "yJ", Base64UrlPart(48), ".", Base64UrlPart(32));

    private static string Base64UrlPart(int bytes) =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
