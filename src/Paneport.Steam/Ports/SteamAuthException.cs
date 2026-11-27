// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Ports;

// A sign-in that Steam refused, with Steam's result. The adapter makes one from SteamKit2's
// AuthenticationException; its message is never shown or logged, only the result.
internal sealed class SteamAuthException : Exception
{
    public SteamAuthException()
        : this(SteamResult.Fail)
    {
    }

    public SteamAuthException(string message)
        : base(message)
    {
        Result = SteamResult.Fail;
    }

    public SteamAuthException(string message, Exception innerException)
        : base(message, innerException)
    {
        Result = SteamResult.Fail;
    }

    public SteamAuthException(SteamResult result)
        : base("Steam refused the sign-in.")
    {
        Result = result;
    }

    public SteamAuthException(SteamResult result, Exception innerException)
        : base("Steam refused the sign-in.", innerException)
    {
        Result = result;
    }

    public SteamResult Result { get; }
}
