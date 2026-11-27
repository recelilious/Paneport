<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# tests/compat

The title files on which the compatibility check, `tools/CheckCompat.cs`, tests itself (C-0011 section 1). Its self-test reads them. They are not part of the compatibility database and never enter `compat/titles/` (C-0008 section 5).

| Folder | What it holds |
|---|---|
| `valid/` | Title files that pass every rule, checked as a whole, as `compat/titles/` is. They cover a Dock title with every field; Dock titles whose `drm` keeps the Direct path closed in each way, with `steamApi` or `steamStub` true, `ceg` `yes` or `unknown`, or no `ceg`, and one without `drm`; Direct titles, one of them with `steamAppIdFile` true, which is no marker; local titles, one of them with `ceg` `unknown` and no `launch`, and one with no `ceg`, `steamAppIdFile` true and no `launch`; and the cases of the rating: target devices that disagree, also where the lower device reports first, a retest on one device, a later report listed before an earlier one, two reports on one day, the two model identifiers of one device, releases ordered by precedence, pre-releases, and reports that do not count, among them one whose device is written in another letter case |
| `invalid/<case>/` | One folder for each fixture, holding one title file that breaks exactly one rule. The name of the case starts with the kind of rule it breaks: `schema-`, `json-`, `file-name-`, `launch-`, `allow-list-`, `rating-` or `evidence-` |

- **One rule each.** The self-test lists every case with the rule it must fail, and no other rule may fail: a schema fixture fails one keyword of the schema, such as `enum`, and the other fixtures fail the rule that their name starts with, or `evidence-file`. The self-test also fails on a folder here that its list lacks, and on a listed folder that is missing.
- **Evidence records.** The reports name records under `docs/evidence/` that do not exist. The self-test takes three of them as existing, and `evidence-record-missing` names a fourth in its second report.
- **Only fictional values.** The app ids, from 4100000501 up, the names, the slugs and the hashes are made up. The device model identifiers are those of real devices, which the personal-data scan allows (C-0011 section 2).
- **Run it** from the repository root: `dotnet run tools/CheckCompat.cs -- --self-test`.
- **Design:** [compat-db.md](../../docs/design/compat-db.md) sections 2.2 and 8.1 give the rules.
