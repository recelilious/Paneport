<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# compat/schema

The JSON Schemas of the compatibility database (C-0004 section 3).

| File | What it is |
|---|---|
| `title.schema.json` | The title schema, in JSON Schema draft 2020-12. Every `*.json` file in [titles/](../titles/README.md) follows it (C-0008 section 1). |

- **Contents.** It implements C-0008 section 2 and never contradicts it. [compat-db.md](../../docs/design/compat-db.md) section 2 maps every field of the contract to its clause here. `$defs/targetDevices` lists the model identifiers of the target devices, whose reports count for the rating (C-0008 section 3).
- **Changes** follow C-0008 section 5. An optional field or an allow-list entry changes the schema only. A required field, or a field or value removed or renamed, raises `schemaVersion` and needs a change to C-0008. A list or a name here changes in Paneport.Core in the same commit, because the app checks the same rules in code, and a test compares the two.
- **Checks.** Until M8.3 adds the schema check to CI (D8), the `dotnet` job loads every title file through the app's loader, which checks the same rules.
