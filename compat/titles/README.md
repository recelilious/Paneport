<!--
SPDX-FileCopyrightText: 2026 recelilious and the Paneport contributors
SPDX-License-Identifier: MIT
-->

# compat/titles

One JSON file per title (C-0008 section 1):

- `steam-<appid>.json` for a Steam title;
- `local-<slug>.json` for a title not obtained through Steam. The slug is one or more groups of lowercase ASCII letters and digits joined by single hyphens, and it is unique in the folder.

Every `*.json` file here follows [the title schema](../schema/title.schema.json); this README is not a title file. The folder holds no entry yet. The first ones come with the test titles of M6.6 and L7, which recelilious chooses in D6.

- **What an entry holds.** Only the fields of C-0008 section 2. Its values come from Paneport's own tests and recelilious's own checks, and each report names its evidence record under `docs/evidence/` (C-0008 sections 4 and 5).
- **What it never holds.** Game files or any other game material; personal data, such as an Apple ID, a Steam account name or Steam ID, a device name, a serial number or a local path; anything copied from another project's compatibility list; and any setting that skips, satisfies or replaces a Steam or DRM check (C-0008 section 5).
- **Checks.** The `dotnet` job loads every file here through the app's loader, and the `checks` job scans the folder for personal data. M8.3 adds the schema check and the file-name and rating checks (C-0011 section 1).
- **Design:** [compat-db.md](../../docs/design/compat-db.md).
