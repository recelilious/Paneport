// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Paneport.Interop;

/// <summary>
/// The native host, through the bindings of C-0005: its interface version, its life cycle, its
/// events and log lines, and its memory budget. A failed call throws a
/// <see cref="HostException"/> with the host's message.
/// </summary>
/// <remarks>
/// <para>
/// Thread rules follow the host's: <see cref="Initialize"/>, <see cref="Shutdown"/>,
/// <see cref="SetEventHandler"/> and <see cref="SetLogHandler"/> run on the main thread only,
/// and the host refuses them elsewhere. The other members run on any thread.
/// </para>
/// <para>
/// The handlers run on the host event thread, which becomes known to the runtime when it first
/// runs one (C-0004 section 1). They must return quickly and must not call into the host. An
/// exception that a handler throws is caught before it reaches the host and goes to the handler
/// that <see cref="SetCallbackFailureHandler"/> sets (C-0005 section 1).
/// </para>
/// </remarks>
public static unsafe class NativeHost
{
    /// <summary>
    /// The library that every binding imports: the framework that the app embeds (C-0009
    /// section 4). On macOS, the tests resolve this name to the host test library.
    /// </summary>
    public const string LibraryName = "@rpath/paneport_host.framework/paneport_host";

    // The handlers that the host's callbacks reach through their user_data. Only the main
    // thread changes them, through calls that the host makes on the main thread only.
    private static GCHandle s_eventHandler;
    private static GCHandle s_logHandler;
    private static Action<Exception>? s_callbackFailureHandler;

    /// <summary>Reads the version of the interface that the host implements (<c>pp_abi_version</c>).</summary>
    /// <returns>The major version in the high 16 bits and the minor version in the low 16 bits.</returns>
    public static uint ReadAbiVersion() => NativeMethods.AbiVersion();

    /// <summary>
    /// Starts the host (<c>pp_init</c>), after checking that it implements the major version of
    /// these bindings. Main thread only; it runs once per process.
    /// </summary>
    /// <param name="crashRecordFolder">
    /// The absolute path of the folder for crash records, which must exist, such as the
    /// <c>Library/Caches/Paneport/</c> folder of the app container; or null for none.
    /// </param>
    /// <exception cref="HostAbiMismatchException">The host implements another major version.</exception>
    /// <exception cref="HostException">The host refused to start.</exception>
    public static void Initialize(string? crashRecordFolder)
    {
        var version = ReadAbiVersion();
        if (!HostAbi.IsCompatible(version))
        {
            throw new HostAbiMismatchException(version);
        }

        var folder = crashRecordFolder is null ? null : Utf8StringMarshaller.ConvertToUnmanaged(crashRecordFolder);
        try
        {
            var config = new NativeInitConfig
            {
                StructSize = (uint)sizeof(NativeInitConfig),
                CrashRecordFolder = (nint)folder,
            };
            HostCalls.Check(NativeMethods.Init(&config));
        }
        finally
        {
            Utf8StringMarshaller.Free(folder);
        }
    }

    /// <summary>
    /// Stops the host (<c>pp_shutdown</c>). Once it returns, no handler runs again. Main thread only.
    /// </summary>
    /// <exception cref="HostException">The host is not running, or this is not the main thread.</exception>
    public static void Shutdown()
    {
        HostCalls.Check(NativeMethods.Shutdown());
        Release(ref s_eventHandler);
        Release(ref s_logHandler);
    }

    /// <summary>
    /// Sets the handler of the host's events (<c>pp_set_event_callback</c>), or removes it.
    /// Once it returns, the previous handler is not running and is never called again. Main
    /// thread only.
    /// </summary>
    /// <param name="handler">The handler, which runs on the host event thread; or null.</param>
    /// <exception cref="HostException">The host is not running, or this is not the main thread.</exception>
    public static void SetEventHandler(Action<HostEvent>? handler)
    {
        var handle = handler is null ? default : GCHandle.Alloc(handler);
        int status;
        try
        {
            status = handler is null
                ? NativeMethods.SetEventCallback(null, null)
                : NativeMethods.SetEventCallback(&OnEvent, (void*)GCHandle.ToIntPtr(handle));
        }
        catch
        {
            // The call never reached the host, as when the library is missing: nothing holds
            // the new handle.
            Release(ref handle);
            throw;
        }

        Replace(ref s_eventHandler, handle, status);
    }

