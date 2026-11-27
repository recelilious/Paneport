// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

namespace Paneport.Core.Steam;

/// <summary>
/// A snapshot of a sign-in attempt, for a page to show.
/// </summary>
/// <param name="Stage">Where the attempt stands.</param>
/// <param name="CodeKind">The code asked for while <paramref name="Stage"/> is <see cref="SignInStage.WaitingForCode"/>; otherwise <see cref="SteamCodeKind.None"/>.</param>
/// <param name="CodeWasWrong">Whether Steam refused the code entered before, so that the page asks again.</param>
/// <param name="QrChallenge">
/// The text that the QR code encodes while <paramref name="Stage"/> is
/// <see cref="SignInStage.ShowingQrCode"/>; otherwise null. Steam replaces it from time to time, and
/// the page draws the code again. It is shown only as a QR code and never logged.
/// </param>
/// <param name="QrRevision">How many times the QR challenge changed after the first, so a page can tell a new code from the old.</param>
/// <param name="Problem">Why the attempt failed, in words that name no secret, when <paramref name="Stage"/> is <see cref="SignInStage.Failed"/>.</param>
public sealed record SignInProgress(
    SignInStage Stage,
    SteamCodeKind CodeKind = SteamCodeKind.None,
    bool CodeWasWrong = false,
    string? QrChallenge = null,
    int QrRevision = 0,
    string? Problem = null)
{
    /// <summary>Gets a value indicating whether the attempt has ended.</summary>
    public bool HasEnded => Stage is SignInStage.LoggedOn or SignInStage.Failed or SignInStage.Cancelled;

    /// <summary>Returns a text that leaves out the QR challenge.</summary>
    /// <returns>The text.</returns>
    public override string ToString() =>
        $"{nameof(SignInProgress)} {{ Stage = {Stage}, CodeKind = {CodeKind}, CodeWasWrong = {CodeWasWrong}, QrRevision = {QrRevision} }}";
}
