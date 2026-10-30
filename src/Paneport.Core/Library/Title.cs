// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Library;

/// <summary>
/// A title in the user's library.
/// </summary>
public sealed record Title
{
    /// <summary>Creates a title.</summary>
    /// <param name="id">The title's identity.</param>
    /// <param name="name">The title's display name.</param>
    /// <param name="installState">How far the title is installed.</param>
    /// <param name="launchPath">The way the title starts.</param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> is empty, or a local title is given the Dock path.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="launchPath"/> is not a defined value.</exception>
    public Title(TitleId id, string name, InstallState installState, LaunchPath launchPath)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!Enum.IsDefined(launchPath))
        {
            throw new ArgumentOutOfRangeException(nameof(launchPath), launchPath, "The launch path is not defined.");
        }

        // A title that was not obtained through Steam can only start directly; the app refuses to
        // launch one that shows a Steam marker (C-0007 section 5, C-0008 section 5).
        if (id.Source == TitleSource.Local && launchPath == LaunchPath.Dock)
        {
            throw new ArgumentException("A local title never takes the Dock path.", nameof(launchPath));
        }

        Id = id;
        Name = name;
        InstallState = installState;
        LaunchPath = launchPath;
    }

    /// <summary>Gets the title's identity.</summary>
    public TitleId Id { get; }

    /// <summary>Gets the title's display name.</summary>
    public string Name { get; }

    /// <summary>Gets how far the title is installed.</summary>
    public InstallState InstallState { get; }

    /// <summary>Gets the way the title starts.</summary>
    public LaunchPath LaunchPath { get; }
}
