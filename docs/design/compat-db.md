# Compatibility Database

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-31T03:07Z |

Back to the [design index](README.md).

This document records the design of the compatibility database core: the title schema in `compat/schema/`, the typed model, the loader, the precedence rules and the device layer store in Paneport.Core, and the desktop head's folders. Stage M6.4 wrote it (see the [L6 brief](stages/L6-2d-game.md)). It implements [C-0008](../contracts/C-0008-compatibility-database.md) and joins the domain model of [app-shell.md](app-shell.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Components

| Part | Where | What it does |
|---|---|---|
| Title schema | `compat/schema/title.schema.json` | JSON Schema draft 2020-12 for the files in `compat/titles/` (C-0008 section 1). CI validates against it from M8.3 on. |
| Typed model | `src/Paneport.Core/Compat/` | `TitleEntry` and its parts (`TitleEntryId`, `ExecutableSpec`, `DrmClassification`, `GraphicsSettings`, `HelperPolicy`, `TestReport`) with enumerations for every list of values. `ThermalState` lives in `Hosting/`, because the host reports it too (C-0005 section 2). |
| Reader and writer | `TitleEntryJson` | Reads a file into a `TitleEntry` or the rules it breaks, checks an entry made in code, and writes one. Source-generated `System.Text.Json` code, with no reflection (C-0010 section 2). |
| Precedence | `CompatPrecedence`, `LaunchRule` | Merge the layers into `EffectiveTitleSettings` (section 4), and derive the launch decision from the classification (section 5). |
| Layers | `BundledSnapshot`, `DeviceLayerStore` | Read the bundled snapshot; read and write the device layer (section 7). |
| Database | `CompatDatabase` | Reads both layers of a title and merges them into a `CompatTitle`. |
| Desktop folders | `src/Paneport.Desktop/LocalFolders.cs` | The desktop head's stand-ins for the bundle and the container (section 7). |

The app needs no schema library. It checks in code the rules it relies on: types, ranges, allow-lists and the launch rule, and also every other rule of the schema that is simple to state in code, so that entries added before M8.3 are checked in the `dotnet` job.

## 2. The title schema

Every row of C-0008 section 2, and of section 4 for the reports, maps to a clause of the schema and to the check in code. "Required" means required in a reviewed file; the device layer's rules are in section 3.

