// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Logging;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Reads the entitlements of C-0009 section 4 from both of their sources: the app's code
/// signature, which the kernel checks, and its embedded provisioning profile, which grants them
/// (docs/design/ios-app-head.md section 7). Everything else in either source is discarded unread.
/// </summary>
public static class EntitlementReport
{
    private const string LogSource = nameof(EntitlementReport);
    private const string ProfileDictionary = "Entitlements";

    /// <summary>Reads the three entitlements.</summary>
    /// <param name="openExecutable">Opens the app's executable, readable and seekable.</param>
    /// <param name="readProfile">Reads the embedded profile's bytes, or returns null when the app holds none.</param>
    /// <param name="log">The log, which gets a line for a source that cannot be read, without its content.</param>
    /// <returns>One reading per entitlement, in the order of <see cref="EntitlementNames.All"/>.</returns>
    public static IReadOnlyList<EntitlementReading> Read(Func<Stream> openExecutable, Func<byte[]?> readProfile, ILog log)
    {
        ArgumentNullException.ThrowIfNull(openExecutable);
        ArgumentNullException.ThrowIfNull(readProfile);
        ArgumentNullException.ThrowIfNull(log);

        IReadOnlyDictionary<string, bool?>? signature = null;
        EntitlementSourceState signatureState;
        try
        {
            using var executable = openExecutable();
            if (CodeSignatureReader.ReadEntitlements(executable) is { } xml)
            {
                signature = PlistBooleans.Read(xml, EntitlementNames.All, null);
                signatureState = EntitlementSourceState.Read;
            }
            else
            {
                signatureState = CodeSignatureReader.HasSignature(executable)
                    ? EntitlementSourceState.NoXmlEntitlements
                    : EntitlementSourceState.Absent;
            }
        }
        catch (Exception exception)
        {
            log.Write(LogLevel.Warning, LogSource, $"Reading the code signature failed with {exception.GetType().Name}.");
            signatureState = EntitlementSourceState.Unreadable;
        }

        IReadOnlyDictionary<string, bool?>? profile = null;
        EntitlementSourceState profileState;
        try
        {
            if (readProfile() is { } bytes)
            {
                profile = PlistBooleans.Read(ProvisioningProfileReader.ReadPropertyList(bytes), EntitlementNames.All, ProfileDictionary);
                profileState = EntitlementSourceState.Read;
            }
            else
            {
                profileState = EntitlementSourceState.Absent;
            }
        }
        catch (Exception exception)
        {
            log.Write(LogLevel.Warning, LogSource, $"Reading the embedded profile failed with {exception.GetType().Name}.");
            profileState = EntitlementSourceState.Unreadable;
        }

        return [.. EntitlementNames.All.Select(name => new EntitlementReading(
            name,
            signature?.GetValueOrDefault(name),
            signatureState,
            profile?.GetValueOrDefault(name),
            profileState))];
    }
}
