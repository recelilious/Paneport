# Managed Exception Record

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-26T14:45Z, revised 2026-11-26T20:55Z after review round 1 |

Back to the [design index](README.md).

This document describes the managed exception record of stage M2.4 (see the [L2 brief](stages/L2-phase-0-gates.md)). It covers:

- which entry points see a .NET exception that goes unhandled, and why each one is needed;
- how one exception is recorded once;
- the record's file, and how it survives the abort that follows;
- what the record holds;
- what the host's crash record says for that abort;
- how the next launch reads both records and shows them;
- the tests, and what the workstation cannot verify.

It was written in the stage's first session, before any code, and revised after the first round of its review. The changes it needs in C-0005 section 2 and C-0010 section 2 are proposed, as C-0005 1.1 and C-0010 1.3, and are not yet accepted. Until recelilious answers the seven questions of the drafts' review, these parts are proposals and not settled:

- section 5, the record's own file rather than a host function (question 1);
- the rules of section 6 on what the record may hold, which C-0010's new rule fixes for the record only (question 2);
- the rule of section 4 that nothing goes on after an unhandled exception (question 3);
- section 7, the host's changes for a SIGABRT (question 4);
- section 9, the layout on Diagnostics, for recelilious to choose from (question 5);
- section 10, the page of exception probes (question 6);
- section 3's entry for `TaskScheduler.UnobservedTaskException`, a log line only (question 7).

[signals.md](signals.md) describes the signal dispatcher and the last-resort handler, [native-host.md](native-host.md) the host's crash record, and [ios-app-head.md](ios-app-head.md) the startup and Diagnostics. Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Scope

An unhandled .NET exception ends the app, and today neither flavor records it. Paneport's only trace is the host's last-resort record, "A fault that the runtime did not handle.", with no exception type and no frame. On Diagnostics a managed exception therefore looks like a native fault, while the phase 0 signal gate asks for no native crashes (C-0012 section 3). The coordinating session's sweep of `main` at `24908f0`, in the review of [#42](https://github.com/recelilious/Paneport/pull/42), found the gap and gave this design its input; M1.3's session log of 2026-11-24 in the [L1 brief](stages/L1-app-on-iphone.md) records the review, and section 2 records what the sweep found on exceptions. Where the sweep's finder and its skeptic differ, this design follows the skeptic.

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

**The term.** In this design, as in the C-0010 1.3 draft, an *unhandled exception* is one that leaves a dispatcher operation, a callback into native code, or a thread, with nothing catching it. A faulted task is not one, and neither is a timer tick whose exception the dispatcher keeps in the tick's task: their exceptions stay in their tasks, and the app goes on.

The table follows an exception that the app's own code does not catch. In the first four rows it is unhandled; in the last three it is not:

| Where the exception is thrown | Where it goes | How the app ends |
|---|---|---|
| Posted work on the UI thread: `Dispatcher.Post` jobs; posts of `AvaloniaSynchronizationContext`, which carry `async void` event handlers and the rethrows of async commands; and the render job of `MediaContext`, which runs queued layout passes and rendering on the UI thread | `DispatcherOperation.InvokeCore` catches it, raises the dispatcher's filter from its catch block after the throwing frames have unwound, and rethrows it. It then escapes `RunJobs` and the platform callback that called it | At the native boundary |
| Input: `PresentationSource.HandleInput` runs it through `Dispatcher.Send` | The filter runs before the stack unwinds; the exception escapes `Send` and the platform's input callback | At the native boundary |
| Layout that runs outside any dispatcher operation, such as the platform's resize pass (`TopLevel.HandleResized`, which `AvaloniaView.LayoutSubviews` starts) or a test's direct `UpdateLayout`; and the head's UIKit overrides (`ViewDidAppear`, `ViewSafeAreaInsetsDidChange`, `SceneDelegate.WillConnect`) | No dispatcher event; it goes straight to the UIKit callback. A direct layout pass inside a posted job or inside input belongs to the first two rows instead | At the native boundary |
| A managed thread other than the UI thread, such as the signal gate's threads or a thread-pool work item | The runtime's unhandled-exception path, which raises `AppDomain.UnhandledException` with `IsTerminating` true | Mono's unhandled path calls `abort()` (reasoned) |
| Avalonia's render thread | Avalonia catches and logs it ("Exception in render loop", "Exception when rendering"), and `AvaloniaLogForwarder` writes it to the log | It does not end |
| `InvokeAsync` callbacks and `DispatcherTimer` ticks | The operation's task faults and keeps the exception, and a tick that throws stops its timer | It does not end |
| A task that nothing awaits | The task keeps the exception, and `TaskScheduler.UnobservedTaskException` sees it after a garbage collection | It does not end |