| C-0008 field | Schema clause | Checked in code |
|---|---|---|
| `schemaVersion` | `properties.schemaVersion`: `const` 1; required | Missing, not 1, or newer (section 6) |
| `id` | `$defs/id`: required; `oneOf` `steamAppId` or `exeSha256`; `slug` required | The same; and the file name must match the id |
| `id.steamAppId` | integer from 1 to 4294967295 | The same range |
| `id.exeSha256` | array of `$defs/sha256` (64 lowercase hex digits), at least one, unique | The same |
| `id.slug` | `$defs/slug`: `^[a-z0-9]+(-[a-z0-9]+)*$` | `TitleId.IsValidSlug`, the same pattern (app-shell.md section 2) |
| `name` | string with a character other than white space; required | The same |
| `executable` | `$defs/executable`: `path` required; for a local title the whole object is required | The same |
| `executable.path` | `$defs/relativePath`: relative, forward slashes, no empty, `.` or `..` segment, none of `\ : * ? " < > \|` or U+0000 to U+001F | The same rule |
| `executable.workingDirectory` | `.` for the install folder, or `$defs/relativePath` | The same |
| `executable.arguments` | array of `$defs/text` (no U+0000); default `[]` | The same |
| `architecture` | `x86_64` or `x86` | The same names |
| `drm` | `$defs/drm`: four optional members, no default | The same; per-layer rules in section 3 |
| `drm.steamApi`, `drm.steamStub`, `drm.steamAppIdFile` | booleans | The same |
| `drm.ceg` | `yes`, `no` or `unknown` | The same names |
| `launch` | `direct` or `dock`, with the conditions under `allOf` (section 5) | The same |
| `graphicsApi` | unique items of `gdi`, `ddraw`, `d3d8`, `d3d9`, `d3d10`, `d3d11`, `d3d12`, `opengl`, `vulkan`; default `[]` | The same |
| `runtimes` | unique items of `$defs/runtime`, the fixed list `vcredist-v14-x64`, `vcredist-v14-x86`; default `[]` | `CompatLists.RuntimeIds` |
| `memoryEstimateMiB` | integer, at least 1 | The same |
| `jitPoolMiB` | integer from 256 to 896 | The same; the JIT flow still applies C-0006 section 4 |
| `translator` | object whose keys are the `properties` listed, none so far; default `{}` | `CompatLists.TranslatorKeys`; values are strings without U+0000 |
| `graphics.renderScale` | number greater than 0 and at most 1; default 1 | The same |
| `graphics.metalFx` | `off` or `spatial`; default `off` | The same names |
| `graphics.frameRateCap` | integer, at least 1 | The same. That it divides the display's refresh rate is for the session to check, which knows the display. |
| `environment` | object whose keys are the `properties` listed, none so far; default `{}` | `CompatLists.EnvironmentKeys`; values are strings without U+0000 |
| `helperPolicy.blockedExecutables` | unique items of `$defs/relativePath`; default `[]` | The same |
| `prefix` | `shared` or `isolated`; default `shared` | The same names |
| `rating` | `$defs/rating`, the six ratings of section 3; default `untested` | The same names. Computing it is M8.3's. |
| `reports` | array of `$defs/report`; default `[]` | The same |
| `reports[].date` | `YYYY-MM-DD` pattern, `format: date` | A calendar date |
| `reports[].device` | `^[A-Za-z]+[0-9]+,[0-9]+$`, a model identifier | The same, which keeps device names out |
| `reports[].iosVersion` | version and build, such as `27.2 (24B119)` | The same |
| `reports[].paneportVersion` | `v` and a Semantic Version (C-0009 section 6), or a 40-digit commit hash | The same |
| `reports[].gameBuild` | a Steam build ID for a Steam title, a `$defs/sha256` for a local title (`allOf`) | The same |
| `reports[].rating` | one of the five observed ratings, never `untested` | The same |
| `reports[].evidence` | `docs/evidence/YYYY-MM-DD-<topic>.md` (C-0012 section 4) | The same |
| `reports[].fpsMedian`, `fpsLow1` | numbers greater than 0, required for `runs` or better | The same |
| `reports[].peakMemoryMiB` | integer, at least 1, required for `runs` or better | The same |
| `reports[].thermalState` | `nominal`, `fair`, `serious` or `critical`, iOS's thermal states | The same names |
| `reports[].notes` | string | Nothing more |
| Any other field | `additionalProperties: false` on every object | Rejected (section 6) |

Sizes are in MiB, and the field names say so.

**The slug.** C-0008 section 1 allows "lowercase letters, digits and single hyphens". The schema reads this as app-shell.md section 2 does: one or more groups of lowercase ASCII letters and digits joined by single hyphens, with no leading, trailing or double hyphen. Every title has a slug, so the pattern applies to a Steam title's slug too.

**The allow-lists** start empty, as the brief asks, so `translator` and `environment` hold only `{}` for now. A stage that needs an entry adds it to the schema's `properties` and to `CompatLists` in one change (C-0008 section 5). The environment allow-list never holds `SteamAppId`, `SteamGameId`, `SteamAppPath` or any other variable that `steam_api` or a DRM wrapper reads; a test rejects any name that starts with `Steam` in any letter case, because Windows reads environment names that way. Translator values are strings, like those of the environment; each allowed option may narrow its value in the schema.

**The fixed list of runtimes** names the Microsoft Visual C++ v14 redistributable for both architectures, the example that C-0008 section 2 gives. Runtimes are not provisioned yet (plan section 7.5); a later stage adds the identifiers it needs.

