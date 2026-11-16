// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using Foundation;
using Paneport.Core.Diagnostics;

namespace Paneport.IOS;

// The build stamp of Diagnostics (ios-app-head.md section 6): the values that AppHead.props
// records in the runtime's configuration, the versions of the bundle's Info.plist, and the
// compilation mode as the runtime reports it.
internal static class BuildInfo
{
    public static BuildStamp Read()
    {
        var bundle = NSBundle.MainBundle;
        var version = bundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "unknown";
        var build = bundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString() ?? "unknown";
        var interpreter = string.Equals(Value("Paneport.Interpreter"), "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off";

        // Under full ahead-of-time compilation the runtime can run no code that it did not compile
        // in advance; the interpreter would make it able to.
        var compilation = RuntimeFeature.IsDynamicCodeSupported
            ? "Ahead-of-time, with the interpreter for dynamic code"
            : "Full ahead-of-time";
        return new BuildStamp(
            Commit: Value("Paneport.Commit"),
            Version: $"{version} ({build}), {Value("Paneport.Flavor")}",
            Configuration: Value("Paneport.Configuration"),
            Runtime: "Mono",
            CompilationMode: compilation,
            Interpreter: interpreter,
            HostImport: $"Framework, the {Value("Paneport.HostPreset")} build");
    }

    private static string Value(string name) => AppContext.GetData(name) as string ?? "Not recorded";
}
