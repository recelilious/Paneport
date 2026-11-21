// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Paneport.Steam.Apps;
using Paneport.Steam.Content;

namespace Paneport.Steam.Tests.Fakes;

// App information and manifests made by the tests, with made-up IDs far above any that Steam has
// assigned, so that none names a real app or depot (C-0011 section 1).
internal static class SyntheticContent
{
    public const uint AppId = 4_100_000_100;
    public const uint WindowsDepot = 4_100_000_111;
    public const uint MacDepot = 4_100_000_112;
    public const uint OlderWindowsDepot = 4_100_000_113;
    public const ulong WindowsManifest = 4_100_000_000_111;
    public const ulong OlderManifest = 4_100_000_000_113;

    public static KeyValueNode Value(string name, string value) => new(name, value);

    public static KeyValueNode Section(string name, params KeyValueNode[] children) => new(name, children);

    // An app with a Windows depot in the newer manifest form, a macOS depot, a depot for every
    // platform in the older form, and a branches entry that is no depot.
    public static KeyValueNode AppInfo() =>
        Section(
            "appinfo",
            Section("common", Value("name", "Lantern Test App")),
            Section(
                "depots",
                Section(
                    "4100000111",
                    Value("name", "Lantern Test App Content"),
                    Section("config", Value("oslist", "windows"), Value("osarch", "64")),
                    Section("manifests", Section("public", Value("gid", "4100000000111"), Value("size", "1048576"), Value("download", "524288")))),
                Section(
                    "4100000112",
                    Section("config", Value("oslist", "macos")),
                    Section("manifests", Section("public", Value("gid", "4100000000112"), Value("size", "4096")))),
                Section(
                    "4100000113",
                    Value("maxsize", "2048"),
                    Section("manifests", Value("public", "4100000000113"))),
                Section("branches", Section("public", Value("buildid", "100")))));

    // A chunk of the given content, with the entry that a manifest gives it.
    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "A manifest names each chunk by the SHA-1 hash of its content; the test makes such an ID.")]
    public static (ManifestChunk Entry, byte[] Content) Chunk(int length, byte seed)
    {
        var content = new byte[length];
        for (var i = 0; i < content.Length; i++)
        {
            content[i] = (byte)(seed + (i * 31));
        }

        var entry = new ManifestChunk(SHA1.HashData(content), checksum: 0, offset: 0, compressedLength: (uint)length / 2, uncompressedLength: (uint)length);
        return (entry, content);
    }

    // A manifest of a folder and three files, two of which share a chunk.
    public static (DepotManifestData Manifest, IReadOnlyList<(ManifestChunk Entry, byte[] Content)> Chunks) Manifest()
    {
        var first = Chunk(64, 1);
        var shared = Chunk(32, 2);
        var last = Chunk(48, 3);
        var files = new List<ManifestFile>
        {
            new("data", 0, isDirectory: true, []),
            new("data/first.bin", 96, isDirectory: false, [first.Entry, shared.Entry]),
            new("data/second.bin", 80, isDirectory: false, [shared.Entry, last.Entry]),
            new("empty.txt", 0, isDirectory: false, []),
        };
        return (new DepotManifestData(WindowsDepot, WindowsManifest, files), [first, shared, last]);
    }
}
