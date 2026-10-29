// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class PageViewModelTests
{
    [Fact]
    public async Task IsLoadingHoldsUntilTheLoadEnds()
    {
        var release = new TaskCompletionSource();
        var page = new ProbePage(new RecordingLog(), _ => release.Task);

        var loading = page.LoadAsync(TestContext.Current.CancellationToken);
        Assert.True(page.IsLoading);

        release.SetResult();
        await loading;

        Assert.False(page.IsLoading);
        Assert.Null(page.LoadError);
    }

    [Fact]
    public async Task AFailedLoadShowsAnErrorAndLogsOnlyTheExceptionType()
    {
        var log = new RecordingLog();
        var page = new ProbePage(log, _ => throw new InvalidOperationException("detail that must stay out of the log"));

        await page.LoadAsync(TestContext.Current.CancellationToken);

        Assert.False(page.IsLoading);
        Assert.NotNull(page.LoadError);
        var line = Assert.Single(log.Lines);
        Assert.Equal(LogLevel.Error, line.Level);
        Assert.Contains(nameof(InvalidOperationException), line.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("detail", line.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACancelledLoadIsNotAnError()
    {
        var log = new RecordingLog();
        var page = new ProbePage(log, token => Task.Delay(Timeout.Infinite, token));
        using var cancellation = new CancellationTokenSource();

        var loading = page.LoadAsync(cancellation.Token);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loading);
        Assert.False(page.IsLoading);
        Assert.Null(page.LoadError);
        Assert.Empty(log.Lines);
    }

    private sealed class ProbePage(ILog log, Func<CancellationToken, Task> load) : PageViewModel(log)
    {
        public override string Header => "Probe";

        protected override Task OnLoadAsync(CancellationToken cancellationToken) => load(cancellationToken);
    }
}
