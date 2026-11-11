// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Interop;

/// <summary>
/// The status codes of the host interface (<c>pp_status_t</c>, C-0005 section 1), as
/// <c>paneport/common.h</c> defines them in version 1.0. Later function groups add codes.
/// </summary>
public enum HostStatus
{
    /// <summary>The call succeeded (<c>PP_OK</c>).</summary>
    Ok = 0,

    /// <summary>An argument is missing, too small or out of range (<c>PP_E_INVALID_ARGUMENT</c>).</summary>
    InvalidArgument = -1,

    /// <summary>A main-thread function was called on another thread (<c>PP_E_WRONG_THREAD</c>).</summary>
    WrongThread = -2,

    /// <summary>The function needs a running host (<c>PP_E_NOT_INITIALIZED</c>).</summary>
    NotInitialized = -3,

    /// <summary><c>pp_init</c> runs once per process, and it already ran (<c>PP_E_ALREADY_INITIALIZED</c>).</summary>
    AlreadyInitialized = -4,

    /// <summary>The host could not allocate memory (<c>PP_E_OUT_OF_MEMORY</c>).</summary>
    OutOfMemory = -5,

    /// <summary>A file or folder could not be read or written (<c>PP_E_IO</c>).</summary>
    InputOutput = -6,

    /// <summary>The call is not available in this build or on this platform (<c>PP_E_UNSUPPORTED</c>).</summary>
    Unsupported = -7,

    /// <summary>The resource is in use, or a queue is full (<c>PP_E_BUSY</c>).</summary>
    Busy = -8,

    /// <summary>The system refused something the host needs (<c>PP_E_SYSTEM</c>).</summary>
    SystemFailure = -9,
}
