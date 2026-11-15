// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Reflection;
using System.Runtime.CompilerServices;
using Foundation;
using Paneport.Core.Diagnostics;

namespace Paneport.IOS;

// The build stamp of Diagnostics (ios-app-head.md section 6): the values that the project file
// records as assembly metadata, the versions of the bundle's Info.plist, and the compilation mode
// as the runtime reports it.
internal static class BuildInfo
{
    public static BuildStamp Read()
    {
        var metadata = typeof(BuildInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Where(attribute => attribute.Value is not null)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value!, StringComparer.Ordinal);
        string valueOf(string key) => metadata.TryGetValue(key, out var value) ? value : "Not recorded";

        var bundle = NSBundle.MainBundle;
        var version = bundle.ObjectForInfoDictionary("CFBundleShortVersionString")?.ToString() ?? "unknown";
        var build = bundle.ObjectForInfoDictionary("CFBundleVersion")?.ToString() ?? "unknown";
        var interpreter = string.Equals(valueOf("PaneportInterpreter"), "true", StringComparison.OrdinalIgnoreCase) ? "On" : "Off";

        // Under full ahead-of-time compilation the runtime can run no code that it did not compile
        // in advance; the interpreter would make it able to.
        var compilation = RuntimeFeature.IsDynamicCodeSupported
            ? "Ahead-of-time, with the interpreter for dynamic code"
            : "Full ahead-of-time";
        return new BuildStamp(
            Commit: valueOf("PaneportCommit"),
            Version: $"{version} ({build}), {valueOf("PaneportFlavor")}",
            Configuration: valueOf("PaneportConfiguration"),
            Runtime: "Mono",
            CompilationMode: compilation,
            Interpreter: interpreter,
            HostImport: $"Framework, the {valueOf("PaneportHostPreset")} build");
    }
}