**At the native boundary,** .NET for iOS marshals the managed exception. The build sets no `MarshalManagedExceptionMode`, so the mode is the default, `ThrowObjectiveCException`:

1. The runtime raises `ObjCRuntime.Runtime.MarshalManagedException` with the exception object.
2. It wraps the exception in an Objective-C exception that carries a handle to it, and throws that into the native frames.
3. What catches it depends on the path (reasoned):
   - From a run-loop callback or a UIKit override on the main thread, it unwinds to the catch that .NET for iOS puts around `UIApplicationMain`. That catch takes the managed exception back from the handle and throws it again. It escapes `Main`, and Mono's unhandled path raises `AppDomain.UnhandledException` on the main thread, then ends the app in `abort()`.
   - From a callout of libdispatch, or on a thread with no catch, nothing catches it. The uncaught-exception handler of .NET for iOS takes the managed exception back from the handle, raises `AppDomain.UnhandledException` with it, and calls `abort()`.

So on the main thread, `AppDomain.UnhandledException` fires after `MarshalManagedException`, with the same object, and the record-once rule skips it (section 4).

**The round trip of an Objective-C exception** (reasoned). A UIKit call that managed code makes can throw an Objective-C exception, N. .NET for iOS turns it into a managed `ObjCRuntime.ObjCException`, E1. When E1 goes unhandled, the native boundary sees E1, takes N out of it and throws N again, now without a handle to a managed exception. Then:

- on the run-loop and UIKit paths, the catch around `UIApplicationMain` finds no managed exception in N and makes a new `ObjCException`, E3, which reaches `AppDomain.UnhandledException`;
- on a callout or a thread with no catch, the uncaught-exception handler throws N into managed code as a new `ObjCException`, E2, which reaches `MarshalManagedException` a second time.

Neither E2 nor E3 is E1 or holds it in its inner exceptions, but all three carry N. Section 4 therefore keys them by N.

**The abort** raises SIGABRT on that thread:

1. The dispatcher sends the signal to the runtime's handler, because it was sent on an unmarked thread.
2. Mono's SIGABRT handler finds the program counter in native code and chains to the last-resort handler.
3. The last-resort handler writes "A fault that the runtime did not handle." (signals.md section 5).

The dispatcher writes no record before the runtime's handler for a SIGABRT. So if Mono's handler did not chain, no record would remain.

**What the sweep found.** On exceptions, the sweep found the following. The later sections and the L2 brief's notes rely on it.

- Neither flavor has a managed hook for an unhandled exception. The record of a managed abort is the host's last-resort record: SIGABRT, code 0 or `SI_USER`, address 0, and "A fault that the runtime did not handle." (reasoned).
- `Dispatcher.UnhandledExceptionFilter` runs before the stack unwinds only for `Dispatcher.Send`, which carries input. For posted work, `DispatcherOperation.InvokeCore` raises it from its catch block, after the throwing frames have unwound. Either way, the exception's own stack trace keeps the throw site's methods.
- A layout pass outside any dispatcher operation never raises the dispatcher's events.
- An input exception reaches the filter during `Dispatcher.Send`, and then the native boundary, so the same exception must be recorded once.
- `FileLog.Write` only queues its line, so the record must be written synchronously.
- Avalonia catches the exceptions of its render thread and logs them; the sweep's skeptic probed this. `InvokeAsync` callbacks and `DispatcherTimer` ticks fault their tasks. The exception of every dispatcher operation reaches `TaskScheduler.UnobservedTaskException` after a collection.
- After one handled measure exception, queued layout stops. With the single top level of the iOS shell, the whole screen freezes. After one handled render exception, nothing renders again in any top level.
- An `AppDomain.UnhandledException` handler is only notified, with `IsTerminating` true, and cannot keep the process alive.

