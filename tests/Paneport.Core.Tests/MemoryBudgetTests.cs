// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Xunit;

namespace Paneport.Core.Tests;

// C-0005 section 2: the budget is the memory limit, the available memory, the current footprint
// and whether the limit reached the Game Mode level.
public sealed class MemoryBudgetTests
{
    [Fact]
    public void KeepsItsFourValues()
    {
        var budget = new MemoryBudget(
            limitBytes: 8_589_934_592,
            availableBytes: 7_516_192_768,
            footprintBytes: 1_073_741_824,
            gameModeLevelReached: true);

        Assert.Equal(8_589_934_592ul, budget.LimitBytes);
        Assert.Equal(7_516_192_768ul, budget.AvailableBytes);
        Assert.Equal(1_073_741_824ul, budget.FootprintBytes);
        Assert.True(budget.GameModeLevelReached);
    }
}
