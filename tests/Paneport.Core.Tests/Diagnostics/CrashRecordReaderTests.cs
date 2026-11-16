// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

// S1.3.8: the crash record reader on synthetic records built from the documented layout. A valid
// record, a test record, a truncated one, a wrong version and an empty file each give the
// expected result, and so do the other ways a file can differ from the layout.
public sealed class CrashRecordReaderTests
{
    [Fact]
    public void AValidRecordGivesEveryField()
    {
        var reading = CrashRecordReader.Parse(CrashRecordBytes.Build());

        Assert.Equal(CrashRecordStatus.Valid, reading.Status);
        var record = Assert.IsType<CrashRecord>(reading.Record);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1_763_200_000).AddMilliseconds(250), record.Time);
        Assert.Equal(11, record.Signal);
        Assert.Equal("SIGSEGV", record.SignalName);
        Assert.Equal(2, record.SignalCode);
        Assert.Equal(1U, record.ThreadRole);
        Assert.Equal("main thread", record.ThreadRoleName);
        Assert.Equal(0x10UL, record.FaultAddress);
        Assert.Equal(0x1_0000_4000UL, record.ProgramCounter);
        Assert.False(record.IsTest);
        Assert.Equal("A fault outside the guest path.", record.Message);
        Assert.Null(reading.Problem);
    }

    [Fact]
    public void ATestRecordCarriesItsFlag()
    {
        var reading = CrashRecordReader.Parse(CrashRecordBytes.Build(signal: 0, code: 0, threadRole: 0, faultAddress: 0, programCounter: 0, test: true, message: "Test record"));

        var record = Assert.IsType<CrashRecord>(reading.Record);
        Assert.True(record.IsTest);
        Assert.Equal("none (test record)", record.SignalName);
        Assert.Equal("other", record.ThreadRoleName);
        Assert.Equal("Test record", record.Message);
    }

    [Fact]
    public void AMessageOfSixtyThreeBytesIsReadWhole()
    {
        var message = new string('m', 63);

        Assert.Equal(message, CrashRecordReader.Parse(CrashRecordBytes.Build(message: message)).Record?.Message);
    }

    [Fact]
    public void ATruncatedRecordIsMalformed()
    {
        var reading = CrashRecordReader.Parse(CrashRecordBytes.Build().AsSpan(0, 127));

        Assert.Equal(CrashRecordStatus.Malformed, reading.Status);
        Assert.Null(reading.Record);
        Assert.Equal("The file has 127 bytes; a crash record has exactly 128.", reading.Problem);
    }

    [Fact]
    public void ALongerFileIsMalformed()
    {
        var longer = new byte[129];
        CrashRecordBytes.Build().CopyTo(longer, 0);

        Assert.Equal(CrashRecordStatus.Malformed, CrashRecordReader.Parse(longer).Status);
    }

    [Fact]
    public void AWrongVersionIsMalformed()
    {
        var reading = CrashRecordReader.Parse(CrashRecordBytes.Build(version: 2));

        Assert.Equal(CrashRecordStatus.Malformed, reading.Status);
        Assert.Equal("The record has version 2; this app reads version 1.", reading.Problem);
    }

    [Fact]
    public void AnEmptyFileIsMalformed()
    {
        var reading = CrashRecordReader.Parse([]);

        Assert.Equal(CrashRecordStatus.Malformed, reading.Status);
        Assert.Equal("The file has 0 bytes; a crash record has exactly 128.", reading.Problem);
    }

    [Fact]
    public void AnotherMagicIsMalformed()
    {
        var record = CrashRecordBytes.Build();
        record[3] = (byte)'X';

        Assert.Equal("The file does not start with the crash record's magic.", CrashRecordReader.Parse(record).Problem);
    }

    [Fact]
    public void AnotherSizeIsMalformed()
    {
        Assert.Equal(
            "The record names a size of 64 bytes, not 128.",
            CrashRecordReader.Parse(CrashRecordBytes.Build(size: 64)).Problem);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1_000_000_000)]
    public void NanosecondsOutOfRangeAreMalformed(int nanoseconds)
    {
        Assert.Equal(CrashRecordStatus.Malformed, CrashRecordReader.Parse(CrashRecordBytes.Build(nanoseconds: nanoseconds)).Status);
    }

    [Fact]
    public void AMessageWithoutItsTerminatorIsMalformed()
    {
        var record = CrashRecordBytes.Build();
        record.AsSpan(64).Fill((byte)'m');

        Assert.Equal("The record's message is not terminated.", CrashRecordReader.Parse(record).Problem);
    }

    [Fact]
    public void ATimeOutOfRangeIsMalformed()
    {
        Assert.Equal(CrashRecordStatus.Malformed, CrashRecordReader.Parse(CrashRecordBytes.Build(seconds: long.MaxValue)).Status);
    }
}
