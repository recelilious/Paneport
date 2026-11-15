// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using Paneport.Core.Hosting;

namespace Paneport.Core.Diagnostics;

/// <summary>One sample of the memory timeline: the host's budget and the thermal state at one time.</summary>
/// <param name="Elapsed">The time since the timeline's first sample.</param>
/// <param name="Budget">The memory budget that the host reported.</param>
/// <param name="ThermalState">The device's thermal state.</param>
public readonly record struct MemorySample(TimeSpan Elapsed, MemoryBudget Budget, ThermalState ThermalState);