**What was shown, and what was reasoned.** The sweep probed the rows of the UI thread, the render thread, the timers and the tasks headless, on Windows with CoreCLR and Avalonia 12.1.4. The review of this design probed them again, with the same results, and probed `AppDomain.UnhandledException` on a worker thread. The native boundary, the main thread's routes, the round trip and the abort are reasoned. They come from Microsoft's documentation and from the review of this design, whose reviewers traced them in the sources of .NET for iOS and Mono at the pinned versions (section 14). Section 12 lists what stays to be shown.

## 3. Entry points

| Entry point | Project | What it sees | Why it is needed | What it leaves as it is |
|---|---|---|---|---|
| `Dispatcher.UIThread.UnhandledExceptionFilter` | Paneport.UI | Posted work and input (the first two rows of section 2) | It sees most exceptions of the UI thread at the earliest point, before they cross into native code. Unlike the native boundary, it can be tested headless on Windows and Linux | `RequestCatch`, so the dispatcher never catches the exception and never raises `UnhandledException`, to which nothing subscribes. Nothing sets `Handled` |
| `ObjCRuntime.Runtime.MarshalManagedException` | Paneport.iOS | Every managed exception that leaves managed code into native code, among them the first three rows of section 2 when they get there | The third row reaches no dispatcher event. Without this entry point, a layout pass that UIKit starts or a UIKit override would leave no record | The event's `ExceptionMode`. The build keeps the default mode, so C-0009 section 3 does not change |
| `AppDomain.CurrentDomain.UnhandledException` | Paneport.Core | An exception that ends a managed thread other than the main thread (the fourth row). On the main thread, an exception after the native boundary, by both routes of section 2: the same object, which the record-once rule skips, or after a round trip, E3 | Neither of the others sees a thread without a dispatcher and without a native boundary | Everything: the event only notifies, and the process ends whatever its handler does |
| `TaskScheduler.UnobservedTaskException` | Paneport.Core | A faulted task that nothing observed (the last row), and later the faulted tasks of the sixth row. These are not unhandled exceptions (section 2) | Such a fault leaves no trace today | The task, which it does not mark as observed. It writes no record, because the app goes on, but one log line with the types of the task's inner exceptions, at most 4, and never their messages. The event's own exception is always an `AggregateException`, so its type alone would say nothing |

**Not used.**

- **Avalonia's `Dispatcher.UnhandledException`.** To raise it, the dispatcher must catch the exception, and the only reason to catch it would be to mark it handled. After a handled layout or render exception, Avalonia 12.1.4 leaves the screen frozen: queued layout stops, and after a render exception nothing renders again (section 2).
- **`AppDomain.FirstChanceException`.** It sees every thrown exception, also those that are caught at once, such as the thousands of null references a second (64 between pauses of a millisecond) of the signal gate.
- **.NET 10's `ExceptionHandling.SetUnhandledExceptionHandler`.** Its handler returns a Boolean, so it can do more than observe, and the reference documentation says only that it sets a handler for unhandled exceptions. The events above observe without it. Whether Mono for iOS calls it is not known here.
- **`Runtime.MarshalObjectiveCException`.** An Objective-C exception that enters managed code becomes a managed exception there. If it goes unhandled, the entry points above see it.

**What each handler may do.** Each handler runs where the exception went unhandled, often on a thread that is about to end:

| Entry point | Where it runs |
|---|---|
| The dispatcher's filter | On the UI thread. For input, it runs before the throwing frames' `finally` blocks |
| `MarshalManagedException` | On the thread that crossed into native code, inside Mono's handling of the exception |
| `AppDomain.UnhandledException` | On the ending thread. On Mono, a handler that throws is itself fatal |
| `UnobservedTaskException` | On the finalizer thread |

So every handler keeps to three rules:

- **It takes no lock.** The throwing code may hold one. The gate of section 4 is an interlocked flag, and its only wait is bounded.
- **It never waits for work on the UI thread,** such as a dispatcher call, because that thread may be the one that is ending.
- **It never throws** (section 4).

The handlers may allocate and do I/O. Avalonia's documentation asks a filter's handler, which can run before the stack unwinds, to avoid secondary exceptions and catch any that occur, and to avoid allocating memory or doing work that needs many resources. The recorder catches every failure of its own (section 4). It does allocate and write a file, and that is acceptable here. Nothing asks the dispatcher to catch an exception that reaches the filter, so the exception escapes, and in practice it ends the app. The cost is therefore paid once, on the way out.

