// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Paneport.Core.Diagnostics;
using Paneport.Core.Hosting;
using Paneport.Core.Logging;
using Paneport.Core.Steam;

namespace Paneport.Core.ViewModels;

/// <summary>
/// The Steam gate page (S2.3.9): everything that L2 steps 16 to 28 do and read, from sign-in to
/// verified chunks, with a summary to copy into the evidence record (C-0012 section 3).
/// </summary>
/// <remarks>
/// <para>
/// Only the developer menu of the device test flavor and a developer entry of the desktop head open
/// it. It shows no password, token, depot key, manifest request code or Steam ID: the services it
/// talks to never hand it one. The password box is emptied as soon as a sign-in starts. The QR code's
/// challenge is offered only for the view to draw.
/// </para>
/// <para>
/// The services report changes from their own threads; the page posts each update to the UI thread
/// through the dispatcher, and reads the latest state there.
/// </para>
/// </remarks>
public sealed partial class SteamGateViewModel : PageViewModel
{
    /// <summary>How many chunks the chunk step downloads (L2 step 26).</summary>
    public const int ChunkCount = 10;

    private const string LogSource = nameof(SteamGateViewModel);

    private readonly ISteamService _steam;
    private readonly ISteamGateSteps _steps;
    private readonly MemoryMonitor? _memory;
    private readonly IUiDispatcher _dispatcher;
    private readonly Dictionary<string, SignInResult> _signIns = new(StringComparer.Ordinal);
    private ISignInAttempt? _attempt;
    private bool _sawApproval;
    private bool _sawCode;
    private int _licenses = -1;
    private SteamAppInfo? _app;
    private SteamGrant? _depotKey;
    private SteamGrant? _manifestCode;
    private ManifestSummary? _manifest;
    private ChunkRunResult? _chunks;

    /// <summary>Creates the page.</summary>
    /// <param name="steam">The Steam service.</param>
    /// <param name="steps">The steps of the gate after sign-in.</param>
    /// <param name="build">The build stamp, for the build line and the summary.</param>
    /// <param name="memory">The memory timeline of the run, for the summary; null where there is none.</param>
    /// <param name="dispatcher">Runs the services' updates on the UI thread.</param>
    /// <param name="log">The log.</param>
    public SteamGateViewModel(
        ISteamService steam,
        ISteamGateSteps steps,
        BuildStamp build,
        MemoryMonitor? memory,
        IUiDispatcher dispatcher,
        ILog log)
        : base(log)
    {
        ArgumentNullException.ThrowIfNull(steam);
        ArgumentNullException.ThrowIfNull(steps);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentNullException.ThrowIfNull(dispatcher);
        _steam = steam;
        _steps = steps;
        _memory = memory;
        _dispatcher = dispatcher;
        BuildText = $"{build.Configuration}, {build.Runtime}, {build.CompilationMode}, interpreter: {build.Interpreter}";
        ApplyStatus();
        UpdateSummary();
    }

    /// <summary>Gets the page's heading.</summary>
    public override string Header => "Steam gate";

    /// <summary>Gets the build line: configuration, runtime, compilation and interpreter (L2 step 16).</summary>
    public string BuildText { get; }

    /// <summary>Gets the session's state as text.</summary>
    [ObservableProperty]
    public partial string SessionText { get; private set; } = string.Empty;

    /// <summary>Gets whether a sign-in is saved in the token store, as text; the token itself is never shown.</summary>
    [ObservableProperty]
    public partial string TokenStoredText { get; private set; } = string.Empty;

    /// <summary>Gets the transport of the connection, as text.</summary>
    [ObservableProperty]
    public partial string TransportText { get; private set; } = string.Empty;

    /// <summary>Gets what went wrong with the session last; null when nothing did.</summary>
    [ObservableProperty]
    public partial string? SessionProblem { get; private set; }

