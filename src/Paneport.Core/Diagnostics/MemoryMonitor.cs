// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.Logging;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// The memory timeline of the current run: the host's memory budget and the thermal state,
/// sampled once a second from launch, with the first and current values, the highest limit, the
/// peak footprint, the highest thermal state and when the limit first reached the Game Mode level
/// (C-0011 section 1, C-0005 section 2).
/// </summary>
/// <remarks>
/// The app head calls <see cref="Sample"/> from a timer on the UI thread, so the timeline starts
/// again with each launch. Every member runs on the UI thread. The timeline keeps the last
/// <see cref="MaxSamples"/> samples; the aggregates cover the whole run.
/// </remarks>
public sealed class MemoryMonitor
{
    /// <summary>The samples that the timeline keeps: ten minutes at one a second.</summary>
    public const int MaxSamples = 600;

    /// <summary>The time between samples that the app heads use.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private const string LogSource = nameof(MemoryMonitor);

    private readonly IRuntimeHost _host;
    private readonly IDeviceInfo _device;
    private readonly TimeProvider _time;
    private readonly ILog _log;
    private readonly Queue<MemorySample> _samples = new();
    private long? _startTimestamp;
    private bool _failureLogged;

    /// <summary>Creates an empty timeline.</summary>
    /// <param name="host">The host whose budget is sampled.</param>
    /// <param name="device">The device, whose thermal state is sampled.</param>
    /// <param name="time">The clock.</param>
    /// <param name="log">The log, which gets one line when a reading fails.</param>
    public MemoryMonitor(IRuntimeHost host, IDeviceInfo device, TimeProvider time, ILog log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(log);
        _host = host;
        _device = device;
        _time = time;
        _log = log;
    }

    /// <summary>Raised after each sample that was taken.</summary>
    public event EventHandler? Sampled;

    /// <summary>Gets the samples that the timeline keeps, oldest first.</summary>
    public IReadOnlyList<MemorySample> Samples => [.. _samples];

    /// <summary>Gets how many samples the run took.</summary>
    public int SampleCount { get; private set; }

    /// <summary>Gets the run's first sample, or null before the first.</summary>
    public MemorySample? First { get; private set; }

    /// <summary>Gets the latest sample, or null before the first.</summary>
    public MemorySample? Latest { get; private set; }

    /// <summary>Gets the highest memory limit of the run, in bytes.</summary>
    public ulong HighestLimitBytes { get; private set; }

    /// <summary>Gets the highest footprint of the run, in bytes.</summary>
    public ulong PeakFootprintBytes { get; private set; }

    /// <summary>Gets the highest thermal state of the run.</summary>
    public ThermalState HighestThermalState { get; private set; }

    /// <summary>Gets when the limit first reached the Game Mode level, after the first sample; null while it has not.</summary>
    public TimeSpan? GameModeReachedAfter { get; private set; }

    /// <summary>Gets a value indicating whether the latest limit is far below the expected one.</summary>
    public bool LimitWarning => Latest is { } latest && MemoryLimitCheck.IsFarBelowExpected(latest.Budget.LimitBytes);

    /// <summary>Takes a sample now. A reading that fails is skipped, and the first failure is logged.</summary>
    /// <returns>True when a sample was taken.</returns>
    public bool Sample()
    {
        MemoryBudget budget;
        ThermalState thermal;
        try
        {
            budget = _host.GetMemoryBudget();
            thermal = _device.ReadThermalState();
        }
        catch (Exception exception)
        {
            if (!_failureLogged)
            {
                _failureLogged = true;
                _log.Write(LogLevel.Warning, LogSource, $"Reading the memory budget failed with {exception.GetType().Name}.");
            }

            return false;
        }

        var now = _time.GetTimestamp();
        _startTimestamp ??= now;
        var sample = new MemorySample(_time.GetElapsedTime(_startTimestamp.Value, now), budget, thermal);

        _samples.Enqueue(sample);
        if (_samples.Count > MaxSamples)
        {
            _samples.Dequeue();
        }

        SampleCount++;
        First ??= sample;
        Latest = sample;
        HighestLimitBytes = Math.Max(HighestLimitBytes, budget.LimitBytes);
        PeakFootprintBytes = Math.Max(PeakFootprintBytes, budget.FootprintBytes);
        if (thermal > HighestThermalState)
        {
            HighestThermalState = thermal;
        }

        if (budget.GameModeLevelReached && GameModeReachedAfter is null)
        {
            GameModeReachedAfter = sample.Elapsed;
            _log.Write(LogLevel.Information, LogSource, $"The memory limit reached the Game Mode level after {sample.Elapsed.TotalSeconds:F0} s.");
        }

        Sampled?.Invoke(this, EventArgs.Empty);
        return true;
    }
}