**Installation.** `CompositionRoot.Compose` installs the entry points on the main thread:

1. After step 4 of [ios-app-head.md](ios-app-head.md) section 3, which reads the previous run's records, it creates the recorder for the crash record folder, with the iOS head's key function (section 4). It then installs the entry points of `AppDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and `MarshalManagedException`.
2. After step 5, which starts Avalonia's platform, it installs the dispatcher's filter.

An exception before that ends in the composition's own catch, which shows its type in place of the shell. The entry points stay installed for the life of the process. The desktop head installs none, because it has no crash record folder. The UI tests install the dispatcher's filter over a fake writer.

## 4. Recording once

Every entry point calls one recorder, `ManagedExceptionRecorder` in Paneport.Core, with three things: the exception, the entry point, and whether the thread is the main thread. The process has one recorder, and it records unhandled exceptions in the sense of section 2.

- **The key.** Paneport.Core and Paneport.UI cannot see `ObjCRuntime`. So the iOS head gives the recorder a key function when it creates it (section 3), and every entry point goes through the recorder, and so through that function. For an `ObjCRuntime.ObjCException`, the key is the native handle of its `NSException`. Any other exception has no key, and the recorder compares the objects themselves. The desktop head gives no function, and the tests give a fake one.
- **The same exception.** The recorder keeps the exception it recorded last, with its key. A call writes nothing when its exception, or one in its chain of inner exceptions, is that exception or has that key. This happens when:
  - an input exception reaches the filter during `Dispatcher.Send`, and then the native boundary (section 2);
  - an exception reaches the native boundary and then, on the main thread, `AppDomain.UnhandledException`, as the same object;
  - an exception at the boundary wraps one that the filter recorded;
  - an `ObjCException` makes the round trip of section 2: E2 and E3 carry E1's `NSException`, so they have E1's key.
- **Another exception.** A call with another exception writes a new record over the old one. Because a later exception replaces an earlier record, a record that native code made moot by catching its exception (section 12) never hides the exception that ends the app. The host makes the same trade-off for its own record (signals.md section 3). What it loses is the first cause in a cascade. When the unwinding of one exception throws another, such as one from a `finally` block, or a second thread fails after the first one's write, the record shows the later exception. The log keeps one line for each exception that was recorded (section 5), so the first cause still has its type in the log.
- **The gate.** An interlocked gate decides which call writes. A call that finds the gate held by another thread waits until that write has renamed its file or failed, at most 2 seconds, as the host's writer does (signals.md section 3). It then returns without writing. In a crash on two threads at once, the first exception's record is therefore on storage before the second thread goes on to the abort. A call never waits when its own thread holds the gate: it returns at once.
- **It never throws.** Every step of the recorder catches its own failures. A failed write is logged with the failure's type, and the call returns. The exception then does not count as recorded, so a later entry point may try again.
- **It changes nothing else.** It never sets `RequestCatch` or `Handled`, never changes `ExceptionMode`, and never calls `SetObserved`. It returns once the record is on storage, or once the gate's bound has passed. The runtime then goes on as it would without it. Nothing continues after an unhandled exception (C-0010 section 2, as proposed).

## 5. The record's file, and how it survives the abort

**The choice: a file of its own, written by managed code.** The record is `managed-exception.json` in the crash record folder, `Library/Caches/Paneport/` (C-0004 section 4), next to the host's `crash-record.bin`. The alternative was a host function that the entry points call, with a flag that would make the host's crash record carry the exception. A file of its own is the better choice, for four reasons:

1. **It does not depend on the signal chain.** It is on storage before the abort starts. The chain that writes the host's record, Mono's SIGABRT handler chaining to the last-resort handler, is reasoned, not shown on the device (signals.md section 5). Section 7's record-first rule frees the host's record from that chain as well, so this reason weighs less than the others.
2. **The host's write cannot replace it.** The host renames its own file, `crash-record.bin`, which has another name.
3. **The host's interface and record stay as they are.** Section 7 changes only how `signal_handler.c` handles a SIGABRT.
   - The interface gains no function and keeps its version (C-0005 section 1).
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

**The cost** has not been measured. The write, its flush to storage and the rename are expected to take milliseconds, and the log's flush takes at most 1 second. The abort waits for them. Section 12 lists the measurement.

**Survival.** The record is on storage before the entry point that wrote it returns, so before its exception goes on to the abort. A call that lost the gate waits for that write, up to 2 seconds (section 4). These cases leave no record:

- out of memory, where building the record fails, and the recorder catches the failure and returns;
- a kill by the system during the write, such as the watchdog's, which no handler sees;
- a write that is still going on when a waiting call's 2-second bound passes;
- a failure of the storage itself.

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
| `exceptions[].type` | The exception's type, by its full name, such as `System.NullReferenceException`. A generic type is named by the full name of its generic type definition, such as ``Example.GenericProbeException`1`` for a made-up type, so the name holds no type arguments and no assembly names, as `DiagnosticMethodInfo` names a frame's type |
| `exceptions[].frames` | Up to 32 frames, from the throw site outward. Each is named by its type's full name and its method's name, joined by a dot, such as `Paneport.Core.ViewModels.SettingsViewModel.OnShown`. A frame without a name is `?` |
| `exceptions[].framesLeftOut` | How many frames beyond the 32 were left out |

