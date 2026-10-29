// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Desktop.Fakes;

// Stands in for the native host, which exists only on the device. The values are the ones that
// docs/design/app-shell.md lists: the memory limit that C-0009 section 4 cites without Game Mode,
// and a footprint and available memory that add up to it, as the host's estimate does.
internal sealed class FakeRuntimeHost : IRuntimeHost
{
    private const ulong BytesPerMegabyte = 1024 * 1024;

    public string ImplementationName => "Fake host (desktop head sample values, not a device reading)";

    public HostAbiVersion AbiVersion { get; } = new(1, 0);

    public MemoryBudget GetMemoryBudget() => new(
        limitBytes: 6_144 * BytesPerMegabyte,
        availableBytes: 5_632 * BytesPerMegabyte,
        footprintBytes: 512 * BytesPerMegabyte,
        gameModeLevelReached: false);
}
