// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// How the running app was built, as the build recorded it: the commit, the version, the
/// configuration, the runtime and its compilation mode, the interpreter setting and the host
/// import (docs/design/ios-app-head.md section 6).
/// </summary>
/// <param name="Commit">The commit that was built, or a text that says none was recorded.</param>
/// <param name="Version">The app's version, such as <c>0.1.0 (12)</c>.</param>
/// <param name="Configuration">The build configuration, such as <c>Release</c>.</param>
/// <param name="Runtime">The .NET runtime, such as <c>Mono</c>.</param>
/// <param name="CompilationMode">How managed code was compiled, such as <c>full ahead-of-time</c>.</param>
/// <param name="Interpreter">The interpreter setting, such as <c>off</c> (C-0009 section 3).</param>
/// <param name="HostImport">How the app imports the native host (C-0009 section 4).</param>
public sealed record BuildStamp(
    string Commit,
    string Version,
    string Configuration,
    string Runtime,
    string CompilationMode,
    string Interpreter,
    string HostImport);
