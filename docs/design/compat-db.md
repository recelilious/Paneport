# Compatibility Database

| Author | Timestamp (UTC) |
|---|---|
| Claude (Anthropic), for the Paneport project | 2026-10-31T03:07Z |

Back to the [design index](README.md).

This document records the design of the compatibility database core: the title schema in `compat/schema/`, the typed model, the loader, the precedence rules and the device layer store in Paneport.Core, and the desktop head's folders. Stage M6.4 wrote it (see the [L6 brief](stages/L6-2d-game.md)). Stage M8.3 added the compatibility check that CI runs on the title files, with the rating it computes (sections 2.2 and 8.1; see the [L8 brief](stages/L8-dock-alpha.md)). It implements [C-0008](../contracts/C-0008-compatibility-database.md) and joins the domain model of [app-shell.md](app-shell.md). Where this document and a contract differ, the [contracts](../contracts/README.md) win.

## 1. Components

| Part | Where | What it does |
|---|---|---|
| Title schema | `compat/schema/title.schema.json` | JSON Schema draft 2020-12 for the files in `compat/titles/` (C-0008 section 1). The compatibility check validates every file against it in CI (section 8.1). |
| Typed model | `src/Paneport.Core/Compat/` | `TitleEntry` and its parts (`TitleEntryId`, `ExecutableSpec`, `DrmClassification`, `GraphicsSettings`, `HelperPolicy`, `TestReport`) with enumerations for every list of values. `ThermalState` lives in `Hosting/`, because the host reports it too (C-0005 section 2). |
| Reader and writer | `TitleEntryJson` | Reads a file into a `TitleEntry` or the rules it breaks, checks an entry made in code, and writes one. Source-generated `System.Text.Json` code, with no reflection (C-0010 section 2). |
| Precedence | `CompatPrecedence`, `LaunchRule` | Merge the layers into `EffectiveTitleSettings` (section 4), and derive the launch decision from the classification (section 5). |
| Layers | `BundledSnapshot`, `DeviceLayerStore` | Read the bundled snapshot; read and write the device layer (section 7). |
| Database | `CompatDatabase` | Reads both layers of a title and merges them into a `CompatTitle`. |
| Desktop folders | `src/Paneport.Desktop/LocalFolders.cs` | The desktop head's stand-ins for the bundle and the container (section 7). |
| Compatibility check | `tools/CheckCompat.cs` | Validates every file in `compat/titles/` against the schema with a validator of our own (plan decision D8), and checks the file names, the launch and allow-list rules, the rating and the evidence records, in the `checks` CI job (section 8.1). Its fixtures are in `tests/compat/`. |

The app needs no schema library. It checks in code the rules it relies on: types, ranges, allow-lists and the launch rule, and also every other rule of the schema that is simple to state in code, so that the `dotnet` job checks the entries as the app reads them. The compatibility check is written apart from that code, so that each checks the other (section 8.1).

## 2. The title schema

Every row of C-0008 section 2, and of section 4 for the reports, maps to a clause of the schema and to the check in code. "Required" means required in a reviewed file; the device layer's rules are in section 3.

