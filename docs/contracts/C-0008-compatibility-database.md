# C-0008 Compatibility Database

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

This contract defines where per-title settings and test results live, their format, and the rules for changing them.

## 1. Location, format and layers

- The reviewed database lives in `compat/` (C-0004 section 3).
- Each title is one JSON file in `compat/titles/`:
  - Steam titles are named `steam-<appid>.json`.
  - Titles not obtained through Steam are named `local-<slug>.json`, where `<slug>` uses only lowercase letters, digits and single hyphens and is unique in the folder.
- Every `*.json` title file is validated against a JSON Schema (draft 2020-12) in `compat/schema/title.schema.json`. The schema implements section 2 and never contradicts it. The folder's `README.md` is not a title file.
- The app bundles a snapshot of `compat/` at build time.
- On the device the app keeps its own layer, one JSON file per title in `Library/Application Support/Paneport/compat/` (C-0004 section 4). It holds the classification computed under C-0007 section 5 and the user's own overrides. It never leaves the device.
- **Precedence.** The effective settings are the schema defaults, then the bundled entry, then the device layer, with two exceptions: a `drm` value set in the bundled entry always wins over a computed one, and the device layer can never turn `launch` into `direct` for a title whose `drm` requires the Dock path (section 5).
- A title with no entry runs with the schema defaults and shows as `untested`.

## 2. Fields

All sizes are in MiB (1,048,576 bytes).

| Field | Type | Meaning |
|---|---|---|
| `schemaVersion` | integer | Currently 1. |
| `id` | object | `steamAppId` (integer, Steam titles), `exeSha256` (array of SHA-256 hashes of the file named by `executable.path`, one per known build, for `local-` titles), and `slug` (string, always). |
| `name` | string | The title's display name. |
| `executable` | object | `path` and optional `workingDirectory`, both relative to the install folder with forward slashes, and `arguments` (array of strings). Used on the Direct path only. |
| `architecture` | string | `x86_64` or `x86`. |
| `drm` | object | `steamApi` (boolean), `steamStub` (boolean), `steamAppIdFile` (boolean) and `ceg` (`yes`, `no` or `unknown`). A classification only (C-0007 section 5). Absent means not yet classified. |
| `launch` | string | `direct` or `dock`, derived from `drm` (section 5). |
| `graphicsApi` | array | Any of `gdi`, `ddraw`, `d3d8`, `d3d9`, `d3d10`, `d3d11`, `d3d12`, `opengl`, `vulkan`. |
| `runtimes` | array | Runtime identifiers from a fixed list in the schema, for example `vcredist-v14-x64` for the Microsoft Visual C++ v14 redistributable. |
| `memoryEstimateMiB` | integer | Expected peak memory added by the game: the process footprint during play minus the footprint just before the game's first process started. |
| `jitPoolMiB` | integer | Optional JIT pool size, 256 to 896, still limited by C-0006 section 4. |
| `translator` | object | Translator options. Keys must come from an allow-list in the schema. |
| `graphics` | object | `renderScale` (greater than 0, at most 1: the internal resolution as a fraction of the output), `metalFx` (`off` or `spatial`), `frameRateCap` (an integer that divides the display's refresh rate). |
| `environment` | object | Environment variables for the game. Keys must come from an allow-list in the schema. |
| `helperPolicy` | object | `blockedExecutables`: helper programs the session refuses to start, such as crash reporters. |
| `prefix` | string | `shared` (default) or `isolated`. |
| `rating` | string | Computed from the reports, see section 3. |
| `reports` | array | Test reports, see section 4. |

A controller or touch layout field is added together with the design document for on-screen controls.

## 3. Ratings

| Rating | Meaning |
|---|---|
| `perfect` | Completes the main story at its target frame rate with no known issues. |
| `playable` | Can be played through, with minor issues. |
| `runs` | Starts and gets in-game, but has serious issues. |
| `boots` | Starts but does not reach gameplay. |
| `broken` | Does not start. |
| `untested` | No report counts yet. |

`rating` is computed, not edited by hand:

1. Only reports whose `paneportVersion` is a release tag and whose `device` is a target device of C-0001 section 2 count. The schema lists the model identifiers of those devices as `targetDevices`.
2. Among those, the reports for the highest release count.
3. If target devices disagree on that release, the lower rating wins; otherwise the latest report wins.

The CI check rejects a file whose `rating` differs from this result (C-0011).

## 4. Test reports

| Field | Required | Meaning |
|---|---|---|
| `date` | Yes | Test date, `YYYY-MM-DD`, in UTC. |
| `device` | Yes | The device model identifier, for example `iPhone18,1`. |
| `iosVersion` | Yes | iOS version and build. |
| `paneportVersion` | Yes | Release tag or commit hash. |
| `gameBuild` | Yes | The Steam build ID, or for `local-` titles the SHA-256 of the program file. |
| `rating` | Yes | The rating this test observed. |
| `evidence` | Yes | Path of the evidence record under `docs/evidence/` (C-0012 section 4). |
| `fpsMedian`, `fpsLow1` | When rating is `runs` or better | Median and 1% low frame rates. |
| `peakMemoryMiB` | When rating is `runs` or better | Peak memory footprint of the whole process. |
| `thermalState` | No | The highest thermal state seen. |
| `notes` | No | Free text. |

## 5. Rules

- **Launch follows the classification.** `launch` is `direct` only when `steamApi` and `steamStub` are false and `ceg` is `no`; otherwise it is `dock`. A `local-` title is launched only when `steamApi` and `steamStub` are false and `ceg` is `no`; otherwise the app refuses to launch it. The CI check rejects any other combination, including a `local-` file whose `drm` shows a Steam marker.
- **No field may skip, satisfy or replace a Steam or DRM check** (C-0007 section 5). The environment allow-list never contains `SteamAppId`, `SteamGameId`, `SteamAppPath` or any other variable read by `steam_api` or a DRM wrapper. `arguments` never contain a switch that skips a launcher or an ownership check. `blockedExecutables` never lists a program that performs a license or DRM check.
- **Our own data only.** Every setting and report comes from Paneport's own tests. Nothing is copied, translated or imported from another project's compatibility list or per-game settings, such as those of Madeira and its forks, Winlator, GameNative or dxmt.report (C-0001 section 6). Such lists may only be read to learn what is feasible.
- **No game material.** No game files, assets, program files or other copyrighted material from a game.
- **No personal data.** No Apple ID, Steam account name or Steam ID, device serial number, UDID, device name, real name or local path.
- **Reviewed changes.** Every change comes through a reviewed branch (C-0002), and every report names its evidence record (C-0012).
- **Schema changes.** Adding an optional field or an allow-list entry changes the schema only, within the rules above. Adding a required field, or removing or renaming a field or value, raises `schemaVersion` and needs a change to this contract.
- **CI.** CI validates every `*.json` file in `compat/titles/` against the schema, checks that each file name matches its `id`, and checks the launch, allow-list and rating rules (C-0011). The other rules in this section are checked in review.

## 6. License of the data

The data is released under the repository's MIT license, as recelilious decided on 2026-10-28. JSON files cannot carry a license header, so `REUSE.toml` at the repository root declares their license (C-0010 section 6).

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious |
