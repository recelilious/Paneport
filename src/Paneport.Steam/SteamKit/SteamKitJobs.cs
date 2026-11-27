// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Steam;
using SteamKit2;

namespace Paneport.Steam.SteamKit;

// How the adapters await SteamKit2's jobs (steam-service.md section 3).
internal static class SteamKitJobs
{
    public const string ConnectionEnded = "The connection to Steam ended. Try the step again once the session has logged on again.";

    public const string SteamFailed = "Steam could not complete the request. Try the step again.";

    public const string NoAnswer = "Steam did not answer in time. Try the step again.";

    // Waits for a SteamKit2 task with the caller's token, which SteamKit2's jobs do not take, so
    // that a cancel ends the wait at once. The task that the wait then abandons is observed, so that
    // a later fault of it does not reach the finalizer unobserved; nothing of it is logged, since a
    // late result or message can hold a secret. Never for a call that writes into memory that the
    // caller may give to anyone else once the wait has ended.
    public static Task<T> WaitAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(task);
        _ = task.ContinueWith(
            static ended => _ = ended.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task.WaitAsync(cancellationToken);
    }

    // Runs a gate step's SteamKit2 job, and gives in plain words each way in which SteamKit2 ends a
    // job that the caller did not cancel. A job that starts after the connection ended fails at once
    // with AsyncJobFailedException, and so does one that Steam fails while the connection is up. A
    // job that is out when the connection ends is cancelled, and so is one that Steam leaves
    // unanswered for its timeout of 10 seconds. SteamKit2 marks the client as not connected before
    // it cancels the jobs of a connection that ended, so whether the client is connected tells the
    // end of the connection from Steam's failure or silence. The caller's own cancel stays a cancel.
    public static async Task<T> StepAsync<T>(Func<Task<T>> job, Func<bool> connected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(connected);
        try
        {
            return await job().ConfigureAwait(false);
        }
        catch (AsyncJobFailedException exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new SteamStepException(connected() ? SteamFailed : ConnectionEnded, exception);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new SteamStepException(connected() ? NoAnswer : ConnectionEnded, exception);
        }
    }
}
