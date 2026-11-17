// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Paneport.Core.Diagnostics;
using Xunit;

namespace Paneport.Core.Tests.Hosting;

// The iOS head's InteropNativeHost guards each binding of a function that a later minor version
// of the interface added with HostExports. Paneport.iOS builds only for iOS, and C-0004 section 2
// lets no test project reference both Paneport.Core and Paneport.Interop, so no test can run that
// class. This test reads its source, which the build copies into the test output, so that a
// binding without the guard fails here and not first on a device with an older host.
public sealed class InteropNativeHostSourceTests
{
    // Each binding that a minor version after 1.0 added, with the expression for that version.
    [Theory]
    [InlineData("ReadDeviceModel", nameof(DeviceModel) + "." + nameof(DeviceModel.AddedIn))]
    public void ABindingThatALaterMinorVersionAddedGoesThroughHostExports(string binding, string addedIn)
    {
        var source = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Repository", "src", "Paneport.iOS", "InteropNativeHost.cs"));

        var guarded = $@"=>\s*HostExports\.Call\(\s*{Regex.Escape(addedIn)}\s*,\s*\(\)\s*=>\s*Call\(\s*NativeHost\.{binding}\s*\)\s*\)";
        Assert.Single(Regex.Matches(source, guarded));
        Assert.Single(Regex.Matches(source, $@"\bNativeHost\.{binding}\b"));
    }
}
