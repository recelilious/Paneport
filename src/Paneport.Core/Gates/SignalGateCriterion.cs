// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Gates;

/// <summary>One criterion of the signal gate's verdict, as the result screen lists it.</summary>
/// <param name="Name">What it checks, such as <c>Duration</c>.</param>
/// <param name="Met">Whether the run met it.</param>
/// <param name="Observed">What the run showed, such as <c>4:00:12 of at least 4:00:00</c>.</param>
public sealed record SignalGateCriterion(string Name, bool Met, string Observed);
