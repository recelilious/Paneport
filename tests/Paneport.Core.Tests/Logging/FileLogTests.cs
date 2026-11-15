// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;
using Paneport.Core.Tests.Compat;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Logging;

// The log file of the device, in a temporary folder: lines from many threads arrive whole, each
// with its UTC time, level and source, and the previous run's file is kept once.
public sealed class FileLogTests
{
    [Fact]
    public void LinesFromManyThreadsArriveWholeWithTheirTimeLevelAndSource()
    {
        using var folder = new TemporaryFolder();
        var time = new FakeTimeProvider();
        string path;
        using (var log = new FileLog(folder.Path, time))
        {
            path = log.Path;
            var writers = Enumerable.Range(0, 8).Select(thread => new Thread(() =>
            {
                for (var line = 0; line < 100; line++)
                {
                    log.Write(LogLevel.Information, $"Thread{thread}", $"line {line}");
                }
            })).ToList();
            writers.ForEach(writer => writer.Start());
            writers.ForEach(writer => writer.Join());
        }

        var lines = File.ReadAllLines(path);
        Assert.Equal(800, lines.Length);
        Assert.All(lines, line => Assert.Matches(@"^2026-11-15T12:00:00\.000Z Information Thread\d: line \d+$", line));
        for (var thread = 0; thread < 8; thread++)
        {
            var mine = lines.Where(line => line.Contains($" Thread{thread}: ", StringComparison.Ordinal)).ToList();
            Assert.Equal(Enumerable.Range(0, 100).Select(line => $"line {line}"), mine.Select(line => line[(line.LastIndexOf(':') + 2)..]));
        }
    }

    [Fact]
    public void ThePreviousRunsFileIsKeptOnce()
    {
        using var folder = new TemporaryFolder();
        var time = new FakeTimeProvider();
        using (var first = new FileLog(folder.Path, time))
        {
            first.Write(LogLevel.Warning, "Run", "first");
        }

        using (var second = new FileLog(folder.Path, time))
        {
            second.Write(LogLevel.Error, "Run", "second");
        }

        using (var third = new FileLog(folder.Path, time))
        {
            third.Write(LogLevel.Debug, "Run", "third");
        }

        Assert.Equal(["2026-11-15T12:00:00.000Z Debug Run: third"], File.ReadAllLines(Path.Combine(folder.Path, FileLog.FileName)));
        Assert.Equal(["2026-11-15T12:00:00.000Z Error Run: second"], File.ReadAllLines(Path.Combine(folder.Path, FileLog.PreviousFileName)));
    }

    [Fact]
    public void ARunsFileStopsAtItsLimitWithOneLastLine()
    {
        using var folder = new TemporaryFolder();
        var line = new string('x', 1024);
        string path;
        using (var log = new FileLog(folder.Path, new FakeTimeProvider()))
        {
            path = log.Path;
            for (var count = 0; count < (FileLog.MaxBytes / 1024) + 50; count++)
            {
                log.Write(LogLevel.Information, "Big", line);
            }
        }

        Assert.True(new FileInfo(path).Length <= FileLog.MaxBytes + 256);
        Assert.Equal("The log reached its size limit; later lines are dropped.", File.ReadLines(path).Last());
    }
}
