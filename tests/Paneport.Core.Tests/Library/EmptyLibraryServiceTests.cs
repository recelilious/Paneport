// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;
using Xunit;

namespace Paneport.Core.Tests.Library;

// The iOS head's library until M7.1 and M6.5: no titles, and a cancelled load ends as cancelled.
public sealed class EmptyLibraryServiceTests
{
    [Fact]
    public async Task ItHasNoTitles() =>
        Assert.Empty(await new EmptyLibraryService().GetTitlesAsync(CancellationToken.None));

    [Fact]
    public async Task ACancelledLoadThrows() =>
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new EmptyLibraryService().GetTitlesAsync(new CancellationToken(canceled: true)));
}