**No `$id`.** The schema is used from the repository only, and M8.3's validator loads it by its path, so it names no URI of its own.

### 2.1 Target devices

`$defs/targetDevices` lists the model identifiers of the devices of C-0001 section 2, whose reports count for the rating (C-0008 section 3). A test reads this table and compares it with the schema.

| Device | Model identifiers |
|---|---|
| iPhone 17 Pro | `iPhone18,1` |
| iPhone 17 Pro Max | `iPhone18,2` |
| iPhone 18 Pro | `iPhone19,2` |
| iPhone 18 Pro Max | `iPhone19,3`, `iPhone19,7` |

The research named `iPhone18,1` and `iPhone18,2`. This stage confirmed all five identifiers on 2026-10-31 against the firmware listings of ipsw.me, and the iPhone 18 Pro's also against EveryMac and AppleDB (section 11). The iPhone 18 Pro Max has two: ipsw.me names `iPhone19,3` "iPhone 18 Pro Max (US)" and `iPhone19,7` "iPhone 18 Pro Max". No identifier is missing.

## 3. Layers

Both layers use the fields and types of the schema. Each has rules of its own, which `TitleEntryJson` applies by `CompatLayer`.

| Rule | Bundled entry | Device entry |
|---|---|---|
| Follows the schema | Yes, as a reviewed file | Its fields and types only; the schema itself is for `compat/titles/` |
| `name` | Required | Optional; Steam's app information or the import names the title |
| `executable` for a local title | Required, because `exeSha256` hashes the program it names | Optional; the bundled entry may name the program |
| `launch` | Allowed when its own `drm` decides it (section 5) | Never: the launch path follows from `drm` |
| `rating`, `reports` | Allowed | Never: only reviewed entries hold them |
| `drm.ceg` | `yes`, `no` or `unknown` | Only `unknown` |
| A local title's Steam marker | Rejected: `steamApi` or `steamStub` true, or `ceg` `yes` (C-0008 section 5) | Allowed: it is the computed classification, and the launch is refused (section 5) |

**Why the device layer's `ceg` is only `unknown`.** The device layer holds the classification computed under C-0007 section 5 and the user's overrides (C-0008 section 1). CEG cannot be detected by reading files and is set by hand in the reviewed entry (C-0007 section 5), so the classifier always records `unknown`, as M6.5's brief says. A device entry with `yes` or `no` would be a user's override of the classification. Whether the user may set `ceg` is a question that M6.5 puts to recelilious; until it is answered, the device layer cannot set it, which is the safe side. A "yes" answer changes the rule in `TitleEntryRules` and the merge in `CompatPrecedence`, and probably C-0008.

**Only the classifier writes `drm`.** A file cannot tell a computed value from an override, so the code that saves device entries keeps to this rule. The user's overrides are the other fields.

## 4. Precedence and merging

`CompatPrecedence.Merge` gives the effective settings: the schema defaults, then the bundled entry, then the device layer (C-0008 section 1). It takes entries that passed their layer's checks.

| Field | How the layers merge |
|---|---|
| Scalars: `name`, `architecture`, `memoryEstimateMiB`, `jitPoolMiB`, `prefix` | The device layer's value when it has one, else the bundled entry's, else the schema default or none |
| `executable` | Replaced as a whole, because the working directory and the arguments belong to the program they were set for. The effective arguments are never null. |
| `graphics` | Member by member, so that a user's render scale keeps the reviewed frame-rate cap |
| `helperPolicy` | Member by member; its one member is a list |
| `translator`, `environment` | Key by key: the device layer's keys replace the bundled entry's, and the others stay. A key cannot be removed, only given another allowed value. |
| Lists: `graphicsApi`, `runtimes`, `arguments`, `blockedExecutables` | Replaced as a whole. An empty list in the device layer replaces the bundled list; a list left out keeps it. |
| `drm` | Member by member, with the bundled entry first: exception 1 (below) |
| `launch` | Never merged. `LaunchRule` derives it from the merged `drm` (section 5). |
| `rating`, `reports` | The bundled entry's only, else `untested` and none |

