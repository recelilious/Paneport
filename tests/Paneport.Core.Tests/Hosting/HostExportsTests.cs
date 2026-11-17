// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Xunit;

namespace Paneport.Core.Tests.Hosting;

// A function that a later minor version of the interface added fails on an older host as an
// unsupported call, never as an EntryPointNotFoundException that no caller handles.
public sealed class HostExportsTests
{
    [Fact]
    public void AFunctionThatTheHostLacksFailsAsUnsupported()
    {
        var exception = Assert.Throws<NativeHostException>(() =>
            HostExports.Call<string>(new HostAbiVersion(1, 1), () => throw new EntryPointNotFoundException("pp_device_get_model")));

        Assert.Equal(HostExports.UnsupportedStatus, exception.Status);
        Assert.Equal("The host predates interface 1.1, which added this function.", exception.Message);
        Assert.IsType<EntryPointNotFoundException>(exception.InnerException);
    }

    [Fact]
    public void AnyOtherFailurePassesThrough()
    {
        var failure = new NativeHostException("SystemFailure", "sysctl failed.", null);

        var exception = Assert.Throws<NativeHostException>(() =>
            HostExports.Call<string>(new HostAbiVersion(1, 1), () => throw failure));

        Assert.Same(failure, exception);
    }

    // A missing host library and a type that cannot load are TypeLoadExceptions too, like
    // EntryPointNotFoundException, but neither means an older host, so neither reads as Unsupported.
    [Fact]
    public void AMissingHostLibraryPassesThrough()
    {
        var failure = new DllNotFoundException("paneport_host");

        var exception = Assert.Throws<DllNotFoundException>(() =>
            HostExports.Call<string>(new HostAbiVersion(1, 1), () => throw failure));

        Assert.Same(failure, exception);
    }

    [Fact]
    public void ATypeThatCannotLoadPassesThrough()
    {
        var failure = new TypeLoadException("Paneport.Interop.NativeHost");

        var exception = Assert.Throws<TypeLoadException>(() =>
            HostExports.Call<string>(new HostAbiVersion(1, 1), () => throw failure));

        Assert.Same(failure, exception);
    }

    [Fact]
    public void TheResultPassesThrough() =>
        Assert.Equal("iPhone18,1", HostExports.Call(new HostAbiVersion(1, 1), () => "iPhone18,1"));
}
