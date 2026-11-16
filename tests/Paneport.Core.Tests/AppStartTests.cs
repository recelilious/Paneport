// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

// The start of the run, once per process: a scene that iOS connects again gets a new root view
// controller, which must not start a second memory timer or load the shell again.
public sealed class AppStartTests
{
    [Fact]
    public void TheFirstStartSamplesStartsTheTimerAndLoadsTheShell()
    {
        var fakes = new FakeServices();
        var shell = new ShellViewModel(fakes.ToAppServices());
        var timers = new List<FakeTimer>();
        var start = new AppStart(shell, fakes.Memory, (tick, interval) => Add(timers, new FakeTimer(tick, interval)));

        Assert.False(start.Started);
        Assert.True(start.TryStart());

        Assert.True(start.Started);
        Assert.Equal(1, fakes.Memory.SampleCount);
        var timer = Assert.Single(timers);
        Assert.Equal(TimeSpan.FromSeconds(1), timer.Interval);
        Assert.NotNull(shell.StartCommand.ExecutionTask);

        // Each tick samples the timeline and keeps the timer running.
        Assert.True(timer.Tick());
        Assert.Equal(2, fakes.Memory.SampleCount);
    }

    [Fact]
    public void ALaterStartDoesNothing()
    {
        var fakes = new FakeServices();
        var shell = new ShellViewModel(fakes.ToAppServices());
        var timers = new List<FakeTimer>();
        var start = new AppStart(shell, fakes.Memory, (tick, interval) => Add(timers, new FakeTimer(tick, interval)));
        start.TryStart();
        var firstLoad = shell.StartCommand.ExecutionTask;

        Assert.False(start.TryStart());

        Assert.True(start.Started);
        var timer = Assert.Single(timers);
        Assert.False(timer.Stopped);
        Assert.Equal(1, fakes.Memory.SampleCount);
        Assert.Same(firstLoad, shell.StartCommand.ExecutionTask);
    }

    private static FakeTimer Add(List<FakeTimer> timers, FakeTimer timer)
    {
        timers.Add(timer);
        return timer;
    }

    // A timer that a test ticks by hand.
    private sealed class FakeTimer(Func<bool> tick, TimeSpan interval) : IDisposable
    {
        public Func<bool> Tick { get; } = tick;

        public TimeSpan Interval { get; } = interval;

        public bool Stopped { get; private set; }

        public void Dispose() => Stopped = true;
    }
}
