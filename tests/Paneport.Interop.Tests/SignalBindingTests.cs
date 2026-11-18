// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Xunit;

namespace Paneport.Interop.Tests;

// The bindings of the signal dispatcher's test exports (M2.2), against the host test library,
// which pp_init gave the dispatcher. The harness runs without threads here: the CTest suite
// plants its faults (tests/native/test_signal_harness.c), and this test checks that the calls
// and the counters cross the interface.
public sealed class SignalBindingTests
{
    [Fact]
    public void TheDispatcherIsInstalledForEverySignal()
    {
        Assert.Equal(0, HostTestExports.CheckSignalsInstalled());
    }

    [Fact]
    public void TheHarnessStartsStopsAndReportsItsCounters()
    {
        HostTestExports.StartSignalHarness(0, 0, 1000);
        try
        {
            var running = HostTestExports.ReadSignalHarness();
            Assert.True(running.Running);
            Assert.Equal(0UL, running.GuestFaultsRaisedByMark);
            Assert.Equal(0UL, running.UnexpectedFaults);

            var again = Assert.Throws<HostException>(() => HostTestExports.StartSignalHarness(0, 0, 1000));
            Assert.Equal(HostStatus.Busy, again.Status);
        }
        finally
        {
            HostTestExports.StopSignalHarness();
        }

        Assert.False(HostTestExports.ReadSignalHarness().Running);
        var wrong = Assert.Throws<HostException>(() => HostTestExports.StartSignalHarness(17, 0, 1000));
        Assert.Equal(HostStatus.InvalidArgument, wrong.Status);
    }
}
