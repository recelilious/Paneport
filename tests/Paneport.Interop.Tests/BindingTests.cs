// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;
using Xunit;

namespace Paneport.Interop.Tests;

// Every binding, called against the host test library (S1.2.7). Each test leaves the handlers
// as it found them, unset.
public sealed class BindingTests(HostFixture host)
{
    private const ulong Mebibyte = 1024 * 1024;
    private const int WaitMilliseconds = 30_000;

    [Fact]
    public void TheMemoryBudgetAddsTheTwoReadings()
    {
        HostTestExports.SetMemoryStub(5000 * Mebibyte, 1144 * Mebibyte);
        Assert.Equal(
            new HostMemoryBudget(6144 * Mebibyte, 5000 * Mebibyte, 1144 * Mebibyte, false),
            NativeHost.GetMemoryBudget());

        // A published reading at Game Mode: 7,259 MiB available plus 932 MiB of footprint.
        HostTestExports.SetMemoryStub(7259 * Mebibyte, 932 * Mebibyte);
        Assert.True(NativeHost.GetMemoryBudget().GameModeLevelReached);
    }

    [Fact]
    public void AFailedCallThrowsWithTheHostsStatusAndMessage()
    {
        var exception = Assert.Throws<HostException>(() => host.Main.Run(() => NativeHost.Initialize(null)));

        Assert.Equal(HostStatus.AlreadyInitialized, exception.Status);
        Assert.Contains("once per process", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AMainThreadCallOnAnotherThreadFails()
    {
        var exception = Assert.Throws<HostException>(() => NativeHost.SetEventHandler(_ => { }));

        Assert.Equal(HostStatus.WrongThread, exception.Status);
        Assert.Contains("main thread", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EventsReachTheHandlerOnTheEventThread()
    {
        using var received = new BlockingCollection<(HostEvent Event, int Thread)>();
        host.Main.Run(() => NativeHost.SetEventHandler(hostEvent =>
            received.Add((hostEvent, Environment.CurrentManagedThreadId))));
        try
        {
            HostTestExports.PostEvent(41, -7);

            Assert.True(received.TryTake(out var item, WaitMilliseconds, TestContext.Current.CancellationToken));
            Assert.Equal(new HostEvent(41, -7, 0, 0), item.Event);
            Assert.NotEqual(Environment.CurrentManagedThreadId, item.Thread);
            Assert.NotEqual(host.Main.ManagedThreadId, item.Thread);
        }
        finally
        {
            host.Main.Run(() => NativeHost.SetEventHandler(null));
        }
    }

    [Fact]
    public void LogLinesReachTheHandlerOnTheEventThread()
    {
        const string Text = "a line from the binding tests";
        using var lines = new BlockingCollection<(HostLogLine Line, int Thread)>();
        host.Main.Run(() => NativeHost.SetLogHandler(line => lines.Add((line, Environment.CurrentManagedThreadId))));
        try
        {
            HostTestExports.Log(HostLogLevel.Warning, Text);

            // The host's own lines may come first, such as the one it logged when it started.
            (HostLogLine Line, int Thread) item;
            do
            {
                Assert.True(lines.TryTake(out item, WaitMilliseconds, TestContext.Current.CancellationToken));
            }
            while (item.Line.Text != Text);

            Assert.Equal(HostLogLevel.Warning, item.Line.Level);
            Assert.NotEqual(0UL, item.Line.ThreadId);
            Assert.NotEqual(Environment.CurrentManagedThreadId, item.Thread);
            Assert.NotEqual(host.Main.ManagedThreadId, item.Thread);
        }
        finally
        {
            host.Main.Run(() => NativeHost.SetLogHandler(null));
        }
    }

    [Fact]
    public void AnExceptionInAHandlerIsCaughtAndReported()
    {
        using var failures = new BlockingCollection<Exception>();
        using var events = new BlockingCollection<HostEvent>();
        NativeHost.SetCallbackFailureHandler(failures.Add);
        host.Main.Run(() => NativeHost.SetEventHandler(hostEvent =>
        {
            if (hostEvent.Kind == 13)
            {
                throw new InvalidOperationException("thrown by the event handler");
            }

            events.Add(hostEvent);
        }));
        try
        {
            HostTestExports.PostEvent(13, 0);
            Assert.True(failures.TryTake(out var failure, WaitMilliseconds, TestContext.Current.CancellationToken));
            Assert.Equal("thrown by the event handler", Assert.IsType<InvalidOperationException>(failure).Message);

            // The exception never reached the host, which still runs and delivers the next event.
            HostTestExports.PostEvent(14, 1);
            Assert.True(events.TryTake(out var next, WaitMilliseconds, TestContext.Current.CancellationToken));
            Assert.Equal(14U, next.Kind);
        }
        finally
        {
            host.Main.Run(() => NativeHost.SetEventHandler(null));
            NativeHost.SetCallbackFailureHandler(null);
        }
    }

    [Fact]
    public void TextRoundTripsThroughHostMemory()
    {
        var text = "h" + (char)0xE9 + "llo from host memory";

        Assert.Equal(text, HostTestExports.DuplicateText(text));
        Assert.Equal(string.Empty, HostTestExports.DuplicateText(string.Empty));
    }

    [Fact]
    public void ATestCrashRecordHasTheDocumentedLayout()
    {
        host.Main.Run(() => HostTestExports.WriteTestCrashRecord("from the binding tests"));

        var record = File.ReadAllBytes(Path.Combine(host.CrashRecordFolder, "crash-record.bin"));
        Assert.Equal(128, record.Length);
        Assert.Equal("PPCR"u8.ToArray(), record[..4]);
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(4)));
        Assert.Equal(128, BinaryPrimitives.ReadUInt16LittleEndian(record.AsSpan(6)));
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        Assert.InRange(BinaryPrimitives.ReadInt64LittleEndian(record.AsSpan(8)), now - 600, now + 5);
        Assert.Equal(0, BinaryPrimitives.ReadInt32LittleEndian(record.AsSpan(20)));
        Assert.Equal(1U, BinaryPrimitives.ReadUInt32LittleEndian(record.AsSpan(28)));
        Assert.Equal(1, record[48]);
        Assert.Equal("from the binding tests", Encoding.UTF8.GetString(record.AsSpan(64, 64)).TrimEnd('\0'));
    }
}