    /// <summary>
    /// Sets the handler of the host's log lines (<c>pp_set_log_callback</c>), or removes it.
    /// Lines that the host logged before arrive once a handler is set. Once it returns, the
    /// previous handler is not running and is never called again. Main thread only.
    /// </summary>
    /// <param name="handler">The handler, which runs on the host event thread; or null.</param>
    /// <exception cref="HostException">The host is not running, or this is not the main thread.</exception>
    public static void SetLogHandler(Action<HostLogLine>? handler)
    {
        var handle = handler is null ? default : GCHandle.Alloc(handler);
        int status;
        try
        {
            status = handler is null
                ? NativeMethods.SetLogCallback(null, null)
                : NativeMethods.SetLogCallback(&OnLogLine, (void*)GCHandle.ToIntPtr(handle));
        }
        catch
        {
            // The call never reached the host: nothing holds the new handle.
            Release(ref handle);
            throw;
        }

        Replace(ref s_logHandler, handle, status);
    }

    /// <summary>
    /// Sets where an exception goes that an event or log handler throws, or removes it. Any
    /// thread. Without it, such an exception is dropped, because it must never reach the host.
    /// </summary>
    /// <param name="handler">The handler, which runs on the host event thread and must not throw; or null.</param>
    public static void SetCallbackFailureHandler(Action<Exception>? handler) =>
        Volatile.Write(ref s_callbackFailureHandler, handler);

    /// <summary>Reads the memory budget now (<c>pp_memory_get_budget</c>). Any thread.</summary>
    /// <returns>The memory figures.</returns>
    /// <exception cref="HostException">The system did not report the footprint.</exception>
    public static HostMemoryBudget GetMemoryBudget()
    {
        var budget = new NativeMemoryBudget { StructSize = (uint)sizeof(NativeMemoryBudget) };
        HostCalls.Check(NativeMethods.MemoryGetBudget(&budget));
        return new HostMemoryBudget(
            budget.LimitBytes, budget.AvailableBytes, budget.FootprintBytes, budget.GameModeLevelReached != 0);
    }

    /// <summary>
    /// Reads the device's model identifier, such as <c>iPhone18,1</c>, which version 1.1 of the
    /// interface added (<c>pp_device_get_model</c>). Any thread.
    /// </summary>
    /// <returns>The model identifier.</returns>
    /// <exception cref="HostException">The system did not report it.</exception>
    public static string ReadDeviceModel()
    {
        const int Size = 64;
        var buffer = stackalloc byte[Size];
        HostCalls.Check(NativeMethods.DeviceGetModel(buffer, Size));
        return HostCalls.Text(buffer);
    }

    /// <summary>
    /// Reads the JIT state, which version 1.3 of the interface added (<c>pp_jit_get_state</c>,
    /// C-0006 section 5). Any thread.
    /// </summary>
    /// <returns>The state and what the host knows about the request.</returns>
    /// <exception cref="HostException">The host is not running.</exception>
    public static HostJitStatus GetJitStatus()
    {
        var status = new NativeJitStatus { StructSize = (uint)sizeof(NativeJitStatus) };
        HostCalls.Check(NativeMethods.JitGetState(&status));
        return new HostJitStatus(
            (HostJitState)status.State,
            (HostJitFailure)status.Failure,
            status.DebuggedSinceLaunch != 0,
            status.AttachedNow != 0,
            status.EnablerOpened != 0,
            status.ForeignDebugger != 0,
            status.RequestedMiB,
            status.PoolMiB,
            status.RegionsPlanned,
            status.RegionsMapped,
            status.InStateMilliseconds,
            status.DebuggerAttachedMilliseconds,
            status.RequestingMilliseconds,
            status.WaitingDetachMilliseconds,
            status.VerifyingMilliseconds,
            status.AuthorizationMilliseconds);
    }

