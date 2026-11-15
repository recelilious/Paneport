// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Core.Storage;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Diagnostics page: the host, the memory timeline, the entitlements, the previous run's crash
/// record, the storage folders, the build and the device (docs/design/ios-app-head.md section 8).
/// </summary>
/// <remarks>
/// <para>
/// Sizes are shown in MB of 1,048,576 bytes, the unit that C-0005 and C-0009 use for the memory
/// limit. While the page is shown it follows the memory timeline, which the app head samples once a
/// second, and the host's events, which reach it on the UI thread.
/// </para>
/// <para>
/// The page never shows a device name, a team identifier, an application-identifier prefix or a
/// bundle identifier (C-0012 section 4): no source it reads gives one, and the entitlement readers
/// keep only three Boolean values.
/// </para>
/// </remarks>
public sealed partial class DiagnosticsViewModel : PageViewModel
{
    /// <summary>The number of samples that the timeline shows, newest first.</summary>
    public const int TimelineLength = 12;

    private readonly IRuntimeHost _host;
    private readonly DiagnosticsSources _sources;

    /// <summary>Creates the page, which shows the latest sample of the memory timeline, or takes one.</summary>
    /// <param name="host">The runtime host to describe.</param>
    /// <param name="sources">What the page reads besides the host.</param>
    /// <param name="log">The log.</param>
    public DiagnosticsViewModel(IRuntimeHost host, DiagnosticsSources sources, ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(sources);
        _host = host;
        _sources = sources;
        HostName = host.ImplementationName;
        AbiVersion = host.AbiVersion.ToString();
        AbiCompatibility = host.AbiVersion.IsCompatibleWith(host.BindingsVersion)
            ? $"Compatible: the app's bindings use version {host.BindingsVersion}"
            : $"Not compatible: the app's bindings use version {host.BindingsVersion}";
        HostImport = host.ImportName;
        EntitlementRows = EntitlementRowsOf(sources.Entitlements);
        BuildRows = BuildRowsOf(sources.Build);
        DeviceRows =
        [
            new("Model identifier", sources.Device.ModelIdentifier),
            new("iOS version", sources.Device.SystemVersion),
            new("iOS build", sources.Device.SystemBuild),
        ];
        if (sources.Memory.Latest is null)
        {
            sources.Memory.Sample();
        }

        UpdateHostEvents();
        UpdateMemory();
        UpdateCrashRecord();
        StorageRows = StorageRowsOf(sources.Storage());
    }

    /// <summary>Gets the page's heading.</summary>
    public override string Header => "Diagnostics";

    /// <summary>Gets the name of the host implementation, so that a fake is never taken for the device.</summary>
    public string HostName { get; }

    /// <summary>Gets the host's interface version, such as <c>1.1</c>.</summary>
    public string AbiVersion { get; }

    /// <summary>Gets whether the host's version is compatible with the app's bindings (C-0005 section 1).</summary>
    public string AbiCompatibility { get; }

    /// <summary>Gets how the app imports the host (C-0009 section 4).</summary>
    public string HostImport { get; }

    /// <summary>Gets the host's events so far.</summary>
    [ObservableProperty]
    public partial string HostEvents { get; private set; } = string.Empty;

    /// <summary>Gets the memory budget of the latest sample.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemoryLimit), nameof(AvailableMemory), nameof(Footprint), nameof(GameModeLevel))]
    public partial MemoryBudget Budget { get; private set; }

    /// <summary>Gets the memory limit, such as <c>6,144 MB</c>.</summary>
    public string MemoryLimit => Megabytes(Budget.LimitBytes);

    /// <summary>Gets the memory still available to the app.</summary>
    public string AvailableMemory => Megabytes(Budget.AvailableBytes);

    /// <summary>Gets the app's current memory footprint.</summary>
    public string Footprint => Megabytes(Budget.FootprintBytes);

    /// <summary>Gets whether the limit has reached the Game Mode level, as text.</summary>
    public string GameModeLevel => Budget.GameModeLevelReached ? "Reached" : "Not reached";

    /// <summary>Gets the run's memory figures besides the current ones.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiagnosticsRow> MemoryRows { get; private set; } = [];

    /// <summary>Gets the latest samples of the timeline, newest first.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<string> Timeline { get; private set; } = [];

