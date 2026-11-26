# Managed Exception Record

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-26T14:45Z |

Back to the [design index](README.md).

This document describes the managed exception record of stage M2.4 (see the [L2 brief](stages/L2-phase-0-gates.md)). It covers:

- which entry points see a .NET exception that goes unhandled, and why each one is needed;
- how one exception is recorded once;
- the record's file, and how it survives the abort that follows;
- what the record holds;
- what the host's crash record says for that abort;
- how the next launch reads both records and shows them;
- the tests, and what the workstation cannot verify.

It was written in the stage's first session, before any code. The changes it needs in C-0005 section 2 and C-0010 section 2 are proposed, as C-0005 1.1 and C-0010 1.3, and are not yet accepted. Section 9 is a proposal for recelilious to choose from.

[signals.md](signals.md) describes the signal dispatcher and the last-resort handler, [native-host.md](native-host.md) the host's crash record, and [ios-app-head.md](ios-app-head.md) the startup and Diagnostics. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Scope

An unhandled .NET exception ends the app, and today neither flavor records it. Paneport's only trace is the host's last-resort record, "A fault that the runtime did not handle.", with no exception type and no frame. On Diagnostics a managed exception therefore looks like a native fault, while the phase 0 signal gate asks for no native crashes (C-0012 section 3). The coordinating session's sweep of `main` at `24908f0`, in the review of [#42](https://github.com/recelilious/Paneport/pull/42), found the gap (finding U1) and gave this design its input (finding U13). Where the sweep's finder and its skeptic differ, this design follows the skeptic.

M2.4 adds these parts:

| Part | Section |
|---|---|
| The entry points | 3 |
| The recorder and the record-once rule | 4 |
| The record's file | 5 |
| What the record holds | 6 |
| The host's message for an abort | 7 |
| Reading and pairing the two records | 8 |
| Diagnostics | 9 |
| The developer page of probes | 10 |

Out of scope:

- The sweep's other findings: timer ticks that stop silently when they throw, the head's unguarded UIKit overrides, and the signal gate's worker threads.
- Changing `MarshalManagedExceptionMode` (C-0009 section 3).
- A record in the desktop head.
- Exporting the record, which comes with the export of the log (C-0004 section 4).
- CoreCLR (M10.1).

## 2. How an unhandled exception ends the app today

| Where the exception is thrown | Where it goes | How the app ends |
|---|---|---|
| Posted work on the UI thread: `Dispatcher.Post` jobs; posts of `AvaloniaSynchronizationContext`, which carry `async void` methods and the rethrows of async commands; and the render job of `MediaContext`, which runs queued layout passes and rendering on the UI thread | `DispatcherOperation.InvokeCore` catches it, raises the dispatcher's filter from its catch block after the throwing frames have unwound, and rethrows it. It then escapes `RunJobs` and the run loop callback that called it | At the native boundary |
| Input: `PresentationSource.HandleInput` runs it through `Dispatcher.Send` | The filter runs before the stack unwinds; the exception escapes `Send` and the platform's input callback | At the native boundary |
| Layout that the platform's resize runs (`TopLevel.HandleResized`), a direct `UpdateLayout`, and the head's UIKit overrides (`ViewDidAppear`, `ViewSafeAreaInsetsDidChange`, `SceneDelegate.WillConnect`) | No dispatcher event; it goes straight to the UIKit callback | At the native boundary |
| A managed thread other than the UI thread, such as the signal gate's threads or a thread-pool work item | The runtime's unhandled-exception path, which raises `AppDomain.UnhandledException` with `IsTerminating` true | Mono's unhandled path calls `abort()` (reasoned) |
| Avalonia's render thread | Avalonia catches and logs it ("Exception in render loop", "Exception when rendering"), and `AvaloniaLogForwarder` writes it to the log | It does not end |
| `InvokeAsync` callbacks and `DispatcherTimer` ticks | The operation's task faults, and a tick that throws stops its timer | It does not end |
| A task that nothing awaits | `TaskScheduler.UnobservedTaskException`, after a garbage collection | It does not end |

**At the native boundary,** .NET for iOS marshals the managed exception. The build sets no `MarshalManagedExceptionMode`, so the mode is the default, `ThrowObjectiveCException`:

1. The runtime raises `ObjCRuntime.Runtime.MarshalManagedException`.
2. It turns the exception into an Objective-C exception and throws it into the native frames.
3. Nothing in UIKit catches it, so the app ends in `abort()` (reasoned).

