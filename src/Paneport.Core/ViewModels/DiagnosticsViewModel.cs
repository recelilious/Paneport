// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Diagnostics page: which host implementation runs, its interface version and its memory
/// figures.
/// </summary>
/// <remarks>
/// Sizes are shown in MB of 1,048,576 bytes, the unit that C-0005 and C-0009 use for the
/// memory limit.
/// </remarks>
public sealed partial class DiagnosticsViewModel : PageViewModel
{
    private const ulong BytesPerMegabyte = 1024 * 1024;

    private readonly IRuntimeHost _host;

    /// <summary>Creates the page and reads the host's memory budget once.</summary>
    /// <param name="host">The runtime host to describe.</param>
    /// <param name="log">The log.</param>
    public DiagnosticsViewModel(IRuntimeHost host, ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
        HostName = host.ImplementationName;
        AbiVersion = host.AbiVersion.ToString();
        Budget = host.GetMemoryBudget();
    }

    /// <summary>Gets the page's heading.</summary>
    public override string Header => "Diagnostics";

    /// <summary>Gets the name of the host implementation, so that a fake is never taken for the device.</summary>
    public string HostName { get; }

    /// <summary>Gets the host's interface version, such as <c>1.0</c>.</summary>
    public string AbiVersion { get; }

    /// <summary>Gets the memory budget that the host reported last.</summary>
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

    // Whole megabytes, rounded down, with a thousands separator.
    private static string Megabytes(ulong bytes) =>
        string.Create(CultureInfo.InvariantCulture, $"{bytes / BytesPerMegabyte:N0} MB");

    /// <summary>Reads the memory budget from the host again.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Budget = _host.GetMemoryBudget();
    }
}