| C-0008 field | Schema clause | Checked in code |
|---|---|---|
| `schemaVersion` | `properties.schemaVersion`: `const` 1; required | Missing, not 1, or newer (section 6) |
| `id` | `$defs/id`: required; `oneOf` `steamAppId` or `exeSha256`; `slug` required | The same; and the file name must match the id |
| `id.steamAppId` | integer from 1 to 4294967295 | The same range |
| `id.exeSha256` | array of `$defs/sha256` (64 lowercase hex digits), at least one, unique | The same |
| `id.slug` | `$defs/slug`: `^[a-z0-9]+(-[a-z0-9]+)*$`, at most 64 characters | `TitleId.IsValidSlug`, the same pattern and length (app-shell.md section 2) |
| `name` | string with a character other than white space, white space being every unit that any dialect of regular expression counts as such, U+0085 and U+FEFF included (section 9); required | The same; and text without an unpaired surrogate (section 6) |
| `executable` | `$defs/executable`: `path` required; the whole object is required for a local title and for an entry that states `direct` (section 5) | The same |
| `executable.path` | `$defs/relativePath`: relative, forward slashes, no empty, `.` or `..` segment, no segment ending in a dot or a space (Windows trims them), none of `\ : * ? " < > \|` or U+0000 to U+001F | The same rule |
| `executable.workingDirectory` | `.` for the install folder, or `$defs/relativePath` | The same |
| `executable.arguments` | array of `$defs/text` (no U+0000); default `[]` | The same |
| `architecture` | `x86_64` or `x86` | The same names |
| `drm` | `$defs/drm`: four optional members, no default | The same; per-layer rules in section 3 |
| `drm.steamApi`, `drm.steamStub`, `drm.steamAppIdFile` | booleans | The same |
| `drm.ceg` | `yes`, `no` or `unknown` | The same names |
| `launch` | `direct` or `dock`, with the conditions under `allOf` (section 5) | The same |
| `graphicsApi` | unique items of `gdi`, `ddraw`, `d3d8`, `d3d9`, `d3d10`, `d3d11`, `d3d12`, `opengl`, `vulkan`; default `[]` | The same |
| `runtimes` | unique items of `$defs/runtime`, the fixed list `vcredist-v14-x64`, `vcredist-v14-x86`; default `[]` | `CompatLists.RuntimeIds` |
| `memoryEstimateMiB` | integer from 1 to 2147483647 | The same |
| `jitPoolMiB` | integer from 256 to 896 | The same; the JIT flow still applies C-0006 section 4 |
| `translator` | object whose keys are the `properties` listed, none so far; default `{}` | `CompatLists.TranslatorKeys`; values are strings without U+0000 |
| `graphics.renderScale` | number greater than 0 and at most 1; default 1 | The same |
| `graphics.metalFx` | `off` or `spatial`; default `off` | The same names |
| `graphics.frameRateCap` | integer from 1 to 2147483647 | The same. That it divides the display's refresh rate is for the session to check, which knows the display. |
| `environment` | object whose keys are the `properties` listed, none so far; default `{}` | `CompatLists.EnvironmentKeys`; values are strings without U+0000 |
| `helperPolicy.blockedExecutables` | unique items of `$defs/relativePath`; default `[]` | The same |
| `prefix` | `shared` or `isolated`; default `shared` | The same names |
| `rating` | `$defs/rating`, the six ratings of section 3; default `untested` | The same names. The compatibility check computes it (section 2.2). |
| `reports` | array of `$defs/report`; default `[]` | The same |
| `reports[].date` | `YYYY-MM-DD` pattern, `format: date` | A calendar date |
| `reports[].device` | `^[A-Za-z]+[0-9]+,[0-9]+$`, a model identifier | The same, which keeps device names out |
| `reports[].iosVersion` | version and build, such as `27.2 (24B119)` | The same |
| `reports[].paneportVersion` | `v` and a Semantic Version (C-0009 section 6), or a 40-digit commit hash | The same |
| `reports[].gameBuild` | a Steam build ID for a Steam title, a `$defs/sha256` for a local title (`allOf`) | The same |
| `reports[].rating` | one of the five observed ratings, never `untested` | The same |
| `reports[].evidence` | `docs/evidence/YYYY-MM-DD-<topic>.md` (C-0012 section 4) | The same |
| `reports[].fpsMedian`, `fpsLow1` | numbers greater than 0, required for `runs` or better | The same |
| `reports[].peakMemoryMiB` | integer from 1 to 2147483647, required for `runs` or better | The same |
| `reports[].thermalState` | `nominal`, `fair`, `serious` or `critical`, iOS's thermal states | The same names |
| `reports[].notes` | string | The same; and text without an unpaired surrogate (section 6) |
| Any other field | `additionalProperties: false` on every object | Rejected (section 6) |

Sizes are in MiB, and the field names say so.

**The slug.** C-0008 section 1 allows "lowercase letters, digits and single hyphens". The schema reads this as app-shell.md section 2 does: one or more groups of lowercase ASCII letters and digits joined by single hyphens, with no leading, trailing or double hyphen. Every title has a slug, so the pattern applies to a Steam title's slug too. The slug is at most 64 characters long, as `TitleId.MaxSlugLength` says: it becomes the file names `local-<slug>.json` and `local-<slug>.json.tmp` (section 7), which NTFS and APFS limit to 255 units. The temporary file's name holds 15 characters besides the slug, so with a slug of more than 240 characters every save would fail.

**The allow-lists** start empty, as the brief asks, so `translator` and `environment` hold only `{}` for now. A stage that needs an entry adds it to the schema's `properties` and to `CompatLists` in one change (C-0008 section 5). The environment allow-list never holds `SteamAppId`, `SteamGameId`, `SteamAppPath` or any other variable that `steam_api` or a DRM wrapper reads; a test rejects any name that starts with `Steam` in any letter case, because Windows reads environment names that way. Translator values are strings, like those of the environment; each allowed option may narrow its value in the schema.

**The fixed list of runtimes** names the Microsoft Visual C++ v14 redistributable for both architectures, the example that C-0008 section 2 gives. Runtimes are not provisioned yet (plan section 7.5); a later stage adds the identifiers it needs.

**No `$id`.** The schema is used from the repository only, and the compatibility check loads it by its path, so it names no URI of its own.

### 2.1 Target devices

`$defs/targetDevices` lists the model identifiers of the devices of C-0001 section 2, whose reports count for the rating (C-0008 section 3). A test reads this table and compares it with the schema.

| Device | Model identifiers |
|---|---|
| iPhone 17 Pro | `iPhone18,1` |
| iPhone 17 Pro Max | `iPhone18,2` |
| iPhone 18 Pro | `iPhone19,2` |
| iPhone 18 Pro Max | `iPhone19,3`, `iPhone19,7` |

The research named `iPhone18,1` and `iPhone18,2`. This stage confirmed all five identifiers on 2026-10-31 against the firmware listings of ipsw.me, and the iPhone 18 Pro's also against EveryMac and AppleDB (section 11). The iPhone 18 Pro Max has two: ipsw.me names `iPhone19,3` "iPhone 18 Pro Max (US)" and `iPhone19,7` "iPhone 18 Pro Max". No identifier is missing.

### 2.2 The rating

`rating` is computed from the reports, not edited by hand (C-0008 section 3). The compatibility check computes it and rejects a file whose `rating` differs; a file without `rating` has the schema's default, `untested`.

