// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// The calls into the native host that an app head makes, as Paneport.Interop offers them
/// (C-0005 sections 1 and 2). Paneport.iOS implements it over Paneport.Interop; tests use a fake.
/// </summary>
/// <remarks>
/// Paneport.Core cannot reference Paneport.Interop (C-0004 section 2), so the host adapter
/// <see cref="NativeRuntimeHost"/> and the startup sequence <see cref="HostStartup"/> work through
/// this interface. Thread rules follow the host's: <see cref="Initialize"/>,
/// <see cref="SetEventHandler"/> and <see cref="SetLogHandler"/> run on the main thread only,
/// the other members on any thread. A failed call throws <see cref="NativeHostException"/>.
/// </remarks>
public interface INativeHost
{
    /// <summary>
    /// Gets how the app imports the host: the framework, or <c>__Internal</c> as the fallback of
    /// C-0009 section 4.
    /// </summary>
    string ImportName { get; }

    /// <summary>Gets the version of the interface that the app's bindings were written against.</summary>
    HostAbiVersion BindingsVersion { get; }

    /// <summary>Reads the version of the interface that the host implements (<c>pp_abi_version</c>).</summary>
    /// <returns>The host's version.</returns>
    HostAbiVersion ReadAbiVersion();

    /// <summary>Starts the host (<c>pp_init</c>). Main thread only, once per process.</summary>
    /// <param name="crashRecordFolder">The absolute path of the folder for crash records, which exists.</param>
    void Initialize(string crashRecordFolder);

    /// <summary>
    /// Sets the handler of the host's events, or removes it. The handler runs on the host event
    /// thread, must return quickly and must not call into the host. Main thread only.
    /// </summary>
    /// <param name="handler">The handler, or null.</param>
    void SetEventHandler(Action<HostEventInfo>? handler);

    /// <summary>
    /// Sets the handler of the host's log lines, or removes it. The handler runs on the host
    /// event thread. Main thread only.
    /// </summary>
    /// <param name="handler">The handler, or null.</param>
    void SetLogHandler(Action<HostLogEntry>? handler);

    /// <summary>
    /// Sets where an exception goes that an event or log handler throws, so that it never
    /// reaches the host (C-0005 section 1). Any thread.
    /// </summary>
    /// <param name="handler">The handler, which must not throw; or null.</param>
    void SetCallbackFailureHandler(Action<Exception>? handler);

    /// <summary>Reads the memory budget now (<c>pp_memory_get_budget</c>). Any thread.</summary>
    /// <returns>The memory figures.</returns>
    MemoryBudget GetMemoryBudget();

    /// <summary>
    /// Reads the device's model identifier, such as <c>iPhone18,1</c>
    /// (<c>pp_device_get_model</c>). Any thread.
    /// </summary>
    /// <returns>The model identifier.</returns>
    string ReadDeviceModel();
}