**Where the names come from.** The recorder reads the exception's own frames through `new StackTrace(exception, false)`, without file information. For each frame it calls `DiagnosticMethodInfo.Create(frame)`, which .NET 9 added for logging. It gives the declaring type's full name and the method's name without reflection over the method, which the trimming and AOT analyzers accept (C-0010 section 2). The exception's own frames keep the throw site's methods even when an entry point runs after the stack has unwound (section 2).

**Limits.**

- Each name holds at most 200 characters, and is cut at a character boundary.
- A file holds at most 4 exceptions of 32 frames each.
- A file holds at most 64 KiB. The default encoder of `System.Text.Json` writes characters such as `<`, `>`, `+` and `` ` `` as `\uXXXX`, six bytes each, so names that are dense in them could pass that size within the other limits; realistic names stay far below it. The writer therefore measures the serialized record. While it is over 64 KiB, the writer drops the outermost frame of the exception that has the most frames, and counts it in that exception's `framesLeftOut`. Types and four exceptions without frames always fit.
- A reader rejects a file over 64 KiB.

**Never in the record:**

- the exception's message, `Data`, `HResult`, `Source` or `HelpLink`;
- the source-file information of debug symbols: file names and line numbers; and IL or native offsets;
- parameter lists and argument values;
- assembly names and paths;
- thread names.

Type and method names are identifiers of code, never data that the user gave. So the record holds no personal data and no path on the device (C-0010 section 2, C-0012 section 4). One kind of name looks like a path but is not one. Avalonia's XAML compiler names the loader methods of a XAML file without `x:Class` after the file's resource name, such as `Populate:` or `Build:` followed by the resource's path inside the assembly. So a frame of an exception thrown while such a file loads holds that resource name. It is part of the code, the same in every build, and not a path on the device.

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

1. **A message of its own.** A SIGABRT gets the message "An abort: abort() was called, or SIGABRT was sent.". The message is chosen by signal, in all three places that write a record for a SIGABRT:
   - in the record that item 2 writes first, where it replaces "A fault that ended in the runtime's crash path.";
   - in the last-resort handler, where it replaces "A fault that the runtime did not handle.";
   - when the saved action is the default action, where it replaces "A fault outside the guest path that nothing handled.".

   The change needs no new call, so the signal-safe calls check stays as it is (signals.md section 10).
2. **The record goes first.** A SIGABRT joins the signals whose record the dispatcher writes before the runtime's handler gets them (signals.md section 5). A record written first never describes a crash that did not happen:
   - nothing in Paneport sends SIGABRT;
   - `abort()`, which raises it, never returns: if a handler returns, it puts the default action back and raises the signal again.

   The record therefore stays, with the abort's message, if Mono's handler does not chain. If Mono chains, the last-resort handler writes the same message again.

**The other source of SIGABRT records.** Since M4.3, single mode ends Wine's fatal errors with `abort()` as well. It first takes the thread's guest mark away, so that the dispatcher ends the app with a crash record ([single-process-wine.md](single-process-wine.md) section 6). Today that record depends on Mono's chain, like the record of a managed abort. The record-first rule gives it a record without the chain.

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

The pairing can be wrong in two ways:

- **A step of the clock** inside the gap between the two writes, such as a correction from the network's time, can lose a pairing or make a wrong one. That needs a step of seconds at the moment of a crash, which is rare.
- **Another abort.** A host record of SIGABRT also comes from M4.3's fatal path, which ends Wine's fatal errors with `abort()` (section 7). Suppose native code made a managed record moot by catching its exception (section 12), and such an abort follows within 10 seconds. The two records would then pair by mistake, and the summary would name the managed exception as the cause. The log can tell the two apart: single mode hands Wine's fatal error to the host's log before it aborts, though the abort may come before the line is written ([single-process-wine.md](single-process-wine.md) section 4).

**What Diagnostics says.**

| Records found | Summary |
|---|---|
| A managed record and a host record that pairs with it | The previous run ended after an unhandled managed exception. |
| A managed record alone | The previous run recorded an unhandled managed exception, but no crash record of the host follows it. |
| A managed record with a host record that does not pair with it | The previous run recorded an unhandled managed exception. The host's crash record on this page is not of an abort that followed it. |
| A host record of SIGABRT alone | The previous run ended with an abort. |
| A host record of another signal | The previous run ended with a fatal fault. (as today) |
| A host test record | A test record from the previous run. (as today) |
| A malformed file | As today, for each file |
| None | No crash record from a previous run. (as today) |

When the two records do not pair, both are shown, each with its own summary, the newer first. The host's record then keeps its summary from the table.

**Dismissing.** One Dismiss button dismisses both records, in every layout of section 9. Each file is deleted only while it still holds the bytes that the launch read ([ios-app-head.md](ios-app-head.md) section 9).

## 9. Diagnostics: the proposed layout

How the page shows the record is recelilious's choice. This section proposes three layouts; once recelilious has chosen, it keeps only the chosen one. Every layout has one Dismiss for both records, and shows the newer record first, as section 8 says.

**Layout A, recommended: one section.** The section keeps its title, "Crash record". Below the summary of section 8, a managed record shows these rows:

| Label | Value, with made-up examples |
|---|---|
| Time | `2026-11-26 14:19:37 UTC` |
| Exception | `System.NullReferenceException` |
| Inner exceptions | `None`, or the types in order, such as `System.InvalidOperationException, then System.IO.IOException` |
| Where | One of five texts, by the entry point and the thread: `Avalonia's dispatcher, on the main thread`; `A call from iOS into the app, on the main thread`; `A call from iOS into the app, on another thread`; `The main thread, through .NET's unhandled-exception event`; `A managed thread other than the main thread` |
| Thrown in | The first frame, such as `Paneport.DeviceTest.DeveloperViews.Menu` |
| Frames | The first 8 frames, one per line, then `and 23 more`, with a button `Show all frames`. When the record left frames out (`framesLeftOut`), a last line says `and 5 more frames, which the record left out` |
| Build | `53c36ae, 0.1.0 (12)` |

`Show all frames` shows every frame of the record: the exception's frames, then those of each inner exception under its type, since the throw site of a wrapper such as `TargetInvocationException` or `AggregateException` lies in its inner exception. The other rows show only the outer exception's frames.

When the records pair, the line "The host's record of the abort that followed:" comes next, then the host's rows as today: Time, Signal, Thread, Fault address, Program counter, Test record and Message. When they do not pair, the newer record comes first, with its summary and rows, then the line "An older record from the previous run:", then the other record with its own summary and rows. One button, `Dismiss`, ends the section.

**Layout B: two sections.**

- "Crash record" keeps the host's record as today.
- A new section, "Managed exception", holds the rows of layout A.

The section of the newer record comes first. One Dismiss, below both sections, dismisses both records. The managed section's summary says whether the host's record in the other section belongs to it.

**Layout C: as A, folded.** As layout A, but when the records pair, the host's rows stay hidden behind a button `Show the host's record`.