1. **The reports that count.** A report counts when its `paneportVersion` is a release tag and its `device` is a target device (section 2.1). A release tag is `v` and a Semantic Version, as the schema's pattern has it (C-0009 section 6), and a pre-release such as `v0.1.0-rc.1` is one too. A numeric identifier of a pre-release with a leading zero, as in `v1.0.0-01`, which the pattern lets through, makes no Semantic Version (its item 9), so such a report does not count either. A report that names a commit hash never counts, so an entry whose reports all come from builds of a branch stays `untested`.
2. **The highest release.** Of the reports that count, those of the highest release count, by the precedence of Semantic Versioning 2.0.0, its section 11: `v0.10.0` is higher than `v0.9.0`, a pre-release is lower than its release, and `v0.5.0-beta.10` is higher than `v0.5.0-beta.2`. Two tags of equal precedence are the same release.
3. **Ties.** What a device says on that release is its latest report for it: the one with the latest `date`, and of two on one date the later in `reports`, where a new report is added at the end. The target devices disagree when what they say differs, and the lower rating then wins. Otherwise the latest report wins, and its rating is the one they all say.

So the rating is the lowest of what the target devices say on the highest release, in the order `broken`, `boots`, `runs`, `playable` and `perfect`, and `untested` when no report counts. C-0008 section 3 does not say how the reports of one device combine. This design takes the latest, so a retest on a device replaces its earlier result for the release, as rule 3 lets the latest report win where the devices agree; recelilious accepted this reading on 2026-11-26, in the review of M8.3.

A device here is one of the four target devices of C-0001 section 2, which C-0008 section 3 counts. A report's `device` is a model identifier (C-0008 section 4), and the compatibility check names its device through the table of section 2.1, which it holds as a map: the reports of `iPhone19,3` and `iPhone19,7` are those of one device, the iPhone 18 Pro Max, and that device's latest report on the release is what it says. Its self-test compares the map with the table and with `$defs/targetDevices`.

## 3. Layers

Both layers use the fields and types of the schema. Each has rules of its own, which `TitleEntryJson` applies by `CompatLayer`.

| Rule | Bundled entry | Device entry |
|---|---|---|
| Follows the schema | Yes, as a reviewed file | Its fields, types and value rules, but not what the schema requires of a reviewed file, such as `name`; the schema itself is for `compat/titles/` |
| `name` | Required | Optional; Steam's app information or the import names the title |
| `executable` for a local title | Required, because `exeSha256` hashes the program it names | Optional; the bundled entry may name the program |
| `launch` | Allowed where the device cannot change the path (section 5) | Never: the launch path follows from `drm` |
| `rating`, `reports` | Allowed | Never: only reviewed entries hold them |
| `drm.ceg` | `yes`, `no` or `unknown` | Only `unknown` |
| A local title's Steam marker | Rejected: `steamApi` or `steamStub` true, or `ceg` `yes` (C-0008 section 5) | Allowed: it is the computed classification, and the launch is refused unless the reviewed entry of a reviewed build sets that member (sections 4 and 5) |

**Why the device layer's `ceg` is only `unknown`.** The device layer holds the classification computed under C-0007 section 5 and the user's overrides (C-0008 section 1). CEG cannot be detected by reading files and is set by hand in the reviewed entry (C-0007 section 5), so the classifier always records `unknown`, as M6.5's brief says. A device entry with `yes` or `no` would be a user's override of the classification. Whether the user may set `ceg` is a question that M6.5 puts to recelilious; until it is answered, the device layer cannot set it, which is the safe side. A "yes" answer changes the rule in `TitleEntryRules` and the merge in `CompatPrecedence`, and probably C-0008.

**Only the classifier writes `drm`.** A file cannot tell a computed value from an override, so the code that saves device entries keeps to this rule. The user's overrides are the other fields.

**An uncertain marker is recorded as true.** C-0008 section 2 gives `steamApi` and `steamStub` only the values true and false, and a missing member means that the title is not classified yet. C-0007 section 5 sends a title whose classification is uncertain to the Dock path, and the app refuses a local title that shows any Steam marker. So the classifier records true for a marker that it cannot rule out, such as a SteamStub signal on a main program it cannot parse (M6.5) or the Steam API in a program file it cannot read (M7.2). The title then takes the Dock path or is refused, and once classified it is never Unclassified (section 5). The classifier's own result can still tell a marker it found from one it could not rule out, for the reasons the UI shows; the device file holds true for both. Recording that difference in the file would need a new value and a change to C-0008.

## 4. Precedence and merging

`CompatPrecedence.Merge` gives the effective settings: the schema defaults, then the bundled entry, then the device layer (C-0008 section 1). It takes entries that passed their layer's checks.

| Field | How the layers merge |
|---|---|
| Scalars: `name`, `architecture`, `memoryEstimateMiB`, `jitPoolMiB`, `prefix` | The device layer's value when it has one, else the bundled entry's, else the schema default or none |
| `executable` | Replaced as a whole: a device `executable` replaces the reviewed program together with its working directory and arguments, because they belong to the program they were set for. The effective arguments are never null. |
| `graphics` | Member by member, so that a user's render scale keeps the reviewed frame-rate cap |
| `helperPolicy` | Member by member; its one member is a list |
| `translator`, `environment` | Key by key: the device layer's keys replace the bundled entry's, and the others stay. A key cannot be removed, only given another allowed value. The allow-lists are enforced where an entry is read or written, not in the merge. |
| Lists: `graphicsApi`, `runtimes`, `blockedExecutables` | Replaced as a whole. An empty list in the device layer replaces the bundled list; a list left out keeps it. `arguments` go with their `executable`. |
| `drm` | Member by member, with the bundled entry first: exception 1 (below) |
| `launch` | Never merged. `LaunchRule` derives it from the merged `drm`, and without a program to start the Direct path is closed (section 5). |
| `rating`, `reports` | The bundled entry's only, else `untested` and none |

