// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Runtime.InteropServices.Marshalling;

namespace Paneport.Interop;

// The session and process groups of version 1.4 (paneport/session.h), called without pointers,
// each turning a failed status into HostException as the safe API does. Nothing in the app calls
// them yet: the binding tests reach the five LibraryImport declarations through them, and the
// safe API of the session builds on them once the app runs one.
internal static unsafe class SessionCalls
{
    // pp_session_start with UTF-8 copies of the two folders, which the bindings free once the
    // call returns, and no environment block. Main thread only.
    internal static void Start(uint architecture, string prefixFolder, string runtimeFolder)
    {
        var prefix = Utf8StringMarshaller.ConvertToUnmanaged(prefixFolder);
        var runtime = Utf8StringMarshaller.ConvertToUnmanaged(runtimeFolder);
        try
        {
            var config = new NativeSessionConfig
            {
                StructSize = (uint)sizeof(NativeSessionConfig),
                Architecture = architecture,
                PrefixFolder = (nint)prefix,
                RuntimeFolder = (nint)runtime,
                Environment = 0,
            };
            HostCalls.Check(NativeMethods.SessionStart(&config));
        }
        finally
        {
            Utf8StringMarshaller.Free(prefix);
            Utf8StringMarshaller.Free(runtime);
        }
    }

    // pp_session_stop. Main thread only.
    internal static void Stop() => HostCalls.Check(NativeMethods.SessionStop());

    // pp_process_launch of a program given as a Windows path on a drive of the prefix, without
    // arguments, with a UTF-8 copy of the path that the bindings free once the call returns. Any
    // thread.
    internal static ulong Launch(string program, bool captureOutput)
    {
        var text = Utf8StringMarshaller.ConvertToUnmanaged(program);
        try
        {
            var config = new NativeProcessConfig
            {
                StructSize = (uint)sizeof(NativeProcessConfig),
                CaptureOutput = captureOutput ? (byte)1 : (byte)0,
                ProgramFolder = 0,
                Program = (nint)text,
                Arguments = 0,
            };
            ulong process = 0;
            HostCalls.Check(NativeMethods.ProcessLaunch(&config, &process));
            return process;
        }
        finally
        {
            Utf8StringMarshaller.Free(text);
        }
    }

    // pp_process_get_state: the PP_PROCESS_ state and the exit code. Any thread.
    internal static (uint State, uint ExitCode) GetState(ulong process)
    {
        var state = new NativeProcessState { StructSize = (uint)sizeof(NativeProcessState) };
        HostCalls.Check(NativeMethods.ProcessGetState(process, &state));
        return (state.State, state.ExitCode);
    }

    // pp_process_terminate. Any thread.
    internal static void Terminate(ulong process, uint exitCode) =>
        HostCalls.Check(NativeMethods.ProcessTerminate(process, exitCode));
}
