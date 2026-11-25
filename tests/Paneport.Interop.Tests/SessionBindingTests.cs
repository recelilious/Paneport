// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Collections.Concurrent;
using Xunit;

namespace Paneport.Interop.Tests;

// The session and process groups of version 1.4 against the host test library, which runs no
// session: each binding is called where it fails without one, the test builds' JIT stand-in
// crosses, and a line's source and process, which version 1.4 appended to pp_log_line_t for a
// program's output, reach the handler. The session itself needs the Wine runtime, which the
// CTest suite fakes (tests/native/test_session.c) and the macOS harness runs; the layouts of its
// structs are in the conformance test.
public sealed class SessionBindingTests(HostFixture host)
{
    private const int WaitMilliseconds = 30_000;
    private const uint ArchitectureX8664 = 1;

    [Fact]
    public void TheJitStandInCrossesTheInterface()
    {
        HostTestExports.SetJitReady(true);
        HostTestExports.SetJitReady(false);
    }

    [Fact]
    public void StoppingWithoutASessionFails()
    {
        var exception = Assert.Throws<HostException>(() => host.Main.Run(SessionCalls.Stop));

        Assert.Equal(HostStatus.NoSession, exception.Status);
        Assert.Contains("pp_session_stop", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void LaunchingWithoutASessionFails()
    {
        var exception = Assert.Throws<HostException>(() => SessionCalls.Launch("C:\\tests\\none.exe", captureOutput: true));

        Assert.Equal(HostStatus.NoSession, exception.Status);
        Assert.Contains("running session", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HandleZeroNamesNoProcess()
    {
        var state = Assert.Throws<HostException>(() => SessionCalls.GetState(0));
        Assert.Equal(HostStatus.InvalidArgument, state.Status);

        var terminate = Assert.Throws<HostException>(() => SessionCalls.Terminate(0, 1));
        Assert.Equal(HostStatus.InvalidArgument, terminate.Status);
    }

    // The JIT check comes before the configuration's. The process runs one host for every test, and
    // a JIT test may have driven M3.1's provider to ready before this one; then the folders, which
    // do not exist, fail the start instead, and no session starts either way.
    [Fact]
    public void StartingNeedsTheJitStateReady()
    {
        HostTestExports.SetJitReady(false);
        var expected = NativeHost.GetJitStatus().State == HostJitState.Ready
            ? HostStatus.InvalidArgument
            : HostStatus.JitNotReady;

        var exception = Assert.Throws<HostException>(() => host.Main.Run(() => SessionCalls.Start(
            ArchitectureX8664, "/nonexistent-paneport-prefix", "/nonexistent-paneport-runtime")));

        Assert.Equal(expected, exception.Status);
    }

    [Fact]
    public void ALinesSourceAndProcessReachTheHandler()
    {
        const string Text = "a line of a program's standard error from the binding tests";
        using var lines = new BlockingCollection<HostLogLine>();
        host.Main.Run(() => NativeHost.SetLogHandler(lines.Add));
        try
        {
            HostTestExports.LogWithSource(HostLogLevel.Information, HostLogSource.GuestErrors, 7, Text);

            // The host's own lines may come first.
            HostLogLine line;
            do
            {
                Assert.True(lines.TryTake(out line, WaitMilliseconds, TestContext.Current.CancellationToken));
            }
            while (line.Text != Text);

            Assert.Equal(HostLogSource.GuestErrors, line.Source);
            Assert.Equal(7UL, line.Process);
            Assert.Equal(HostLogLevel.Information, line.Level);

            var refused = Assert.Throws<HostException>(
                () => HostTestExports.LogWithSource(HostLogLevel.Information, (HostLogSource)4, 0, Text));
            Assert.Equal(HostStatus.InvalidArgument, refused.Status);
        }
        finally
        {
            host.Main.Run(() => NativeHost.SetLogHandler(null));
        }
    }
}
