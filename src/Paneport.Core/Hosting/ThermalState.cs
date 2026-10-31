// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Hosting;

/// <summary>
/// The thermal states of iOS, which the host reports as events (C-0005 section 2) and a test
/// report records as the highest one seen (C-0008 section 4).
/// </summary>
public enum ThermalState
{
    /// <summary>The device is within its normal range, written <c>nominal</c>.</summary>
    Nominal,

    /// <summary>The device is slightly warm, written <c>fair</c>.</summary>
    Fair,

    /// <summary>The device is hot and iOS reduces performance, written <c>serious</c>.</summary>
    Serious,

    /// <summary>The device is very hot and must cool down, written <c>critical</c>.</summary>
    Critical,
}
