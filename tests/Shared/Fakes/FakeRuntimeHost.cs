// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Tests.Fakes;

// A runtime host whose values a test sets. It counts how often the budget is read.
internal sealed class FakeRuntimeHost : IRuntimeHost
{
    public const ulong BytesPerMegabyte = 1024 * 1024;

    public string ImplementationName { get; init; } = "Fake host for tests";

    public HostAbiVersion AbiVersion { get; init; } = new(1, 0);

    public MemoryBudget Budget { get; set; } = new(
        limitBytes: 6_144 * BytesPerMegabyte,
        availableBytes: 5_120 * BytesPerMegabyte,
        footprintBytes: 1_024 * BytesPerMegabyte,
        gameModeLevelReached: false);

    public int BudgetReads { get; private set; }

    public MemoryBudget GetMemoryBudget()
    {
        BudgetReads++;
        return Budget;
    }
}
