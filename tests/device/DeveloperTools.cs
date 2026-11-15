// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Security.Cryptography;
using Paneport.Core.Logging;
using Paneport.Interop;
using Paneport.IOS;

namespace Paneport.DeviceTest;

// The first pages of the developer menu (M1.3, S1.3.10), which L1 steps 13 to 15 use on the
// device: the interface conformance test of C-0005 section 5, a test item in the keychain, and a
// test crash record through the host's test export.
internal static class DeveloperTools
{
    // The key of the token store page's item, which is never the sign-in's (SecureTokenStore.ItemKey).
    public const string TestItemKey = "developer-test-item";

    private const int TestItemLength = 32;

    // The class that tests/Paneport.Interop.Tests runs on macOS, here against the framework of the
    // ios-device-test build.
    public static DeveloperToolViewModel InterfaceConformance(ILog log) => new(
        "Interface conformance",
        "Compares the size and every member offset of each struct of the host interface between the host and Paneport.Interop.",
        [
            new DeveloperAction("Run", _ =>
            {
                var problems = LayoutConformance.Check();
                var structs = HostTestExports.ReadLayoutTable().Count;
                return Task.FromResult(problems.Count == 0
                    ? string.Create(CultureInfo.InvariantCulture, $"PASS: every struct size and member offset matches ({structs} structs).")
                    : "FAIL:\n" + string.Join('\n', problems));
            }),
        ],
        log);

    public static DeveloperToolViewModel TokenStore(KeychainItemStore keychain, ILog log)
    {
        byte[]? written = null;
        return new DeveloperToolViewModel(
            "Token store",
            "Writes a test item of random bytes to the keychain, reads it back with its attributes, and deletes it. The sign-in's own item is never touched.",
            [
                new DeveloperAction("Write test item", async cancellationToken =>
                {
                    written = RandomNumberGenerator.GetBytes(TestItemLength);
                    await keychain.WriteAsync(TestItemKey, written, cancellationToken);
                    return string.Create(CultureInfo.InvariantCulture, $"Written: {written.Length} bytes.");
                }),
                new DeveloperAction("Read", async cancellationToken =>
                {
                    var bytes = await keychain.ReadAsync(TestItemKey, cancellationToken);
                    if (bytes is null || keychain.ReadAttributes(TestItemKey) is not { } attributes)
                    {
                        return "No item.";
                    }

                    var match = written is not null && bytes.AsSpan().SequenceEqual(written)
                        ? "match"
                        : "no match: the item was not written in this run";
                    var accessible = attributes.AfterFirstUnlockThisDeviceOnly
                        ? "accessible after first unlock, this device only"
                        : "accessible otherwise than after first unlock, this device only";
                    var synchronizable = attributes.Synchronizable ? "synchronizable" : "not synchronizable";
                    return string.Create(CultureInfo.InvariantCulture, $"{match}, {bytes.Length} bytes; {accessible}; {synchronizable}.");
                }),
                new DeveloperAction("Delete", async cancellationToken =>
                {
                    await keychain.DeleteAsync(TestItemKey, cancellationToken);
                    written = null;
                    return "Deleted.";
                }),
            ],
            log);
    }

    public static DeveloperToolViewModel CrashRecord(ILog log) => new(
        "Crash record",
        "Writes a crash record marked as a test, through the writer that a fatal fault uses. Close the app in the app switcher and reopen it: Diagnostics then shows the record.",
        [
            new DeveloperAction("Write test record", _ =>
            {
                HostTestExports.WriteTestCrashRecord("Test record from the developer menu");
                return Task.FromResult("Written. Close the app in the app switcher, reopen it and open Diagnostics.");
            }),
        ],
        log);
}