Under this mode, `AppDomain.UnhandledException` is not raised for an exception on the UI thread (reasoned).

**The abort** raises SIGABRT on that thread:

1. The dispatcher sends the signal to the runtime's handler, because it was sent on an unmarked thread.
2. Mono's SIGABRT handler finds the program counter in native code and chains to the last-resort handler.
3. The last-resort handler writes "A fault that the runtime did not handle." (signals.md section 5).

The dispatcher writes no record before the runtime's handler for a SIGABRT. So if Mono's handler did not chain, no record would remain.

**What was shown, and what was reasoned.** The sweep probed the rows of the UI thread, the render thread, the timers and the tasks headless, on Windows with CoreCLR and Avalonia 12.1.4. The native boundary, `AppDomain.UnhandledException` on iOS and the abort are reasoned from Microsoft's documentation and the runtime's sources. Section 12 lists what stays to be shown.

## 3. Entry points

| Entry point | Project | What it sees | Why it is needed | What it leaves as it is |
|---|---|---|---|---|
| `Dispatcher.UIThread.UnhandledExceptionFilter` | Paneport.UI | Posted work and input (the first two rows of section 2) | It sees most exceptions of the UI thread at the earliest point, before they cross into native code. Unlike the native boundary, it can be tested headless on Windows and Linux | `RequestCatch`, so the dispatcher never catches the exception and never raises `UnhandledException`, to which nothing subscribes. Nothing sets `Handled` |
| `ObjCRuntime.Runtime.MarshalManagedException` | Paneport.iOS | Every managed exception that leaves managed code into native code, among them the first three rows of section 2 when they get there | The third row reaches no dispatcher event. Without this entry point, a layout pass that UIKit starts or a UIKit override would leave no record | The event's `ExceptionMode`. The build keeps the default mode, so C-0009 section 3 does not change |
| `AppDomain.CurrentDomain.UnhandledException` | Paneport.Core | An exception that ends a managed thread (the fourth row) | Neither of the others sees a thread without a dispatcher and without a native boundary | Everything: the event only notifies, and the process ends whatever its handler does |
| `TaskScheduler.UnobservedTaskException` | Paneport.Core | A faulted task that nothing observed (the last row), and later the faulted tasks of the sixth row | Such a fault leaves no trace today | The task, which it does not mark as observed. It writes a log line with the exception's type and no record, because the app goes on |

**Not used.**

- **Avalonia's `Dispatcher.UnhandledException`.** To raise it, the dispatcher must catch the exception, and the only reason to catch it would be to mark it handled. After a handled layout or render exception, Avalonia 12.1.4 leaves the screen frozen: queued layout stops, and after a render exception nothing renders again (finding U7).
- **`AppDomain.FirstChanceException`.** It sees every thrown exception, also those that are caught at once, such as the 64 null references a second of the signal gate.
- **.NET 10's `ExceptionHandling.SetUnhandledExceptionHandler`.** A handler that returns true lets the app go on, which this design rules out. Whether Mono for iOS calls it is not known here.
- **`Runtime.MarshalObjectiveCException`.** An Objective-C exception that enters managed code becomes a managed exception there. If it goes unhandled, the entry points above see it.

**Installation.** `CompositionRoot.Compose` installs the entry points on the main thread:

1. After step 4 of [ios-app-head.md](ios-app-head.md) section 3, which reads the previous run's records, it creates the recorder for the crash record folder. It then installs the entry points of `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `MarshalManagedException`.
2. After step 5, which starts Avalonia's platform, it installs the dispatcher's filter.

An exception before that ends in the composition's own catch, which shows its type in place of the shell. The entry points stay installed for the life of the process. The desktop head installs none, because it has no crash record folder. The UI tests install the dispatcher's filter over a fake writer.

## 4. Recording once

Every entry point calls one recorder, `ManagedExceptionRecorder` in Paneport.Core, with three things: the exception, the entry point, and whether the thread is the main thread. The process has one recorder.

- **The same exception.** The recorder keeps the exception it recorded last. A call writes nothing when its exception is that one, or holds it in its chain of inner exceptions. This happens when:
  - an input exception reaches the filter during `Dispatcher.Send`, and then the native boundary (finding U13);
  - an exception at the boundary wraps one that the filter recorded.