    /// <summary>
    /// Tells the host that this launch opens the JIT enabler, right before the app asks iOS to
    /// open <c>stikdebug://enable-jit</c>, or that iOS refused to open it
    /// (<c>pp_jit_set_enabler_opened</c>, C-0006 section 3 step 2). Main thread only.
    /// </summary>
    /// <param name="opened">True before opening the URL; false when opening it failed.</param>
    /// <exception cref="HostException">The JIT state does not take it, or the thread is not the main thread.</exception>
    public static void SetJitEnablerOpened(bool opened) =>
        HostCalls.Check(NativeMethods.JitSetEnablerOpened(opened ? (byte)1 : (byte)0));

    /// <summary>
    /// Tells the host that the app came back to the foreground, so that it reads the debugger
    /// flags again (<c>pp_jit_note_foreground</c>, C-0006 section 3 step 1). Main thread only.
    /// </summary>
    /// <exception cref="HostException">The thread is not the main thread, or the host is not running.</exception>
    public static void NoteJitForeground() => HostCalls.Check(NativeMethods.JitNoteForeground());

    /// <summary>
    /// Starts the request of the JIT code pool (<c>pp_jit_request_pool</c>, C-0006 sections 3
    /// and 4). The host limits the size, and the state reports the progress. Main thread only.
    /// </summary>
    /// <param name="sizeMiB">The size: the title's <c>jitPoolMiB</c> when it has one, else <see cref="HostJit.DefaultPoolMiB"/>.</param>
    /// <exception cref="HostException">The state is not debugger_attached, the size is 0, or the thread is not the main thread.</exception>
    public static void RequestJitPool(uint sizeMiB) => HostCalls.Check(NativeMethods.JitRequestPool(sizeMiB));

    // Keeps a new handler's handle once the host took it, and frees the one it replaced, which
    // the host no longer runs. A refused handle is freed instead.
    private static void Replace(ref GCHandle current, GCHandle replacement, int status)
    {
        if (status != 0)
        {
            Release(ref replacement);
            HostCalls.Check(status);
        }

        Release(ref current);
        current = replacement;
    }

    private static void Release(ref GCHandle handle)
    {
        if (handle.IsAllocated)
        {
            handle.Free();
        }

        handle = default;
    }

    // The callbacks of the host. Nothing may escape them into C (C-0005 section 1), so each
    // catches every exception and hands it to the failure handler.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnEvent(NativeEvent* nativeEvent, void* userData)
    {
        try
        {
            var handler = (Action<HostEvent>?)GCHandle.FromIntPtr((nint)userData).Target;
            handler?.Invoke(new HostEvent(nativeEvent->Kind, nativeEvent->Code, nativeEvent->Handle, nativeEvent->Address));
        }
        catch (Exception exception)
        {
            ReportCallbackFailure(exception);
        }
    }

    // Where the members that version 1.3 appended to pp_log_line_t end. The conformance test
    // checks the offsets that they rest on, 24 for source and 32 for process.
    private const uint SourceEnd = 28;
    private const uint ProcessEnd = 40;

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void OnLogLine(NativeLogLine* line, void* userData)
    {
        try
        {
            var handler = (Action<HostLogLine>?)GCHandle.FromIntPtr((nint)userData).Target;
            // A host older than version 1.3 sends a shorter line, without its source and process.
            var source = line->StructSize >= SourceEnd ? (HostLogSource)line->Source : HostLogSource.Host;
            var process = line->StructSize >= ProcessEnd ? line->Process : 0;
            handler?.Invoke(
                new HostLogLine((HostLogLevel)line->Level, line->ThreadId, HostCalls.Text(line->Text), source, process));
        }
        catch (Exception exception)
        {
            ReportCallbackFailure(exception);
        }
    }

    private static void ReportCallbackFailure(Exception exception)
    {
        try
        {
            Volatile.Read(ref s_callbackFailureHandler)?.Invoke(exception);
        }
        catch (Exception)
        {
            // The failure handler failed as well. Nothing is left to tell, and the exception
            // must not reach the host.
        }
    }
}
