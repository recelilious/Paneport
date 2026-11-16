// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Parses the crash record of the native host: 128 bytes, little-endian, version 1, with the
/// layout of docs/design/native-host.md section 8, which this parser follows rather than the C code.
/// </summary>
public static class CrashRecordReader
{
    /// <summary>The size of a version 1 record, which is also the whole file's size.</summary>
    public const int RecordSize = 128;

    /// <summary>The record's magic: the bytes <c>P</c>, <c>P</c>, <c>C</c>, <c>R</c> read as little-endian.</summary>
    public const uint Magic = 0x52435050;

    /// <summary>The version of the layout that this parser reads.</summary>
    public const ushort Version = 1;

    private const int MessageOffset = 64;
    private const int MessageLength = 64;
    private const long NanosecondsPerSecond = 1_000_000_000;

    /// <summary>Parses the bytes of a crash record file.</summary>
    /// <param name="bytes">The whole file.</param>
    /// <returns>The record, or why the file is not one. An empty file is malformed too.</returns>
    public static CrashRecordReading Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != RecordSize)
        {
            return CrashRecordReading.Malformed(
                $"The file has {bytes.Length} bytes; a crash record has exactly {RecordSize}.");
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes) != Magic)
        {
            return CrashRecordReading.Malformed("The file does not start with the crash record's magic.");
        }

        var version = BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]);
        if (version != Version)
        {
            return CrashRecordReading.Malformed($"The record has version {version}; this app reads version {Version}.");
        }

        var size = BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]);
        if (size != RecordSize)
        {
            return CrashRecordReading.Malformed($"The record names a size of {size} bytes, not {RecordSize}.");
        }

        var seconds = BinaryPrimitives.ReadInt64LittleEndian(bytes[8..]);
        var nanoseconds = BinaryPrimitives.ReadInt32LittleEndian(bytes[16..]);
        if (nanoseconds is < 0 or >= (int)NanosecondsPerSecond)
        {
            return CrashRecordReading.Malformed("The record's time has nanoseconds out of range.");
        }

        DateTimeOffset time;
        try
        {
            time = DateTimeOffset.FromUnixTimeSeconds(seconds).AddTicks(nanoseconds / 100);
        }
        catch (ArgumentOutOfRangeException)
        {
            return CrashRecordReading.Malformed("The record's time is out of range.");
        }

        var message = bytes.Slice(MessageOffset, MessageLength);
        var end = message.IndexOf((byte)0);
        if (end < 0)
        {
            return CrashRecordReading.Malformed("The record's message is not terminated.");
        }

        return CrashRecordReading.Valid(new CrashRecord(
            Time: time,
            Signal: BinaryPrimitives.ReadInt32LittleEndian(bytes[20..]),
            SignalCode: BinaryPrimitives.ReadInt32LittleEndian(bytes[24..]),
            ThreadRole: BinaryPrimitives.ReadUInt32LittleEndian(bytes[28..]),
            FaultAddress: BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]),
            ProgramCounter: BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]),
            IsTest: (bytes[48] & 1) != 0,
            Message: Encoding.UTF8.GetString(message[..end])));
    }
}
