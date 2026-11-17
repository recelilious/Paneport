// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Paneport.Core.Gates;

/// <summary>
/// The verdict of the signal gate (C-0012 section 3): a run passes only when every criterion of
/// L2 step 13 holds (docs/design/signals.md section 9).
/// </summary>
public static class SignalGate
{
    /// <summary>The duration of the gate's run.</summary>
    public static readonly TimeSpan GateDuration = TimeSpan.FromHours(4);

    /// <summary>The duration of the short run that a smoke check and the optional device check use.</summary>
    public static readonly TimeSpan ShortDuration = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The hang threshold: a heartbeat gap this long or longer counts as a hang, such as a
    /// garbage collection that never ends (docs/design/signals.md section 9). L2 and L10 use it.
    /// </summary>
    public static readonly TimeSpan HangThreshold = TimeSpan.FromSeconds(2);

    /// <summary>Evaluates a run against the criteria of L2 step 13.</summary>
    /// <param name="counters">What the run counted.</param>
    /// <param name="requiredDuration">How long the run had to last: <see cref="GateDuration"/> for the gate.</param>
    /// <returns>The verdict and every criterion.</returns>
    public static SignalGateResult Evaluate(SignalGateCounters counters, TimeSpan requiredDuration)
    {
        ArgumentNullException.ThrowIfNull(counters);
        SignalGateCriterion[] criteria =
        [
            new(
                "Valid run",
                counters.StayedInForeground,
                counters.StayedInForeground ? "the app never left the foreground" : "the app left the foreground"),
            new(
                "Duration",
                counters.Duration >= requiredDuration,
                $"{Clock(counters.Duration)} of at least {Clock(requiredDuration)}"),
            Equal("Managed null references", counters.ExceptionsThrown, "thrown", counters.ExceptionsCaught, "caught"),
            Equal(
                "Guest faults by mark",
                counters.GuestFaultsRaisedByMark,
                "raised",
                counters.GuestFaultsHandledByMark,
                "handled by the guest path"),
            Equal(
                "Guest faults by address",
                counters.GuestFaultsRaisedByAddress,
                "raised",
                counters.GuestFaultsHandledByAddress,
                "handled by the guest path"),
            new("Unexpected faults", counters.UnexpectedFaults == 0, Count(counters.UnexpectedFaults)),
            new(
                "Garbage collections",
                counters.GarbageCollections > 0,
                counters.GarbageCollections.ToString("N0", CultureInfo.InvariantCulture)),
            new(
                "Longest heartbeat gap",
                counters.LongestHeartbeatGap < HangThreshold,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{counters.LongestHeartbeatGap.TotalSeconds:0.000} s, below {HangThreshold.TotalSeconds:0} s to pass")),
            new(
                "Dispatcher installed",
                counters.DispatcherInstalled,
                counters.DispatcherInstalled
                    ? "for SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT"
                    : "a signal's handler was replaced"),
        ];
        return new SignalGateResult(criteria.All(criterion => criterion.Met), criteria);
    }

    /// <summary>Formats a duration as the result screen shows it, such as <c>4:00:12</c>.</summary>
    /// <param name="duration">The duration.</param>
    /// <returns>Hours, minutes and seconds.</returns>
    public static string Clock(TimeSpan duration) => string.Create(
        CultureInfo.InvariantCulture,
        $"{(long)duration.TotalHours}:{duration.Minutes:00}:{duration.Seconds:00}");

    // Two counts that must match, and must not be 0: a run in which nothing happened proves nothing.
    private static SignalGateCriterion Equal(string name, ulong first, string firstName, ulong second, string secondName) =>
        new(name, first > 0 && first == second, $"{Count(first)} {firstName}, {Count(second)} {secondName}");

    private static string Count(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
