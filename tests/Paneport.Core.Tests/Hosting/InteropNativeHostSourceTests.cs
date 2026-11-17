// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text.RegularExpressions;
using Paneport.Core.Diagnostics;
using Xunit;

namespace Paneport.Core.Tests.Hosting;

// The iOS head's InteropNativeHost guards each binding of a function that a later minor version
// of the interface added with HostExports. Paneport.iOS builds only for iOS, and C-0004 section 2
// lets no test project reference both Paneport.Core and Paneport.Interop, so no test can run that
// class. These tests read its source, which the build copies into the test output, so that a
// binding without the guard fails here and not first on a device with an older host.
public sealed class InteropNativeHostSourceTests
{
    // The members of Paneport.Interop's NativeHost that every host of interface 1.0 serves: the
    // library's name, the functions of 1.0, and the handler of failed callbacks, which is managed.
    private static readonly string[] s_version10 =
    [
        "LibraryName", "ReadAbiVersion", "Initialize", "Shutdown", "SetEventHandler", "SetLogHandler",
        "SetCallbackFailureHandler", "GetMemoryBudget",
    ];

    // Each binding that a minor version after 1.0 added, with the expression for that version.
    private static readonly (string Binding, string AddedIn)[] s_laterBindings =
    [
        ("ReadDeviceModel", nameof(DeviceModel) + "." + nameof(DeviceModel.AddedIn)),
    ];

    public static TheoryData<string, string> LaterBindings { get; } = new(s_laterBindings);

    [Theory]
    [MemberData(nameof(LaterBindings))]
    public void ABindingThatALaterMinorVersionAddedGoesThroughHostExports(string binding, string addedIn)
    {
        var source = Source();

        var guarded = $@"=>\s*HostExports\.Call\(\s*{Regex.Escape(addedIn)}\s*,\s*\(\)\s*=>\s*Call\(\s*NativeHost\.{binding}\s*\)\s*\)";
        Assert.Single(Regex.Matches(source, guarded));
        Assert.Single(Regex.Matches(source, $@"\bNativeHost\.{binding}\b"));
    }

    // A binding of a function that is neither of 1.0 nor a row above fails here until someone
    // classifies it, so that a new one cannot miss its guard.
    [Fact]
    public void EveryMemberOfNativeHostThatItUsesIsOfVersion10OrARowAbove()
    {
        var used = Regex.Matches(Source(), @"\bNativeHost\.(\w+)").Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal).ToList();

        Assert.NotEmpty(used);
        Assert.All(used, member => Assert.True(
            s_version10.Contains(member, StringComparer.Ordinal) || s_laterBindings.Any(row => row.Binding == member),
            $"NativeHost.{member} is neither of interface 1.0 nor a row of {nameof(LaterBindings)}."));
    }

    private static string Source() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Repository", "src", "Paneport.iOS", "InteropNativeHost.cs"));
}
