// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// S1.3.7: the memory timeline with a fake host and clock: the samples and their times, the peak
// footprint, the Game Mode transition and the warning threshold.
public sealed class MemoryMonitorTests
{
    private const ulong Megabyte = MemoryLimitCheck.BytesPerMegabyte;

    [Fact]
    public void TheHeadsSampleOnceASecond()
    {
        // The heads' timers use this interval, and the page says "one a second" (C-0011 section 1).
        Assert.Equal(TimeSpan.FromSeconds(1), MemoryMonitor.Interval);
        Assert.Equal(TimeSpan.FromMinutes(10), MemoryMonitor.Interval * MemoryMonitor.MaxSamples);
    }

    [Fact]
    public void TheTimelineGainsASampleEachSecond()
    {
        var fakes = new FakeServices();

        for (var second = 0; second < 5; second++)
        {
            Assert.True(fakes.Memory.Sample());
            fakes.Time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(5, fakes.Memory.SampleCount);
        Assert.Equal(
            [0, 1, 2, 3, 4],
            fakes.Memory.Samples.Select(sample => (int)sample.Elapsed.TotalSeconds));
        Assert.Equal(TimeSpan.Zero, fakes.Memory.First?.Elapsed);
        Assert.Equal(TimeSpan.FromSeconds(4), fakes.Memory.Latest?.Elapsed);
    }

    [Fact]
    public void TheTimelineKeepsItsLatestSamplesAndTheAggregatesOfTheWholeRun()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = Budget(6_144, footprint: 2_048);
        fakes.Memory.Sample();
        fakes.Host.Budget = Budget(6_144, footprint: 512);

        for (var sample = 0; sample < MemoryMonitor.MaxSamples + 10; sample++)
        {
            fakes.Time.Advance(TimeSpan.FromSeconds(1));
            fakes.Memory.Sample();
        }

        Assert.Equal(MemoryMonitor.MaxSamples, fakes.Memory.Samples.Count);
        Assert.Equal(MemoryMonitor.MaxSamples + 11, fakes.Memory.SampleCount);
        Assert.Equal(2_048 * Megabyte, fakes.Memory.PeakFootprintBytes);
        Assert.Equal(TimeSpan.Zero, fakes.Memory.First?.Elapsed);
    }

    [Fact]
    public void ThePeakFootprintAndTheHighestLimitAreKept()
    {
        var fakes = new FakeServices();
        foreach (var (limit, footprint) in new[] { (6_144UL, 500UL), (6_144UL, 1_900UL), (8_192UL, 1_200UL), (6_144UL, 800UL) })
        {
            fakes.Host.Budget = Budget(limit, footprint);
            fakes.Memory.Sample();
        }

        Assert.Equal(1_900 * Megabyte, fakes.Memory.PeakFootprintBytes);
        Assert.Equal(8_192 * Megabyte, fakes.Memory.HighestLimitBytes);
        Assert.Equal(6_144 * Megabyte, fakes.Memory.First?.Budget.LimitBytes);
        Assert.Equal(800 * Megabyte, fakes.Memory.Latest?.Budget.FootprintBytes);
    }

    [Fact]
    public void TheGameModeTransitionIsTimedFromTheFirstSample()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = Budget(6_144, 512);
        fakes.Memory.Sample();
        fakes.Time.Advance(TimeSpan.FromSeconds(3));
        fakes.Memory.Sample();
        Assert.Null(fakes.Memory.GameModeReachedAfter);

        fakes.Time.Advance(TimeSpan.FromSeconds(1));
        fakes.Host.Budget = Budget(8_192, 512, gameMode: true);
        fakes.Memory.Sample();
        fakes.Time.Advance(TimeSpan.FromSeconds(1));
        fakes.Memory.Sample();

        Assert.Equal(TimeSpan.FromSeconds(4), fakes.Memory.GameModeReachedAfter);
        Assert.Single(fakes.Log.Lines, line => line.Message == "The memory limit reached the Game Mode level after 4 s.");
    }

    [Theory]
    [InlineData(6_144UL, false)]
    [InlineData(8_192UL, false)]
    [InlineData(5_632UL, false)]
    [InlineData(5_631UL, true)]
    [InlineData(3_379UL, true)]
    public void TheWarningAppearsFarBelowTheExpectedLimit(ulong limitMegabytes, bool warning)
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = Budget(limitMegabytes, 256);

        fakes.Memory.Sample();

        Assert.Equal(warning, fakes.Memory.LimitWarning);
        Assert.Equal(warning, MemoryLimitCheck.IsFarBelowExpected(limitMegabytes * Megabyte));
    }

    [Fact]
    public void TheHighestThermalStateIsKept()
    {
        var fakes = new FakeServices();
        foreach (var state in new[] { ThermalState.Nominal, ThermalState.Serious, ThermalState.Fair })
        {
            fakes.Device.Thermal = state;
            fakes.Memory.Sample();
        }

        Assert.Equal(ThermalState.Serious, fakes.Memory.HighestThermalState);
        Assert.Equal(ThermalState.Fair, fakes.Memory.Latest?.ThermalState);
    }

    [Fact]
    public void AFailedReadingIsSkippedAndLoggedOnce()
    {
        var fakes = new FakeServices();
        fakes.Host.FailReads = true;

        Assert.False(fakes.Memory.Sample());
        Assert.False(fakes.Memory.Sample());

        Assert.Equal(0, fakes.Memory.SampleCount);
        var line = Assert.Single(fakes.Log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("Reading the memory budget failed with NativeHostException.", line.Message);
    }

    [Fact]
    public void EachSampleIsAnnounced()
    {
        var fakes = new FakeServices();
        var announced = 0;
        fakes.Memory.Sampled += (_, _) => announced++;

        fakes.Memory.Sample();
        fakes.Memory.Sample();

        Assert.Equal(2, announced);
    }

    private static MemoryBudget Budget(ulong limit, ulong footprint, bool gameMode = false) =>
        new(limit * Megabyte, (limit - footprint) * Megabyte, footprint * Megabyte, gameMode);
}
