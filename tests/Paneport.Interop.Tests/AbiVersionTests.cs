// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Xunit;

namespace Paneport.Interop.Tests;

// The interface version of C-0005 section 1, as the bindings read and compare it.
public sealed class AbiVersionTests
{
    [Fact]
    public void HostImplementsTheVersionOfTheBindings()
    {
        var version = NativeHost.ReadAbiVersion();

        Assert.True(HostAbi.IsCompatible(version));
        Assert.Equal(HostAbi.Major, HostAbi.MajorOf(version));
        Assert.Equal(HostAbi.Packed, version);
    }

    [Fact]
    public void VersionsDecodeAsMajorAndMinor()
    {
        const uint TwoPointFive = 0x0002_0005;

        Assert.Equal(2, HostAbi.MajorOf(TwoPointFive));
        Assert.Equal(5, HostAbi.MinorOf(TwoPointFive));
        Assert.Equal("2.5", HostAbi.Format(TwoPointFive));
        Assert.False(HostAbi.IsCompatible(TwoPointFive));
        Assert.True(HostAbi.IsCompatible(((uint)HostAbi.Major << 16) | 0xFFFF));
    }
}
