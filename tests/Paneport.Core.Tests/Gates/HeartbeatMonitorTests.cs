// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Paneport.Core.Gates;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Gates;

// S2.2.6: the heartbeat keeps the longest gap between two beats, and counts a gap that is still
// open, as a hang that has not ended is; a thread that reads it while another beats never
// misses a gap that a beat closed.
public sealed class HeartbeatMonitorTests
{
    private const int MaxRounds = 200_000;

    // A reader that sees a beat also sees the gap that the beat closed, because Beat stores the
    // longest gap before the beat. No test can hold the beating thread between those two
    // stores, so a reader spins on LongestGap while the test closes a longer gap each round, on
    // a clock that only the test moves. With the stores in the other order, the reader catches
    // the new beat with the old longest gap within a few dozen rounds on a machine with four or
    // more cores, so the test fails; in this order no round can miss. It stops after a second
    // or 200,000 rounds at most.
    [Fact]
    public void AReaderThatSeesABeatSeesTheGapItClosed()
    {
        var time = new SteppedClock();
        var heartbeat = new HeartbeatMonitor(time);
        long gapToReach = 0;
        var stop = 0;
        long misses = 0;
        var reader = new Thread(() =>
        {
            while (Volatile.Read(ref stop) == 0)
            {
                // The gap that is open now, or that a beat closed since, is at least this long.
                var least = Volatile.Read(ref gapToReach);
                if (heartbeat.LongestGap.Ticks < least)
                {
                    Interlocked.Increment(ref misses);
                }
            }
        })
        {
            IsBackground = true,
        };
        reader.Start();

        var watch = Stopwatch.StartNew();
        long rounds = 0;
        while (rounds < MaxRounds && watch.Elapsed < TimeSpan.FromSeconds(1) &&
               Interlocked.Read(ref misses) == 0)
        {
            rounds++;
            time.Advance(rounds);
            Volatile.Write(ref gapToReach, rounds);
            Thread.SpinWait((int)(rounds % 64));
            heartbeat.Beat();
        }

        Volatile.Write(ref stop, 1);
        reader.Join();
        if (Interlocked.Read(ref misses) != 0)
        {
            Assert.Fail($"The reader missed a closed gap within {rounds} rounds.");
        }
    }

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

    // A clock that only the test moves, which another thread may read while it moves.
    private sealed class SteppedClock : TimeProvider
    {
        private long _ticks;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => Volatile.Read(ref _ticks);

        public void Advance(long ticks) => Interlocked.Add(ref _ticks, ticks);
    }
}
