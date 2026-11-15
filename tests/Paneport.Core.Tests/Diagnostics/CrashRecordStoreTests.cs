// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Logging;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// S1.3.8: the previous run's record at launch. It shows until dismissed and is then deleted; a
// malformed record is reported and dismissed the same way; a record that this run wrote waits
// for the next launch.
public sealed class CrashRecordStoreTests
{
    private const string Folder = "/container/Library/Caches/Paneport";
    private const string RecordPath = Folder + "/crash-record.bin";

    [Fact]
    public async Task NoFileMeansNoRecord()
    {
        var log = new RecordingLog();
        var store = new CrashRecordStore(Folder, new FakeFileSystem(), log);

        var reading = await store.ReadAtLaunchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CrashRecordStatus.None, reading.Status);
        Assert.Same(reading, store.Current);
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task ARecordShowsUntilItIsDismissedAndIsThenDeleted()
    {
        var files = new FakeFileSystem();
        files.SetContent(RecordPath, CrashRecordBytes.Build(test: true));
        var log = new RecordingLog();
        var store = new CrashRecordStore(Folder, files, log);

        var reading = await store.ReadAtLaunchAsync(TestContext.Current.CancellationToken);
        Assert.True(reading.Record?.IsTest);
        Assert.Contains(log.Lines, line => line.Level == LogLevel.Warning && line.Message.Contains("a test record", StringComparison.Ordinal));

        await store.DismissAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CrashRecordStatus.None, store.Current.Status);
        Assert.Null(files.Content(RecordPath));
    }

    [Fact]
    public async Task AMalformedRecordIsReportedAndNeverStopsTheApp()
    {
        var files = new FakeFileSystem();
        files.SetContent(RecordPath, CrashRecordBytes.Build().AsSpan(0, 40).ToArray());
        var log = new RecordingLog();
        var store = new CrashRecordStore(Folder, files, log);

        var reading = await store.ReadAtLaunchAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CrashRecordStatus.Malformed, reading.Status);
        Assert.Contains(log.Lines, line => line.Message.StartsWith("The crash record is malformed", StringComparison.Ordinal));
        await store.DismissAsync(TestContext.Current.CancellationToken);
        Assert.Null(files.Content(RecordPath));
    }

    [Fact]
    public async Task ARecordWrittenDuringThisRunWaitsForTheNextLaunch()
    {
        var files = new FakeFileSystem();
        files.SetContent(RecordPath, CrashRecordBytes.Build());
        var store = new CrashRecordStore(Folder, files, new RecordingLog());
        await store.ReadAtLaunchAsync(TestContext.Current.CancellationToken);

        // The developer menu writes a test record, which replaces the file.
        var newer = CrashRecordBytes.Build(test: true, message: "written in this run");
        files.SetContent(RecordPath, newer);
        Assert.False(store.Current.Record?.IsTest);

        await store.DismissAsync(TestContext.Current.CancellationToken);

        Assert.Equal(newer, files.Content(RecordPath));
        var next = new CrashRecordStore(Folder, files, new RecordingLog());
        Assert.True((await next.ReadAtLaunchAsync(TestContext.Current.CancellationToken)).Record?.IsTest);
    }

    [Fact]
    public async Task AnUnreadableFileIsReportedAndDismissed()
    {
        var files = new FakeFileSystem();
        files.SetContent(RecordPath, CrashRecordBytes.Build());
        files.FailReads = true;
        var log = new RecordingLog();
        var store = new CrashRecordStore(Folder, files, log);

        var reading = await store.ReadAtLaunchAsync(TestContext.Current.CancellationToken);

        Assert.Equal("The crash record file could not be read.", reading.Problem);
        Assert.Equal("Reading the crash record failed with IOException.", Assert.Single(log.Lines).Message);
        await store.DismissAsync(TestContext.Current.CancellationToken);
        Assert.Null(files.Content(RecordPath));
    }
}
