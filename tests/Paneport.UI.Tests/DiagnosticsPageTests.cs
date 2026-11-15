// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Paneport.Core.Diagnostics;
using Paneport.Core.Logging;
using Paneport.Core.Storage;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Paneport.UI.Views;
using Xunit;

namespace Paneport.UI.Tests;

// S1.3.7: the Diagnostics page, rendered headless, shows every section of the brief, and no
// forbidden item reaches its text for sample inputs that carry one: a team identifier, an
// application-identifier prefix and a device identifier in the profile, which the entitlement
// readers must discard (C-0012 section 4).
public sealed class DiagnosticsPageTests
{
    [AvaloniaFact]
    public async Task ShowsEverySectionWithoutAForbiddenItem()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices
        {
            Entitlements = EntitlementReport.Read(
                () => new MemoryStream(SigningSamples.Executable(SigningSamples.Entitlements(
                    true, $"<key>application-identifier</key>\n<string>{SigningSamples.Team}.io.github.recelilious.paneport</string>\n"))),
                () => SigningSamples.Profile(increasedMemoryLimit: true, getTaskAllow: true),
                new RecordingLog()),
            Folders = [.. StorageLayout.Folders.Select(folder => new FolderState(folder, true, folder.Backup == BackupPolicy.Excluded))],
        };
        var files = new FakeFileSystem();
        files.SetContent("/caches/crash-record.bin", CrashRecordBytes.Build(test: true, signal: 0, message: "Test record"));
        fakes.CrashRecords = new CrashRecordStore("/caches", files, new RecordingLog());
        await fakes.CrashRecords.ReadAtLaunchAsync(TestContext.Current.CancellationToken);
        using var harness = ShellHarness.Show(fakes, width: 390, height: 844);

        harness.OpenSection(ShellSection.Diagnostics);
        var page = harness.Page<DiagnosticsPage>();
        var texts = ShellHarness.TextsIn(page);

        foreach (var section in new[] { "Host", "Memory", "Entitlements", "Crash record", "Storage", "Build", "Device" })
        {
            Assert.Contains(section, texts);
        }

        Assert.Contains("increased-memory-limit", texts);
        Assert.Contains("true in the code signature; true in the profile", texts);
        Assert.Contains("A test record from the previous run.", texts);
        Assert.Contains("Library/Caches/Paneport/", texts);
        Assert.Contains("iPhone18,1", texts);
        Assert.Contains("23B85", texts);
        Assert.True(page.FindControl<Button>("DismissCrashRecord")?.IsEffectivelyVisible);
        Assert.True(page.FindControl<ItemsControl>("Timeline")?.ItemCount > 0);

        var all = string.Join('\n', texts);
        Assert.DoesNotContain(SigningSamples.Team, all, StringComparison.Ordinal);
        Assert.DoesNotContain(SigningSamples.DeviceIdentifier, all, StringComparison.Ordinal);
        Assert.DoesNotContain("application-identifier", all, StringComparison.Ordinal);
        Assert.DoesNotContain("io.github.recelilious.paneport", all, StringComparison.Ordinal);
        Assert.Empty(warnings.Entries);
    }

    [AvaloniaFact]
    public void TheWarningShowsForALimitFarBelowTheExpectedOne()
    {
        using var warnings = AvaloniaWarnings.Record();
        var fakes = new FakeServices();
        fakes.Host.Budget = new(3_379UL << 20, 3_000UL << 20, 379UL << 20, gameModeLevelReached: false);
        using var harness = ShellHarness.Show(fakes);

        harness.OpenSection(ShellSection.Diagnostics);

        var warning = harness.Page<DiagnosticsPage>().FindControl<TextBlock>("LimitWarning");
        Assert.True(warning?.IsEffectivelyVisible);
        Assert.StartsWith("The memory limit of 3,379 MB is far below", warning?.Text, StringComparison.Ordinal);
        Assert.Empty(warnings.Entries);
    }

    // The window is tall enough to show the whole page, so that the click reaches the button.
    [AvaloniaFact]
    public void DismissingTheCrashRecordHidesIt()
    {
        using var warnings = AvaloniaWarnings.Record();
        var files = new FakeFileSystem();
        files.SetContent("/caches/crash-record.bin", CrashRecordBytes.Build());
        var fakes = new FakeServices { CrashRecords = new CrashRecordStore("/caches", files, new RecordingLog()) };
        fakes.CrashRecords.ReadAtLaunchAsync(TestContext.Current.CancellationToken).GetAwaiter().GetResult();
        using var harness = ShellHarness.Show(fakes, height: 2400);
        harness.OpenSection(ShellSection.Diagnostics);
        var page = harness.Page<DiagnosticsPage>();

        harness.Click(page.FindControl<Button>("DismissCrashRecord")!);

        Assert.False(page.FindControl<Button>("DismissCrashRecord")?.IsEffectivelyVisible);
        Assert.Contains("No crash record from a previous run.", ShellHarness.TextsIn(page));
        Assert.Contains(fakes.Log.Lines, line => line.Level == LogLevel.Information && line.Message == "The crash record was dismissed.");
        Assert.Empty(warnings.Entries);
    }
}
