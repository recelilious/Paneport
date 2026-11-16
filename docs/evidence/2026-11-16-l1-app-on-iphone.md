# L1 Paneport on the iPhone Acceptance Record

| Template by | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-11-16T19:43Z |

Back to the [evidence index](README.md).

This record is a template until the acceptance run of the [L1 brief](../design/stages/L1-app-on-iphone.md). recelilious runs the steps on the iPhone and fills in every field marked _to fill in_, or reports the results so that Claude fills them in. Steps 1 to 10 use the main app, steps 11 to 16 the device test flavor, and step 17 is this record. Both `.ipa` files come from the `ios` job of the L1 pull request's last CI run, and they are installed by the route of [dev-environment.md](../design/dev-environment.md) section 6 (S1.0.2). Text in quotation marks is what Paneport shows, word for word.

The commit that fills in this record also updates these places:

- If the run happened on another UTC date than the one in the file name, the file is renamed to that date (C-0012 section 4), and its link in the Records table of the [evidence index](README.md) changes with it. A broken link there fails lychee in the `checks` job.
- The record's row in that Records table gets the verdict in place of "Template; the acceptance run is pending".
- Section 6 of [dev-environment.md](../design/dev-environment.md) records the install path that worked and the versions of the tools, which completes S1.0.2.
- With a Pass that recelilious accepts, L1's row in the status table of the [L1 brief](../design/stages/L1-app-on-iphone.md) becomes Accepted, and L1's session log records the run (plan section 3.3). With a Fail, L1 stays In progress or becomes Blocked, the session log says which step failed, and the fix goes to the stage that owns the part.

The record never holds what C-0012 section 4 forbids, as the [evidence index](README.md) lists it. On this run the likely leaks are what AltStore, AltServer, iTunes, iCloud and Settings show: the Apple ID, the developer's name under VPN & Device Management, the device's name, its UDID or serial number, the re-signed bundle identifier, and the download path of an `.ipa`. Copy values only from Paneport's own pages, which show none of these (M1.3), and never attach a screenshot of another app or of Settings. A screenshot, if one helps, shows only a Paneport page. The tester appears as a project handle.

## Build and environment

| Field (C-0012 section 4) | Value |
|---|---|
| Feature | L1 Paneport on the iPhone. No C-0012 gate: the phase 1 gate at L5 checks the framework import on a device (C-0012 section 3), and this record states the import that L1's build uses. |
| Build: commit | _to fill in: Commit in the Build section of Diagnostics, the same for both flavors (steps 4 and 12); it is the L1 pull request's last commit_ |
| Build: version | _to fill in: Version in the Build section of Diagnostics for each flavor, "0.1.0 (N), main app" and "0.1.0 (N), device test flavor", with the same N_ |
| Build: CI run | _to fill in: a link to the CI run whose artifacts were installed_ |
| Device model identifier | _to fill in: Model identifier in the Device section of Diagnostics, such as `iPhone18,1`_ |
| iOS version and build | _to fill in: iOS version and iOS build in the Device section of Diagnostics_ |
| Signing route | A free Apple account, which signs as a Personal Team (D2, plan section 4.2). The sideloading tool re-signs the ad-hoc-signed build with its own development certificate and profile; CI holds no Apple identity (C-0009 section 5). |
| Sideloading tool and its version | _to fill in: AltStore and its version on the iPhone and AltServer for Windows and its version, with the path that installed each flavor (AltServer's Sideload .ipa, or AltStore's My Apps); or Xcode or `devicectl` and the command_ |
| Entitlement values (C-0009 section 4) | _to fill in from step 7, each as Diagnostics shows it, with its sources:_ get-task-allow: _to fill in_; increased-memory-limit: _to fill in_; extended-virtual-addressing: _to fill in_ |
| Warnings of the sideloading tool | _to fill in: any entitlement warning that the tool showed in steps 1, 11 and 16, without account or device details, or "None"_ |
| JIT enabler and its version | None. JIT comes with L3 (C-0006). |
| Game Mode | _to fill in from step 6: whether the Game Mode level was reached, and after how long (Game Mode level first reached), or "Not reached in 60 seconds"_ |
| Memory limit read during the run | _to fill in from steps 5 and 6: the first and the highest limit (First limit and Highest limit)_ |
| Host import (C-0009 section 4) | _to fill in from steps 3 and 4: Import in the Host section and Host import in the Build section_ |

