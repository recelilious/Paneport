# Evidence

This folder holds the acceptance evidence records (C-0012 section 4). Every Large stage of the [development plan](../design/development-plan.md) ends with a record of its manual acceptance run, and every milestone gate of C-0012 section 3 is decided by one.

## Naming

Each record is one Markdown file named `YYYY-MM-DD-<topic>.md`, with the UTC date of the run (C-0012 section 4), for example `2026-10-30-l0-foundation.md`. A stage's integration session commits its record as a template (C-0012 section 2). If the run happens on another date, the file is renamed to the date of the run, and its link in the Records table below changes in the same commit; lychee fails the `checks` job on a broken link.

## Contents

A record holds the fields of C-0012 section 4:

- the gate or feature, and the build: its commit hash and version;
- the device model identifier, and the iOS version and build;
- the signing route (free or paid team), the sideloading tool and its version, and the values of the entitlements that C-0009 section 4 lists (`get-task-allow`, `increased-memory-limit` and `extended-virtual-addressing`) in the installed app;
- the JIT enabler and its version, whether Game Mode was active, and the memory limit read during the run;
- the steps with their expected and observed results, the metrics, and scrubbed logs;
- the verdict, the tester as a project handle (`recelilious` or `Claude`, never a real name), and the date.

A field that does not apply says so, and the stage's brief names what the record holds in its place.

A record and its attachments never contain personal data or secrets: no UDID, ECID, serial number, device name, Apple ID, team identifier, application-identifier prefix, re-signed bundle identifier, Steam account name, Steam ID, IP address, access or refresh token, depot key, manifest request code, real name or local path (C-0012 section 4). In CI, `tools/ScanPersonalData.cs` checks the text of this folder for the items it can recognize by their form, such as UDIDs, e-mail addresses, Steam IDs, IP addresses, tokens and local paths, and lychee checks its links (C-0011 section 2). The scanner cannot recognize a device name, a real name, a Steam account name or an Apple ID that is not an e-mail address, and it does not read images such as screenshots; it only lists them. The writer and the reviewer check those by eye ([repository-checks.md](../design/repository-checks.md) section 3).

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

Every record has a row here. The commit that fills in a record gives its row the verdict, and for a gate also updates that gate's row above.

| Record | Stage | Gate | Verdict |
|---|---|---|---|
| [2026-10-30-l0-foundation.md](2026-10-30-l0-foundation.md) | L0 Foundation | None | Pass |