- **Another exception.** A call with another exception writes a new record over the old one. While another thread is still writing, the call writes nothing, so in a crash the first exception's record stays. A later exception replaces an earlier record, so a record that native code made moot by catching its exception (section 12) never hides the exception that ends the app.
- **No lock.** An interlocked gate decides who writes, so no entry point waits for another.
- **It never throws.** Every step of the recorder catches its own failures. A failed write is logged with the failure's type, and the call returns.
- **It changes nothing else.** It never sets `RequestCatch` or `Handled`, and never changes `ExceptionMode`. It returns once the record is on storage, and the runtime then goes on as it would without it. Nothing continues after an unhandled exception (C-0010 section 2, as proposed).

## 5. The record's file, and how it survives the abort

**The choice: a file of its own, written by managed code.** The record is `managed-exception.json` in the crash record folder, `Library/Caches/Paneport/` (C-0004 section 4), next to the host's `crash-record.bin`. The alternative was a host function that the entry points call, with a flag that would make the host's crash record carry the exception. A file of its own is the better choice, for four reasons:

1. **It does not depend on the signal chain.** It is on storage before the abort starts. The chain that writes the host's record, Mono's SIGABRT handler chaining to the last-resort handler, is reasoned, not shown on the device (signals.md section 5).
2. **The host's write cannot replace it.** The host renames its own file, `crash-record.bin`, which has another name.
3. **The host stays as it is.**
   - Its interface gains no function and keeps its version (C-0005 section 1).
   - No managed text is handed to a signal handler.
   - Its record keeps its 128 bytes, whose 63-byte message could not hold the names anyway (native-host.md section 8).
4. **Managed code has more room.** Outside signal context, the writer may allocate, format JSON through a source generator (C-0010 section 2) and use the file system's ordinary calls.

The cost is that the host's record cannot say that a managed exception caused its abort, so Diagnostics pairs the two records by their times (section 8).

**Writing.** The writer runs on the thread that saw the exception, before the entry point returns:

1. It builds the record (section 6) and serializes it into memory, with the source-generated JSON context.
2. It writes the record to `managed-exception.tmp` with a synchronous write that flushes it to storage. `IFileSystem` gains this write, because the asynchronous one may not be awaited on a thread that is ending.
3. It renames the file over `managed-exception.json` with `IFileSystem.ReplaceFile`, so a reader sees either a whole record or the older one.
4. On a failure, it deletes the temporary file and logs the failure's type once.

**The log.** The recorder then writes one log line, "An unhandled managed exception was recorded: *type*, *entry point*.", and flushes the log. `FileLog.Write` only queues its line, and the abort ends the writer's task. So `FileLog` gains a flush that waits until the lines queued before it are written, for at most 1 second. The lines that led up to the exception then reach the file as well.

**The cost.** The write and the rename take a few milliseconds, and the abort waits for them.

**Survival.** The record is on storage before the entry point returns, so before the exception goes on to the abort. Only a failure of the storage itself would lose it.

## 6. What the record holds

The file is UTF-8 JSON, format 1:

| Field | Holds |
|---|---|
| `format` | 1 |
| `time` | When the recorder wrote the record, in UTC, in ISO 8601 with milliseconds |
| `build` | The build stamp's commit and version ([ios-app-head.md](ios-app-head.md) section 6) |
| `entry` | `dispatcher`, `native-boundary` or `thread`: the entry point of section 3 that wrote it |
| `thread` | `main` or `other` |
| `exceptions` | The exception, then its inner exceptions through `InnerException`; at most 4 in all |
| `exceptions[].type` | The exception's type, by its full name, such as `System.NullReferenceException` |
| `exceptions[].frames` | Up to 32 frames, from the throw site outward. Each is named by its type's full name and its method's name, joined by a dot, such as `Paneport.Core.ViewModels.SettingsViewModel.OnShown`. A frame without a name is `?` |
| `exceptions[].framesLeftOut` | How many frames beyond the 32 were left out |

**Where the names come from.** The recorder reads the exception's own frames through `new StackTrace(exception, false)`, without file information. For each frame it calls `DiagnosticMethodInfo.Create(frame)`, which .NET 9 added for logging. It gives the declaring type's full name and the method's name without reflection over the method, which the trimming and AOT analyzers accept (C-0010 section 2). The exception's own frames keep the throw site's methods even when an entry point runs after the stack has unwound (finding U13).

**Limits.**