The model keeps a list that a file leaves out apart from an empty one, so that this distinction survives reading and writing.

**Exception 1.** "A `drm` value set in the bundled entry always wins over a computed one." Each member counts as a value of its own. A reviewed entry can set `ceg` by hand while the classifier computes `steamApi`, `steamStub` and `steamAppIdFile`, which is how the entries of S6.6.5 and S7.0.3 work.

**Exception 2.** "The device layer can never turn `launch` into `direct` for a title whose `drm` requires the Dock path." The structure enforces it:

- the device layer never holds `launch`, and the merge never reads it;
- the bundled entry's `drm` members win over the device layer's;
- the device layer's `ceg` is only `unknown`, and the merge takes nothing else from it, even from an entry that skipped its checks.

The Direct path needs `ceg` `no`, which only a reviewed entry can give. A title whose reviewed classification blocks the Direct path keeps it blocked, whatever the device computes.

**A title with no entry** runs with the schema defaults and shows as `untested`; its launch decision is Unclassified until the classifier runs (C-0008 section 1).

## 5. The launch rule

C-0008 section 5: "`launch` is `direct` only when `steamApi` and `steamStub` are false and `ceg` is `no`; otherwise it is `dock`", and a local title is launched only under the same condition. `LaunchRule.Decide` applies it to the effective classification:

| Effective `drm` | Steam title | Local title |
|---|---|---|
| A member that blocks the Direct path: `steamApi` or `steamStub` true, or `ceg` `yes` or `unknown` | Dock | Refused |
| Otherwise, `steamApi` or `steamStub` missing | Unclassified | Unclassified |
| `steamApi` and `steamStub` false, `ceg` `no` | Direct | Direct |
| `steamApi` and `steamStub` false, `ceg` missing | Dock | Refused |

A missing `ceg` counts as unknown, because the device's classification can never set it. `steamAppIdFile` is recorded only and decides nothing. `LaunchDecision` has its own `Refused` value, because a local title can take no other path; `Title` and `LaunchPath` of the shell stay as M0.3 made them, and M6.5 shows a refusal as its UI needs.

**A stated `launch`.** A reviewed entry may state `launch`, but only when its own `drm` decides it whatever the device computes: `direct` needs `steamApi` and `steamStub` false and `ceg` `no` in the entry itself, and `dock` needs a member there that blocks the Direct path. An entry that sets only some members leaves `launch` out. The stated path is then always the path the app takes, so a reviewer can trust it. A local title never states `dock`.

**A local title's markers.** A reviewed local entry is rejected when its `drm` shows a Steam marker: `steamApi` or `steamStub` true, or `ceg` `yes`, CEG being Steam's own DRM. `steamAppIdFile` is not a marker for the rule, which names only the other three. On the device, such values are the classification of an imported folder, and the launch is refused.

## 6. Reading and writing

`TitleEntryJson.Read` takes a file's bytes and its layer, and the identity its name gives. A file that breaks a rule is rejected as a whole: none of its values are used.

| Step | Rejects |
|---|---|
| Size | More than 1 MiB (`Json`) |
| Encoding | Bytes that are not UTF-8 (`Json`); a UTF-8 byte-order mark is skipped |
| Syntax | Anything but one JSON object, comments and trailing commas (`Json`) |
| Strict scan | `null`, which no field takes (`Type`); a field that appears twice in one object (`DuplicateField`); a field the schema does not define, in any letter case (`UnknownField`); an entry of `translator` or `environment` that is not a string (`Type`) |
| Schema version | A newer one (`NewerSchemaVersion`), reported alone; missing, or not 1 (`MissingField`, `InvalidValue`) |
| Types | A value of the wrong type, including an integer with a fraction or an exponent (`Type`) |
| Values | Required fields, names on their lists in their exact letter case, ranges, patterns, duplicate items (`MissingField`, `InvalidValue`), allow-lists (`NotAllowed`) |
| Rules | The identity (`Identity`), the launch rule (`Launch`), the device layer's rules (`DeviceLayer`) |
| File name | An `id` that does not match the name (`Identity`) |

