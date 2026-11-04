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

- **In the app.** The app bundles a snapshot of this folder at build time (C-0009 section 4). On the device it keeps a layer of its own, with the computed classification and the user's overrides, which never leaves the device (C-0008 section 1).
- **Our own data only.** Every setting and report comes from Paneport's own tests. Nothing is copied from another project's compatibility list, and nothing here is game material or personal data (C-0008 section 5).
- **Changes** come through a reviewed branch, and every report names its evidence record (C-0008 section 5).
- **License.** The data is MIT (C-0008 section 6). JSON files cannot carry a license header, so `REUSE.toml` declares it.
- **Later.** The list of verified iOS builds, `ios-builds.json`, and its schema come with M3.1 (C-0006 section 6).
- **Design:** [compat-db.md](../docs/design/compat-db.md).
