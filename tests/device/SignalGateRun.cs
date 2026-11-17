// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics;
using Paneport.Core.Gates;
using Paneport.Interop;

namespace Paneport.DeviceTest;

// One run of the signal gate (L2 steps 12 and 13, docs/design/signals.md section 9): the host's
// native stress harness, a managed null-reference loop, a thread that keeps the garbage collector
// busy and a heartbeat, from Start until Stop. Start, Stop and MarkLeftForeground run on the UI
// thread; Snapshot runs on any thread.
internal sealed class SignalGateRun
{
    // The harness's threads, and each one's pause between two faults.
    private const uint MarkedThreads = 4;
    private const uint UnmarkedThreads = 4;
    private const uint PauseMicroseconds = 200;

    // The loop throws this many null-reference exceptions between two pauses of a millisecond.
    private const int ExceptionsPerPause = 64;

    // The pressure thread allocates blocks of this size, one per millisecond, keeps the last ones
    // alive so that some survive a collection, and asks for a full collection every so often.
    private const int BlockSize = 64 * 1024;
    private const int KeptBlocks = 16;
    private const int BlocksPerFullCollection = 5000;

    private static readonly TimeSpan s_heartbeatInterval = TimeSpan.FromMilliseconds(100);

    private readonly HeartbeatMonitor _heartbeat;
    private readonly Stopwatch _clock = new();
    private readonly List<Thread> _threads = [];
    private readonly int _collectionsAtStart;
    private readonly ulong _runtimeFaultsAtStart;
    private volatile bool _stopping;
    private volatile bool _leftForeground;
    private long _thrown;
    private long _caught;
    private string? _nothing;
    private volatile SignalGateCounters? _final;

    public SignalGateRun(TimeSpan target, TimeProvider time)
    {
        Target = target;
        _heartbeat = new HeartbeatMonitor(time);
        _collectionsAtStart = GC.CollectionCount(0);
        _runtimeFaultsAtStart = HostTestExports.ReadSignalHarness().RuntimeFaults;

        // The loop reads through this reference, which stays null.
        Volatile.Write(ref _nothing, null);
    }

    // How long the run must last to pass.
    public TimeSpan Target { get; }

    public TimeSpan Elapsed => _clock.Elapsed;

    public bool Finished => _final is not null;

    public void Start()
    {
        HostTestExports.StartSignalHarness(MarkedThreads, UnmarkedThreads, PauseMicroseconds);
        _clock.Start();
        StartThread("Signal gate: null references", ThrowNullReferences);
        StartThread("Signal gate: garbage", MakeGarbage);
        StartThread("Signal gate: heartbeat", Beat);
    }

    // The app left the foreground, which makes the run invalid (L2 step 13).
    public void MarkLeftForeground() => _leftForeground = true;

    // Ends the run and keeps its final counters. The heartbeat's gap is read before its thread
    // ends, so that the end of the run does not count as a gap.
    public SignalGateCounters Stop()
    {
        if (_final is { } final)
        {
            return final;
        }

        var gap = _heartbeat.LongestGap;
        _stopping = true;
        foreach (var thread in _threads)
        {
            thread.Join();
        }

        HostTestExports.StopSignalHarness();
        _clock.Stop();
        _final = Read(gap);
        return _final;
    }

    public SignalGateCounters Snapshot() => _final ?? Read(_heartbeat.LongestGap);

    private SignalGateCounters Read(TimeSpan longestGap)
    {
        var native = HostTestExports.ReadSignalHarness();
        return new SignalGateCounters
        {
            Duration = _clock.Elapsed,
            StayedInForeground = !_leftForeground,
            ExceptionsThrown = (ulong)Interlocked.Read(ref _thrown),
            ExceptionsCaught = (ulong)Interlocked.Read(ref _caught),
            GuestFaultsRaisedByMark = native.GuestFaultsRaisedByMark,
            GuestFaultsHandledByMark = native.GuestFaultsHandledByMark,
            GuestFaultsRaisedByAddress = native.GuestFaultsRaisedByAddress,
            GuestFaultsHandledByAddress = native.GuestFaultsHandledByAddress,
            UnexpectedFaults = native.UnexpectedFaults,
            RuntimeFaults = native.RuntimeFaults - _runtimeFaultsAtStart,
            GarbageCollections = GC.CollectionCount(0) - _collectionsAtStart,
            LongestHeartbeatGap = longestGap,
            DispatcherInstalled = HostTestExports.CheckSignalsInstalled() == 0,
        };
    }

    private void StartThread(string name, ThreadStart work)
    {
        var thread = new Thread(work) { IsBackground = true, Name = name };
        _threads.Add(thread);
        thread.Start();
    }

    // Reads the length of a string through a reference that is always null, so that the load
    // faults and Mono turns the fault into a NullReferenceException (docs/design/signals.md
    // section 5).
    private void ThrowNullReferences()
    {
        var round = 0;
        while (!_stopping)
        {
            Interlocked.Increment(ref _thrown);
            try
            {
                _ = Volatile.Read(ref _nothing)!.Length;
            }
            catch (NullReferenceException)
            {
                Interlocked.Increment(ref _caught);
            }

            if (++round % ExceptionsPerPause == 0)
            {
                Thread.Sleep(1);
            }
        }
    }

    private void MakeGarbage()
    {
        var kept = new byte[KeptBlocks][];
        var round = 0;
        while (!_stopping)
        {
            kept[round % KeptBlocks] = new byte[BlockSize];
            if (++round % BlocksPerFullCollection == 0)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            }

            Thread.Sleep(1);
        }
    }

    private void Beat()
    {
        while (!_stopping)
        {
            _heartbeat.Beat();
            Thread.Sleep(s_heartbeatInterval);
        }
    }
}
