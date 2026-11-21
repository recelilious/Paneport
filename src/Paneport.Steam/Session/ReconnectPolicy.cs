// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Steam.Session;

// How the session keeps its connection (steam-service.md section 4): after a connection ends or
// an attempt fails, it waits FirstDelay, then twice as long before each further attempt, up to
// MaxDelay, and gives up after MaxAttempts failed attempts in a row. It follows Steam's request to
// try another server at most MaxServerSwitches times per logon, and waits at most LogOffTimeout
// for the logoff to be confirmed before it disconnects.
internal sealed record ReconnectPolicy(
    int MaxAttempts,
    TimeSpan FirstDelay,
    TimeSpan MaxDelay,
    int MaxServerSwitches,
    TimeSpan LogOffTimeout)
{
    // Five attempts, after 1, 2, 4, 8 and 16 seconds: about half a minute in all.
    public static ReconnectPolicy Default { get; } = new(
        MaxAttempts: 5,
        FirstDelay: TimeSpan.FromSeconds(1),
        MaxDelay: TimeSpan.FromSeconds(30),
        MaxServerSwitches: 3,
        LogOffTimeout: TimeSpan.FromSeconds(2));

    // The wait before attempt n, counted from 1.
    public TimeSpan DelayBefore(int attempt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempt, 1);
        var delay = FirstDelay;
        for (var i = 1; i < attempt && delay < MaxDelay; i++)
        {
            delay += delay;
        }

        return delay < MaxDelay ? delay : MaxDelay;
    }
}
