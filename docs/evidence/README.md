# Evidence

This folder holds the acceptance evidence records (C-0012 section 4). Every Large stage of the [development plan](../design/development-plan.md) ends with a record of its manual acceptance run, and every milestone gate of C-0012 section 3 is decided by one.

## Naming

Each record is one Markdown file named `YYYY-MM-DD-<topic>.md`, with the UTC date of the run (C-0012 section 4), for example `2026-10-30-l0-foundation.md`. A stage's integration session commits its record as a template (C-0012 section 2). If the run happens on another date, the file is renamed to the date of the run.

## Contents

A record holds the fields of C-0012 section 4: the gate or feature and the build; the device and its iOS version and build; the signing route, the sideloading tool and the entitlement values; the JIT enabler, Game Mode and the memory limit; the steps with their expected and observed results, the metrics and scrubbed logs; and the verdict, the tester as a project handle (`recelilious` or `Claude`) and the date. A field that does not apply says so, and the stage's brief names what the record holds in its place.

A record and its attachments never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). In CI, `tools/ScanPersonalData.cs` checks this folder for them, and lychee checks its links (C-0011 section 2).

## Gates

The current verdict of each gate of C-0012 section 3, with its latest record. A gate ends as Pass, Fail or Waived. Only recelilious can waive a gate, through a new record that states the reason.

| Phase | Gate | Verdict | Latest record |
|---|---|---|---|
| 0 | Transparent overlay | No record yet | |
| 0 | Signal dispatcher | No record yet | |
| 0 | SteamKit2 on the device | No record yet | |
| 1 | One triangle | No record yet | |
| 2 | A 2D game | No record yet | |
| 3 | Steam download | No record yet | |
| 4 | Alpha with Dock | No record yet | |
| Before 2027-05-11 | Runtime migration | No record yet | |

## Records

| Record | Stage | Gate | Verdict |
|---|---|---|---|
| [2026-10-30-l0-foundation.md](2026-10-30-l0-foundation.md) | L0 Foundation | None | Template; the acceptance run is pending |
