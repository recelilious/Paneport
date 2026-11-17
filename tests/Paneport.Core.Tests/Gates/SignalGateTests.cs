// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Gates;
using Xunit;

namespace Paneport.Core.Tests.Gates;

// S2.2.6: on synthetic counters, the verdict is PASS only when every criterion of L2 step 13
// holds; each criterion alone that fails turns it to FAIL and is named.
public sealed class SignalGateTests
{
    // A run that meets every criterion of the four-hour gate.
    private static readonly SignalGateCounters s_passing = new()
    {
        Duration = TimeSpan.FromHours(4) + TimeSpan.FromSeconds(12),
        StayedInForeground = true,
        ExceptionsThrown = 1_000_000,
        ExceptionsCaught = 1_000_000,
        GuestFaultsRaisedByMark = 500_000,
        GuestFaultsHandledByMark = 500_000,
        GuestFaultsRaisedByAddress = 400_000,
        GuestFaultsHandledByAddress = 400_000,
        UnexpectedFaults = 0,
        RuntimeFaults = 1_000_000,
        GarbageCollections = 3_000,
        LongestHeartbeatGap = TimeSpan.FromMilliseconds(350),
        DispatcherInstalled = true,
    };

    public static TheoryData<string, SignalGateCounters> SingleFailures => new()
    {
        { "Valid run", s_passing with { StayedInForeground = false } },
        { "Duration", s_passing with { Duration = TimeSpan.FromHours(4) - TimeSpan.FromSeconds(1) } },
        { "Managed null references", s_passing with { ExceptionsCaught = 999_999 } },
        { "Managed null references", s_passing with { ExceptionsThrown = 0, ExceptionsCaught = 0 } },
        { "Guest faults by mark", s_passing with { GuestFaultsHandledByMark = 499_999 } },
        { "Guest faults by mark", s_passing with { GuestFaultsRaisedByMark = 0, GuestFaultsHandledByMark = 0 } },
        { "Guest faults by address", s_passing with { GuestFaultsHandledByAddress = 399_999 } },
        { "Guest faults by address", s_passing with { GuestFaultsRaisedByAddress = 0, GuestFaultsHandledByAddress = 0 } },
        { "Unexpected faults", s_passing with { UnexpectedFaults = 1 } },
        { "Garbage collections", s_passing with { GarbageCollections = 0 } },
        { "Longest heartbeat gap", s_passing with { LongestHeartbeatGap = SignalGate.HangThreshold } },
        { "Dispatcher installed", s_passing with { DispatcherInstalled = false } },
    };

    [Fact]
    public void ARunThatMeetsEveryCriterionPasses()
    {
        var result = SignalGate.Evaluate(s_passing, SignalGate.GateDuration);

        Assert.True(result.Passed);
        Assert.Equal("PASS", result.Verdict);
        Assert.All(result.Criteria, criterion => Assert.True(criterion.Met, criterion.Name));
        Assert.Equal(
            [
                "Valid run", "Duration", "Managed null references", "Guest faults by mark", "Guest faults by address",
                "Unexpected faults", "Garbage collections", "Longest heartbeat gap", "Dispatcher installed",
            ],
            result.Criteria.Select(criterion => criterion.Name));
    }

    [Theory]
    [MemberData(nameof(SingleFailures))]
    public void ARunThatMissesOneCriterionFailsOnIt(string criterion, SignalGateCounters counters)
    {
        var result = SignalGate.Evaluate(counters, SignalGate.GateDuration);

        Assert.False(result.Passed);
        Assert.Equal("FAIL", result.Verdict);
        Assert.Equal(criterion, Assert.Single(result.Criteria, item => !item.Met).Name);
    }

    [Fact]
    public void TheRequiredDurationIsTheCallers()
    {
        var shortRun = s_passing with { Duration = TimeSpan.FromMinutes(10) };

        Assert.True(SignalGate.Evaluate(shortRun, SignalGate.ShortDuration).Passed);
        Assert.False(SignalGate.Evaluate(shortRun, SignalGate.GateDuration).Passed);
    }

    [Fact]
    public void TheResultScreenShowsWhatTheRunObserved()
    {
        var criteria = SignalGate.Evaluate(s_passing, SignalGate.GateDuration).Criteria;

        Assert.Equal("4:00:12 of at least 4:00:00", criteria.Single(item => item.Name == "Duration").Observed);
        Assert.Equal(
            "1,000,000 thrown, 1,000,000 caught",
            criteria.Single(item => item.Name == "Managed null references").Observed);
        Assert.Equal(
            "0.350 s, below 2 s to pass",
            criteria.Single(item => item.Name == "Longest heartbeat gap").Observed);
        Assert.Equal("26:03:04", SignalGate.Clock(new TimeSpan(1, 2, 3, 4)));
    }
}
