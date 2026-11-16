// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>Whether a source of the entitlements, the code signature or the profile, could be read.</summary>
public enum EntitlementSourceState
{
    /// <summary>The app has no such source: an unsigned executable, or no embedded profile, as in an ad-hoc-signed build.</summary>
    Absent,

    /// <summary>The source was read.</summary>
    Read,

    /// <summary>The source exists but could not be read.</summary>
    Unreadable,

    /// <summary>
    /// The code signature exists but holds no XML entitlements blob: it was made without
    /// entitlements, or by a tool that writes only their DER form, which is not read. Code
    /// signature only.
    /// </summary>
    NoXmlEntitlements,
}
