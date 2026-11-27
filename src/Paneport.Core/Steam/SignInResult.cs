// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// How a sign-in or a resume ended, with its times, which the SteamKit2 gate records
/// (C-0012 section 3).
/// </summary>
/// <param name="Outcome">How it ended.</param>
/// <param name="Total">The time from the start to the end.</param>
/// <param name="WaitingForUser">
/// The part of <paramref name="Total"/> spent waiting for the user: for an approval, a code or a QR scan.
/// </param>
/// <param name="Problem">Why it failed, in words that name no secret; null unless <paramref name="Outcome"/> is <see cref="SignInOutcome.Failed"/>.</param>
public sealed record SignInResult(
    SignInOutcome Outcome,
    TimeSpan Total,
    TimeSpan WaitingForUser,
    string? Problem)
{
    /// <summary>Gets the result of a resume that found no saved sign-in.</summary>
    public static SignInResult NothingSaved { get; } = new(SignInOutcome.NothingSaved, TimeSpan.Zero, TimeSpan.Zero, Problem: null);
}