**Unknown fields** are rejected in both layers. In a reviewed file an unknown field is most often a misspelled one, which would otherwise be ignored without a word. The device layer is written by the app itself. Adding an optional field does not raise `schemaVersion` (C-0008 section 5), so a build can meet a device file that a newer build wrote, after recelilious installs an older build. That file is rejected: the title falls back to the defaults and the bundled entry, is classified again before its next launch, and the file stays until the next save replaces it. A build never carries values that it cannot check against its own rules.

**A newer `schemaVersion`** may add or rename fields, so the reader reports only the version, before any other check. The file is rejected and left in place, as above. The bundled snapshot comes from the same commit as the app, so only the device layer can meet one.

**An older `schemaVersion`** does not exist yet. The change that raises the version also brings the migration of device files and its tests.

**Problems** name the field as a JSON path, such as `$.reports[0].date`, the rule, and what the rule asks for, never the value: a rejected value may be a local path or other personal data, and problems reach logs (C-0010 section 2). A field name that is not a plain name shows as `<name not shown>`, as the personal-data scanner prints such names (repository-checks.md section 3).

**Names and integers.** The document classes keep the names of enumerations as strings and integers as 64-bit numbers, and the reader matches names ordinally and checks ranges itself, because the serializer's own enumeration handling ignores letter case and takes numbers.

**Writing.** `TitleEntryJson.Write` checks the entry against its layer's rules and throws `ArgumentException` naming the first problem. It writes the schema's field order, two-space indents and LF line endings on every platform (C-0002 section 7), leaves out the fields the entry does not set, writes map keys in ordinal order and ends with a line break. Reading a valid file, writing it and reading it again keeps every value.

## 7. Storage

**Bundled snapshot.** `BundledSnapshot` reads `compat/titles/` in the app bundle, where the build copies `compat/` (C-0009 section 4). It never writes. `LoadAllAsync` reads every file, such as for matching an imported program by its hash (M6.5).

**Device layer.** `DeviceLayerStore` keeps one file per title in `Library/Application Support/Paneport/compat/`, named `steam-<appid>.json` or `local-<slug>.json` like the repository's files (C-0008 section 1).

- **Atomic replace.** A save writes the file's bytes to `<name>.tmp` in the same folder, flushes them to the disk, and moves the temporary file over the old one. A crash leaves the old file or the new one. A save that fails or is cancelled before the move leaves the old file and deletes the temporary one.
- **One operation at a time.** The store runs its operations one by one, so two saves never share the temporary file and a load waits for a pending save. One store serves a folder.
- **Rejected files** stay until a save replaces them. Deleting a title deletes its file (M6.5).
- **Backup.** The folder is backed up (C-0004 section 4), so the store sets no exclusion.
- **Folder listing.** `LoadAllAsync` reads every `*.json` file in ordinal order of the names, and rejects a name that is not a title identity, matching the extension in any letter case so that Windows reports names that iOS, whose file system is case-sensitive, would not find. Other files, such as a leftover temporary file, are ignored.

**Heads.** Each app head creates the database over its own folders.

| Head | Bundled snapshot | Device layer |
|---|---|---|
| iOS (M6.5) | `compat/titles` in the app bundle, with the build step that copies `compat/` | `Library/Application Support/Paneport/compat/` in the container |
| Desktop | `compat/titles` next to the program: the project copies `compat/**/*.json` into its output | `Paneport.Desktop/compat` in the user's local application data |

`LocalFolders` in Paneport.Desktop names the desktop folders. No page reads the database yet, so neither head opens it; the stages whose pages first need it, M6.5 and M7.1, add it to the services that the heads provide (app-shell.md section 3).

