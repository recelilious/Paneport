// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Buffers.Binary;
using System.Text;

namespace Paneport.Tests.Fakes;

// Synthetic code signatures and profiles, built from the public formats of Mach-O and of Apple's
// code signing blobs, and made-up account values that exist only in memory, so that no file of
// the repository holds an identifier's shape (C-0011 section 1).
internal static class SigningSamples
{
    // A made-up team identifier and device identifier, assembled at run time.
    public static string Team { get; } = string.Concat("ZQ", "9", "XW", "7", "VU", "5", "TS");

    public static string DeviceIdentifier { get; } = string.Concat(Enumerable.Repeat("0123", 6)) + "abcdef0123456789";

    public static string Plist(string body) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + $"<plist version=\"1.0\">\n{body}\n</plist>\n";

    public static string Entitlements(bool increasedMemoryLimit, string extra = "") =>
        Plist($"<dict>\n<key>com.apple.developer.kernel.increased-memory-limit</key>\n<{(increasedMemoryLimit ? "true" : "false")}/>\n{extra}</dict>");

    // A profile as a re-signing tool embeds one: a signed message whose content is the property
    // list, between bytes of its own. It names the team, the application identifier, a device
    // and the entitlements that it grants.
    public static byte[] Profile(bool increasedMemoryLimit, bool getTaskAllow)
    {
        var plist = Plist(
            "<dict>\n"
                + $"<key>TeamIdentifier</key>\n<array>\n<string>{Team}</string>\n</array>\n"
                + $"<key>ProvisionedDevices</key>\n<array>\n<string>{DeviceIdentifier}</string>\n</array>\n"
                + "<key>Entitlements</key>\n<dict>\n"
                + $"<key>application-identifier</key>\n<string>{Team}.io.github.recelilious.paneport</string>\n"
                + $"<key>get-task-allow</key>\n<{(getTaskAllow ? "true" : "false")}/>\n"
                + $"<key>com.apple.developer.kernel.increased-memory-limit</key>\n<{(increasedMemoryLimit ? "true" : "false")}/>\n"
                + "</dict>\n</dict>");
        return [0x30, 0x82, 0x10, 0x00, 0x06, 0x09, .. Encoding.UTF8.GetBytes(plist), 0xA0, 0x82, 0x00, 0x10];
    }

    // A thin arm64 executable whose one load command is LC_CODE_SIGNATURE, pointing at a super
    // blob with a code directory slot and the entitlements slot; or no load command at all.
    public static byte[] Executable(string? entitlements)
    {
        var bytes = new List<byte>();
        void little(uint value) => bytes.AddRange(BitConverter.GetBytes(value));
        void big(uint value)
        {
            var buffer = new byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
            bytes.AddRange(buffer);
        }

        little(0xFEEDFACF);
        little(0x0100000C);
        little(0);
        little(2);
        little(entitlements is null ? 0U : 1U);
        little(entitlements is null ? 0U : 16U);
        little(0);
        little(0);
        if (entitlements is null)
        {
            bytes.AddRange(new byte[64]);
            return [.. bytes];
        }

        var xml = Encoding.UTF8.GetBytes(entitlements);
        const uint SignatureOffset = 128;
        const uint DirectoryLength = 16;
        var entitlementsLength = (uint)(8 + xml.Length);
        var superLength = 12 + 16 + DirectoryLength + entitlementsLength;
        little(0x1D);
        little(16);
        little(SignatureOffset);
        little(superLength);
        while (bytes.Count < SignatureOffset)
        {
            bytes.Add(0);
        }

        big(0xFADE0CC0);
        big(superLength);
        big(2);
        big(0);
        big(28);
        big(5);
        big(28 + DirectoryLength);
        big(0xFADE0C02);
        big(DirectoryLength);
        big(0);
        big(0);
        big(0xFADE7171);
        big(entitlementsLength);
        bytes.AddRange(xml);
        return [.. bytes];
    }

    // A fat file with one arm64 slice at the given offset.
    public static byte[] Fat(byte[] slice)
    {
        const int Offset = 4096;
        var bytes = new byte[Offset + slice.Length];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, 0xCAFEBABE);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 0x0100000C);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), Offset);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)slice.Length);
        slice.CopyTo(bytes, Offset);
        return bytes;
    }
}
