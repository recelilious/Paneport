// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Foundation;
using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using UIKit;

namespace Paneport.IOS;

// What the device tells about itself: the model identifier from the native host (hw.machine), the
// iOS version and build, and the thermal state, never the device's name (C-0012 section 4). The
// version and the build are read once, on the main thread; the thermal state on any thread.
internal sealed class DeviceInfo : IDeviceInfo
{
    public DeviceInfo(INativeHost host, ILog log)
    {
        try
        {
            ModelIdentifier = host.ReadDeviceModel();
        }
        catch (NativeHostException exception)
        {
            log.Write(LogLevel.Warning, nameof(DeviceInfo), $"Reading the model identifier failed ({exception.Status}).");
            ModelIdentifier = "unknown";
        }

        SystemVersion = UIDevice.CurrentDevice.SystemVersion;
        SystemBuild = SystemVersionText.BuildOf(NSProcessInfo.ProcessInfo.OperatingSystemVersionString);
    }

    public string ModelIdentifier { get; }

    public string SystemVersion { get; }

    public string SystemBuild { get; }

    public ThermalState ReadThermalState() => NSProcessInfo.ProcessInfo.ThermalState switch
    {
        NSProcessInfoThermalState.Fair => ThermalState.Fair,
        NSProcessInfoThermalState.Serious => ThermalState.Serious,
        NSProcessInfoThermalState.Critical => ThermalState.Critical,
        _ => ThermalState.Nominal,
    };
}
