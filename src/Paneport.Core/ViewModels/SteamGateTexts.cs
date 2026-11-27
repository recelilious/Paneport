// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using Paneport.Core.Hosting;
using Paneport.Core.Steam;

namespace Paneport.Core.ViewModels;

// The words of the Steam gate page. None of them can hold a secret: they are made from states,
// counts, sizes and times only.
internal static class SteamGateTexts
{
    public static string Session(SteamSessionState state) => state switch
    {
        SteamSessionState.SignedOut => "Signed out",
        SteamSessionState.Connecting => "Connecting",
        SteamSessionState.SigningIn => "Signing in",
        SteamSessionState.LoggingOn => "Logging on",
        SteamSessionState.LoggedOn => "Logged on",
        SteamSessionState.Reconnecting => "Reconnecting",
        SteamSessionState.Offline => "Offline: reconnecting gave up",
        SteamSessionState.SigningOut => "Signing out",
        _ => state.ToString(),
    };

    public static string Saved(bool? saved) => saved switch
    {
        true => "yes",
        false => "no",
        null => "not checked yet",
    };

    public static string Transport(SteamTransport transport) => transport switch
    {
        SteamTransport.WebSocket => "WebSocket",
        SteamTransport.Tcp => "TCP",
        _ => "none yet",
    };

    public static string Stage(SignInProgress progress) => progress.Stage switch
    {
        SignInStage.Connecting => "Connecting to Steam.",
        SignInStage.Starting => "Asking Steam to start the sign-in.",
        SignInStage.WaitingForApproval => "Approve the sign-in in the Steam mobile app, then come back.",
        SignInStage.WaitingForCode when progress.CodeWasWrong => "Steam refused the code. Enter the new one.",
        SignInStage.WaitingForCode when progress.CodeKind == SteamCodeKind.Email => "Enter the code that Steam sent to your email.",
        SignInStage.WaitingForCode => "Enter the Steam Guard code from the Steam mobile app.",
        SignInStage.ShowingQrCode => "Scan the code with the Steam mobile app on another device.",
        SignInStage.LoggingOn => "Logging on.",
        SignInStage.LoggedOn => "Logged on.",
        SignInStage.Failed => $"The sign-in failed. {progress.Problem}".TrimEnd(),
        SignInStage.Cancelled => "The sign-in was cancelled.",
        _ => progress.Stage.ToString(),
    };

    public static string Seconds(TimeSpan time) => string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.0} s");

    public static string Bytes(ulong bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes:N0} bytes");

    public static string Times(SignInResult result) =>
        $"total {Seconds(result.Total)}, waiting for you {Seconds(result.WaitingForUser)}";

    public static string Grant(SteamGrant grant) => grant == SteamGrant.Obtained ? "obtained" : "not granted";

    public static string Manifest(ManifestSummary manifest) => string.Create(
        CultureInfo.InvariantCulture,
        $"{manifest.FileCount:N0} files, {manifest.ChunkCount:N0} chunks, {Bytes(manifest.TotalSize)}, downloaded in {Seconds(manifest.DownloadTime)}");

    public static string Chunks(ChunkRunResult chunks) => string.Create(
        CultureInfo.InvariantCulture,
        $"{chunks.Verified} verified, {chunks.Rejected} rejected, {chunks.Failed} failed of {chunks.Requested}; {Bytes(chunks.DownloadedBytes)} downloaded, {Bytes(chunks.VerifiedBytes)} verified, in {Seconds(chunks.Time)}");

    public static string Thermal(ThermalState state) => state switch
    {
        ThermalState.Nominal => "nominal",
        ThermalState.Fair => "fair",
        ThermalState.Serious => "serious",
        ThermalState.Critical => "critical",
        _ => state.ToString(),
    };
}