- Each name holds at most 200 characters, and is cut at a character boundary.
- A file holds at most 4 exceptions of 32 frames each.
- A reader rejects a file over 64 KiB.

**Never in the record:**

- the exception's message, `Data`, `HResult`, `Source` or `HelpLink`;
- file names, line numbers, and IL or native offsets;
- parameter lists and argument values;
- assembly names and paths;
- thread names.

Type and method names are identifiers of code, never data that the user gave. So the record holds no personal data and no path (C-0010 section 2, C-0012 section 4).

A record, with made-up values:

```json
{
  "format": 1,
  "time": "2026-11-26T14:19:37.123Z",
  "build": "53c36ae, 0.1.0 (12)",
  "entry": "dispatcher",
  "thread": "main",
  "exceptions": [
    {
      "type": "System.NullReferenceException",
      "frames": [
        "Paneport.DeviceTest.DeveloperViews.Menu",
        "Avalonia.Controls.Templates.FuncDataTemplate.Build",
        "Avalonia.Controls.Presenters.ContentPresenter.CreateChild"
      ],
      "framesLeftOut": 0
    }
  ]
}
```

## 7. The host's record of the abort

The host gains two changes, both inside `signal_handler.c`:

1. **A message of its own.** A SIGABRT gets the message "An abort: abort() was called, or SIGABRT was sent.". It replaces "A fault that the runtime did not handle." in the last-resort handler, and "A fault outside the guest path that nothing handled." when the saved action is the default action. The change needs no new call, so the signal-safe calls check stays as it is (signals.md section 10).
2. **The record goes first.** A SIGABRT joins the signals whose record the dispatcher writes before the runtime's handler gets them (signals.md section 5). A record written first never describes a crash that did not happen:
   - nothing in Paneport sends SIGABRT;
   - `abort()`, which raises it, never returns: if a handler returns, it puts the default action back and raises the signal again.

   The record therefore stays if Mono's handler does not chain. If Mono chains, the last-resort handler writes the same message again.

The host does not learn what called `abort()`, so Diagnostics pairs its record with the managed one (section 8). The record's layout, the writer and the interface do not change. Signals.md section 3 gains the message in its table, and section 5 gains the SIGABRT.

## 8. The next launch

**Reading.** `CrashRecordStore` reads `managed-exception.json` together with `crash-record.bin`, at the same step of the startup ([ios-app-head.md](ios-app-head.md) section 3, step 4). This comes before the entry points are installed, so a record that this run writes waits for the next launch, as the host's does. The reader rejects the record as malformed when:

- the file is larger than 64 KiB;
- its JSON does not parse;
- it has another format, or lacks a field;
- an entry or thread value is not one it knows;
- it holds more exceptions or frames than the limits allow;
- a name is longer than 200 characters;
- its time does not parse.

A malformed record is shown with its problem and logged, and never stops the app. A valid one is logged as "The previous run left a managed exception record: *type*."

**Pairing.** The two records belong to the same end of a run when the host's record:

- is of SIGABRT;
- is not a test record;
- was written at the managed record's time or up to 10 seconds after it.

The writer's waits can delay the host's record by about 4 seconds (signals.md section 3), and the log's flush adds at most 1 second, so 10 seconds leaves room. Both times come from the device's clock: `CLOCK_REALTIME` for the host, `DateTimeOffset.UtcNow` for the recorder.

**What Diagnostics says.**

| Records found | Summary |
|---|---|
| A managed record and a host record that pairs with it | The previous run ended after an unhandled managed exception. |
| A managed record alone, or with a host record that does not pair with it | The previous run recorded an unhandled managed exception, but no crash record of the host follows it. |
| A host record of SIGABRT alone | The previous run ended with an abort. |
| A host record of another signal | The previous run ended with a fatal fault. (as today) |
| A host test record | A test record from the previous run. (as today) |
| A malformed file | As today, for each file |
| None | No crash record from a previous run. (as today) |

When the two records do not pair, both are shown, each with its own summary, the newer first.

**Dismissing.** One Dismiss button dismisses both records. Each file is deleted only while it still holds the bytes that the launch read ([ios-app-head.md](ios-app-head.md) section 9).

## 9. Diagnostics: the proposed layout

How the page shows the record is recelilious's choice. This section proposes three layouts; once recelilious has chosen, it keeps only the chosen one.

