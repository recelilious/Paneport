// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Paneport.Steam;

// What the state machines make of a Steam result (steam-service.md sections 4 and 5).
internal static class SteamResults
{
    // Logon results that say the saved refresh token itself is no good, so it is dropped from the
    // store and the user signs in again (C-0007 section 3). They are the results that name the
    // credential: a wrong password, which a refresh token stands in for, a bad signature, access
    // that was denied, and a token that expired or was revoked.
    public static bool MeansTokenInvalid(SteamResult result) => result
        is SteamResult.InvalidPassword
        or SteamResult.InvalidSignature
        or SteamResult.AccessDenied
        or SteamResult.Expired
        or SteamResult.Revoked;

    // Logon results that ask the client to try another connection manager server.
    public static bool MeansTryAnotherServer(SteamResult result) => result
        is SteamResult.TryAnotherCM
        or SteamResult.ServiceUnavailable;

    // What a sign-in or a resume says when its logon did not succeed. The policy's giving up and a
    // cancelled logon are not refusals by Steam, so they get words of their own.
    public static string DescribeLogOnFailure(SteamResult result) => result switch
    {
        SteamResult.NoConnection => "Paneport could not reach Steam to log on.",
        SteamResult.Cancelled => "The logon was cancelled.",
        _ => $"Steam refused the logon: {Describe(result)}.",
    };

    // The result's name, or its number for one that SteamResult does not name. Neither holds a
    // secret, so it may be shown and logged.
    public static string Describe(SteamResult result) =>
        Enum.IsDefined(result)
            ? string.Create(CultureInfo.InvariantCulture, $"{result} ({(int)result})")
            : string.Create(CultureInfo.InvariantCulture, $"result {(int)result}");
}