The model keeps a list that a file leaves out apart from an empty one, so that this distinction survives reading and writing.

**Exception 1.** "A `drm` value set in the bundled entry wins over a computed one while the device runs the reviewed program." Each member counts as a value of its own. A reviewed entry can set `ceg` by hand while the classifier computes `steamApi`, `steamStub` and `steamAppIdFile`, which is how the entries of S6.6.5 and S7.0.3 work.

**The reviewed program.** The reviewed values describe the program that was reviewed, so they count only while the device runs that program. recelilious chose this on 2026-11-01 and accepted it the same day as C-0008 1.1 (section 1) and C-0007 1.1 (section 5); version 1.0 let a reviewed value always win. The device runs the reviewed program when:

- for a Steam title, the device layer names no `executable.path` other than the bundled entry's. Another path, the same path in another letter case, or any path where the bundled entry names none, is another program: a reviewed entry that names no program has reviewed none in particular. Other arguments or another working directory for the same path are not another program.
- for a local title, the device layer has an entry whose `exeSha256` values are all among the bundled entry's, whatever path that entry names. The hashes are what the entry reviewed, one per build (C-0008 section 2), so a reviewed build moved to another folder keeps the reviewed classification, and a build that the entry does not list does not. Without a device entry nothing shows which build the device runs. The rule trusts the device entry's hash, so the code that writes it hashes the program that the device runs (M6.5).

Otherwise only the computed classification counts. Its `ceg` is only `unknown` (section 3), so once classified the title takes the Dock path or is refused, whatever the reviewed entry says; before that, it is Unclassified. `EffectiveTitleSettings.ReviewedClassification` says which case applies: `Used`, `OtherProgram`, `UnreviewedBuild`, or `None` when no bundled entry sets a `drm` member, so that a title's page can say why the title is not Direct. Without this rule, a device `executable` such as a launcher whose computed `steamStub` is true, or an unreviewed build of a local title, took the reviewed `drm` along and could take the Direct path. Only `drm` follows this rule; the other reviewed settings still apply (section 10).

**What a reviewed value hides.** A Steam title is judged by its path, so a reviewed value also covers whatever program an update puts at that path. After an update that adds the Steam API or SteamStub, a reviewed `steamApi` or `steamStub` of false still wins over the computed true, and the title keeps the Direct path; an entry that sets only `ceg` lets the computed members count, and the title moves to the Dock path. This follows C-0008 section 1, which judges a Steam title by its path: the merge cannot tell an updated program from the reviewed one. A reviewed Steam entry therefore sets only `ceg`, unless it has a reason to set another member, such as a marker that the classifier misreads, and its review records that reason. A local title is judged by its hashes, so an update that changes its program is an unreviewed build.

**Exception 2.** "The device layer can never turn `launch` into `direct` for a title whose `drm` requires the Dock path." The structure enforces it:

- the device layer never holds `launch`, and the merge never reads it;
- the bundled entry's `drm` members win over the device layer's, and where the device runs another program none of them counts, `ceg` `no` included;
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

Unclassified means only that the classifier has not run yet. It records true for a marker that it cannot rule out (section 3), so a classified title is Direct, Dock or Refused, and a reviewed `ceg` of `no` with an undecided marker still gives Dock or a refusal. A missing `ceg` counts as unknown, because the device's classification can never set it. `steamAppIdFile` is recorded only and decides nothing. `LaunchDecision` has its own `Refused` value, because a local title can take no other path; `Title` and `LaunchPath` of the shell stay as M0.3 made them, and M6.5 shows a refusal as its UI needs.

**A program to start.** The Direct path starts "the program named in the compatibility entry" (C-0007 section 5), so `CompatPrecedence.Merge` gives the Dock path, or a refusal for a local title, where no layer names an `executable`, even when the classification allows the Direct path. `ReviewedClassification` can still be `Used` then, and a title's page tells this case by the missing `Executable`. For a Steam title, only the bundled entry can name the program of the Direct path, because a device `executable` where the bundled entry names none is another program (section 4). A reviewed local entry always names its program (section 3).

**A stated `launch`.** A reviewed entry may state `launch` where the device's classification cannot change the path. `direct` needs `steamApi` and `steamStub` false and `ceg` `no` in the entry itself, and an `executable`. `dock` is what C-0008 section 5 derives from any other `drm`, including none at all, and an entry may state it, except one whose `drm` sets `ceg` to `no` and neither `steamApi` nor `steamStub` to true: the device could fill in the other members with false and make the path Direct, so the rule is narrower here, on purpose, than the literal text of C-0008 section 5, which derives `dock` from such a `drm` too. Without a reviewed `ceg` of `no` a title never takes the Direct path (section 4), so a stated `dock` holds once the title is classified; before that, the decision is Unclassified. A local title never states `dock`.

**A local title's markers.** A reviewed local entry is rejected when its `drm` shows a Steam marker: `steamApi` or `steamStub` true, or `ceg` `yes`, CEG being Steam's own DRM. `steamAppIdFile` is not a marker for the rule, which names only the other three. On the device, such values are the classification of an imported folder, and the launch is refused, unless the reviewed entry of a reviewed build sets that member (section 4).

## 6. Reading and writing

