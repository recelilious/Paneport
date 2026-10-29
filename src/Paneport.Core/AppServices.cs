// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.Library;
using Paneport.Core.Logging;
using Paneport.Core.Security;
using Paneport.Core.Settings;
using Paneport.Core.Steam;

namespace Paneport.Core;

/// <summary>
/// The services that an app head provides to the shell. Each head creates them in its
/// composition root: the desktop head with fakes, the iOS head with the device implementations.
/// </summary>
public sealed class AppServices
{
    /// <summary>Collects the services.</summary>
    /// <param name="host">The native runtime host.</param>
    /// <param name="library">The library of titles.</param>
    /// <param name="steam">The Steam service.</param>
    /// <param name="settings">The settings store.</param>
    /// <param name="tokens">The token store.</param>
    /// <param name="log">The log.</param>
    public AppServices(
        IRuntimeHost host,
        ILibraryService library,
        ISteamService steam,
        ISettingsStore settings,
        ITokenStore tokens,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(tokens);
        ArgumentNullException.ThrowIfNull(log);
        Host = host;
        Library = library;
        Steam = steam;
        Settings = settings;
        Tokens = tokens;
        Log = log;
    }

    /// <summary>Gets the native runtime host.</summary>
    public IRuntimeHost Host { get; }

    /// <summary>Gets the library of titles.</summary>
    public ILibraryService Library { get; }

    /// <summary>Gets the Steam service.</summary>
    public ISteamService Steam { get; }

    /// <summary>Gets the settings store.</summary>
    public ISettingsStore Settings { get; }

    /// <summary>
    /// Gets the token store. No page uses it yet; Steam sign-in is its first user (M2.3).
    /// </summary>
    public ITokenStore Tokens { get; }

    /// <summary>Gets the log.</summary>
    public ILog Log { get; }
}
