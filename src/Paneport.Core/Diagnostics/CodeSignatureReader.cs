// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Finds the entitlements in the code signature of a Mach-O executable: the XML property list
/// that <c>codesign --entitlements</c> embeds, which the kernel checks against the profile.
/// </summary>
/// <remarks>
/// It follows the formats of Apple's open-source <c>mach-o/loader.h</c>, <c>mach-o/fat.h</c> and
/// the code signing blobs of <c>codesign</c>: the <c>LC_CODE_SIGNATURE</c> load command points at
/// a super blob, whose index names the entitlements blob. Mach-O headers are little-endian on arm64;
/// fat headers and signature blobs are big-endian. It reads only those structures, by seeking.
/// </remarks>
public static class CodeSignatureReader
{
    private const uint MachMagic64 = 0xFEEDFACF;
    private const uint FatMagic = 0xCAFEBABE;
    private const uint CpuTypeArm64 = 0x0100000C;
    private const uint LoadCommandCodeSignature = 0x1D;
    private const uint SuperBlobMagic = 0xFADE0CC0;
    private const uint EntitlementsBlobMagic = 0xFADE7171;
    private const uint EntitlementsSlot = 5;
    private const int MachHeader64Size = 32;
    private const int FatArchSize = 20;
    private const int MaxLoadCommandsSize = 16 * 1024 * 1024;
    private const int MaxBlobCount = 64;
    private const int MaxEntitlementsSize = 1024 * 1024;

    /// <summary>Reads the entitlements of an executable's code signature.</summary>
    /// <param name="executable">The executable, readable and seekable.</param>
    /// <returns>
    /// The entitlements as an XML property list, or null when the executable is not signed or its
    /// signature holds no XML entitlements blob; <see cref="HasSignature"/> tells the two apart.
    /// </returns>
    /// <exception cref="InvalidDataException">The file is not a Mach-O executable of the expected form.</exception>
    public static byte[]? ReadEntitlements(Stream executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return FindSignature(executable) is { } signature
            ? ReadEntitlementsBlob(executable, signature.Offset, signature.Size)
            : null;
    }

    /// <summary>
    /// Tells whether an executable has a code signature, such as one without an XML entitlements
    /// blob: signed without entitlements, or by a tool that writes only their DER form.
    /// </summary>
    /// <param name="executable">The executable, readable and seekable.</param>
    /// <returns>True when the executable has an <c>LC_CODE_SIGNATURE</c> load command.</returns>
    /// <exception cref="InvalidDataException">The file is not a Mach-O executable of the expected form.</exception>
    public static bool HasSignature(Stream executable)
    {
        ArgumentNullException.ThrowIfNull(executable);
        return FindSignature(executable) is not null;
    }

    // Where the code signature's super blob lies, from the LC_CODE_SIGNATURE load command; null
    // when the executable has none.
    private static (long Offset, uint Size)? FindSignature(Stream executable)
    {
        var start = SliceStart(executable);
        var header = ReadAt(executable, start, MachHeader64Size);
        if (BinaryPrimitives.ReadUInt32LittleEndian(header) != MachMagic64)
        {
            throw new InvalidDataException("The file is not a 64-bit Mach-O executable.");
        }

        var commandCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(16));
        var commandsSize = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20));
        if (commandsSize > MaxLoadCommandsSize)
        {
            throw new InvalidDataException("The load commands are larger than any executable's.");
        }

        var commands = ReadAt(executable, start + MachHeader64Size, (int)commandsSize);
        var offset = 0;
        for (var index = 0; index < commandCount; index++)
        {
            if (offset + 8 > commands.Length)
            {
                throw new InvalidDataException("A load command runs past the load commands.");
            }

            var command = BinaryPrimitives.ReadUInt32LittleEndian(commands.AsSpan(offset));
            var size = BinaryPrimitives.ReadUInt32LittleEndian(commands.AsSpan(offset + 4));
            if (size < 8 || offset + size > commands.Length)
            {
                throw new InvalidDataException("A load command has an invalid size.");
            }

            if (command == LoadCommandCodeSignature)
            {
                if (size < 16)
                {
                    throw new InvalidDataException("The code signature's load command is too short.");
                }

                var dataOffset = BinaryPrimitives.ReadUInt32LittleEndian(commands.AsSpan(offset + 8));
                var dataSize = BinaryPrimitives.ReadUInt32LittleEndian(commands.AsSpan(offset + 12));
                return (start + dataOffset, dataSize);
            }

            offset += (int)size;
        }

        return null;
    }

    // The offset of the arm64 executable: 0 for a thin file, the slice's offset for a fat one.
    private static long SliceStart(Stream executable)
    {
        var magic = ReadAt(executable, 0, 8);
        if (BinaryPrimitives.ReadUInt32BigEndian(magic) != FatMagic)
        {
            return 0;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(magic.AsSpan(4));
        if (count > MaxBlobCount)
        {
            throw new InvalidDataException("The fat header names too many architectures.");
        }

        var architectures = ReadAt(executable, 8, (int)count * FatArchSize);
        for (var index = 0; index < count; index++)
        {
            var entry = architectures.AsSpan(index * FatArchSize);
            if (BinaryPrimitives.ReadUInt32BigEndian(entry) == CpuTypeArm64)
            {
                return BinaryPrimitives.ReadUInt32BigEndian(entry[8..]);
            }
        }

        throw new InvalidDataException("The fat file holds no arm64 executable.");
    }

    private static byte[]? ReadEntitlementsBlob(Stream executable, long superBlobOffset, uint superBlobSize)
    {
        if (superBlobSize < 12)
        {
            throw new InvalidDataException("The code signature is too short.");
        }

        var header = ReadAt(executable, superBlobOffset, 12);
        if (BinaryPrimitives.ReadUInt32BigEndian(header) != SuperBlobMagic)
        {
            throw new InvalidDataException("The code signature does not start with a super blob.");
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        if (count > MaxBlobCount || 12 + (count * 8) > superBlobSize)
        {
            throw new InvalidDataException("The code signature's index is invalid.");
        }

        var index = ReadAt(executable, superBlobOffset + 12, (int)count * 8);
        for (var position = 0; position < count; position++)
        {
            var slot = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan(position * 8));
            var blobOffset = BinaryPrimitives.ReadUInt32BigEndian(index.AsSpan((position * 8) + 4));
            if (slot != EntitlementsSlot)
            {
                continue;
            }

            if (blobOffset + 8L > superBlobSize)
            {
                throw new InvalidDataException("The entitlements blob lies outside the code signature.");
            }

            var blobHeader = ReadAt(executable, superBlobOffset + blobOffset, 8);
            var length = BinaryPrimitives.ReadUInt32BigEndian(blobHeader.AsSpan(4));
            if (BinaryPrimitives.ReadUInt32BigEndian(blobHeader) != EntitlementsBlobMagic
                || length < 8 || length > MaxEntitlementsSize || blobOffset + (long)length > superBlobSize)
            {
                throw new InvalidDataException("The entitlements blob is invalid.");
            }

            return ReadAt(executable, superBlobOffset + blobOffset + 8, (int)length - 8);
        }

        return null;
    }

    private static byte[] ReadAt(Stream stream, long offset, int count)
    {
        if (offset < 0 || offset + count > stream.Length)
        {
            throw new InvalidDataException("A structure lies outside the file.");
        }

        var buffer = new byte[count];
        stream.Position = offset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
