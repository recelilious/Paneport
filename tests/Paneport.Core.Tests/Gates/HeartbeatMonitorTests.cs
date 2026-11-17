// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Gates;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Gates;

// S2.2.6: the heartbeat keeps the longest gap between two beats, and counts a gap that is still
// open, as a hang that has not ended is.
public sealed class HeartbeatMonitorTests
{
    [Fact]
    public void TheLongestGapIsKept()
    {
        var time = new FakeTimeProvider();
        var heartbeat = new HeartbeatMonitor(time);

        time.Advance(TimeSpan.FromMilliseconds(100));
        heartbeat.Beat();
        time.Advance(TimeSpan.FromMilliseconds(900));
        heartbeat.Beat();
        time.Advance(TimeSpan.FromMilliseconds(100));
        heartbeat.Beat();

        Assert.Equal(TimeSpan.FromMilliseconds(900), heartbeat.LongestGap);
    }

    [Fact]
    public void AnOpenGapCounts()
    {
        var time = new FakeTimeProvider();
        var heartbeat = new HeartbeatMonitor(time);
        heartbeat.Beat();

        time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(TimeSpan.FromSeconds(5), heartbeat.LongestGap);
    }

    [Fact]
    public void TheFirstGapStartsWithTheMonitor()
    {
        var time = new FakeTimeProvider();
        var heartbeat = new HeartbeatMonitor(time);

        time.Advance(TimeSpan.FromMilliseconds(300));
        heartbeat.Beat();

        Assert.Equal(TimeSpan.FromMilliseconds(300), heartbeat.LongestGap);
    }
}