## 10. The developer page of exception probes

The test flavor's developer menu gains a page, "Exception probes". Each button but the last ends the app, and the next launch's Diagnostics shows the record:

| Button | What throws | Entry point expected |
|---|---|---|
| Throw in a posted job | A job posted with `Dispatcher.UIThread.Post` | `dispatcher` |
| Throw in a layout pass | A control whose `MeasureOverride` throws, in a queued layout pass | `dispatcher` |
| Throw in this tap | The button's own `RelayCommand`, which the page binds directly. It does not run through `DeveloperToolViewModel.RunAsync`, whose catch-all would keep the app alive and show the message | `dispatcher`, then the native boundary, recorded once |
| Throw in a UIKit callback | A one-shot `NSTimer` whose block throws | `native-boundary` |
| Throw on the next rotation | Arms a control whose `MeasureOverride` throws in the next layout pass that a resize runs; the tester then turns the phone | `native-boundary` |
| Throw on a managed thread | A new thread | `thread` |
| Fault a task that nothing awaits | `Task.Run`, then a garbage collection | A log line with the inner exception's type only; the app goes on |

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
  - a name of 300 characters is cut at 200, at a character boundary, also with a surrogate pair at the cut;
  - a record at the limits, 4 exceptions of 32 frames whose names are 200 characters that the encoder escapes, is cut to at most 64 KiB by dropping frames into `framesLeftOut`, and reads back.

  An exception of a generic type is recorded by its generic definition's full name, with no type arguments and no assembly names. The file holds none of the made-up path, token and e-mail address of the exception's message, and no file name or line number.
