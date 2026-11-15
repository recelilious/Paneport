// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Tests.Fakes;

// A runtime host whose values a test sets. It counts how often the budget is read, and a test can
// raise an event as the adapter does on the UI thread.
internal sealed class FakeRuntimeHost : IRuntimeHost
{
    public const ulong BytesPerMegabyte = 1024 * 1024;

    public event EventHandler<HostEventInfo>? EventReceived;

    public string ImplementationName { get; init; } = "Fake host for tests";

    public HostAbiVersion AbiVersion { get; init; } = new(1, 1);

    public HostAbiVersion BindingsVersion { get; init; } = new(1, 1);

    public string ImportName { get; init; } = "Fake import for tests";

    public int EventCount { get; private set; }

    public HostEventInfo? LastEvent { get; private set; }

    public MemoryBudget Budget { get; set; } = new(
        limitBytes: 6_144 * BytesPerMegabyte,
        availableBytes: 5_120 * BytesPerMegabyte,
        footprintBytes: 1_024 * BytesPerMegabyte,
        gameModeLevelReached: false);

    public int BudgetReads { get; private set; }

    public bool FailReads { get; set; }

    public MemoryBudget GetMemoryBudget()
    {
        BudgetReads++;
        return FailReads ? throw new NativeHostException("SystemFailure", "The test fails the read.", null) : Budget;
    }

    public void Raise(HostEventInfo hostEvent)
    {
        EventCount++;
        LastEvent = hostEvent;
        EventReceived?.Invoke(this, hostEvent);
    }
}
