// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;

namespace Paneport.Desktop.Fakes;

// Stands in for the device on Windows, saying that it is not one, so that Diagnostics never shows a
// desktop value as a device reading.
internal sealed class DesktopDevice : IDeviceInfo
{
    public string ModelIdentifier => "None: the desktop head does not run on a device";

    public string SystemVersion => "None";

    public string SystemBuild => "None";

    public ThermalState ReadThermalState() => ThermalState.Nominal;
}