    /// <summary>Gets the warning about a limit far below the expected one, or null when there is none.</summary>
    [ObservableProperty]
    public partial string? LimitWarning { get; private set; }

    /// <summary>Gets the entitlements of C-0009 section 4, each with its sources.</summary>
    public IReadOnlyList<DiagnosticsRow> EntitlementRows { get; }

    /// <summary>Gets what the previous run's crash record says.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiagnosticsRow> CrashRecordRows { get; private set; } = [];

    /// <summary>Gets the sentence about the crash record above its fields.</summary>
    [ObservableProperty]
    public partial string CrashRecordSummary { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether there is a crash record to dismiss.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DismissCrashRecordCommand))]
    public partial bool HasCrashRecord { get; private set; }

    /// <summary>Gets the storage folders of C-0004 section 4 with their backup marks.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<DiagnosticsRow> StorageRows { get; private set; } = [];

    /// <summary>Gets the build stamp.</summary>
    public IReadOnlyList<DiagnosticsRow> BuildRows { get; }

    /// <summary>Gets the device's model identifier and its system version and build.</summary>
    public IReadOnlyList<DiagnosticsRow> DeviceRows { get; }

    /// <inheritdoc />
    protected override void OnShown()
    {
        _sources.Memory.Sampled += OnSampled;
        _host.EventReceived += OnHostEvent;
        UpdateMemory();
        UpdateHostEvents();
    }

    /// <inheritdoc />
    protected override void OnHidden()
    {
        _sources.Memory.Sampled -= OnSampled;
        _host.EventReceived -= OnHostEvent;
    }

    // Whole megabytes, rounded down, with a thousands separator.
    private static string Megabytes(ulong bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / MemoryLimitCheck.BytesPerMegabyte:N0} MB");

    private static string Seconds(TimeSpan time) =>
        string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes:00}:{time.Seconds:00}");

    private static string ThermalText(ThermalState state) => state switch
    {
        ThermalState.Nominal => "nominal",
        ThermalState.Fair => "fair",
        ThermalState.Serious => "serious",
        ThermalState.Critical => "critical",
        _ => "unknown",
    };

    private static IReadOnlyList<DiagnosticsRow> EntitlementRowsOf(IReadOnlyList<EntitlementReading> readings)
    {
        if (readings.Count == 0)
        {
            return [new("Entitlements", "None: this head does not run on the device")];
        }

        return [.. readings.Select(reading => new DiagnosticsRow(ShortName(reading.Name), reading.Description))];
    }

    // The part after the last dot, as Apple's documentation titles the entitlements.
    private static string ShortName(string name) => name[(name.LastIndexOf('.') + 1)..];

    private static IReadOnlyList<DiagnosticsRow> BuildRowsOf(BuildStamp build) =>
    [
        new("Commit", build.Commit),
        new("Version", build.Version),
        new("Configuration", build.Configuration),
        new("Runtime", build.Runtime),
        new("Compilation", build.CompilationMode),
        new("Interpreter", build.Interpreter),
        new("Host import", build.HostImport),
    ];

    private static IReadOnlyList<DiagnosticsRow> StorageRowsOf(IReadOnlyList<FolderState> folders)
    {
        if (folders.Count == 0)
        {
            return [new("Folders", "None: this head does not run on the device")];
        }

        return [.. folders.Select(state => new DiagnosticsRow(state.Folder.RelativePath + "/", BackupText(state)))];
    }

    private static string BackupText(FolderState state)
    {
        if (!state.Exists)
        {
            return "Missing";
        }

        var mark = state.ExcludedFromBackup switch
        {
            true => "marked as excluded from backup",
            false => "not marked",
            null => "mark unreadable",
        };
        return state.Folder switch
        {
            { ExcludedBySystem: true } => $"Excluded from backup by iOS; {mark}",
            { Backup: BackupPolicy.Excluded } => state.ExcludedFromBackup == true ? "Excluded from backup" : $"Should be excluded from backup, but {mark}",
            { Backup: BackupPolicy.Included } => state.ExcludedFromBackup == true ? "Should be backed up, but is marked as excluded" : "Backed up",
            _ => $"The user's choice; shown in the Files app; {mark}",
        };
    }

    private void OnSampled(object? sender, EventArgs e) => UpdateMemory();

    private void OnHostEvent(object? sender, HostEventInfo e) => UpdateHostEvents();

    private void UpdateHostEvents()
    {
        HostEvents = _host.LastEvent is { } last
            ? string.Create(CultureInfo.InvariantCulture, $"{_host.EventCount} received; the last: kind {last.Kind}, code {last.Code}")
            : "None yet";
    }

    private void UpdateMemory()
    {
        var memory = _sources.Memory;
        if (memory.Latest is { } latest)
        {
            Budget = latest.Budget;
        }

        MemoryRows =
        [
            new("First limit", memory.First is { } first ? Megabytes(first.Budget.LimitBytes) : "No sample yet"),
            new("Highest limit", Megabytes(memory.HighestLimitBytes)),
            new("Game Mode level first reached", memory.GameModeReachedAfter is { } after ? $"After {Seconds(after)}" : "Not yet"),
            new("Peak footprint", Megabytes(memory.PeakFootprintBytes)),
            new("Thermal state", memory.Latest is { } current
                ? $"{ThermalText(current.ThermalState)}; highest {ThermalText(memory.HighestThermalState)}"
                : "No sample yet"),
            new("Samples", string.Create(CultureInfo.InvariantCulture, $"{memory.SampleCount}, one a second")),
        ];
        Timeline = [.. memory.Samples.Reverse().Take(TimelineLength).Select(sample =>
            $"{Seconds(sample.Elapsed)}  limit {Megabytes(sample.Budget.LimitBytes)}, footprint {Megabytes(sample.Budget.FootprintBytes)}"
                + $"{(sample.Budget.GameModeLevelReached ? ", Game Mode level" : string.Empty)}, {ThermalText(sample.ThermalState)}")];
        LimitWarning = memory.LimitWarning
            ? $"The memory limit of {Megabytes(Budget.LimitBytes)} is far below the {Megabytes(MemoryLimitCheck.ExpectedLimitBytes)} "
                + "that an iPhone with 12 GB gives Paneport. The install most likely dropped the increased-memory-limit entitlement."
            : null;
    }

    private void UpdateCrashRecord()
    {
        var reading = _sources.CrashRecords?.Current ?? CrashRecordReading.None;
        HasCrashRecord = reading.Status != CrashRecordStatus.None;
        switch (reading)
        {
            case { Status: CrashRecordStatus.Valid, Record: { } record }:
                CrashRecordSummary = record.IsTest
                    ? "A test record from the previous run."
                    : "The previous run ended with a fatal fault.";
                CrashRecordRows =
                [
                    new("Time", record.Time.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)),
                    new("Signal", string.Create(CultureInfo.InvariantCulture, $"{record.SignalName}, code {record.SignalCode}")),
                    new("Thread", record.ThreadRoleName),
                    new("Fault address", string.Create(CultureInfo.InvariantCulture, $"0x{record.FaultAddress:X16}")),
                    new("Program counter", string.Create(CultureInfo.InvariantCulture, $"0x{record.ProgramCounter:X16}")),
                    new("Test record", record.IsTest ? "Yes" : "No"),
                    new("Message", record.Message.Length == 0 ? "(none)" : record.Message),
                ];
                break;
            case { Status: CrashRecordStatus.Malformed }:
                CrashRecordSummary = $"A crash record was found, but it is malformed: {reading.Problem}";
                CrashRecordRows = [];
                break;
            default:
                CrashRecordSummary = _sources.CrashRecords is null
                    ? "None: this head does not run on the device."
                    : "No crash record from a previous run.";
                CrashRecordRows = [];
                break;
        }
    }

    /// <summary>Reads the memory budget from the host again, as a sample of the timeline.</summary>
    [RelayCommand]
    private void Refresh()
    {
        _sources.Memory.Sample();
        UpdateMemory();
        StorageRows = StorageRowsOf(_sources.Storage());
    }

    /// <summary>Dismisses the previous run's crash record, which deletes it.</summary>
    /// <param name="cancellationToken">Cancels the dismissal.</param>
    /// <returns>A task that completes when the record is dismissed.</returns>
    [RelayCommand(CanExecute = nameof(HasCrashRecord))]
    private async Task DismissCrashRecordAsync(CancellationToken cancellationToken)
    {
        if (_sources.CrashRecords is { } store)
        {
            await store.DismissAsync(cancellationToken);
            Log.Write(LogLevel.Information, nameof(DiagnosticsViewModel), "The crash record was dismissed.");
        }

        UpdateCrashRecord();
    }
}
