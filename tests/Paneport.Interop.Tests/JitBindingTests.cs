// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Xunit;

namespace Paneport.Interop.Tests;

// The JIT group of version 1.3 against the host test library, whose macOS backend has a
// simulated broker (M3.1). The JIT state belongs to the process, which runs one host for every
// test, so a single test drives the whole request.
public sealed class JitBindingTests(HostFixture host)
{
    private const ulong Mebibyte = 1024 * 1024;
    private const int WaitMilliseconds = 30_000;

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