**Layout A, recommended: one section.** The section keeps its title, "Crash record". Below the summary of section 8, a managed record shows these rows:

| Label | Value, with made-up examples |
|---|---|
| Time | `2026-11-26 14:19:37 UTC` |
| Exception | `System.NullReferenceException` |
| Inner exceptions | `None`, or the types in order, such as `System.InvalidOperationException, then System.IO.IOException` |
| Where | One of four texts: `Avalonia's dispatcher, on the main thread`; `A call from iOS into the app, on the main thread`; `A call from iOS into the app, on another thread`; `A managed thread other than the main thread` |
| Thrown in | The first frame, such as `Paneport.DeviceTest.DeveloperViews.Menu` |
| Frames | The first 8 frames, one per line, then `and 23 more`, with a button `Show all frames` |
| Build | `53c36ae, 0.1.0 (12)` |

When the records pair, the line "The host's record of the abort that followed:" comes next, then the host's rows as today: Time, Signal, Thread, Fault address, Program counter, Test record and Message. One button, `Dismiss`, ends the section.

**Layout B: two sections.**

- "Crash record" keeps the host's record as today.
- A new section, "Managed exception", holds the rows of layout A.

Each section has its own Dismiss. The managed section's summary says whether the host's record above belongs to it.

**Layout C: as A, folded.** As layout A, but when the records pair, the host's rows stay hidden behind a button `Show the host's record`.

## 10. The developer page of exception probes

The test flavor's developer menu gains a page, "Exception probes". Each button but the last ends the app, and the next launch's Diagnostics shows the record:

| Button | What throws | Entry point expected |
|---|---|---|
| Throw in a posted job | A job posted with `Dispatcher.UIThread.Post` | `dispatcher` |
| Throw in a layout pass | A control whose `MeasureOverride` throws, in a queued layout pass | `dispatcher` |
| Throw in this tap | The button's own command | `dispatcher`, then the native boundary, recorded once |
| Throw in a UIKit callback | A one-shot `NSTimer` whose block throws | `native-boundary` |
| Throw on the next rotation | Arms a control whose `MeasureOverride` throws in the next layout pass that a resize runs; the tester then turns the phone | `native-boundary` |
| Throw on a managed thread | A new thread | `thread` |
| Fault a task that nothing awaits | `Task.Run`, then a garbage collection | A log line only; the app goes on |

Each probe throws an `ExceptionProbeException`. Its message holds a made-up path and a made-up token, assembled at run time, so the device check also shows that a message never reaches the record or Diagnostics. The M2.2 note in the L2 brief proposes native fault probes of the same kind, one per launch; if L2's integration adds them, this page can carry them.

## 11. Tests

The tests follow C-0011 section 1:

- they use hand-written fakes;
- their inputs are synthetic, and made-up values are assembled at run time, so that no source holds an identifier's shape;
- the new public APIs of Paneport.Core come with tests;
- a fix comes with a test that fails without it, wherever that is possible.

**C# unit tests,** in `tests/Paneport.Core.Tests`:

- **The record.** An exception with two inner exceptions, thrown and caught by the test, round-trips. The limits hold:
  - 33 frames give 32 and one left out;
  - 5 exceptions give 4;
  - a name of 300 characters is cut at 200, at a character boundary, also with a surrogate pair at the cut.

  The file holds none of the made-up path, token and e-mail address of the exception's message, and no file name or line number.
- **The reader.** Every malformed case of section 8.
- **The recorder.**
  - The same exception from two entry points is written once.
  - An outer exception whose inner exception was recorded is not written.
  - Another exception replaces the record.
  - Concurrent calls write once, and no exception gets out.
  - A file system that throws lets no exception out, logs one line and leaves no temporary file.
  - The log's flush is called with its bound.
- **`FileLog`.** The bounded flush writes the queued lines, and returns within its bound when the writer is stuck.
- **The store and the pairing.**
  - Every row of section 8's table.
  - The 10-second window, on both sides.
  - A host record of SIGSEGV, and a test record, which do not pair.
  - Dismissal deletes both files, each only while it is unchanged.
- **`DiagnosticsViewModel`.** The summary and the rows of every state.

**UI tests,** headless, in `tests/Paneport.UI.Tests`. The sweep's probes become tests, with the dispatcher's filter attached to a recording writer:

- **What the filter sees.** Each of these writes exactly one record, with the entry `dispatcher`, the right type and the throw site's frame:
  - a job posted with `Post`;
  - an `async void` method that throws after an `await`;
  - an `AsyncRelayCommand`;
  - a `MeasureOverride` that throws in a queued layout pass;
  - a `Render` that throws on the UI thread;
  - a tap whose `RelayCommand` throws.

  The exception still escapes `RunJobs` or the input call each time, so the filter changes nothing.
- **What it does not see.**
  - A layout pass that the top level's `Resized` callback runs, and a direct `UpdateLayout`, write no record. This pins why the native boundary is needed.
  - A custom visual whose `OnRender` throws on the render thread writes no record, and one line through `AvaloniaLogForwarder`.
  - An `InvokeAsync` callback and a `DispatcherTimer` tick write no record; their tasks fault.
- **Once.** The same exception, passed first by the filter and then as the native boundary would pass it, gives one record.
- **The page.** The Diagnostics page shows each state on a phone-sized window.

**Native tests** run with CTest in `tests/native`, with the `asan-ubsan` and `tsan` presets in the `native` job:

- A SIGABRT on an unmarked thread leaves the abort's message, through the last-resort handler and through the default action.
- With a stand-in runtime handler that returns without chaining, the first record stays.
- A SIGSEGV keeps its messages.

**The `ios` job** builds both flavors, with the native boundary's entry point and the probe page.

**The device check,** by recelilious, runs section 10's probes, one per launch, and reads Diagnostics after each.

## 12. What the workstation cannot verify

The workstation has no iOS packs, no Avalonia.iOS package and no device. The sweep's probes ran headless on CoreCLR. These points therefore stay unverified until the device check:

1. That .NET for iOS raises `MarshalManagedException` with the original exception object, and on which thread, for an exception that leaves:
   - a UIKit override;
   - a block;
   - Avalonia.iOS's run loop callbacks or input callbacks;
   - a layout pass that UIKit starts.
2. That Mono for iOS raises `AppDomain.UnhandledException` on a worker thread before the abort, and does not raise it for the UI thread.
3. That the app then ends in `abort()`, and that the SIGABRT reaches the last-resort handler through Mono's handler (signals.md section 5).
4. That `DiagnosticMethodInfo` gives the frames' names under Mono's full ahead-of-time build with trimming.
5. That Avalonia.iOS catches no exception itself. If it caught one, the app would go on after the dispatcher's record; the replacing rule of section 4 and the wording of section 8 cover that case.
6. How much time lies between the two records, which section 8's window assumes.

CI verifies the rest:

- the `dotnet` job, the unit and UI tests;
- the `native` job, the native cases;
- the `ios` job, the builds.

## 13. Notes for later stages

- **L2.** When the app's end cuts a signal gate run short, the gate's evidence record copies what Diagnostics shows at the next launch. That tells a native crash from an unhandled managed exception.
- **The stage that exports the log** exports the record with it. The record holds nothing that the scrubbing of C-0004 section 4 removes.
- **M10.1.** CoreCLR has no `UnwindNativeCode` mode, and its unhandled-exception path does not run through Mono's SIGABRT handler, so the entry points and the host's record are checked again on CoreCLR.

## 14. Sources

Public material consulted (C-0001 section 6):

- Microsoft's guide "Exception Marshaling on iOS, tvOS, Mac Catalyst and macOS": <https://learn.microsoft.com/en-us/dotnet/ios/advanced-concepts/exception-marshaling>. It gives the default mode for managed exceptions, `ThrowObjectiveCException`; the events, and the `ExceptionMode` that a handler may change; and the build property `MarshalManagedExceptionMode`.
- The XML documentation in Avalonia 12.1.4's `Avalonia.Base` package: `Dispatcher.UnhandledException`, `Dispatcher.UnhandledExceptionFilter` and `DispatcherUnhandledExceptionFilterEventArgs.RequestCatch`.
- The XML documentation in .NET 10's reference pack, `Microsoft.NETCore.App.Ref` 10.0.14: `System.Diagnostics.DiagnosticMethodInfo` and `System.Runtime.ExceptionServices.ExceptionHandling.SetUnhandledExceptionHandler`.
- In this repository: [signals.md](signals.md) sections 3, 5 and 10; [native-host.md](native-host.md) section 8; [ios-app-head.md](ios-app-head.md) sections 3, 6, 8 and 9; and the L1 brief's session log of 2026-11-24, which records the sweep.
