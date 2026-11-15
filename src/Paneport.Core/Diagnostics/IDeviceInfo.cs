// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Core.Diagnostics;

/// <summary>
/// What the device tells about itself. It never gives the device's name, which is personal data
/// (C-0012 section 4).
/// </summary>
public interface IDeviceInfo
{
    /// <summary>Gets the device's model identifier, such as <c>iPhone18,1</c>, or a text that says it is not a device.</summary>
    string ModelIdentifier { get; }

    /// <summary>Gets the system's version, such as <c>26.1</c>.</summary>
    string SystemVersion { get; }

    /// <summary>Gets the system's build, such as <c>23B85</c>, or <c>unknown</c>.</summary>
    string SystemBuild { get; }

    /// <summary>Reads the device's thermal state now. Any thread.</summary>
    /// <returns>The thermal state.</returns>
    ThermalState ReadThermalState();
}
