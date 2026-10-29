// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// A title's install status, with the download progress while the title downloads
/// (C-0007 section 4).
/// </summary>
/// <remarks>The default value is <see cref="NotInstalled"/>.</remarks>
public readonly record struct InstallState
{
    private InstallState(InstallStatus status, double? downloadProgress)
    {
        Status = status;
        DownloadProgress = downloadProgress;
    }

    /// <summary>Gets the state of a title that has none of its files on the device.</summary>
    public static InstallState NotInstalled => default;

    /// <summary>Gets the state of a title that is installed and up to date.</summary>
    public static InstallState Installed { get; } = new(InstallStatus.Installed, null);

    /// <summary>Gets the state of an installed title whose update has not finished yet.</summary>
    public static InstallState UpdatePending { get; } = new(InstallStatus.UpdatePending, null);

    /// <summary>Gets the install status.</summary>
    public InstallStatus Status { get; }

    /// <summary>
    /// Gets the part of the download that has finished, from 0 to 1, while the status is
    /// <see cref="InstallStatus.Downloading"/>; otherwise null.
    /// </summary>
    public double? DownloadProgress { get; }

    /// <summary>Creates the state of a title whose files are being downloaded.</summary>
    /// <param name="progress">The part of the download that has finished, from 0 to 1.</param>
    /// <returns>A <see cref="InstallStatus.Downloading"/> state with that progress.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="progress"/> is not a number, or is outside 0 to 1.
    /// </exception>
    public static InstallState Downloading(double progress)
    {
        // The range checks below let NaN through, because every comparison with NaN is false.
        if (double.IsNaN(progress))
        {
            throw new ArgumentOutOfRangeException(nameof(progress), progress, "The download progress is not a number.");
        }

        ArgumentOutOfRangeException.ThrowIfNegative(progress);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(progress, 1.0);
        return new InstallState(InstallStatus.Downloading, progress);
    }
}
