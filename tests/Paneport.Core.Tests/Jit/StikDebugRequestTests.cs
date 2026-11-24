// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Jit;
using Xunit;

namespace Paneport.Core.Tests.Jit;

// S3.1.6: the URL that asks StikDebug for JIT names its built-in script and carries the bundle
// identifier and the process identifier, in a fixed order (C-0006 section 3 step 2).
public sealed class StikDebugRequestTests
{
    [Fact]
    public void TheRequestNamesTheBuiltInScript()
    {
        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=io.github.recelilious.paneport&pid=4242&script-name=universal.js",
            StikDebugRequest.Build("io.github.recelilious.paneport", 4242));
    }

    [Fact]
    public void ABundleIdentifierKeepsItsLettersDigitsHyphensAndPeriods()
    {
        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=com.example.My-App2&pid=1&script-name=universal.js",
            StikDebugRequest.Build("com.example.My-App2", 1));
    }

    [Fact]
    public void AnyOtherCharacterOfTheBundleIdentifierIsEscaped()
    {
        // Characters that would end the query item, start another, end the query or change a
        // value's meaning stay inside the one value, as does a letter beyond ASCII.
        var identifier = "a b&pid=2#c+d%e/f?" + (char)0x00E9;

        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=a%20b%26pid%3D2%23c%2Bd%25e%2Ff%3F%C3%A9&pid=7&script-name=universal.js",
            StikDebugRequest.Build(identifier, 7));
    }

    [Fact]
    public void TheLargestProcessIdentifierIsWrittenInFull()
    {
        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=a&pid=2147483647&script-name=universal.js",
            StikDebugRequest.Build("a", int.MaxValue));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AProcessIdentifierMustBeAboveZero(int processId)
    {
        var refused = Assert.Throws<ArgumentOutOfRangeException>(
            () => StikDebugRequest.Build("io.github.recelilious.paneport", processId));
        Assert.Equal("processId", refused.ParamName);
    }

    [Fact]
    public void ABundleIdentifierMustBeGiven()
    {
        var empty = Assert.Throws<ArgumentException>(() => StikDebugRequest.Build("", 1));
        Assert.Equal("bundleIdentifier", empty.ParamName);
        var missing = Assert.Throws<ArgumentNullException>(() => StikDebugRequest.Build(null!, 1));
        Assert.Equal("bundleIdentifier", missing.ParamName);
    }
}
