// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices;

namespace Paneport.Interop.Tests;

/// <summary>
/// Loads the host test library and starts the host once for all tests. The library is the one
/// that the CMake preset macos-test builds, named by the environment variable
/// <c>PANEPORT_HOST_LIBRARY</c>; without it every test fails.
/// </summary>
public sealed class HostFixture : IDisposable
{
    /// <summary>The environment variable that names the host test library.</summary>
    public const string LibraryVariable = "PANEPORT_HOST_LIBRARY";

    /// <summary>Loads the library, sends the bindings to it, and starts the host on <see cref="Main"/>.</summary>
    public HostFixture()
    {
        var path = Environment.GetEnvironmentVariable(LibraryVariable);
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new InvalidOperationException(
                $"Set {LibraryVariable} to the host test library, build/native/macos-test/libpaneport_host.dylib, "
                    + "which the CMake preset macos-test builds.");
        }

        // The bindings import the framework's name (C-0009 section 4), which only an app
        // resolves; here it resolves to the test library.
        var library = NativeLibrary.Load(path);
        NativeLibrary.SetDllImportResolver(
            typeof(NativeHost).Assembly, (name, _, _) => name == NativeHost.LibraryName ? library : 0);

        CrashRecordFolder = Directory.CreateTempSubdirectory("paneport-interop-").FullName;
        Main.Run(() => NativeHost.Initialize(CrashRecordFolder));
    }

    /// <summary>Gets the thread that stands in for the main thread.</summary>
    public HostThread Main { get; } = new();

    /// <summary>Gets the crash record folder that the host was started with.</summary>
    public string CrashRecordFolder { get; }

    /// <summary>Stops the host and removes the crash record folder.</summary>
    public void Dispose()
    {
        Main.Run(NativeHost.Shutdown);
        Main.Dispose();
        Directory.Delete(CrashRecordFolder, recursive: true);
    }
}