## 8. Rules the code enforces, with their tests

Every rule of C-0008 sections 1 and 5 that the loader enforces, and the tests in `tests/Paneport.Core.Tests/Compat/` that cover it.

| Rule | Tests |
|---|---|
| Effective settings: defaults, then bundled, then device (section 1) | `CompatPrecedenceTests`: `TitleWithNoEntryRunsWithTheSchemaDefaultsAndShowsUntested`, `BundledEntryOverridesTheDefaults`, `DeviceLayerOverridesTheBundledEntry`; `CompatDatabaseTests.LoadsBothLayersAndMergesThem` |
| Exception 1: a bundled `drm` value wins over a computed one | `CompatPrecedenceTests.DrmValueOfTheBundledEntryWinsOverTheComputedOne`, `ReviewedCegAndComputedMembersTogetherGiveTheDirectPath` |
| Exception 2: the device layer never turns the launch path into Direct | `CompatPrecedenceTests.DeviceLayerCannotTurnTheLaunchPathIntoDirect`; the device fixtures `launch.json`, `ceg-no.json`, `ceg-yes.json` |
| A title with no entry runs with the defaults and shows as untested | `CompatPrecedenceTests.TitleWithNoEntryRunsWithTheSchemaDefaultsAndShowsUntested`, `CompatDatabaseTests.TitleWithNoEntryShowsAsUntested`, `TitleSchemaTests.DefaultsMatchWhatTheMergeGivesWithoutEntries` |
| `drm` has no default that allows a launch | `TitleSchemaTests.NothingThatAllowsALaunchHasADefault` |
| File names: `steam-<appid>.json`, `local-<slug>.json`, matching the id (section 1) | `DeviceLayerStoreTests`: `EachTitleHasOneFileNamedAfterItsId`, `FileWhoseIdIsAnotherTitlesIsRejected`, `LoadAllReadsTitleFilesAndRejectsOtherNames`; `TitleEntryJsonTests.FileOfAnotherTitleIsRejected` |
| The slug pattern (section 1) | The fixture `bad-slug.json`; `TitleIdTests` |
| Launch follows the classification, for Steam and local titles (section 5) | `LaunchRuleTests`; the fixtures `direct-without-ceg.json`, `dock-without-blocker.json`, `local-dock.json`, `launch-unclassified.json` |
| A local title shows no Steam marker (section 5) | The fixtures `local-steam-api.json`, `local-steam-stub.json`, `local-ceg-yes.json`; `LaunchRuleTests.ClassificationDecidesThePath` for the refusal |
| The environment allow-list holds no Steam variable (section 5) | `TitleSchemaTests.EnvironmentAllowListHoldsNoSteamVariable`, `ListsMatchTheCode`; the fixture `environment-steam-key.json` |
| Allow-list of translator options (section 2) | The fixture `translator-key.json`; `TitleSchemaTests.ListsMatchTheCode` |
| Every `*.json` file in `compat/titles/` is checked (sections 1 and 5) | `CompatDatabaseTests.EveryFileInCompatTitlesLoads` |
| The schema implements section 2 | `TitleSchemaTests`: `SchemaDefinesTheFieldsOfC0008AndNoOthers`, `SchemaNamesTheFieldsTheCodeWrites`, and one test per list of names |
| Each field's type, range and form (sections 2 and 4) | `TitleEntryJsonTests.InvalidFixtureIsRejectedForTheRuleItBreaks`, which lists every invalid fixture with the rule it breaks |

Not enforced in code, and checked in review or by M8.3, as C-0008 section 5 says: that `arguments` hold no switch that skips a launcher or an ownership check; that `blockedExecutables` lists no program that performs a license or DRM check; that the data is our own, with no game material and no personal data beyond what the scanner finds; that `rating` matches the reports (M8.3); and that an evidence record exists.

## 9. Where the schema and the code differ

