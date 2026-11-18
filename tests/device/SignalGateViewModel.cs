// SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
// SPDX-License-Identifier: MIT

using System.Globalization;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Foundation;
using Paneport.Core.Diagnostics;
using Paneport.Core.Gates;
using Paneport.Core.Logging;
using Paneport.Core.ViewModels;
using Paneport.Interop;
using UIKit;

namespace Paneport.DeviceTest;

// The signal gate page (L2 steps 11 to 13, docs/design/signals.md section 9): the startup check,
// a run of four hours or of ten minutes with live counters, and the result screen with the
// verdict. While a run lasts the screen stays on, and the run turns invalid if the app leaves
// the foreground. The page lives on the UI thread.
internal sealed class SignalGateViewModel : PageViewModel
{
    private const string LogSource = "SignalGate";

    // The null-reference exceptions that each refresh throws on the main thread.
    private const int MainThreadExceptions = 64;

    private static readonly TimeSpan s_refreshInterval = TimeSpan.FromSeconds(1);

    private readonly CrashChainingState _crashChaining;
    private SignalGateRun? _run;
    private IDisposable? _timer;
    private NSObject? _backgroundObserver;
    private string _startupCheck = string.Empty;
    private string _status = "No run yet.";
    private string _counters = string.Empty;
    private string _result = string.Empty;

    public SignalGateViewModel(CrashChainingState crashChaining, ILog log)
        : base(log)
    {
        _crashChaining = crashChaining;
        StartGateRunCommand = new RelayCommand(() => Start(SignalGate.GateDuration), CanStart);
        StartShortRunCommand = new RelayCommand(() => Start(SignalGate.ShortDuration), CanStart);
        StopCommand = new RelayCommand(StopEarly, () => _run is { Finished: false });
        CheckStartup();
    }

    public override string Header => "Signal gate";

    public static string Description =>
        "Runs the signal dispatcher's stress test: a managed null-reference loop, marked native threads "
            + "that fault on a guard page and a read-only page, unmarked threads that fault in a guest "
            + "reservation, garbage collections and a heartbeat. Start the run, leave the app in the "
            + "foreground without touching it, and read the result when the timer ends.";

    public RelayCommand StartGateRunCommand { get; }

    public RelayCommand StartShortRunCommand { get; }

    public RelayCommand StopCommand { get; }

    // L2 step 11: the call that turned off Mono's crash chaining, and the dispatcher's six signals.
    public string StartupCheck
    {
        get => _startupCheck;
        private set => SetProperty(ref _startupCheck, value);
    }

    public string Status
    {
        get => _status;
        private set => SetProperty(ref _status, value);
    }

    // L2 step 12: the live counters.
    public string Counters
    {
        get => _counters;
        private set => SetProperty(ref _counters, value);
    }

    // L2 step 13: the verdict and every criterion.
    public string Result
    {
        get => _result;
        private set => SetProperty(ref _result, value);
    }

    private static string SignalName(int signal) => signal switch
    {
        4 => "SIGILL",
        5 => "SIGTRAP",
        6 => "SIGABRT",
        8 => "SIGFPE",
        10 => "SIGBUS",
        11 => "SIGSEGV",
        _ => string.Create(CultureInfo.InvariantCulture, $"signal {signal}"),
    };

