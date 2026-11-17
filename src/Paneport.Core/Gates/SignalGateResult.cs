// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Gates;

/// <summary>The verdict of a signal gate run and the criteria it rests on.</summary>
/// <param name="Passed">Whether every criterion holds.</param>
/// <param name="Criteria">The criteria of L2 step 13, in its order.</param>
public sealed record SignalGateResult(bool Passed, IReadOnlyList<SignalGateCriterion> Criteria)
{
    /// <summary>Gets the verdict as the result screen shows it: <c>PASS</c> or <c>FAIL</c>.</summary>
    public string Verdict => Passed ? "PASS" : "FAIL";
}
