// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// A running sign-in: its progress, its end, and what the user can do while it waits.
/// </summary>
/// <remarks>
/// Members may be called from any thread. <see cref="ProgressChanged"/> is raised on whichever
/// thread changed the progress. A call that does not fit the current stage, such as a code while
/// none is asked for, is ignored.
/// </remarks>
public interface ISignInAttempt
{
    /// <summary>Gets the latest progress.</summary>
    SignInProgress Progress { get; }

    /// <summary>Raised after <see cref="Progress"/> changed, on the thread that changed it.</summary>
    event EventHandler? ProgressChanged;

    /// <summary>Gets a task that completes with the result when the attempt ends. It never fails.</summary>
    Task<SignInResult> Completion { get; }

    /// <summary>Gives the Steam Guard code that the attempt waits for.</summary>
    /// <param name="code">The code as the user typed it.</param>
    void SubmitCode(string code);

    /// <summary>
    /// Stops waiting for an approval in the Steam mobile app and asks for a code instead.
    /// </summary>
    void UseCodeInstead();

    /// <summary>Cancels the attempt.</summary>
    void Cancel();
}
