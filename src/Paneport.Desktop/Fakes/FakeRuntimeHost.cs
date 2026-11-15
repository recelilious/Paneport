// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Desktop.Fakes;

// Stands in for the native host, which exists only on the device. The values are the ones that
// docs/design/app-shell.md lists: the memory limit that C-0009 section 4 cites without Game Mode,
// and a footprint and available memory that add up to it, as the host's estimate does. It reports
// the interface version of the native host's headers, and sends no event.
internal sealed class FakeRuntimeHost : IRuntimeHost
{
    private const ulong BytesPerMegabyte = 1024 * 1024;

    public event EventHandler<HostEventInfo>? EventReceived
    {
        add { }
        remove { }
    }

    public string ImplementationName => "Fake host (desktop head sample values, not a device reading)";

    public HostAbiVersion AbiVersion { get; } = new(1, 1);

    public HostAbiVersion BindingsVersion { get; } = new(1, 1);

    public string ImportName => "None: the desktop head loads no native host";

    public int EventCount => 0;

    public HostEventInfo? LastEvent => null;

    public MemoryBudget GetMemoryBudget() => new(
        limitBytes: 6_144 * BytesPerMegabyte,
        availableBytes: 5_632 * BytesPerMegabyte,
        footprintBytes: 512 * BytesPerMegabyte,
        gameModeLevelReached: false);
}
