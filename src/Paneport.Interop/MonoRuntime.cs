// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop;

/// <summary>
/// The one call into Mono's embedding API that the app makes: turning off Mono's crash chaining
/// at startup (C-0005 section 4, docs/design/signals.md section 5).
/// </summary>
/// <remarks>
/// On the device Mono is linked into the app's executable, so its functions are found in the
/// main program, which an import names <c>__Internal</c> (docs/design/interop.md section 2). Only
/// the iOS head calls it; the desktop head and the tests run on CoreCLR, which has no such
/// function.
/// </remarks>
public static partial class MonoRuntime
{
    /// <summary>The library that the import names: the main program.</summary>
    public const string LibraryName = "__Internal";

    /// <summary>
    /// Turns Mono's crash chaining off (<c>mono_set_crash_chaining(FALSE)</c>), so that Mono hands
    /// a fault it does not take to the handler it found when it started, the host's last-resort
    /// handler, instead of writing its own crash report first. Main thread, at startup, after the
    /// runtime started and before <c>pp_init</c>.
    /// </summary>
    /// <exception cref="EntryPointNotFoundException">The runtime has no such function.</exception>
    /// <exception cref="DllNotFoundException">The main program cannot be opened.</exception>
    public static void TurnOffCrashChaining() => SetCrashChaining(0);

    // void mono_set_crash_chaining (mono_bool chain_signals), where mono_bool is a 32-bit integer.
    [LibraryImport(LibraryName, EntryPoint = "mono_set_crash_chaining")]
    private static partial void SetCrashChaining(int chainSignals);
}