`TitleEntryJson.Read` takes a file's bytes and its layer, and the identity its name gives. A file that breaks a rule is rejected as a whole: none of its values are used.

| Step | Rejects |
|---|---|
| Size | More than 1 MiB (`Json`) |
| Encoding | Bytes that are not UTF-8 (`Json`); a UTF-8 byte-order mark is skipped |
| Syntax | Anything but one JSON object, comments and trailing commas (`Json`) |
| Strict scan | `null`, which no field takes (`Type`); a field that appears twice in one object (`DuplicateField`); a field the schema does not define, in any letter case (`UnknownField`); an entry of `translator` or `environment` that is not a string (`Type`); a name or string that escapes half of a surrogate pair without the other, such as `"\ud800"`, which is valid JSON but no text (`Json`, at `$`, because the name may be the part that cannot be decoded) |
| Schema version | A newer one (`NewerSchemaVersion`), reported alone unless the version itself appears twice; missing, or not 1 (`MissingField`, `InvalidValue`) |
| Types | A value of the wrong type, including an integer with a fraction or an exponent (`Type`) |
| Values | Required fields, names on their lists in their exact letter case, ranges, patterns, duplicate items (`MissingField`, `InvalidValue`), allow-lists (`NotAllowed`) |
| Rules | The identity (`Identity`), the launch rule (`Launch`), the device layer's rules (`DeviceLayer`) |
| File name | An `id` that does not match the name (`Identity`) |

**Unknown fields** are rejected in both layers. In a reviewed file an unknown field is most often a misspelled one, which would otherwise be ignored without a word. The device layer is written by the app itself. Adding an optional field does not raise `schemaVersion` (C-0008 section 5), so a build can meet a device file that a newer build wrote, after recelilious installs an older build. That file is rejected: the title falls back to the defaults and the bundled entry, is classified again before its next launch, and the file stays until the next save replaces it. A build never carries values that it cannot check against its own rules.

**A newer `schemaVersion`** may add or rename fields, so the reader reports only the version, before any other check. The file is rejected and left in place, as above. The bundled snapshot comes from the same commit as the app, so only the device layer can meet one.

**An older `schemaVersion`** does not exist yet. The change that raises the version also brings the migration of device files and its tests.

**Problems** name the field as a JSON path, such as `$.reports[0].date`, the rule, and what the rule asks for, never the value: a rejected value may be a local path or other personal data, and problems reach logs (C-0010 section 2). A field name that is not a plain name shows as `<name not shown>`, as the personal-data scanner prints such names (repository-checks.md section 3). A path from the serializer shows its plain names and indices up to the first other segment, and `<name not shown>` in place of the rest, because the end of a name that the serializer puts in brackets cannot be told for sure. Only known names reach the serializer, so this only guards.

**Names and integers.** The document classes keep the names of enumerations as strings and integers as 64-bit numbers, and the reader matches names ordinally and checks ranges itself, because the serializer's own enumeration handling ignores letter case and takes numbers.

**Text.** A string that the app passes on as text, such as `name`, a path, an argument, a value of `translator` or `environment`, or a report's `notes`, has no surrogate without its pair (`InvalidValue`). The writer would replace such a surrogate with U+FFFD, so the file would hold another value than the one checked. A file that is read never holds one, because the scan rejects its escape, so only an entry made in code can break this rule.

**Writing.** `TitleEntryJson.Write` checks the entry against its layer's rules and throws `ArgumentException` naming the first problem. It writes the schema's field order, two-space indents and LF line endings on every platform (C-0002 section 7), leaves out the fields the entry does not set, writes map keys in ordinal order and ends with a line break. Reading a valid file, writing it and reading it again keeps every value.

## 7. Storage

**Bundled snapshot.** `BundledSnapshot` reads `compat/titles/` in the app bundle, where the build copies `compat/` (C-0009 section 4). It never writes. `LoadAllAsync` reads every file, such as for matching an imported program by its hash (M6.5).

**Device layer.** `DeviceLayerStore` keeps one file per title in `Library/Application Support/Paneport/compat/`, named `steam-<appid>.json` or `local-<slug>.json` like the repository's files (C-0008 section 1).

