// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;
using Paneport.Core.Logging;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// Reads the device's model identifier from the native host (<c>pp_device_get_model</c>), for an
/// <see cref="IDeviceInfo"/> of the device.
/// </summary>
public static class DeviceModel
{
    /// <summary>What Diagnostics shows when the host cannot tell the model.</summary>
    public const string Unknown = "unknown";

    /// <summary>Gets the version of the host interface that added <c>pp_device_get_model</c>.</summary>
    public static HostAbiVersion AddedIn { get; } = new(1, 1);

    /// <summary>Reads the model identifier, such as <c>iPhone18,1</c>.</summary>
    /// <param name="host">The host's calls.</param>
    /// <param name="log">The log, which gets the status of a reading that failed.</param>
    /// <returns>
    /// The model identifier, or <see cref="Unknown"/> when the host cannot tell it, as a host older
    /// than <see cref="AddedIn"/> cannot.
    /// </returns>
    public static string Read(INativeHost host, ILog log)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(log);
        try
        {
            return host.ReadDeviceModel();
        }
        catch (NativeHostException exception)
        {
            log.Write(LogLevel.Warning, nameof(DeviceModel), $"Reading the model identifier failed ({exception.Status}).");
            return Unknown;
        }
    }
}
