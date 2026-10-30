// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Library;

namespace Paneport.Tests.Fakes;

// A library that lists the titles a test gives it, or fails when the test sets a failure.
internal sealed class FakeLibraryService(IReadOnlyList<Title> titles) : ILibraryService
{
    public Exception? Failure { get; set; }

    public Task<IReadOnlyList<Title>> GetTitlesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Failure is null ? Task.FromResult(titles) : Task.FromException<IReadOnlyList<Title>>(Failure);
    }
}