- **Atomic replace.** A save writes the file's bytes to `<name>.tmp` in the same folder, flushes them to the disk, and moves the temporary file over the old one. An app crash leaves the old file or the new one. A save that fails or is cancelled before the move leaves the old file and deletes the temporary one.
- **Power loss.** On iOS, .NET's `Flush(flushToDisk: true)` is `fsync`, not `F_FULLFSYNC`, which .NET uses only on macOS (section 11), and the folder is not synced after the move. A save made just before a power loss may therefore be lost, or leave a file that the reader rejects, after which the title falls back as section 6 describes and is classified again.
- **Errors.** A file or folder that cannot be read, written or deleted throws `IOException` or `UnauthorizedAccessException`, as the file system reports it. Their messages can hold a local path, so callers log only the exception's type (app-shell.md section 3).
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
| Exception 1: a bundled `drm` value wins over a computed one, while the device runs the reviewed program | `CompatPrecedenceTests`: `DrmValueOfTheBundledEntryWinsOverTheComputedOne`, `ReviewedCegAndComputedMembersTogetherGiveTheDirectPath`, `ReviewedSteamProgramNeedsNoDeviceEntry`, `ReviewedDrmCoversOnlyTheReviewedProgram`, `ReviewedDrmCoversOnlyTheReviewedBuildsOfALocalTitle`, `LocalDeviceEntryThatListsNoBuildRunsNoReviewedBuild`, `AnotherProgramWithoutAComputedClassificationWaitsForTheClassifier`, `OnlyDrmDependsOnTheReviewedProgram`; the fixtures `empty-hash-list.json` of both layers |
| Exception 2: the device layer never turns the launch path into Direct | `CompatPrecedenceTests`: `DeviceLayerCannotTurnTheLaunchPathIntoDirect`, `ReviewedBlockersKeepTheDirectPathClosedForAnotherProgram`; the device fixtures `launch.json`, `ceg-no.json`, `ceg-yes.json` |
| The Direct path starts the program that an entry names (C-0007 section 5) | `CompatPrecedenceTests.DirectPathNeedsAProgramToStart`; the fixture `direct-without-executable.json` |
| A title with no entry runs with the defaults and shows as untested | `CompatPrecedenceTests.TitleWithNoEntryRunsWithTheSchemaDefaultsAndShowsUntested`, `CompatDatabaseTests.TitleWithNoEntryShowsAsUntested`, `TitleSchemaTests.DefaultsMatchWhatTheMergeGivesWithoutEntries` |
| `drm` has no default that allows a launch | `TitleSchemaTests.NothingThatAllowsALaunchHasADefault` |
| File names: `steam-<appid>.json`, `local-<slug>.json`, matching the id (section 1) | `DeviceLayerStoreTests`: `EachTitleHasOneFileNamedAfterItsId`, `FileWhoseIdIsAnotherTitlesIsRejected`, `LoadAllReadsTitleFilesAndRejectsOtherNames`; `TitleEntryJsonTests.FileOfAnotherTitleIsRejected` |
| The slug pattern (section 1) | The fixture `bad-slug.json`; `TitleIdTests` |
| Launch follows the classification, for Steam and local titles (section 5) | `LaunchRuleTests`; `TitleEntryJsonTests.EntryStatesDockWhereTheDeviceCannotMakeItDirect`; the fixtures `direct-without-ceg.json`, `dock-without-blocker.json`, `local-dock.json`, `launch-unclassified.json` |
| A local title shows no Steam marker (section 5) | The fixtures `local-steam-api.json`, `local-steam-stub.json`, `local-ceg-yes.json`; `LaunchRuleTests.ClassificationDecidesThePath` for the refusal |
| The environment allow-list holds no Steam variable (section 5) | `TitleSchemaTests.EnvironmentAllowListHoldsNoSteamVariable`, `ListsMatchTheCode`; the fixture `environment-steam-key.json` |
| Allow-list of translator options (section 2) | The fixture `translator-key.json`; `TitleSchemaTests.ListsMatchTheCode` |
| Every `*.json` file in `compat/titles/` is checked (sections 1 and 5) | `CompatDatabaseTests.EveryFileInCompatTitlesLoads` |
| The schema implements section 2 | `TitleSchemaTests`: `SchemaDefinesTheFieldsOfC0008AndNoOthers`, `SchemaNamesTheFieldsTheCodeWrites`, `RangesMatchTheCode`, `NamePatternFindsTheSameBlankNamesAsTheCode`, and one test per list of names |
| Each field's type, range and form (sections 2 and 4) | `TitleEntryJsonTests.InvalidFixtureIsRejectedForTheRuleItBreaks`, which lists every invalid fixture with the rule it breaks |

Not enforced in the app's code: that `rating` matches the reports, and that each report's evidence record exists, which the compatibility check enforces (section 8.1); and, checked in review as C-0008 section 5 says, that `arguments` hold no switch that skips a launcher or an ownership check, that `blockedExecutables` lists no program that performs a license or DRM check, and that the data is our own, with no game material and no personal data beyond what the scanner finds. Review sees only `compat/titles/`, never a device file, and a device `executable` that keeps the reviewed path with other arguments keeps the reviewed classification (section 4). So the app takes no device `arguments` from free text without a check of its own. No code writes device `arguments` yet; the stage that first does, such as M6.5 or M7.2, designs that check.

### 8.1 The compatibility check

`tools/CheckCompat.cs` runs in the `checks` job on every file in `compat/titles/` (C-0011 sections 1 and 4), and fails when any of them breaks a rule. It reads each file as the app's reader does (section 6), validates it against the schema with a validator of our own (plan decision D8), and then checks in code what a schema cannot state. Each problem names the file, the JSON pointer and the rule, never the value, and a member name that is not a plain name shows as `<name not shown>`, as in section 6.

The validator is the one that M3.1 wrote for the list of verified iOS builds ([jit.md](jit.md) section 9), which the check reads the same way, extended with the keywords that the title schema uses. Each schema may use only its vocabulary, the keywords and annotations that it uses now, so that a keyword that a schema starts to use comes with self-test cases of its own: the list's schema may not use `enum` or `$comment`, which the title schema uses. The self-test checks that each schema uses all of its vocabulary and that the validator implements nothing that no schema uses, and the validator refuses a schema with a keyword's value in a form that it does not implement, or with an annotation that breaks its rules ([repository-checks.md](repository-checks.md) section 9).

