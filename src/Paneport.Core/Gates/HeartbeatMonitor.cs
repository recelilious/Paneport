// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Gates;

/// <summary>
/// The heartbeat of a signal gate run: a thread beats at a fixed interval, and the longest gap
/// between two beats shows a hang, such as a garbage collection that stops every thread for too
/// long (docs/design/signals.md section 9).
/// </summary>
/// <remarks>One thread beats; any thread reads.</remarks>
public sealed class HeartbeatMonitor
{
    private readonly TimeProvider _time;
    private long _lastBeat;
    private long _longestGapTicks;

    /// <summary>Starts the heartbeat now.</summary>
    /// <param name="time">The clock.</param>
    public HeartbeatMonitor(TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        _time = time;
        _lastBeat = time.GetTimestamp();
    }

    /// <summary>Gets the longest gap between two beats so far, the current one included.</summary>
    public TimeSpan LongestGap
    {
        get
        {
            var current = _time.GetElapsedTime(Volatile.Read(ref _lastBeat));
            var longest = TimeSpan.FromTicks(Interlocked.Read(ref _longestGapTicks));
            return current > longest ? current : longest;
        }
    }

    /// <summary>Records a beat. One thread only.</summary>
    public void Beat()
    {
        var now = _time.GetTimestamp();
        var gap = _time.GetElapsedTime(Volatile.Read(ref _lastBeat), now);

        // The longest gap is stored before the new beat, so that a reader that sees the new beat
        // also sees the gap that it closed.
        if (gap.Ticks > Interlocked.Read(ref _longestGapTicks))
        {
            Interlocked.Exchange(ref _longestGapTicks, gap.Ticks);
        }

        Volatile.Write(ref _lastBeat, now);
    }
}
