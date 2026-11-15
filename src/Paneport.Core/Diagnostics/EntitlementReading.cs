// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Diagnostics;

/// <summary>
/// One entitlement of C-0009 section 4 as the installed app carries it: its value in the app's
/// code signature and in its embedded provisioning profile.
/// </summary>
/// <param name="Name">The entitlement's name.</param>
/// <param name="Signature">The value in the code signature, or null when the signature does not name it.</param>
/// <param name="SignatureState">Whether the code signature could be read.</param>
/// <param name="Profile">The value in the embedded profile, or null when the profile does not name it.</param>
/// <param name="ProfileState">Whether the app holds an embedded profile, as a re-signed app does, and whether it could be read.</param>
public sealed record EntitlementReading(
    string Name,
    bool? Signature,
    EntitlementSourceState SignatureState,
    bool? Profile,
    EntitlementSourceState ProfileState)
{
    /// <summary>Gets the value in each source, as Diagnostics shows it.</summary>
    public string Description
    {
        get
        {
            if (SignatureState != EntitlementSourceState.Read && ProfileState != EntitlementSourceState.Read)
            {
                return "unknown: neither the code signature nor a profile could be read";
            }

            var signature = SignatureState switch
            {
                EntitlementSourceState.Read => $"{Text(Signature)} in the code signature",
                EntitlementSourceState.Absent => "the app is not signed",
                _ => "the code signature could not be read",
            };
            var profile = ProfileState switch
            {
                EntitlementSourceState.Read => $"{Text(Profile)} in the profile",
                EntitlementSourceState.Absent => "no profile, as in an ad-hoc-signed build",
                _ => "the profile could not be read",
            };
            return $"{signature}; {profile}";
        }
    }

    /// <summary>
    /// Gets the value that iOS applies, as far as the sources tell: an entitlement holds only when
    /// the signature carries it and, once a profile is embedded, the profile grants it too; null
    /// when no source could be read.
    /// </summary>
    public bool? Effective => (SignatureState, ProfileState) switch
    {
        (EntitlementSourceState.Read, EntitlementSourceState.Read) => Signature == true && Profile == true,
        (EntitlementSourceState.Read, EntitlementSourceState.Absent) => Signature == true,
        (EntitlementSourceState.Read, _) => null,
        (_, EntitlementSourceState.Read) => Profile == true ? null : false,
        _ => null,
    };

    private static string Text(bool? value) => value switch
    {
        true => "true",
        false => "false",
        null => "absent",
    };
}
