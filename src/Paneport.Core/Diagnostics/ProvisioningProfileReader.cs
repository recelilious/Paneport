// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Text;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Finds the property list inside an embedded provisioning profile: a signed CMS message whose
/// content is the profile's XML property list, stored as it is.
/// </summary>
/// <remarks>
/// The profile also holds the team, the device list and other data of the signer's account.
/// Only the span of the property list is located; <see cref="EntitlementReport"/> then reads three
/// Boolean values from it, and nothing else of the profile is kept or logged.
/// </remarks>
public static class ProvisioningProfileReader
{
    private static readonly byte[] s_start = Encoding.ASCII.GetBytes("<?xml");
    private static readonly byte[] s_end = Encoding.ASCII.GetBytes("</plist>");

    /// <summary>Locates the property list of a profile.</summary>
    /// <param name="profile">The bytes of <c>embedded.mobileprovision</c>.</param>
    /// <returns>The property list's bytes.</returns>
    /// <exception cref="InvalidDataException">The bytes hold no property list.</exception>
    public static byte[] ReadPropertyList(ReadOnlySpan<byte> profile)
    {
        var start = profile.IndexOf(s_start);
        var end = profile.LastIndexOf(s_end);
        if (start < 0 || end < start)
        {
            throw new InvalidDataException("The profile holds no property list.");
        }

        return profile[start..(end + s_end.Length)].ToArray();
    }
}
