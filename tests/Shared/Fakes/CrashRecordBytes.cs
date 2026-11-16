// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace Paneport.Tests.Fakes;

// Synthetic crash records built from the documented layout of docs/design/native-host.md
// section 8, not from the C code: 128 bytes, little-endian, version 1.
internal static class CrashRecordBytes
{
    public static byte[] Build(
        long seconds = 1_763_200_000,
        int nanoseconds = 250_000_000,
        int signal = 11,
        int code = 2,
        uint threadRole = 1,
        ulong faultAddress = 0x10,
        ulong programCounter = 0x1_0000_4000,
        bool test = false,
        string message = "A fault outside the guest path.",
        ushort version = 1,
        ushort size = 128)
    {
        var record = new byte[128];
        "PPCR"u8.CopyTo(record);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(4), version);
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(6), size);
        BinaryPrimitives.WriteInt64LittleEndian(record.AsSpan(8), seconds);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(16), nanoseconds);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(20), signal);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(24), code);
        BinaryPrimitives.WriteUInt32LittleEndian(record.AsSpan(28), threadRole);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(32), faultAddress);
        BinaryPrimitives.WriteUInt64LittleEndian(record.AsSpan(40), programCounter);
        record[48] = test ? (byte)1 : (byte)0;
        var text = Encoding.UTF8.GetBytes(message);
        text.AsSpan(0, Math.Min(text.Length, 63)).CopyTo(record.AsSpan(64));
        return record;
    }
}
