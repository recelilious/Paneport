// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Xunit;

namespace Paneport.Core.Tests;

// C-0005 section 1: pp_abi_version returns the major version in the high 16 bits and the minor
// version in the low 16 bits, and the .NET side refuses a different major version.
public sealed class HostAbiVersionTests
{
    [Fact]
    public void MajorComesFromTheHighBitsAndMinorFromTheLowBits()
    {
        var version = HostAbiVersion.FromPacked(0x0002_0005);

        Assert.Equal(2, version.Major);
        Assert.Equal(5, version.Minor);
        Assert.Equal("2.5", version.ToString());
    }

    [Theory]
    [InlineData(0x0000_0000u)]
    [InlineData(0x0001_0000u)]
    [InlineData(0x1234_ABCDu)]
    [InlineData(0xFFFF_FFFFu)]
    public void PackingRoundTrips(uint packed)
    {
        Assert.Equal(packed, HostAbiVersion.FromPacked(packed).Packed);
    }

    [Fact]
    public void AnotherMinorVersionIsCompatible()
    {
        Assert.True(new HostAbiVersion(1, 0).IsCompatibleWith(new HostAbiVersion(1, 7)));
        Assert.True(new HostAbiVersion(1, 7).IsCompatibleWith(new HostAbiVersion(1, 0)));
    }

    [Fact]
    public void AnotherMajorVersionIsIncompatible()
    {
        Assert.False(new HostAbiVersion(1, 0).IsCompatibleWith(new HostAbiVersion(2, 0)));
        Assert.False(new HostAbiVersion(2, 0).IsCompatibleWith(new HostAbiVersion(1, 9)));
    }
}
