<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# compat/schema

The JSON Schemas of the compatibility database (C-0004 section 3).

| File | What it is |
|---|---|
| `ios-builds.schema.json` | The schema of the list of verified iOS builds, `../ios-builds.json`, in JSON Schema draft 2020-12 (C-0006 section 6): for each entry the iOS version and build, the device model identifier and one or more evidence records. `tools/CheckCompat.cs` validates the list against it in the `checks` job, with a validator that implements exactly the keywords that the schemas use and refuses any keyword that this schema does not use |
| `title.schema.json` | The title schema, in JSON Schema draft 2020-12. Every `*.json` file in [titles/](../titles/README.md) follows it (C-0008 section 1), and `tools/CheckCompat.cs` validates each against it in the `checks` job. |

- **Contents.** It implements C-0008 section 2 and never contradicts it. [compat-db.md](../../docs/design/compat-db.md) section 2 maps every field of the contract to its clause here. `$defs/targetDevices` lists the model identifiers of the target devices, whose reports count for the rating (C-0008 section 3).
- **Changes** follow C-0008 section 5. An optional field or an allow-list entry changes the schema only. A required field, or a field or value removed or renamed, raises `schemaVersion` and needs a change to C-0008. A list or a name here changes in Paneport.Core in the same commit, because the app checks the same rules in code, and a test compares the two.
- **Checks.** The `checks` job validates every title file against the title schema with `tools/CheckCompat.cs`, whose validator is our own (D8), and the `dotnet` job also loads every title file through the app's loader, which checks the same rules ([compat-db.md](../../docs/design/compat-db.md) section 8.1).
- **Keywords.** Each schema may use only the JSON Schema keywords and annotations that it uses now, each in the forms that the validator implements, which the tool lists for it. A change that uses another one adds it to that list in `tools/CheckCompat.cs`, with self-test cases, and the self-test fails when a schema no longer uses a keyword of its list ([repository-checks.md](../../docs/design/repository-checks.md) section 9).
