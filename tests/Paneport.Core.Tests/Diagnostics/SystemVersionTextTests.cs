// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Xunit;

namespace Paneport.Core.Tests.Diagnostics;

public sealed class SystemVersionTextTests
{
    [Theory]
    [InlineData("Version 26.1 (Build 23B85)", "23B85")]
    [InlineData("Version 27.0 (Build 24A5279h)", "24A5279h")]
    [InlineData("Version 26.1", SystemVersionText.Unknown)]
    [InlineData("Version 26.1 (Build )", SystemVersionText.Unknown)]
    [InlineData("Version 26.1 (Build 23B 85)", SystemVersionText.Unknown)]
    [InlineData("", SystemVersionText.Unknown)]
    [InlineData(null, SystemVersionText.Unknown)]
    public void TheBuildIsReadFromTheVersionText(string? text, string build)
    {
        Assert.Equal(build, SystemVersionText.BuildOf(text));
    }
}
