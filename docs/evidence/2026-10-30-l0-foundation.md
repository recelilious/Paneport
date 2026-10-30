# L0 Foundation Acceptance Record

| Template by | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-30T02:20Z |

Back to the [evidence index](README.md).

This record is a template until the acceptance run of the [L0 brief](../design/stages/L0-foundation.md). recelilious runs the steps and fills in every field marked _to fill in_, or reports the results so that Claude fills them in. Steps 1 to 10 run on the Windows 11 workstation, in a terminal at the root of a clone with the L0 integration branch checked out. Steps 11 and 12 run on GitHub. If the run happens on another date than the one in the file name, rename the file to the UTC date of the run (C-0012 section 4).

Nothing in this record may name a person, a machine, an account or a local path (C-0012 section 4). On Windows the likely leaks are local paths and the user or machine name in the `dotnet` output and in screenshots. Copy only the output lines that the Logs section names, and if you add a screenshot, show only the Paneport window. If the SDK prints in another language, set `DOTNET_CLI_UI_LANGUAGE` to `en` first, so that the copied lines are in English.

## Build and environment

| Field (C-0012 section 4) | Value |
|---|---|
| Gate or feature | L0 Foundation. No C-0012 gate. |
| Build: commit | _to fill in: the commit hash that was tested, the last commit of the L0 pull request_ |
| Build: version | None yet. Versions start at 0.1.0 (C-0009 section 6). |
| Device model identifier | Not applicable: L0 runs only on Windows. |
| iOS version and build | Not applicable. Recorded instead: the Windows version and build, _to fill in_, and the SDK version that step 1 printed, _to fill in_. |
| Signing route | Not applicable. |
| Sideloading tool and its version | Not applicable. |
| Entitlement values (C-0009 section 4) | Not applicable. |
| JIT enabler and its version | Not applicable. |
| Game Mode | Not applicable. |
| Memory limit read during the run | Not applicable. The values on the Diagnostics page came from the desktop head's fake host, not from a device. |

## Steps

| Step | Expected | Observed |
|---|---|---|
| 1. Run `dotnet --version`. | It prints exactly the SDK version in `global.json`. | _to fill in_ |
| 2. Run `dotnet build eng/ci.slnf -c Release`. | The build succeeds with 0 warnings and 0 errors. | _to fill in_ |
| 3. Run `dotnet test eng/ci.slnf -c Release`. | Every test passes, and the summary shows no failed test. | _to fill in_ |
| 4. Open any three `.cs` files under `src/` and `tests/`. | Each starts with the two SPDX lines of C-0010 section 6. | _to fill in_ |
| 5. Run `dotnet run --project src/Paneport.Desktop`. | Within a few seconds a window titled Paneport opens on the Library page, with navigation to Library, Settings and Diagnostics. | _to fill in_ |
| 6. Look at the Library page. | It lists the sample titles of `docs/design/app-shell.md` section 6, each with its install state and launch path. The sample with the very long name stays readable. | _to fill in_ |
| 7. Select a sample title, then go back. | Title details shows the same name, identity, install state and launch path. Going back returns to the Library page. | _to fill in_ |
| 8. Open Settings, change one setting, open another page and come back. | The changed value is still shown. | _to fill in_ |
| 9. Open Diagnostics. | It says that the host is a fake. It shows the fake host's values of `docs/design/app-shell.md` section 6: host ABI version 1.0, a memory limit of 6,144 MB, 5,632 MB of available memory, a current footprint of 512 MB, and the Game Mode level not reached. | _to fill in_ |
| 10. Close the window. | The app closes without an error dialog, and the terminal shows no exception. Nothing in the window suggested an affiliation with Microsoft, Valve or Apple (C-0001 section 7 item 4). | _to fill in_ |
| 11. Open the L0 pull request on GitHub. | On its last commit, the `dotnet` job passed on Windows and on Ubuntu, and the `checks` job passed. | _to fill in_ |
| 12. Optional. In GitHub's web editor, add one line with an e-mail address at the `example.com` domain to `tests/README.md` and propose the change as a new pull request. Afterwards, close that pull request without merging and delete its branch. | `checks` fails, and its personal-data step names the file and the line. `dotnet` passes. | _to fill in, or "not run"_ |

## Metrics

None. L0 measures nothing on a device.

## Logs

Only these lines, without the lines around them, which name local paths:

- Step 2, the end of the build output, the lines `Build succeeded.`, `0 Warning(s)` and `0 Error(s)`: _to fill in_
- Step 3, the end of the test summary, the lines `total`, `failed`, `succeeded` and `skipped`: _to fill in_
- Step 11, the CI run of the pull request's last commit: _to fill in: link_
- Step 12, the CI run of the scratch pull request: _to fill in: link, or "not run"_

## Verdict

| Field | Value |
|---|---|
| Verdict | _to fill in: Pass or Fail_ |
| Tester | `recelilious` |
| Date (UTC) | _to fill in_ |