    private static string Count(ulong value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string CountersText(SignalGateRun run, SignalGateCounters counters) =>
        string.Join(
            '\n',
            $"Elapsed: {SignalGate.Clock(counters.Duration)} of {SignalGate.Clock(run.Target)}",
            $"Null-reference exceptions: {Count(counters.ExceptionsThrown)} thrown, {Count(counters.ExceptionsCaught)} caught",
            $"Guest faults by mark: {Count(counters.GuestFaultsRaisedByMark)} raised, {Count(counters.GuestFaultsHandledByMark)} handled",
            $"Guest faults by address: {Count(counters.GuestFaultsRaisedByAddress)} raised, {Count(counters.GuestFaultsHandledByAddress)} handled",
            $"Faults passed to the runtime's handler: {Count(counters.RuntimeFaults)}",
            $"Unexpected faults: {Count(counters.UnexpectedFaults)}",
            string.Create(CultureInfo.InvariantCulture, $"Garbage collections: {counters.GarbageCollections:N0}"),
            string.Create(CultureInfo.InvariantCulture, $"Longest heartbeat gap: {counters.LongestHeartbeatGap.TotalSeconds:0.000} s"),
            counters.StayedInForeground ? "Foreground: never left" : "Foreground: left, so the run is invalid");

    private static string ResultText(SignalGateRun run, SignalGateResult result)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Verdict: {result.Verdict}\n");
        text.Append(CultureInfo.InvariantCulture, $"Run of {SignalGate.Clock(run.Target)}\n");
        foreach (var criterion in result.Criteria)
        {
            text.Append(CultureInfo.InvariantCulture, $"{(criterion.Met ? "met" : "NOT MET")}: {criterion.Name}: {criterion.Observed}\n");
        }

        return text.ToString().TrimEnd();
    }

    protected override void OnShown()
    {
        if (_run is null)
        {
            CheckStartup();
        }
    }

    private bool CanStart() => _run is null or { Finished: true };

    private void CheckStartup()
    {
        string dispatcher;
        try
        {
            var replaced = HostTestExports.CheckSignalsInstalled();
            dispatcher = replaced == 0
                ? "installed for SIGSEGV, SIGBUS, SIGILL, SIGFPE, SIGTRAP and SIGABRT"
                : $"not installed: {SignalName(replaced)} has another handler";
        }
        catch (Exception exception)
        {
            dispatcher = $"not checked ({exception.GetType().Name})";
        }

        StartupCheck = $"Crash chaining: {_crashChaining.Description}\nDispatcher: {dispatcher}";
    }

    private void Start(TimeSpan target)
    {
        try
        {
            var run = new SignalGateRun(target, TimeProvider.System);
            run.Start();
            _run = run;
        }
        catch (Exception exception)
        {
            Status = $"FAIL: the run did not start ({exception.GetType().Name}: {exception.Message})";
            Log.Write(LogLevel.Warning, LogSource, $"The run did not start ({exception.GetType().Name}).");
            return;
        }

        UIApplication.SharedApplication.IdleTimerDisabled = true;
        _backgroundObserver = UIApplication.Notifications.ObserveDidEnterBackground((_, _) => _run?.MarkLeftForeground());
        _timer = DispatcherTimer.Run(Refresh, s_refreshInterval);
        Result = string.Empty;
        Status = $"Running: leave the app in the foreground for {SignalGate.Clock(target)}.";
        Log.Write(LogLevel.Information, LogSource, $"A run of {SignalGate.Clock(target)} started.");
        UpdateCommands();
        Refresh();
    }

    private bool Refresh()
    {
        if (_run is not { Finished: false } run)
        {
            return false;
        }

        if (run.Elapsed >= run.Target)
        {
            Finish(run, "The run ended at its target.");
            return false;
        }

        run.ThrowOnCallingThread(MainThreadExceptions);
        Counters = CountersText(run, run.Snapshot());
        return true;
    }

    private void StopEarly()
    {
        if (_run is { Finished: false } run)
        {
            Finish(run, "The run was stopped before its target.");
        }
    }

    private void Finish(SignalGateRun run, string status)
    {
        var counters = run.Stop();
        _timer?.Dispose();
        _timer = null;
        _backgroundObserver?.Dispose();
        _backgroundObserver = null;
        UIApplication.SharedApplication.IdleTimerDisabled = false;

        var result = SignalGate.Evaluate(counters, run.Target);
        Counters = CountersText(run, counters);
        Result = ResultText(run, result);
        Status = status;
        Log.Write(LogLevel.Information, LogSource, $"A run of {SignalGate.Clock(run.Target)} ended: {result.Verdict}.");
        UpdateCommands();
    }

    private void UpdateCommands()
    {
        StartGateRunCommand.NotifyCanExecuteChanged();
        StartShortRunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }
}
