// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Storage;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// What the Diagnostics page reads besides the host: the memory timeline, the device, the build,
/// the entitlements, the crash record, the storage folders and the call that turns off Mono's
/// crash chaining. Each app head provides its own: the iOS head the device's readings, the
/// desktop head stand-ins that say they are not.
/// </summary>
public sealed class DiagnosticsSources
{
    /// <summary>Collects the sources.</summary>
    /// <param name="memory">The memory timeline of the run.</param>
    /// <param name="device">The device.</param>
    /// <param name="build">The build stamp.</param>
    /// <param name="entitlements">The entitlements, read once at launch; empty where there are none, as on the desktop.</param>
    /// <param name="crashRecords">The crash record of the previous run, or null where there is none, as on the desktop.</param>
    /// <param name="storage">Reads the storage folders now; empty where there are none, as on the desktop.</param>
    /// <param name="crashChaining">What became of the call that turns off Mono's crash chaining; null where the head makes none, as on the desktop.</param>
    public DiagnosticsSources(
        MemoryMonitor memory,
        IDeviceInfo device,
        BuildStamp build,
        IReadOnlyList<EntitlementReading> entitlements,
        CrashRecordStore? crashRecords,
        Func<IReadOnlyList<FolderState>> storage,
        CrashChainingState? crashChaining = null)
    {
        ArgumentNullException.ThrowIfNull(memory);
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(entitlements);
        ArgumentNullException.ThrowIfNull(storage);
        Memory = memory;
        Device = device;
        Build = build;
        Entitlements = entitlements;
        CrashRecords = crashRecords;
        Storage = storage;
        CrashChaining = crashChaining ?? CrashChainingState.NotCalled;
    }

    /// <summary>Gets the memory timeline of the run.</summary>
    public MemoryMonitor Memory { get; }

    /// <summary>Gets the device.</summary>
    public IDeviceInfo Device { get; }

    /// <summary>Gets the build stamp.</summary>
    public BuildStamp Build { get; }

    /// <summary>Gets the entitlements, in the order of C-0009 section 4.</summary>
    public IReadOnlyList<EntitlementReading> Entitlements { get; }

    /// <summary>Gets the crash record of the previous run, or null where there is none.</summary>
    public CrashRecordStore? CrashRecords { get; }

    /// <summary>Gets the function that reads the storage folders now.</summary>
    public Func<IReadOnlyList<FolderState>> Storage { get; }

    /// <summary>Gets what became of the call that turns off Mono's crash chaining at startup.</summary>
    public CrashChainingState CrashChaining { get; }
}
