// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;
using Paneport.Core.Logging;
using Paneport.Core.Settings;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Settings;

// S1.3.5: the settings file over a fake file system. Settings round-trip, an interrupted write
// leaves the previous file, an unreadable file falls back to the defaults with a log line, and
// the store runs its saves and loads in order (app-shell.md section 10).
public sealed class FileSettingsStoreTests
{
    private const string Folder = "/container/Library/Application Support/Paneport/settings";
    private const string FilePath = Folder + "/settings.json";
    private const string TemporaryPath = FilePath + ".tmp";

    private static AppSettings German => new() { Locale = "de-DE" };

    private static AppSettings Japanese => new() { Locale = "ja-JP" };

    [Fact]
    public async Task SettingsRoundTripThroughTheFile()
    {
        var files = new FakeFileSystem();
        using var store = new FileSettingsStore(Folder, files, new RecordingLog());

        await store.SaveAsync(German, TestContext.Current.CancellationToken);

        Assert.Equal(German, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal([FilePath], files.Replacements);
        Assert.Null(files.Content(TemporaryPath));
    }

    [Fact]
    public async Task TheFileIsJsonWithItsVersion()
    {
        var files = new FakeFileSystem();
        using var store = new FileSettingsStore(Folder, files, new RecordingLog());

        await store.SaveAsync(German, TestContext.Current.CancellationToken);

        Assert.Equal("{\n  \"version\": 1,\n  \"locale\": \"de-DE\"\n}", Encoding.UTF8.GetString(files.Content(FilePath)!));
    }

    [Fact]
    public async Task NoFileGivesTheDefaultsWithoutALogLine()
    {
        var log = new RecordingLog();
        using var store = new FileSettingsStore(Folder, new FakeFileSystem(), log);

        Assert.Equal(AppSettings.Default, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Empty(log.Lines);
    }

    [Fact]
    public async Task AnInterruptedWriteLeavesThePreviousFile()
    {
        var files = new FakeFileSystem();
        var log = new RecordingLog();
        using var store = new FileSettingsStore(Folder, files, log);
        await store.SaveAsync(German, TestContext.Current.CancellationToken);
        var previous = files.Content(FilePath);

        files.FailNextWrite = true;
        await Assert.ThrowsAsync<IOException>(() => store.SaveAsync(Japanese, TestContext.Current.CancellationToken));

        Assert.Equal(previous, files.Content(FilePath));
        Assert.Null(files.Content(TemporaryPath));
        Assert.Equal(German, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Empty(log.Lines);
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{\"version\": 2, \"locale\": \"de-DE\"}")]
    [InlineData("{\"version\": 1}")]
    [InlineData("{\"version\": 1, \"locale\": \"  \"}")]
    [InlineData("[]")]
    public async Task AnUnreadableFileFallsBackToTheDefaultsWithALogLine(string content)
    {
        var files = new FakeFileSystem();
        files.SetContent(FilePath, Encoding.UTF8.GetBytes(content));
        var log = new RecordingLog();
        using var store = new FileSettingsStore(Folder, files, log);

        Assert.Equal(AppSettings.Default, await store.LoadAsync(TestContext.Current.CancellationToken));

        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.Equal("The settings file could not be read; the defaults apply.", line.Message);
    }

    [Fact]
    public async Task AFileThatCannotBeOpenedFallsBackToTheDefaultsWithItsTypeLogged()
    {
        var files = new FakeFileSystem();
        files.SetContent(FilePath, Encoding.UTF8.GetBytes("{\"version\": 1, \"locale\": \"de-DE\"}"));
        files.FailReads = true;
        var log = new RecordingLog();
        using var store = new FileSettingsStore(Folder, files, log);

        Assert.Equal(AppSettings.Default, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Reading the settings file failed with IOException; the defaults apply.", Assert.Single(log.Lines).Message);
    }

    [Fact]
    public async Task ASaveCancelledBeforeItsFileIsReplacedLeavesThePreviousFile()
    {
        var files = new FakeFileSystem();
        using var store = new FileSettingsStore(Folder, files, new RecordingLog());
        await store.SaveAsync(German, TestContext.Current.CancellationToken);

        using var newer = new CancellationTokenSource();
        files.HoldWrites = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var save = store.SaveAsync(Japanese, newer.Token);
        await newer.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => save);
        files.HoldWrites = null;
        Assert.Equal(German, await store.LoadAsync(TestContext.Current.CancellationToken));
        Assert.Single(files.Replacements);
    }

    [Fact]
    public async Task ALoadWaitsForAPendingSave()
    {
        var files = new FakeFileSystem();
        using var store = new FileSettingsStore(Folder, files, new RecordingLog());
        await store.SaveAsync(German, TestContext.Current.CancellationToken);

        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        files.HoldWrites = hold;
        var save = store.SaveAsync(Japanese, TestContext.Current.CancellationToken);
        var load = store.LoadAsync(TestContext.Current.CancellationToken);
        await Task.Delay(50, TestContext.Current.CancellationToken);
        Assert.False(load.IsCompleted);

        hold.SetResult();
        await save;
        Assert.Equal(Japanese, await load);
    }
}