## Steps

| Step | Expected | Observed |
|---|---|---|
| 1. Install the main app's `.ipa` with AltStore, or with Xcode or `devicectl` on a Mac, by the route of [dev-environment.md](../design/dev-environment.md) section 6. | The tool reports success and Paneport is on the home screen. Note any entitlement warning the tool shows, without account or device details. | _to fill in_ |
| 2. Open Paneport from the home screen. If iOS asks you to trust the developer first, do so in Settings. | Within a few seconds the shell fills the screen on an empty Library page, with navigation to Library, Settings and Diagnostics. The app does not close. If a message that starts with "Paneport cannot start" or "Paneport could not start" shows instead, record it word for word and stop. | _to fill in_ |
| 3. Open Diagnostics. | In the Host section, Host reads "Native host (paneport_host)". Host ABI version reads "1.1", and Compatibility reads "Compatible: the app's bindings use version 1.1" (C-0005 section 1). Import reads "Framework: @rpath/paneport_host.framework/paneport_host" (C-0009 section 4). Host events reads "None yet". | _to fill in_ |
| 4. Read the Build and Device sections. | In Build, Commit is the pull request's last commit hash, in full; Version reads "0.1.0 (N), main app", where N is the number of the CI run; Configuration reads "Release", Runtime "Mono", Compilation "Full ahead-of-time" and Interpreter "Off" (C-0009 section 3); Host import reads "Framework, the ios-device build". In Device, Model identifier shows a device model identifier such as `iPhone18,1`, and iOS version and iOS build show the system's version and build. No device name appears. | _to fill in_ |
| 5. Read the Memory section. | Memory limit reads about 6,144 MB, or about 8,192 MB when Game Mode has already engaged. Available memory plus Current footprint equals the Memory limit, or falls 1 MB short of it, because each value is rounded down to whole MB. No warning shows. A limit far below that means the install dropped the entitlement: below 5,632 MB the page shows a warning that names `increased-memory-limit`, and you record the value and stop. | _to fill in_ |
| 6. Keep Diagnostics in the foreground for 60 seconds. | Samples grows by one a second, and the list under "Timeline, newest first" gains a line every second with the limit, the footprint and the thermal state. Peak footprint shows the run's highest footprint, and Thermal state the current and the highest state. If Game Mode engages, the limit rises to about 8,192 MB a few seconds after the app came to the front, Game Mode level reads "Reached", and "Game Mode level first reached" gives the time since the first sample (C-0009 section 4, C-0005 section 2). L1 does not require the rise. Copy the values into the Metrics section below. | _to fill in_ |
| 7. Read the Entitlements section. | get-task-allow reads "true in the code signature; true in the profile"; increased-memory-limit reads "true in the code signature; true in the profile"; extended-virtual-addressing reads "absent" for both sources, so it is not granted (C-0009 section 4). Each value names its source. No team identifier, application-identifier prefix or bundle identifier appears. | _to fill in_ |
| 8. Read the Crash record section. | "No crash record from a previous run." | _to fill in_ |
| 9. Read the Storage section, then open the Files app at On My iPhone. | Every folder of C-0004 section 4 has a row, and none reads "Missing". The rows that end in `prefixes/`, `library/`, `downloads/` and `runtime/` read "Excluded from backup"; those that end in `compat/` and `settings/` read "Backed up"; `Library/Caches/Paneport/` reads "Excluded from backup by iOS; marked as excluded from backup"; and `Documents/Import/` reads "The user's choice; shown in the Files app; not marked". The Files app shows a Paneport folder with an Import folder. | _to fill in_ |
| 10. Send the app to the background for 30 seconds and back. Then close it in the app switcher and reopen it. | The shell returns each time. After the relaunch, Samples in Diagnostics counts again from the first seconds, and the Crash record section still reads "No crash record from a previous run." | _to fill in_ |
| 11. Install the test flavor's `.ipa` the same way. | It replaces the main app, since both share one bundle identifier (D12, plan section 7.3). There is one icon, named Paneport Test, and no extra app slot is used. | _to fill in_ |
| 12. Open it, then its Developer entry. | The navigation shows Library, Settings, Diagnostics and Developer, and the Developer page lists Interface conformance, Token store and Crash record. In Diagnostics, Build shows the commit and N of step 4, with Version "0.1.0 (N), device test flavor" and Host import "Framework, the ios-device-test build". | _to fill in_ |
| 13. Open Interface conformance and tap Run. | "PASS: every struct size and member offset matches (6 structs)." (C-0005 section 5, C-0011 section 1) | _to fill in_ |
| 14. Open Token store. Tap Write test item, then Read; then Delete, then Read. | "Written: 32 bytes.", then "match, 32 bytes; accessible after first unlock, this device only; not synchronizable." (C-0004 section 4); then "Deleted.", then "No item." | _to fill in_ |
| 15. Open Crash record and tap Write test record. Close the app in the app switcher, reopen it and open Diagnostics. Tap Dismiss and relaunch. | First "Written. Close the app in the app switcher, reopen it and open Diagnostics." After reopening, the Crash record section reads "A test record from the previous run.", with Time in UTC, Signal "none (test record), code 0", Thread "main thread", Fault address and Program counter 0x0000000000000000, Test record "Yes" and Message "Test record from the developer menu". After Dismiss and the relaunch, "No crash record from a previous run." | _to fill in_ |
| 16. Install the main app's `.ipa` again. | It replaces the test flavor, with one icon named Paneport. Diagnostics shows "No crash record from a previous run." and every storage folder, none "Missing". | _to fill in_ |
| 17. Fill in this record, or give your notes to the next L1 session. | Every field is present, nothing forbidden appears, and the `checks` job passes on it. | _to fill in_ |

