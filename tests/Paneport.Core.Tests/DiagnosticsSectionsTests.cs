// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Storage;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

// S1.3.7: the sections of the Diagnostics page over a fake host, clock and device: the host, the
// memory timeline with its warning, the entitlements, the crash record, the storage folders, the
// build and the device.
public sealed class DiagnosticsSectionsTests
{
    private const ulong Megabyte = MemoryLimitCheck.BytesPerMegabyte;

    [Fact]
    public void TheHostSectionNamesTheVersionItsCompatibilityAndTheImport()
    {
        var fakes = new FakeServices();

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal("1.1", page.AbiVersion);
        Assert.Equal("Compatible: the app's bindings use version 1.1", page.AbiCompatibility);
        Assert.Equal("Fake import for tests", page.HostImport);
        Assert.Equal("None yet", page.HostEvents);
    }

    [Fact]
    public void AnIncompatibleHostIsMarked()
    {
        var fakes = new FakeServices();
        var host = new FakeRuntimeHost { AbiVersion = new(2, 0) };

        var page = new DiagnosticsViewModel(host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal("Not compatible: the app's bindings use version 1.1", page.AbiCompatibility);
    }

    [Fact]
    public void TheTimelineFollowsTheMonitorWhileThePageIsShown()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = new MemoryBudget(6_144 * Megabyte, 5_632 * Megabyte, 512 * Megabyte, false);
        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);
        page.NotifyShown();

        fakes.Time.Advance(TimeSpan.FromSeconds(5));
        fakes.Host.Budget = new MemoryBudget(8_192 * Megabyte, 7_000 * Megabyte, 1_192 * Megabyte, true);
        fakes.Device.Thermal = ThermalState.Fair;
        fakes.Memory.Sample();

        Assert.Equal("8,192 MB", page.MemoryLimit);
        Assert.Equal("Reached", page.GameModeLevel);
        Assert.Equal("00:05  limit 8,192 MB, footprint 1,192 MB, Game Mode level, fair", page.Timeline[0]);
        Assert.Equal("00:00  limit 6,144 MB, footprint 512 MB, nominal", page.Timeline[1]);
        Assert.Equal(
            [
                new DiagnosticsRow("First limit", "6,144 MB"),
                new DiagnosticsRow("Highest limit", "8,192 MB"),
                new DiagnosticsRow("Game Mode level first reached", "After 00:05"),
                new DiagnosticsRow("Peak footprint", "1,192 MB"),
                new DiagnosticsRow("Thermal state", "fair; highest fair"),
                new DiagnosticsRow("Samples", "2, one a second"),
            ],
            page.MemoryRows);
        Assert.Null(page.LimitWarning);