- **The reader.** Every malformed case of section 8.
- **The recorder.**
  - The same exception from two entry points is written once.
  - Two different exception objects to which the key function gives one key are written once; so is an outer exception whose inner exception has that key.
  - An outer exception whose inner exception was recorded is not written.
  - Another exception replaces the record.
  - A fake file system holds the first write. A second call from another thread returns only after the rename, or after the 2-second bound when the hold outlasts it, and writes nothing. A call on the thread that holds the gate returns at once. No exception gets out.
  - A file system that throws lets no exception out, logs one line and leaves no temporary file.
  - The log's flush is called with its bound.
- **The thread and task entry points,** on CoreCLR in the `dotnet` job:
  - In a child process, a worker thread that throws leaves one record, with the entry `thread` and the thread `other`, and the process ends.
  - After a collection, a task that faulted and that nothing observed leaves one log line with the inner exceptions' types, no message, and no record.
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
  - an `async void` event handler of a control that throws after an `await`, since C-0010 section 2 allows `async void` only for event handlers;
  - an `AsyncRelayCommand`;
  - a `MeasureOverride` that throws in a queued layout pass;
  - a `Render` that throws on the UI thread;
  - a tap whose `RelayCommand` throws.

  The exception still escapes `RunJobs` or the input call each time, so the filter changes nothing.
- **What it does not see.**
  - A layout pass that the top level's `Resized` callback runs, and a direct `UpdateLayout`, both outside any dispatcher operation, write no record. This pins why the native boundary is needed. A direct `UpdateLayout` inside a posted job writes one, through the job.
  - A custom visual whose `OnRender` throws on the render thread writes no record, and one line through `AvaloniaLogForwarder`.
  - An `InvokeAsync` callback and a `DispatcherTimer` tick write no record; their tasks fault.
- **Once.** The same exception, passed first by the filter and then as the native boundary would pass it, gives one record.
- **The page.** The Diagnostics page shows each state on a phone-sized window.
- **The rules in the source.** A source test, like `ViewTemplatesTests`, reads the C# files under `src/` and `tests/device/`. It fails on any assignment to `ExceptionMode` or to `RequestCatch`, any call to `SetObserved`, and any subscription to `Dispatcher.UnhandledException`. It does not forbid assignments to `Handled`, which routed events set legitimately.

**Native tests** run with CTest in `tests/native`, with the `asan-ubsan` and `tsan` presets in the `native` job:

- A SIGABRT on an unmarked thread leaves the abort's message, through the last-resort handler and through the default action.
- With a stand-in runtime handler that returns without chaining, the first record stays, and it carries the abort's message.
- A SIGSEGV keeps its messages.

**The `runtime` job** runs the harness, whose single mode links the host since M4.3, so every single-mode case runs the dispatcher's SIGABRT path. Every case passes, and the planted cases fail with their names.

**The `ios` job** builds both flavors, with the native boundary's entry point and the probe page.

**The device check,** by recelilious, runs section 10's probes, one per launch, and reads Diagnostics after each.

## 12. What the workstation cannot verify