- **Integers with a fraction.** JSON Schema counts `512.0` as an integer; the reader does not. Entries write integers without a fraction, and the test over `compat/titles/` catches one in CI.
- **Dates.** `format: date` asserts a calendar date only in validators that assert formats; the pattern checks the shape. The reader checks the calendar.
- **`$` in patterns.** The schema writes ECMA-262 patterns, where `$` matches only at the end. In .NET, `$` also matches before a final line break, so the code's patterns end in `\z`. M8.3 checks how its validator treats `$`.
- **Device files** follow the device layer's rules in code, not the schema, which requires `name`.

A local run of PowerShell 7.6's `Test-Json`, which validates draft 2020-12, accepted every valid bundled fixture and rejected every invalid one except `fraction-integer.json` and `report-bad-date.json`, as expected from the first two points. That run is not part of CI; M8.3 adds the schema check.

## 10. Open questions

- **`ceg` from the user.** Whether the user may set `ceg` in the device layer is for recelilious to decide, through M6.5 (section 3).
- **Read, change, save.** Two callers that each load a device entry, change it and save it can lose one change. The store runs one operation at a time, but not a caller's load and save together. M6.5 decides whether the store gets an update operation, and what it does with a rejected file.
- **Migration.** The first change that raises `schemaVersion` migrates device files (section 6).
- **The database in the services.** M6.5 and M7.1 add the database to the services the heads provide, with the shape their pages need (section 7).
- **Frame-rate cap.** The session that applies the graphics settings checks that `frameRateCap` divides the display's refresh rate (M6.6, S6.6.3).

## 11. Sources

Public material consulted (C-0001 section 6):

- JSON Schema 2020-12, Core: <https://json-schema.org/draft/2020-12/json-schema-core>
- JSON Schema 2020-12, Validation: <https://json-schema.org/draft/2020-12/json-schema-validation>
- ECMAScript regular expressions, as JSON Schema uses them: <https://tc39.es/ecma262/#sec-regexp-regular-expression-objects>
- System.Text.Json source generation: <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/source-generation>
- System.Text.Json, unmapped members: <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members>
- `Utf8JsonReader`: <https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/use-utf8jsonreader>
- `JsonElement.DeepEquals`: <https://learn.microsoft.com/en-us/dotnet/api/system.text.json.jsonelement.deepequals>
- Regular expression anchors in .NET, `$` and `\z`: <https://learn.microsoft.com/en-us/dotnet/standard/base-types/anchors-in-regular-expressions>
- `File.Move` with overwrite: <https://learn.microsoft.com/en-us/dotnet/api/system.io.file.move>
- Naming files on Windows, the reserved characters: <https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file>
- iOS thermal states, `ProcessInfo.ThermalState`: <https://developer.apple.com/documentation/foundation/processinfo/thermalstate>
- Semantic Versioning 2.0.0: <https://semver.org/spec/v2.0.0.html>
- Firmware listings by model identifier: iPhone 17 Pro <https://ipsw.me/iPhone18,1/>, iPhone 17 Pro Max <https://ipsw.me/iPhone18,2/>, iPhone 18 Pro <https://ipsw.me/iPhone19,2/>, iPhone 18 Pro Max (US) <https://ipsw.me/iPhone19,3/>, iPhone 18 Pro Max <https://ipsw.me/iPhone19,7/>
- EveryMac, iPhone 18 Pro (Global/A3714), model identifier `iPhone19,2`: <https://everymac.com/systems/apple/iphone/specs/apple-iphone-18-pro-global-a3714-specs.html>
- AppleDB, iPhone device selection: <https://appledb.dev/device-selection/iPhone.html>
- Research note `ios_platform_constraints.md`, the device identifiers: [ios_platform_constraints.md](../research/ios_platform_constraints.md)
- Roadmap, phase 4, which places the database in the larger plan: [roadmap-2026-10.md](../research/roadmap-2026-10.md)
