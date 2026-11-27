// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// The steps of the SteamKit2 gate after sign-in, one per button of the Steam gate page
/// (L2 steps 22 to 28, C-0012 section 3): licenses, app information, a depot key, a manifest
/// request code, a manifest and verified chunks.
/// </summary>
/// <remarks>
/// <para>
/// Each step needs the session to be logged on, and the later steps need the earlier ones: a
/// depot key and a manifest request code need the app information, and the manifest needs both.
/// The implementation keeps the depot key, the manifest request code and the manifest to itself
/// and reports only whether it got them, so no page can show one (C-0007 section 3, L2 step 24).
/// </para>
/// <para>
/// A step that cannot run throws <see cref="SteamStepException"/>, whose message names no
/// secret and may be shown; any other exception is shown by its type only.
/// </para>
/// </remarks>
public interface ISteamGateSteps
{
    /// <summary>Counts the licenses of the signed-in account.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>The number of licenses.</returns>
    Task<int> CountLicensesAsync(CancellationToken cancellationToken);

    /// <summary>Reads an app's information: its name and its Windows depots with their sizes.</summary>
    /// <param name="appId">The app's ID.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>The app's information, with the sections that were missing named in its problems.</returns>
    Task<SteamAppInfo> ReadAppInfoAsync(uint appId, CancellationToken cancellationToken);

    /// <summary>Gets the decryption key of one of the app's depots, and keeps it.</summary>
    /// <param name="depotId">A depot of the app whose information was read last.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Whether Steam granted the key.</returns>
    Task<SteamGrant> GetDepotKeyAsync(uint depotId, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the request code of the depot's public manifest, and keeps it.
    /// </summary>
    /// <param name="depotId">A depot of the app whose information was read last.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>Whether Steam granted a code; a code of 0 counts as not granted.</returns>
    Task<SteamGrant> GetManifestRequestCodeAsync(uint depotId, CancellationToken cancellationToken);

    /// <summary>Downloads and reads the depot's public manifest, with the key and the code kept before.</summary>
    /// <param name="depotId">The depot whose key and code were kept.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>The manifest's file count, chunk count, size and download time.</returns>
    Task<ManifestSummary> DownloadManifestAsync(uint depotId, CancellationToken cancellationToken);

    /// <summary>
    /// Downloads chunks of the manifest read last, checks each against its manifest entry, and
    /// writes only the chunks that pass to the test folder (C-0007 section 4).
    /// </summary>
    /// <param name="count">How many distinct chunks to download, at least 1.</param>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>How many chunks passed and were rejected, the bytes and the time.</returns>
    Task<ChunkRunResult> DownloadChunksAsync(int count, CancellationToken cancellationToken);

    /// <summary>Deletes the test folder and everything the steps kept.</summary>
    /// <param name="cancellationToken">Cancels the step.</param>
    /// <returns>A task that completes when the test data is gone.</returns>
    Task DeleteTestDataAsync(CancellationToken cancellationToken);
}
