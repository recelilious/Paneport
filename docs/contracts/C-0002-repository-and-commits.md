# C-0002 Repository and Commits

| Field | Value |
|---|---|
| Status | Accepted |
| Version | 1.0 |
| Date | 2026-10-28 |
| Owner | recelilious |

## 1. Name and location

| Item | Convention |
|---|---|
| Project name | Paneport |
| GitHub repository | `recelilious/Paneport`, created by recelilious |
| Remote | `git@github.com:recelilious/Paneport.git` |
| Local path | `E:\Dev\Paneport` |
| Default branch | `main` |

The GitHub repository was created empty, without a README, .gitignore or license, so that the local history could be pushed as it was. recelilious decides the repository's visibility.

## 2. Branches

- The bootstrap commit goes directly to `main`.
- After that, Claude's changes go on short-lived branches named `<type>/<summary>`, for example `feat/jit-request`.
- recelilious merges each pushed branch, through a pull request or a fast-forward.
- recelilious may ask for a change to be committed directly to `main`.

## 3. Push cadence

- Before every development session ends, its commits are pushed to GitHub.
- If a push fails, Claude reports the reason and the local state at the end of the session.

## 4. Authorship and credit

- **Author:** the git identity configured on recelilious's machine.
- **Co-author:** every commit Claude takes part in ends with this trailer:

  ```
  Co-Authored-By: Claude <model name> <noreply@anthropic.com>
  ```

  The model name is the Claude model that actually did the work, for example `Claude Opus 5.5`.
- Commits made by recelilious alone do not carry this line.

## 5. Commit message format

Commits follow Conventional Commits, in English, in the imperative mood.

- The subject is `<type>(<scope>): <subject>`. The scope is optional. The subject is at most 72 characters.
- The body explains why the change was made, with lines of at most 72 characters.
- Trailers come last, separated from the body by a blank line.

| type | Use |
|---|---|
| feat | New feature |
| fix | Bug fix |
| perf | Performance improvement |
| refactor | Restructuring without behavior change |
| docs | Documentation other than contracts |
| contract | Changes under `docs/contracts/`, always in their own commit |
| build | Build system and dependencies |
| ci | Continuous integration |
| test | Tests |
| chore | Anything else |

Example:

```
contract: add licensing policy (C-0003)

Our own code is MIT. Upstream-derived code keeps its upstream
license and lives in a separate fork repository.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

`.gitmessage` in the repository root is the commit template. It is enabled locally with `git config commit.template .gitmessage`.

## 6. Never commit

| Category | Examples |
|---|---|
| Signing material | `.p12`, `.cer`, `.mobileprovision` |
| Device pairing files | `.mobiledevicepairing`, which works like a key to the device |
| Credentials | Apple ID, Steam account, passwords, sign-in tokens, API keys |
| Third-party binaries | Any file from Microsoft, Valve or a game |
| Build output | `.ipa`, `bin/`, `obj/`, `DerivedData/` |

Large binaries are published through GitHub Releases and stay out of git history.

## 7. Text and language

- The repository uses LF line endings, enforced by `.gitattributes`. `.bat` and `.cmd` files are the exception and use CRLF.
- All text is UTF-8.
- All documentation in the repository is written in English. Code identifiers and commit messages are in English too.
- A Simplified Chinese version of a document exists only when recelilious explicitly asks for one. It sits next to the English file with the `_zh_CN` suffix, for example `README_zh_CN.md`, and each file of the pair links to the other at the top.

## Change log

| Version | Date | Notes |
|---|---|---|
| 0.1 | 2026-10-28 | First draft |
| 1.0 | 2026-10-28 | Accepted by recelilious; translated to English; documentation is now English only, with `_zh_CN` versions only on explicit request |
