<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# compat

Paneport's compatibility database: the reviewed settings and test results of each title (C-0008), and the JSON schemas they follow (C-0004 section 3).

| Folder | What it holds |
|---|---|
| [schema/](schema/README.md) | The JSON Schemas, starting with the title schema |
| [titles/](titles/README.md) | One JSON file per title |
| `ios-builds.json` | The iOS builds on which the JIT request protocol has been verified, with the device model and the evidence records of each (C-0006 section 6). Empty until L3's device acceptance passes |

- **In the app.** The app bundles a snapshot of this folder at build time (C-0009 section 4). On the device it keeps a layer of its own, with the computed classification and the user's overrides, which never leaves the device (C-0008 section 1).
- **Our own data only.** Every setting and report comes from Paneport's own tests. Nothing is copied from another project's compatibility list, and nothing here is game material or personal data (C-0008 section 5).
- **Changes** come through a reviewed branch, and every report names its evidence record (C-0008 section 5).
- **License.** The data is MIT (C-0008 section 6). JSON files cannot carry a license header, so `REUSE.toml` declares it.
- **Checks.** In the `checks` job, `tools/CheckCompat.cs` reads every title file and `ios-builds.json` as the app reads a title file, validates each against its schema, and checks the rules of C-0008 for the title files and those of C-0006 section 6 for the list ([compat-db.md](../docs/design/compat-db.md) section 8.1, [jit.md](../docs/design/jit.md) section 9). The same job scans this folder for personal data.
- **Verified iOS builds.** `ios-builds.json` follows `schema/ios-builds.schema.json`. On an iOS build that the list does not hold, the app warns but still runs (C-0006 section 6). An entry is added only after a device acceptance has passed on that build and model, and names its evidence record; the phase 1 gate adds its record to the entry of the same build and model ([jit.md](../docs/design/jit.md) section 9).
- **Design:** [compat-db.md](../docs/design/compat-db.md).
