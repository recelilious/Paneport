// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;

namespace Paneport.Tests.Fakes;

// A device whose readings a test sets. The model identifier is an allowed device fact (C-0011
// section 2).
internal sealed class FakeDeviceInfo : IDeviceInfo
{
    public string ModelIdentifier { get; init; } = "iPhone18,1";

    public string SystemVersion { get; init; } = "26.1";

    public string SystemBuild { get; init; } = "23B85";

    public ThermalState Thermal { get; set; } = ThermalState.Nominal;

    public ThermalState ReadThermalState() => Thermal;
}
