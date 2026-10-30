// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.ViewModels;
using Paneport.Tests.Fakes;
using Xunit;

namespace Paneport.Core.Tests;

public sealed class TitleDetailsViewModelTests
{
    [Fact]
    public void ShowsTheTitleUnderItsName()
    {
        var title = new TitleViewModel(TestTitles.LongName);

        var details = new TitleDetailsViewModel(title, new RecordingLog());

        Assert.Same(title, details.Title);
        Assert.Equal(TestTitles.LongName.Name, details.Header);
    }
}