    /// <summary>Gets or sets the account name to sign in with.</summary>
    [ObservableProperty]
    public partial string AccountName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password, which the page drops when the sign-in starts.</summary>
    [ObservableProperty]
    public partial string Password { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether to enter a code instead of approving in the Steam mobile app.</summary>
    [ObservableProperty]
    public partial bool EnterCodeInstead { get; set; }

    /// <summary>Gets or sets the Steam Guard code that the sign-in waits for.</summary>
    [ObservableProperty]
    public partial string Code { get; set; } = string.Empty;

    /// <summary>Gets where the running or last sign-in stands, as text.</summary>
    [ObservableProperty]
    public partial string SignInStageText { get; private set; } = "No sign-in yet.";

    /// <summary>Gets a value indicating whether a sign-in is running.</summary>
    [ObservableProperty]
    public partial bool IsSigningIn { get; private set; }

    /// <summary>Gets a value indicating whether the sign-in waits for an approval in the Steam mobile app.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForApproval { get; private set; }

    /// <summary>Gets a value indicating whether the sign-in waits for a code.</summary>
    [ObservableProperty]
    public partial bool IsWaitingForCode { get; private set; }

    /// <summary>Gets a value indicating whether the sign-in shows a QR code.</summary>
    [ObservableProperty]
    public partial bool IsShowingQrCode { get; private set; }

    /// <summary>Gets the text that the QR code encodes, for the view to draw and never to show as text.</summary>
    [ObservableProperty]
    public partial string? QrChallenge { get; private set; }

    /// <summary>Gets the times of the last sign-in or resume, as text.</summary>
    [ObservableProperty]
    public partial string SignInTimesText { get; private set; } = string.Empty;

    /// <summary>Gets or sets the app ID to read, as typed.</summary>
    [ObservableProperty]
    public partial string AppIdText { get; set; } = string.Empty;

    /// <summary>Gets the license count, as text.</summary>
    [ObservableProperty]
    public partial string LicensesText { get; private set; } = string.Empty;

    /// <summary>Gets the app's name and its problems, as text.</summary>
    [ObservableProperty]
    public partial string AppInfoText { get; private set; } = string.Empty;

    /// <summary>Gets the app's Windows depots.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<SteamDepotRow> Depots { get; private set; } = [];

    /// <summary>Gets or sets the depot that the later steps use; the smallest is chosen first (L2 step 24).</summary>
    [ObservableProperty]
    public partial SteamDepotRow? SelectedDepot { get; set; }

    /// <summary>Gets whether the depot key was obtained, as text; the key is never shown.</summary>
    [ObservableProperty]
    public partial string DepotKeyText { get; private set; } = string.Empty;

    /// <summary>Gets whether the manifest request code was obtained, as text; the code is never shown.</summary>
    [ObservableProperty]
    public partial string ManifestCodeText { get; private set; } = string.Empty;

    /// <summary>Gets the manifest's counts, size and download time, as text.</summary>
    [ObservableProperty]
    public partial string ManifestText { get; private set; } = string.Empty;

    /// <summary>Gets the chunk results, as text.</summary>
    [ObservableProperty]
    public partial string ChunksText { get; private set; } = string.Empty;

    /// <summary>Gets what went wrong with the last step; null when it worked.</summary>
    [ObservableProperty]
    public partial string? StepProblem { get; private set; }

    /// <summary>Gets or sets a value indicating whether the tester saw a system permission prompt, such as Local Network.</summary>
    [ObservableProperty]
    public partial bool PermissionPromptSeen { get; set; }

    /// <summary>Gets the summary for the evidence record (L2 step 27), which holds no secret.</summary>
    [ObservableProperty]
    public partial string Summary { get; private set; } = string.Empty;

    /// <inheritdoc />
    protected override async Task OnLoadAsync(CancellationToken cancellationToken)
    {
        ApplyStatus();
        var result = await _steam.ResumeAsync(cancellationToken);
        if (result.Outcome != SignInOutcome.NothingSaved)
        {
            Record("the saved sign-in", result);
        }

        ApplyStatus();
        UpdateSummary();
    }

    /// <inheritdoc />
    protected override void OnShown()
    {
        _steam.StatusChanged += OnStatusChanged;
        ApplyStatus();
    }

    /// <inheritdoc />
    protected override void OnHidden() => _steam.StatusChanged -= OnStatusChanged;

    partial void OnPermissionPromptSeenChanged(bool value) => UpdateSummary();

    [RelayCommand]
    private Task SignInAsync()
    {
        var accountName = AccountName.Trim();
        if (accountName.Length == 0 || Password.Length == 0)
        {
            SignInStageText = "Enter an account name and a password.";
            return Task.CompletedTask;
        }

        var password = Password;
        Password = string.Empty;
        var confirmation = EnterCodeInstead ? SignInConfirmation.EnterCode : SignInConfirmation.ApproveInSteamApp;
        return RunAttemptAsync(_steam.StartPasswordSignIn(accountName, password, confirmation), withQrCode: false);
    }

    [RelayCommand]
    private Task SignInWithQrCodeAsync() => RunAttemptAsync(_steam.StartQrSignIn(), withQrCode: true);

    [RelayCommand]
    private void SubmitCode()
    {
        if (_attempt is { } attempt && Code.Trim().Length > 0)
        {
            attempt.SubmitCode(Code);
            Code = string.Empty;
        }
    }

    [RelayCommand]
    private void UseCodeInstead() => _attempt?.UseCodeInstead();

    [RelayCommand]
    private void CancelSignIn() => _attempt?.Cancel();

    [RelayCommand]
    private async Task SignOutAsync(CancellationToken cancellationToken)
    {
        _attempt?.Cancel();
        await RunStepAsync("Sign out", async () =>
        {
            await _steam.SignOutAsync(cancellationToken);
            SignInStageText = "Signed out.";
        });
    }

    [RelayCommand]
    private Task CountLicensesAsync(CancellationToken cancellationToken) => RunStepAsync("Licenses", async () =>
    {
        _licenses = await _steps.CountLicensesAsync(cancellationToken);
        LicensesText = string.Create(CultureInfo.InvariantCulture, $"{_licenses:N0} licenses");
    });

    [RelayCommand]
    private Task ReadAppInfoAsync(CancellationToken cancellationToken) => RunStepAsync("App info", async () =>
    {
        if (!uint.TryParse(AppIdText.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var appId) || appId == 0)
        {
            throw new SteamStepException("Enter an app ID, a number above 0.");
        }

        _app = await _steps.ReadAppInfoAsync(appId, cancellationToken);
        _depotKey = null;
        _manifestCode = null;
        _manifest = null;
        _chunks = null;
        DepotKeyText = ManifestCodeText = ManifestText = ChunksText = string.Empty;
        Depots = [.. _app.WindowsDepots.Select(depot => new SteamDepotRow(depot))];
        SelectedDepot = Depots
            .Where(row => row.Depot.HasPublicManifest)
            .OrderBy(row => row.Depot.Size ?? ulong.MaxValue)
            .FirstOrDefault() ?? (Depots.Count > 0 ? Depots[0] : null);
        var name = _app.Name ?? "an app without a name";
        var problems = _app.Problems.Count == 0 ? string.Empty : " Missing: " + string.Join(" ", _app.Problems);
        AppInfoText = string.Create(CultureInfo.InvariantCulture, $"{name}, {_app.WindowsDepots.Count} Windows depots.{problems}");
    });

    [RelayCommand]
    private Task GetDepotKeyAsync(CancellationToken cancellationToken) => RunStepAsync("Depot key", async () =>
    {
        _depotKey = await _steps.GetDepotKeyAsync(RequireDepot(), cancellationToken);
        DepotKeyText = "depot key: " + SteamGateTexts.Grant(_depotKey.Value);
    });

    [RelayCommand]
    private Task GetManifestCodeAsync(CancellationToken cancellationToken) => RunStepAsync("Manifest code", async () =>
    {
        _manifestCode = await _steps.GetManifestRequestCodeAsync(RequireDepot(), cancellationToken);
        ManifestCodeText = "manifest request code: " + SteamGateTexts.Grant(_manifestCode.Value);
    });

    [RelayCommand]
    private Task DownloadManifestAsync(CancellationToken cancellationToken) => RunStepAsync("Manifest", async () =>
    {
        _manifest = await _steps.DownloadManifestAsync(RequireDepot(), cancellationToken);
        ManifestText = SteamGateTexts.Manifest(_manifest);
    });

    [RelayCommand]
    private Task DownloadChunksAsync(CancellationToken cancellationToken) => RunStepAsync("Chunks", async () =>
    {
        _chunks = await _steps.DownloadChunksAsync(ChunkCount, cancellationToken);
        ChunksText = SteamGateTexts.Chunks(_chunks);
    });

    [RelayCommand]
    private Task DeleteTestDataAsync(CancellationToken cancellationToken) => RunStepAsync("Delete test data", async () =>
    {
        await _steps.DeleteTestDataAsync(cancellationToken);
        ChunksText = "The test data was deleted.";
    });

    private uint RequireDepot() =>
        SelectedDepot?.Depot.DepotId ?? throw new SteamStepException("Read an app's information and choose a depot first.");

    // Runs a step; a failure shows its own words when it is a step exception, and its type otherwise,
    // because another exception's message can hold a server's address or a path.
    private async Task RunStepAsync(string step, Func<Task> run)
    {
        StepProblem = null;
        try
        {
            await run();
            Log.Write(LogLevel.Information, LogSource, $"{step}: done.");
        }
        catch (SteamStepException exception)
        {
            StepProblem = $"{step}: {exception.Message}";
            Log.Write(LogLevel.Warning, LogSource, $"{step} could not run.");
        }
        catch (OperationCanceledException)
        {
            StepProblem = $"{step}: cancelled.";
        }
        catch (Exception exception)
        {
            StepProblem = $"{step} failed ({exception.GetType().Name}).";
            Log.Write(LogLevel.Warning, LogSource, $"{step} failed with {exception.GetType().Name}.");
        }

        ApplyStatus();
        UpdateSummary();
    }

    private async Task RunAttemptAsync(ISignInAttempt attempt, bool withQrCode)
    {
        if (_attempt is { } running)
        {
            running.ProgressChanged -= OnProgressChanged;
        }

        _attempt = attempt;
        _sawApproval = false;
        _sawCode = false;
        attempt.ProgressChanged += OnProgressChanged;
        ApplyProgress();
        var result = await attempt.Completion;
        attempt.ProgressChanged -= OnProgressChanged;
        if (ReferenceEquals(_attempt, attempt))
        {
            ApplyProgress();
            _attempt = null;
            IsSigningIn = IsWaitingForApproval = IsWaitingForCode = IsShowingQrCode = false;
            QrChallenge = null;
        }

        if (result.Outcome == SignInOutcome.LoggedOn)
        {
            Record(withQrCode ? "a QR code" : _sawCode ? "a code" : _sawApproval ? "an approval" : "the password alone", result);
        }

        ApplyStatus();
        UpdateSummary();
    }

    private void OnProgressChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        if (ReferenceEquals(sender, _attempt))
        {
            ApplyProgress();
        }
    });

    private void OnStatusChanged(object? sender, EventArgs e) => _dispatcher.Post(() =>
    {
        ApplyStatus();
        UpdateSummary();
    });

    private void ApplyProgress()
    {
        if (_attempt is not { } attempt)
        {
            return;
        }

        var progress = attempt.Progress;
        _sawApproval |= progress.Stage == SignInStage.WaitingForApproval;
        _sawCode |= progress.Stage == SignInStage.WaitingForCode;
        SignInStageText = SteamGateTexts.Stage(progress);
        IsSigningIn = !progress.HasEnded;
        IsWaitingForApproval = progress.Stage == SignInStage.WaitingForApproval;
        IsWaitingForCode = progress.Stage == SignInStage.WaitingForCode;
        IsShowingQrCode = progress.Stage == SignInStage.ShowingQrCode;
        QrChallenge = IsShowingQrCode ? progress.QrChallenge : null;
    }

    private void ApplyStatus()
    {
        var status = _steam.Status;
        SessionText = SteamGateTexts.Session(status.State);
        TokenStoredText = SteamGateTexts.Saved(status.SignInSaved);
        TransportText = SteamGateTexts.Transport(status.Transport);
        SessionProblem = status.Problem;
    }

    private void Record(string how, SignInResult result)
    {
        _signIns[how] = result;
        SignInTimesText = result.Outcome == SignInOutcome.LoggedOn
            ? $"Logged on with {how}: {SteamGateTexts.Times(result)}."
            : $"Logging on with {how} failed: {result.Problem}";
        Log.Write(LogLevel.Information, LogSource, $"Sign-in with {how}: {result.Outcome}, {SteamGateTexts.Times(result)}.");
    }

    private void UpdateSummary()
    {
        var text = new StringBuilder();
        text.AppendLine(CultureInfo.InvariantCulture, $"Build: {BuildText}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Session: {SessionText}; sign-in saved: {TokenStoredText}; transport: {TransportText}");
        foreach (var (how, result) in _signIns)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Sign-in with {how}: {result.Outcome}, {SteamGateTexts.Times(result)}");
        }

        if (_licenses >= 0)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Licenses: {_licenses:N0}");
        }

        if (_app is { } app)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"App information: {app.WindowsDepots.Count} Windows depots, {app.Problems.Count} problems");
        }

        if (_depotKey is { } key)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Depot key: {SteamGateTexts.Grant(key)}");
        }

        if (_manifestCode is { } code)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Manifest request code: {SteamGateTexts.Grant(code)}");
        }

        if (_manifest is { } manifest)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Manifest: {SteamGateTexts.Manifest(manifest)}");
        }

        if (_chunks is { } chunks)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Chunks: {SteamGateTexts.Chunks(chunks)}");
        }

        text.AppendLine(CultureInfo.InvariantCulture, $"System permission prompt seen: {(PermissionPromptSeen ? "yes" : "no")}");
        if (_memory is { SampleCount: > 0 } memory)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"Peak memory footprint: {memory.PeakFootprintBytes / (1024 * 1024):N0} MB");
            text.AppendLine(CultureInfo.InvariantCulture, $"Highest thermal state: {SteamGateTexts.Thermal(memory.HighestThermalState)}");
        }

        Summary = text.ToString().TrimEnd();
    }
}