| Rule | What fails it |
|---|---|
| `json` | More than 1 MiB, text that is not UTF-8, anything but one JSON value, a comment or a trailing comma, a string or member name that escapes half of a surrogate pair, and a member that appears twice in one object (section 6) |
| A keyword of the schema, such as `enum` or `pattern` | A value that the schema does not allow |
| `file-name` | A Steam title's file named other than `steam-<steamAppId>.json`, a local title's other than `local-<slug>.json`, a `.json` extension in another letter case, any other file but the README, and a folder (C-0008 section 1) |
| `launch` | A stated `launch` that the entry's own `drm` does not allow, a stated `direct` without an `executable`, and a local title on the Dock path or with a Steam marker (section 5) |
| `allow-list` | A `translator` or `environment` key that the schema does not list, a Steam variable in the environment or in the schema's environment allow-list, and a schema whose `translator` or `environment` takes keys that it does not list (section 2) |
| `rating` | A `rating` other than the computed one (section 2.2) |
| `evidence-file` | A report whose evidence record is not in the repository (C-0012 section 4) |

Where the schema states the launch rule, in its `allOf`, and the allow-lists, in `additionalProperties`, a problem names the rule rather than the keyword. The check also states both rules in code, so that a change of the schema that drops one fails the check rather than passing every file: its self-test removes the schema's `allOf`, opens `translator` and `environment` to any key, and adds `SteamAppId`, `SteamGameId`, `SteamAppPath` and `steamappid` to the environment allow-list, and expects the same failures. The rules in code read the file's values, so they run only on a file that matches the schema; the file name is checked in any case. They test the type of each member that they read, so that a change of the schema that drops a member's type makes a value of another type fail the rule that reads it, which the self-test also checks. The root and the id are left out: the rules read a root that is not an object as an entry without members, and check the file name of an id whose `steamAppId` or `slug` has another type only by its form. A change of the schema that drops the root's type fails the self-test, through M6.4's loader fixture `not-an-object.json`. One that drops the id's types passes CI, but a file whose `steamAppId` or `slug` then has another type still fails it: the reader in the `dotnet` job rejects it in `compat/titles/`.

The check does not use Paneport.Core, though C-0004 section 2 would allow it, and the tools use only the base class library (repository-checks.md section 2). Its rules are written apart from the reader's, so that the two check each other and a schema that drifts from C-0008 is caught, while the `dotnet` job still loads every file through the reader (`CompatDatabaseTests.EveryFileInCompatTitlesLoads`). Its self-test checks the valid files of `tests/compat/valid`, and the fixtures of `tests/compat/invalid`, each of which fails exactly one rule. It also checks M6.4's reviewed fixtures in `tests/Paneport.Core.Tests/Compat/Fixtures`: each valid one matches the schema and each invalid one fails the reading or the schema, so on them the check and the reader agree on whether a file is valid.

## 9. Where the schema and the code differ

- **Integers with a fraction.** JSON Schema counts `512.0` as an integer; the reader does not. Entries write integers without a fraction, and the test over `compat/titles/` catches one in CI. The compatibility check follows the reader and rejects it, and it matches a number at the top of a `const` or an `enum` only as the same text, so that `1.0` is not the `schemaVersion` 1. Numbers nested in a list or an object of a `const` or an `enum` compare by value, as JSON Schema compares them, and no schema has such a number.
- **Dates.** `format: date` asserts a calendar date only in validators that assert formats; the pattern checks the shape. The reader checks the calendar, and the compatibility check asserts the format, as RFC 3339's full-date, except that, like the reader, it rejects the year 0000.
- **`$` in patterns.** The schema writes ECMA-262 patterns, where `$` matches only at the end. In .NET, `$` also matches before a final line break, so the code's patterns end in `\z`. The compatibility check gives each pattern its ECMA-262 meaning before .NET runs it: every `$` outside a class becomes `\z`, a `.` excludes the line terminators, `\s`, `\d` and `\w` take ECMA-262's characters, and in a class `\-` may start a range. It refuses a construct whose meaning differs and that the schemas do not use, such as `\b`, `\-` outside a class, which the `u` flag refuses, or a range in a class with `\s`, `\d` or `\w` at one end, as in `[\w-z]`. It also refuses an empty branch, as the last one of `^(?:a+|){2}$`, an empty group and a quantifier `{0}`: .NET reads such a branch otherwise in a counted group, and fails `"a"` there, which ECMA-262 matches. The refusal is broader than that on purpose, since such branches have no use in a schema, and a rule for counted groups alone is harder to get right. For the same reasons it refuses a lazy quantifier, such as `*?`: whether a text matches does not depend on it, and with one .NET misreads some patterns, such as `^(?:^|(a))*?x$`, and fails on others. The meaning is that of ECMA-262 without the `u` flag, by UTF-16 code units, as .NET and the reader match: `.` or a negated class matches half of a character outside the Basic Multilingual Plane, so `^.$` rejects U+1F600. JSON Schema recommends the `u` flag, and only such characters read differently with it; no pattern of the schemas gives them another answer.
- **White space in patterns.** ECMA-262's `\s` counts U+FEFF and not U+0085, and .NET's `\s` and `char.IsWhiteSpace` count U+0085 and not U+FEFF; in the Basic Multilingual Plane they differ in nothing else. .NET's ECMAScript option makes `\s` ASCII only, and PowerShell 7.6's `Test-Json` behaves that way. It is built on JsonSchema.Net 7.4, which Paneport does not use (plan decision D8). With the earlier pattern `[^\s\u0085\ufeff]`, `Test-Json` accepted a name of U+00A0, U+1680, U+2000 to U+200A, U+2028, U+2029, U+202F, U+205F or U+3000, which the code rejects. The pattern of `name` therefore names every white-space unit outside ASCII, `[^\s\u0085\u00a0\u1680\u2000-\u200a\u2028\u2029\u202f\u205f\u3000\ufeff]`, and reads the same in all three dialects: under ECMA-262, node finds it equal to the earlier pattern on every unit of the Basic Multilingual Plane; `Test-Json` rejects a name of each of those units; and `TitleSchemaTests.NamePatternFindsTheSameBlankNamesAsTheCode` compares it with the code under .NET's dialect and its ECMAScript option. The code counts the same units as white space.
- **Numbers.** The reader reads a number with a fraction as a `double`, so a `renderScale` of `1.0000000000000001` reads as 1 and passes, while a validator that compares decimal numbers rejects it as more than 1. Integers are read exactly. The compatibility check compares numbers exactly, from their text, as JSON Schema does: it rejects that value, which is safe, and accepts a `renderScale` of `1e-29`, as the reader does. Like the reader, it rejects a number too large for a `double`, such as `1e400`, where the schema's type is `number`. A positive number too small for a `double`, such as `1e-400`, reads as 0, so the reader rejects it where the schema wants more than 0; the check compares an exclusive minimum also against the `double` and rejects it too.
- **Surrogate halves.** JSON allows a string that escapes half of a surrogate pair, such as `"\ud800"`, and a validator may accept it; the reader and the compatibility check reject the file (section 6).
- **Device files** follow the device layer's rules in code, not the schema, which requires what a reviewed file must hold: `name`, and `executable` for a local title.