        page.NotifyHidden();
        fakes.Host.Budget = new MemoryBudget(6_144 * Megabyte, 6_000 * Megabyte, 144 * Megabyte, false);
        fakes.Memory.Sample();
        Assert.Equal("8,192 MB", page.MemoryLimit);
    }

    [Fact]
    public void ALimitFarBelowTheExpectedOneShowsTheWarning()
    {
        var fakes = new FakeServices();
        fakes.Host.Budget = new MemoryBudget(3_379 * Megabyte, 3_000 * Megabyte, 379 * Megabyte, false);

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal(
            "The memory limit of 3,379 MB is far below the 6,144 MB that an iPhone with 12 GB gives Paneport. "
                + "The install most likely dropped the increased-memory-limit entitlement.",
            page.LimitWarning);
    }

    [Fact]
    public void TheEntitlementsShowTheirSourcesByTheirShortNames()
    {
        var fakes = new FakeServices
        {
            Entitlements =
            [
                new(EntitlementNames.GetTaskAllow, true, EntitlementSourceState.Read, true, EntitlementSourceState.Read),
                new(EntitlementNames.IncreasedMemoryLimit, true, EntitlementSourceState.Read, true, EntitlementSourceState.Read),
                new(EntitlementNames.ExtendedVirtualAddressing, null, EntitlementSourceState.Read, null, EntitlementSourceState.Read),
            ],
        };

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal(
            [
                new DiagnosticsRow("get-task-allow", "true in the code signature; true in the profile"),
                new DiagnosticsRow("increased-memory-limit", "true in the code signature; true in the profile"),
                new DiagnosticsRow("extended-virtual-addressing", "absent in the code signature; absent in the profile"),
            ],
            page.EntitlementRows);
    }

    [Fact]
    public void NoEntitlementsSayThatTheHeadIsNotOnTheDevice()
    {
        var fakes = new FakeServices();

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal([new DiagnosticsRow("Entitlements", "None: this head does not run on the device")], page.EntitlementRows);
        Assert.Equal("None: this head does not run on the device.", page.CrashRecordSummary);
        Assert.False(page.HasCrashRecord);
    }

    [Fact]
    public async Task ACrashRecordShowsItsFieldsUntilItIsDismissed()
    {
        var files = new FakeFileSystem();
        files.SetContent("/caches/crash-record.bin", CrashRecordBytes.Build(test: true, signal: 0, message: "Test record"));
        var fakes = new FakeServices { CrashRecords = new CrashRecordStore("/caches", files, new RecordingLog()) };
        await fakes.CrashRecords.ReadAtLaunchAsync(TestContext.Current.CancellationToken);
        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.True(page.HasCrashRecord);
        Assert.Equal("A test record from the previous run.", page.CrashRecordSummary);
        Assert.Contains(new DiagnosticsRow("Test record", "Yes"), page.CrashRecordRows);
        Assert.Contains(new DiagnosticsRow("Message", "Test record"), page.CrashRecordRows);
        Assert.Contains(new DiagnosticsRow("Time", "2025-11-15 09:46:40 UTC"), page.CrashRecordRows);

        await page.DismissCrashRecordCommand.ExecuteAsync(null);

        Assert.False(page.HasCrashRecord);
        Assert.Equal("No crash record from a previous run.", page.CrashRecordSummary);
        Assert.Empty(page.CrashRecordRows);
        Assert.Null(files.Content("/caches/crash-record.bin"));
        Assert.False(page.DismissCrashRecordCommand.CanExecute(null));
    }

    [Fact]
    public async Task AMalformedRecordSaysWhy()
    {
        var files = new FakeFileSystem();
        files.SetContent("/caches/crash-record.bin", [1, 2, 3]);
        var fakes = new FakeServices { CrashRecords = new CrashRecordStore("/caches", files, new RecordingLog()) };
        await fakes.CrashRecords.ReadAtLaunchAsync(TestContext.Current.CancellationToken);

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.True(page.HasCrashRecord);
        Assert.Equal(
            "A crash record was found, but it is malformed: The file has 3 bytes; a crash record has exactly 128.",
            page.CrashRecordSummary);
    }

    [Fact]
    public void TheStorageFoldersShowTheirBackupMarks()
    {
        var fakes = new FakeServices
        {
            Folders =
            [
                new FolderState(StorageLayout.Prefixes, Exists: true, ExcludedFromBackup: true),
                new FolderState(StorageLayout.Library, Exists: true, ExcludedFromBackup: false),
                new FolderState(StorageLayout.Settings, Exists: true, ExcludedFromBackup: false),
                new FolderState(StorageLayout.Compat, Exists: true, ExcludedFromBackup: true),
                new FolderState(StorageLayout.Caches, Exists: true, ExcludedFromBackup: true),
                new FolderState(StorageLayout.Import, Exists: true, ExcludedFromBackup: false),
                new FolderState(StorageLayout.Runtime, Exists: false, ExcludedFromBackup: null),
            ],
        };

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal(
            [
                new DiagnosticsRow("Library/Application Support/Paneport/prefixes/", "Excluded from backup"),
                new DiagnosticsRow("Library/Application Support/Paneport/library/", "Should be excluded from backup, but not marked"),
                new DiagnosticsRow("Library/Application Support/Paneport/settings/", "Backed up"),
                new DiagnosticsRow("Library/Application Support/Paneport/compat/", "Should be backed up, but is marked as excluded"),
                new DiagnosticsRow("Library/Caches/Paneport/", "Excluded from backup by iOS; marked as excluded from backup"),
                new DiagnosticsRow("Documents/Import/", "The user's choice; shown in the Files app; not marked"),
                new DiagnosticsRow("Library/Application Support/Paneport/runtime/", "Missing"),
            ],
            page.StorageRows);
    }

    [Fact]
    public void TheBuildAndTheDeviceAreListed()
    {
        var fakes = new FakeServices();

        var page = new DiagnosticsViewModel(fakes.Host, fakes.ToDiagnosticsSources(), fakes.Log);

        Assert.Equal(
            [
                new DiagnosticsRow("Commit", "0123456789abcdef0123456789abcdef01234567"),
                new DiagnosticsRow("Version", "0.1.0 (7)"),
                new DiagnosticsRow("Configuration", "Release"),
                new DiagnosticsRow("Runtime", "Mono"),
                new DiagnosticsRow("Compilation", "Full ahead-of-time"),
                new DiagnosticsRow("Interpreter", "Off"),
                new DiagnosticsRow("Host import", "Framework"),
                new DiagnosticsRow("Crash chaining", "Not called: this head does not run on Mono"),
            ],
            page.BuildRows);
        Assert.Equal(
            [
                new DiagnosticsRow("Model identifier", "iPhone18,1"),
                new DiagnosticsRow("iOS version", "26.1"),
                new DiagnosticsRow("iOS build", "23B85"),
            ],
            page.DeviceRows);
    }
}
