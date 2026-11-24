// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;

namespace Paneport.Core.Jit;

/// <summary>
/// The request with which the app asks StikDebug for JIT (C-0006 section 3 step 2): the URL
/// <c>stikdebug://enable-jit</c>, which names StikDebug's built-in script and carries the app's
/// bundle identifier and process identifier, as StikJIT's <c>INTEGRATION.md</c> documents it.
/// </summary>
/// <remarks>
/// The Trusted Execution Monitor is active on every target device, so the request always names
/// the script. Paneport writes, bundles and reads no script: StikDebug runs its own
/// (C-0006 section 2). The app calls <c>pp_jit_set_enabler_opened(1)</c> right before it opens
/// the URL, and <c>pp_jit_set_enabler_opened(0)</c> when iOS refuses to open it.
/// docs/design/jit-broker.md section 5 describes the request.
/// </remarks>
public static class StikDebugRequest
{
    /// <summary>The scheme and host of the request, which open StikDebug's JIT request.</summary>
    public const string Endpoint = "stikdebug://enable-jit";

    /// <summary>The file name of the built-in script that the request names (C-0006 section 2).</summary>
    public const string ScriptName = "universal.js";

    /// <summary>
    /// Builds the request's URL: <c>stikdebug://enable-jit?bundle-id=…&amp;pid=…&amp;script-name=universal.js</c>,
    /// with its three query items in that order.
    /// </summary>
    /// <param name="bundleIdentifier">
    /// The app's bundle identifier, with which StikDebug returns to the app. It is percent-encoded
    /// as a query value, which leaves the letters, digits, hyphens and periods of a bundle
    /// identifier as they are. A lone surrogate, which a real bundle identifier cannot hold,
    /// becomes U+FFFD, <c>%EF%BF%BD</c>, as <see cref="Uri.EscapeDataString(string)"/> writes it.
    /// </param>
    /// <param name="processId">The app's process identifier, above 0, with which StikDebug finds the process.</param>
    /// <returns>The URL's text, for the URL opener of the iOS head.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="bundleIdentifier"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="bundleIdentifier"/> is empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="processId"/> is 0 or below.</exception>
    public static string Build(string bundleIdentifier, int processId)
    {
        ArgumentException.ThrowIfNullOrEmpty(bundleIdentifier);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        var bundle = Uri.EscapeDataString(bundleIdentifier);
        var process = processId.ToString(CultureInfo.InvariantCulture);
        return $"{Endpoint}?bundle-id={bundle}&pid={process}&script-name={ScriptName}";
    }
}