The workstation has no iOS packs, no Avalonia.iOS package and no device. The sweep's probes ran headless on CoreCLR. These points therefore stay unverified until the device check:

1. That .NET for iOS raises `MarshalManagedException` with the original exception object, and on which thread, for an exception that leaves:
   - a UIKit override;
   - a block;
   - Avalonia.iOS's run loop callbacks or input callbacks;
   - a layout pass that UIKit starts.
2. That Mono for iOS raises `AppDomain.UnhandledException` on a worker thread before the abort, and on the main thread after `MarshalManagedException`, with the same object, by both routes of section 2.
3. That the app then ends in `abort()`, and that the SIGABRT reaches the last-resort handler through Mono's handler (signals.md section 5).
4. That `DiagnosticMethodInfo` gives the frames' names under Mono's full ahead-of-time build with trimming.
5. That Avalonia.iOS catches no exception itself. If it caught one, the app would go on after the dispatcher's record; the replacing rule of section 4 and the wording of section 8 cover that case.
6. How much time lies between the two records, which section 8's window assumes.
7. The round trip of an Objective-C exception (section 2): that E2 and E3 carry the same `NSException` as E1, so that the key keeps E1's record.
8. What the write costs on the device: the record, its flush to storage and the rename, and the log's flush (section 5).
9. When the last probe's log line comes. Mono scans stacks conservatively, so one collection may not finalize the faulted task, and the line may come late, or not before the app is closed.

CI verifies the rest:

- the `dotnet` job, the unit, UI and source tests;
- the `native` job, the native cases;
- the `runtime` job, the harness's cases;
- the `ios` job, the builds.

## 13. Notes for later stages

- **L2.** When the app's end cuts a signal gate run short, the tester relaunches the app and copies what Diagnostics shows into the gate's evidence record before tapping Dismiss. When the managed record was written, that tells a native crash from an unhandled managed exception. An abort without one reads "ended with an abort", and a kill by the system leaves no record at all.
- **The stage that exports the log** exports the record with it. The record holds nothing that the scrubbing of C-0004 section 4 removes.
- **M10.1.** CoreCLR has no `UnwindNativeCode` mode, and its unhandled-exception path does not run through Mono's SIGABRT handler, so the entry points and the host's record are checked again on CoreCLR.

## 14. Sources

Public material consulted (C-0001 section 6):

- Microsoft's guide "Exception Marshaling on iOS, tvOS, Mac Catalyst and macOS": <https://learn.microsoft.com/en-us/dotnet/ios/advanced-concepts/exception-marshaling>. It gives the default mode for managed exceptions, `ThrowObjectiveCException`; the events, and the `ExceptionMode` that a handler may change; and the build property `MarshalManagedExceptionMode`.
- The XML documentation in Avalonia 12.1.4's `Avalonia.Base` package: `Dispatcher.UnhandledException`, `Dispatcher.UnhandledExceptionFilter`, with its advice on what a handler may do, and `DispatcherUnhandledExceptionFilterEventArgs.RequestCatch`.
- The XML documentation in .NET 10's reference pack, `Microsoft.NETCore.App.Ref` 10.0.14: `System.Diagnostics.DiagnosticMethodInfo`, and `System.Runtime.ExceptionServices.ExceptionHandling.SetUnhandledExceptionHandler`, which has only a summary.
- The review of this design, round 1, recorded on [#52](https://github.com/recelilious/Paneport/pull/52). It ran headless probes on CoreCLR: of the dispatcher's paths, of `AppDomain.UnhandledException` on a worker thread, of the names that `Type.FullName` and `DiagnosticMethodInfo` give, and of the JSON encoder's escapes. It also traced, in the sources at the pinned versions, the native boundary, the main thread's routes and the round trip of an Objective-C exception through .NET for iOS and Mono, and the names that Avalonia's XAML compiler gives its loader methods. This document takes those paths from the review and did not read the sources itself, so section 12 keeps them unverified.
- In this repository: [signals.md](signals.md) sections 3, 5 and 10; [native-host.md](native-host.md) section 8; [ios-app-head.md](ios-app-head.md) sections 3, 6, 8 and 9; [single-process-wine.md](single-process-wine.md) sections 4 and 6; and M1.3's session log of 2026-11-24 in the [L1 brief](stages/L1-app-on-iphone.md), which records the review of #42 and its sweep.