## Metrics

From steps 3, 5 and 6, as the Host and Memory sections of Diagnostics show them (C-0011 section 1). Sizes are whole MB of 1,048,576 bytes.

- The memory limit over time: the twelve lines under "Timeline, newest first" at the end of step 6, each with its time since the first sample, the limit, the footprint, "Game Mode level" when the limit has reached it, and the thermal state.

  ```text
  to fill in
  ```

- The values of steps 5 and 6:

  | Row of the Memory section | Step 5 | End of step 6 |
  |---|---|---|
  | Memory limit | _to fill in_ | _to fill in_ |
  | Available memory | _to fill in_ | _to fill in_ |
  | Current footprint | _to fill in_ | _to fill in_ |
  | Game Mode level | _to fill in_ | _to fill in_ |
  | First limit | | _to fill in_ |
  | Highest limit | | _to fill in_ |
  | Game Mode level first reached | | _to fill in_ |
  | Peak footprint | | _to fill in_ |
  | Thermal state | | _to fill in_ |
  | Samples | | _to fill in_ |

- The import used: _to fill in from step 3_.

## Logs

Only for a step that failed: the text that Paneport showed, word for word, such as the message of step 2 or a line of a developer page that starts with "FAIL". Paneport's own log file stays on the device, because exporting it needs the scrubbing of C-0004 section 4, which comes later ([ios-app-head.md](../design/ios-app-head.md) section 9).

_to fill in, or "None: no step failed"_

## Verdict

| Field | Value |
|---|---|
| Verdict | _to fill in: Pass or Fail_ |
| Tester | `recelilious` |
| Date (UTC) | _to fill in_ |
