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
    public void ACharacterThatCouldEndTheValueIsEscaped()
    {
        // Every character outside RFC 3986's unreserved set is percent-encoded as UTF-8: those
        // that would end the query item, start another, end the query or change a value's
        // meaning, a control character, a letter beyond ASCII and one beyond the Basic
        // Multilingual Plane. The underscore and the tilde, which are unreserved, stay.
        var identifier = "a b&pid=2#c+d%e/f?g;h\"i" + (char)0x0A + (char)0x00E9 + char.ConvertFromUtf32(0x1F600) + "_~";

        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=a%20b%26pid%3D2%23c%2Bd%25e%2Ff%3Fg%3Bh%22i%0A%C3%A9%F0%9F%98%80_~&pid=7&script-name=universal.js",
            StikDebugRequest.Build(identifier, 7));
    }

    [Fact]
    public void TheIdentifierIsNotTrimmed()
    {
        Assert.Equal(
            "stikdebug://enable-jit?bundle-id=%20a%20&pid=1&script-name=universal.js",
            StikDebugRequest.Build(" a ", 1));
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
