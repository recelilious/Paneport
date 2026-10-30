# L0 Foundation Acceptance Record

| Part | Written by | Timestamp (UTC) |
|---|---|---|
| Template | Claude (Anthropic), for the Paneport project | 2026-10-30T02:20Z |
| Results | Claude (Anthropic), from recelilious's report and Claude's check of step 11 | 2026-10-30T08:15Z |

Back to the [evidence index](README.md).

This record holds the acceptance run of the [L0 brief](../design/stages/L0-foundation.md) on 2026-10-30 (UTC). recelilious ran steps 1 to 10 on the Windows 11 workstation, in a terminal at the root of a checkout of the L0 integration branch, and reported every result as expected. Claude checked step 11 on GitHub at recelilious's request. recelilious skipped the optional step 12.

The record holds nothing that C-0012 section 4 forbids, as the [evidence index](README.md) lists it. The testers appear as project handles, and the logs are only the lines that the Logs section names.

## Build and environment

| Field (C-0012 section 4) | Value |
|---|---|
| Gate or feature | L0 Foundation. No C-0012 gate. |
| Build: commit | `6693b253f0d5225676ba70c4c8baefb45c75cf7e`, the last commit of the L0 pull request [#9](https://github.com/recelilious/Paneport/pull/9) during the run. |
| Build: version | None yet. Versions start at 0.1.0 (C-0009 section 6). |
| Device model identifier | Not applicable: L0 runs only on Windows. |
| iOS version and build | Not applicable. Recorded instead: Windows 11 Pro, version 25H2, OS build 26200.8655, and the SDK version that step 1 printed, 10.0.403. |
| Signing route | Not applicable. |
| Sideloading tool and its version | Not applicable. |
| Entitlement values (C-0009 section 4) | Not applicable. |
| JIT enabler and its version | Not applicable. |
| Game Mode | Not applicable. |
| Memory limit read during the run | Not applicable. The values on the Diagnostics page came from the desktop head's fake host, not from a device. |

## Steps

recelilious ran steps 1 to 10, and Claude checked step 11.

| Step | Expected | Observed |
|---|---|---|
| 1. Run `dotnet --version`. | It prints exactly the SDK version in `global.json`. | As expected: `10.0.403`. |
| 2. Run `dotnet build eng/ci.slnf -c Release`. | The build succeeds with 0 warnings and 0 errors. In a terminal its last line reads `Build succeeded in` and the time; the SDK adds a count to that line only when there are warnings or errors. | As expected. |
| 3. Run `dotnet test eng/ci.slnf -c Release`. | Every test passes, and the summary shows no failed test. | As expected. |
| 4. Open any three `.cs` files under `src/` and `tests/`. | Each starts with the two SPDX lines of C-0010 section 6. | As expected. |
| 5. Run `dotnet run --project src/Paneport.Desktop`. | Within a few seconds a window titled Paneport opens on the Library page, with navigation to Library, Settings and Diagnostics. | As expected. |
| 6. Look at the Library page. | It lists the sample titles of `docs/design/app-shell.md` section 6, each with its install state and launch path. The sample with the very long name stays readable. | As expected. |
| 7. Select a sample title, then go back. | Title details shows the same name, identity, install state and launch path. Going back returns to the Library page. | As expected. |
| 8. Open Settings, change one setting, open another page and come back. | The changed value is still shown. | As expected. |
| 9. Open Diagnostics. | It says that the host is a fake. It shows the fake host's values of `docs/design/app-shell.md` section 6: host ABI version 1.0, a memory limit of 6,144 MB, 5,632 MB of available memory, a current footprint of 512 MB, and the Game Mode level not reached. | As expected. |
| 10. Close the window. | The app closes without an error dialog, and the terminal shows no exception. Nothing in the window suggested an affiliation with Microsoft, Valve or Apple (C-0001 section 7 item 4). | As expected. |
| 11. Open the L0 pull request on GitHub. | On its last commit, the `dotnet` job passed on Windows and on Ubuntu, and the `checks` job passed. | As expected. On 6693b25, the push run and the pull request run of CI each passed `dotnet (windows-2025)`, `dotnet (ubuntu-24.04)` and `checks`. |
| 12. Optional. In GitHub's web editor, add one line with an e-mail address at the `example.com` domain to `tests/README.md` and propose the change as a new pull request. Afterwards, close that pull request without merging and delete its branch. | `checks` fails, and its personal-data step names the file and the line. `dotnet` passes. | Not run. recelilious skipped this optional step. |

## Metrics

None. L0 measures nothing on a device.

## Logs

Only these lines, without the lines around them, which can name local paths. recelilious reported steps 2 and 3 without copying their output. The lines for those steps therefore come from Claude's run of the same two commands on the same workstation and commit, at 2026-10-30T08:12Z, after `dotnet clean eng/ci.slnf -c Release`, so that every project compiled again. Its output went to a file, so the build ends with the three classic lines instead of `Build succeeded in`.

- Step 2, the end of the build output:

  ```text
  Build succeeded.
      0 Warning(s)
      0 Error(s)
  ```

- Step 3, the end of the test output:

  ```text
  Test run summary: Passed!
    total: 143
    failed: 0
    succeeded: 143
    skipped: 0
  ```

- Step 11, the CI runs of the pull request's last commit, 6693b25: the [push run](https://github.com/recelilious/Paneport/actions/runs/40995344477) and the [pull request run](https://github.com/recelilious/Paneport/actions/runs/40995350501). Their `dotnet` jobs also build and test that commit on Windows and on Ubuntu.
- Step 12: not run.

## Verdict

| Field | Value |
|---|---|
| Verdict | Pass |
| Tester | `recelilious` (steps 1 to 10) and `Claude` (step 11) |
| Date (UTC) | 2026-10-30 |
