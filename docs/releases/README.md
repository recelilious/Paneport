# Releases

This folder holds the release notes of every Paneport release, the template they are written from, and the release process (C-0009 section 6, C-0012 section 5). [release-engineering.md](../design/release-engineering.md) describes the `release` workflow and its tools.

| File | What it holds |
|---|---|
| [release-notes-template.md](release-notes-template.md) | The template of the release notes, with the comments where the workflow inserts generated text. A dry run uses it as its notes. |
| `v<version>.md` | The release notes of one release, such as `v0.1.0.md`, written from the template on a branch before the tag. |

## How a release is made

recelilious decides when to release, creates the `v<version>` tag on `main` and publishes the release. Claude prepares the notes on a branch. The `release` workflow builds the release from the tag and attaches everything to a draft release, which only recelilious publishes; the workflow never publishes (C-0009 section 6).

1. **Notes.** On a branch, copy the template to `docs/releases/v<version>.md` and fill it in: the summary, the gates in scope with their verdicts and records, the verified iOS builds and the known issues. Add the version to the release history below. recelilious confirms the gates in scope.
2. **Dry run.** On the head of that branch, run a dry run (below) and check its draft. Fix what it finds on the branch, then delete the dry-run draft and tag.
3. **Merge.** recelilious merges the branch. If the merge made a new commit, run a last dry run on it.
4. **Tag.** recelilious updates the local `main` and creates and pushes the tag on it, for example `git tag -a v0.1.0 -m "Paneport 0.1.0"` and `git push origin v0.1.0`. The `release` workflow starts.
5. **Draft.** The run ends in a draft release `v<version>`. recelilious checks it against the checklist below and the acceptance steps of the release's stage brief, and records the result in an evidence record (C-0012 section 4).
6. **Publish.** recelilious publishes the draft. The release is accepted under C-0012 section 5 once the record's verdict is Pass.

If a check fails before the draft is published, the fix goes through a branch. recelilious then deletes the draft and moves the tag to the fixed commit, or tags the next patch version instead. The tag of a published release is never moved.

## Checklist

One line for each item of C-0012 section 5 and each release item of C-0009 section 6, with where it is checked.

| Item | Where it is checked |
|---|---|
| recelilious decides to release, creates the tag on `main` and publishes the release; Claude prepares the notes on a branch (C-0009 section 6) | Steps 1, 4 and 6 above. The workflow refuses a release tag whose commit is not on `main` or that has no notes. |
| The first public binary release waits for recelilious's decision on the visibility of the repositories and, where possible, for the professional license review that C-0003 recommends (C-0009 section 6) | The decisions of the release's stage brief, recorded in its session log. |
| Semantic Versioning from `0.1.0`; `CFBundleShortVersionString` is `MAJOR.MINOR.PATCH`, and `CFBundleVersion` grows with every published build (C-0009 section 6) | The workflow derives both from the tag and checks them in the built `Info.plist`. |
| Every submodule revision is pushed, and this repository and every fork are public, before tagging (C-0009 section 6; C-0012 section 5 item 4) | In release mode the workflow fetches every revision without credentials and fails otherwise. recelilious opens each repository in a private browser window. |
| The release is built from the tag on a GitHub-hosted macOS runner, and the app and every framework in it are signed ad hoc (C-0009 section 6) | The workflow's build and signing steps; the run summary shows the entitlements and the ad-hoc signature of each file. |
| Source paths are mapped, the build is deterministic, release device builds are stripped, and the package holds no provisioning profile, team identifier, UDID, ECID, serial number, e-mail address or local path (C-0009 section 6; C-0012 section 5 item 3) | The package scan of the workflow, with the reviewed allow-list; the scan section of the run summary. |
| The package holds nothing that C-0009 section 4 excludes (C-0012 section 5 item 3) | The package scan's exclusions and the notices generator's refusal of components that are Under review or Forbidden. |
| The `.ipa`, SHA-256 checksums, `THIRD_PARTY_NOTICES.md` and the full license texts are attached (C-0009 section 6; C-0012 section 5 items 2 and 5) | The workflow attaches them and checks every uploaded asset against the checksum file. |
| `THIRD_PARTY_NOTICES.md` and the license texts cover every component of the package, checked against the register in C-0003 section 6, and the app's licenses screen matches them (C-0012 section 5 item 2) | The notices generator in release mode, which fails on an unattributed file; the coverage section of the run summary; the licenses screen on the device. |
| Source archives of this repository, of every fork at exactly the released revisions, and of each LGPL component that is neither, are attached, and the notes link to the tag, to each fork revision and to each such component's upstream commit (C-0009 section 6; C-0012 section 5 item 4) | The workflow's source archives and the generated sources section of the release body. |
| The archive of each LGPL component that is neither this repository nor a fork comes from `git archive` at the pinned upstream commit, checked against the package; the `ios` job uploads the same archives (C-0009 section 6) | `eng/mac/make-source-archives.sh`, in the `ios` job and in the workflow. |
| Every gate in scope has passed or been waived (C-0012 section 5 item 1) | In release mode the workflow checks the notes' gate table against `docs/evidence/README.md`. |
| Known issues are listed (C-0012 section 5 item 6) | In release mode the workflow refuses notes without a known issue. |
| Release acceptance follows C-0012 section 5 (C-0009 section 6) | The evidence record of the release. |

## Dry run

A dry run checks the workflow without making a release. It runs from a test tag with a pre-release part, `v0.0.0-dryrun.<n>`, and ends in a draft release titled as a dry run and marked as a pre-release, which is never published. It uses the template as its notes, so the checks of the gates, the known issues and the placeholders only warn. recelilious allowed Claude to push and delete the test tags and to delete their drafts (D15 in plan section 4.2); release tags and publishing stay with recelilious.

1. Pick the next number: `git ls-remote --tags origin "v0.0.0-dryrun.*"` lists the test tags that exist.
2. Tag the commit to test and push the tag, for example `git tag v0.0.0-dryrun.1 <commit>` and `git push origin v0.0.0-dryrun.1`. The push runs the workflow file of that commit.
3. Wait for the `release` run of that tag. A failed step fails the run before any draft exists.
4. Check the draft and the run summary: every asset, the scan and coverage reports, the checksum check, the links of the sources section, the version and build number, and the entitlements.
5. To run the same dry run again, delete its draft first, because the workflow stops when a release of the tag exists. Then re-run the run from the Actions tab, or, once the workflow is on `main`, start it by hand with the existing tag: `gh workflow run release.yml --ref v0.0.0-dryrun.<n> -f tag=v0.0.0-dryrun.<n>`. GitHub runs a workflow by hand only when its file is on the default branch. A manual run takes only a test tag, never a release tag.

**Clean-up.** When the checks are done, delete the draft and the tag: `gh release delete v0.0.0-dryrun.<n> --yes --cleanup-tag`, and `git tag -d v0.0.0-dryrun.<n>` where the tag was made. If `gh` does not find the draft by its tag, `gh api "repos/recelilious/Paneport/releases" --paginate --jq '.[] | select(.draft) | [.id, .tag_name] | @tsv'` lists the drafts, `gh api -X DELETE "repos/recelilious/Paneport/releases/<id>"` deletes one, and `git push origin --delete v0.0.0-dryrun.<n>` deletes the tag. A test tag is never reused for another commit.

## Release history

| Version | Tag | Date (UTC) | Notes | Evidence record |
|---|---|---|---|---|

No release yet.
