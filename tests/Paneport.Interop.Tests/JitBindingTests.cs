// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Xunit;

namespace Paneport.Interop.Tests;

// The JIT group of version 1.3 against the host test library, whose macOS backend has a
// simulated broker (M3.1). The JIT state belongs to the process, which runs one host for every
// test, so a single test drives the whole request. Another reads the planted statuses of
// pp_test_jit_plant_status, which leave the state alone, to tell every member apart.
public sealed class JitBindingTests(HostFixture host)
{
    private const ulong Mebibyte = 1024 * 1024;
    private const int WaitMilliseconds = 30_000;

    [Fact]
    public void TheStatusKeepsEveryMemberApart()
    {
        try
        {
            // The patterns of paneport/testing.h: no two numbers are equal, each flag takes
            // both values, and no two flags take the same values.
            HostTestExports.PlantJitStatus(1);
            var first = NativeHost.GetJitStatus();
            Assert.Equal(HostJitState.Failed, first.State);
            Assert.Equal(HostJitFailure.SystemFailure, first.Failure);
            Assert.True(first.DebuggedSinceLaunch);
            Assert.True(first.AttachedNow);
            Assert.False(first.EnablerOpened);
            Assert.False(first.ForeignDebugger);
            Assert.Equal(601U, first.RequestedMiB);
            Assert.Equal(602U, first.PoolMiB);
            Assert.Equal(5U, first.RegionsPlanned);
            Assert.Equal(7U, first.RegionsMapped);
            Assert.Equal(1001UL, first.InStateMilliseconds);
            Assert.Equal(1002UL, first.DebuggerAttachedMilliseconds);
            Assert.Equal(1003UL, first.RequestingMilliseconds);
            Assert.Equal(1004UL, first.WaitingDetachMilliseconds);
            Assert.Equal(1005UL, first.VerifyingMilliseconds);
            Assert.Equal(1006UL, first.AuthorizationMilliseconds);

            HostTestExports.PlantJitStatus(2);
            var second = NativeHost.GetJitStatus();
            Assert.Equal(HostJitState.Verifying, second.State);
            Assert.Equal(HostJitFailure.MemoryLimit, second.Failure);
            Assert.True(second.DebuggedSinceLaunch);
            Assert.False(second.AttachedNow);
            Assert.True(second.EnablerOpened);
            Assert.False(second.ForeignDebugger);
            Assert.Equal(603U, second.RequestedMiB);
            Assert.Equal(604U, second.PoolMiB);
            Assert.Equal(2U, second.RegionsPlanned);
            Assert.Equal(3U, second.RegionsMapped);
            Assert.Equal(2001UL, second.InStateMilliseconds);
            Assert.Equal(2002UL, second.DebuggerAttachedMilliseconds);
            Assert.Equal(2003UL, second.RequestingMilliseconds);
            Assert.Equal(2004UL, second.WaitingDetachMilliseconds);
            Assert.Equal(2005UL, second.VerifyingMilliseconds);
            Assert.Equal(2006UL, second.AuthorizationMilliseconds);

            HostTestExports.PlantJitStatus(3);
            var third = NativeHost.GetJitStatus();
            Assert.Equal(HostJitState.WaitingDetach, third.State);
            Assert.Equal(HostJitFailure.DebuggerLost, third.Failure);
            Assert.False(third.DebuggedSinceLaunch);
            Assert.False(third.AttachedNow);
            Assert.True(third.EnablerOpened);
            Assert.True(third.ForeignDebugger);
            Assert.Equal(605U, third.RequestedMiB);
            Assert.Equal(606U, third.PoolMiB);
            Assert.Equal(4U, third.RegionsPlanned);
            Assert.Equal(8U, third.RegionsMapped);
            Assert.Equal(3001UL, third.InStateMilliseconds);
            Assert.Equal(3002UL, third.DebuggerAttachedMilliseconds);
            Assert.Equal(3003UL, third.RequestingMilliseconds);
            Assert.Equal(3004UL, third.WaitingDetachMilliseconds);
            Assert.Equal(3005UL, third.VerifyingMilliseconds);
            Assert.Equal(3006UL, third.AuthorizationMilliseconds);

            var refused = Assert.Throws<HostException>(() => HostTestExports.PlantJitStatus(4));
            Assert.Equal(HostStatus.InvalidArgument, refused.Status);
        }
        finally
        {
            HostTestExports.PlantJitStatus(0);
        }
    }

    [Fact]
    public void ARequestReachesReadyWithTheSimulatedBroker()
    {
        Assert.Equal(HostJitState.NotDebugged, NativeHost.GetJitStatus().State);

        var refused = Assert.Throws<HostException>(
            () => host.Main.Run(() => NativeHost.RequestJitPool(HostJit.DefaultPoolMiB)));
        Assert.Equal(HostStatus.JitState, refused.Status);
        Assert.Contains("not_debugged", refused.Message, StringComparison.Ordinal);
        var wrongThread = Assert.Throws<HostException>(() => NativeHost.SetJitEnablerOpened(true));
        Assert.Equal(HostStatus.WrongThread, wrongThread.Status);

        using var states = new BlockingCollection<HostJitState>();
        host.Main.Run(() => NativeHost.SetEventHandler(hostEvent =>
        {
            if (hostEvent.Kind == HostJit.StateChangedEvent)
            {
                states.Add((HostJitState)hostEvent.Code);
            }
        }));
        try
        {
            // One eighth of 8 GiB leaves room for the default pool (C-0006 section 4).
            HostTestExports.SetMemoryStub(8192 * Mebibyte, 0);
            host.Main.Run(() => NativeHost.SetJitEnablerOpened(true));
            Assert.Equal(HostJitState.DebuggerAttached, Next(states));
            host.Main.Run(() => NativeHost.RequestJitPool(HostJit.DefaultPoolMiB));
            Assert.Equal(HostJitState.Requesting, Next(states));
            Assert.Equal(HostJitState.WaitingDetach, Next(states));
            Assert.Equal(HostJitState.Verifying, Next(states));
            Assert.Equal(HostJitState.Ready, Next(states));

            var status = NativeHost.GetJitStatus();
            Assert.Equal(HostJitState.Ready, status.State);
            Assert.Equal(HostJitFailure.None, status.Failure);
            Assert.Equal(HostJit.DefaultPoolMiB, status.RequestedMiB);
            Assert.Equal(512U, status.PoolMiB);
            Assert.Equal(4U, status.RegionsPlanned);
            Assert.Equal(4U, status.RegionsMapped);
            Assert.True(status.DebuggedSinceLaunch);
            Assert.False(status.AttachedNow);
            Assert.True(status.EnablerOpened);
            Assert.False(status.ForeignDebugger);

            // The pool is fixed for the life of the process (C-0006 section 3 step 8).
            host.Main.Run(NativeHost.NoteJitForeground);
            Assert.Equal(HostJitState.Ready, NativeHost.GetJitStatus().State);
            var again = Assert.Throws<HostException>(
                () => host.Main.Run(() => NativeHost.RequestJitPool(HostJit.DefaultPoolMiB)));
            Assert.Equal(HostStatus.JitState, again.Status);
        }
        finally
        {
            host.Main.Run(() => NativeHost.SetEventHandler(null));
        }
    }

    private static HostJitState Next(BlockingCollection<HostJitState> states)
    {
        Assert.True(states.TryTake(out var state, WaitMilliseconds, TestContext.Current.CancellationToken));
        return state;
    }
}