Each of the check's readings that are stricter than JSON Schema, an integer written without a fraction or an exponent, a finite number, the exclusive minimum also as a `double`, a number at the top of a `const` or an `enum` as the same text, and the asserted date, is looser than JSON Schema below `not`, where a match fails the value: `{"not": {"const": 1}}` takes `1.0`. In an `if` condition or a `oneOf` branch, where a match picks the branch or is counted, each can go either way. No schema has one in those places, which the self-test checks by walking both schemas.

A local run of PowerShell 7.6's `Test-Json`, which validates draft 2020-12, accepted every valid bundled fixture and rejected every invalid bundled one except `fraction-integer.json` and `report-bad-date.json`, as expected from the first two points. It rejects every device fixture, valid or not: `surrogate-name.json` because it cannot parse it, and the others because each lacks what the schema requires of a reviewed file, as the last point says. That run is not part of CI. The compatibility check took its place in M8.3: it agrees with the reader on every bundled fixture, the two above included, and its self-test checks them all (section 8.1).

## 10. Open questions

- **`ceg` from the user.** Whether the user may set `ceg` in the device layer is for recelilious to decide, through M6.5 (section 3).
- **Read, change, save.** Two callers that each load a device entry, change it and save it can lose one change. The store runs one operation at a time, but not a caller's load and save together. M6.5 decides whether the store gets an update operation, and what it does with a rejected file.
- **Migration.** The first change that raises `schemaVersion` migrates device files (section 6).
- **The database in the services.** M6.5 and M7.1 add the database to the services the heads provide, with the shape their pages need (section 7).
- **Frame-rate cap.** The session that applies the graphics settings checks that `frameRateCap` divides the display's refresh rate (M6.6, S6.6.3).
- **Another program's rating.** Where the device runs another program than the reviewed one, only `drm` stops counting (section 4); the rating, the reports and the other reviewed settings still come from the bundled entry. M6.5 and M7.1 decide whether the title's page says that they belong to another program.

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
- .NET's `SystemNative_FSync`, which uses `F_FULLFSYNC` only when built for macOS and `fsync` elsewhere, iOS included: <https://github.com/dotnet/runtime/blob/release/10.0/src/native/libs/System.Native/pal_io.c>
- Naming files on Windows, the reserved characters: <https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file>
- iOS thermal states, `ProcessInfo.ThermalState`: <https://developer.apple.com/documentation/foundation/processinfo/thermalstate>
- Semantic Versioning 2.0.0, whose section 11 orders the release tags of the rating: <https://semver.org/spec/v2.0.0.html>
- RFC 3339, the full-date that `format: date` names: <https://www.rfc-editor.org/rfc/rfc3339#section-5.6>
- RFC 6901, JavaScript Object Notation (JSON) Pointer, the pointer that each problem of the compatibility check names: <https://www.rfc-editor.org/rfc/rfc6901>
- Firmware listings by model identifier: iPhone 17 Pro <https://ipsw.me/iPhone18,1/>, iPhone 17 Pro Max <https://ipsw.me/iPhone18,2/>, iPhone 18 Pro <https://ipsw.me/iPhone19,2/>, iPhone 18 Pro Max (US) <https://ipsw.me/iPhone19,3/>, iPhone 18 Pro Max <https://ipsw.me/iPhone19,7/>
- EveryMac, iPhone 18 Pro (Global/A3714), model identifier `iPhone19,2`: <https://everymac.com/systems/apple/iphone/specs/apple-iphone-18-pro-global-a3714-specs.html>
- AppleDB, iPhone device selection: <https://appledb.dev/device-selection/iPhone.html>
- Research note `ios_platform_constraints.md`, the device identifiers: [ios_platform_constraints.md](../research/ios_platform_constraints.md)
- Roadmap, phase 4, which places the database in the larger plan: [roadmap-2026-10.md](../research/roadmap-2026-10.md)
