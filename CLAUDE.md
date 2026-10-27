# Working Rules for Claude

This file is for Claude sessions working on Paneport. The binding versions of these rules live in `docs/contracts/`. When the two disagree, the contracts win.

1. **Read the contracts first.** At the start of every session, read `docs/contracts/README.md` and the contracts relevant to the task. Change a contract only with recelilious's approval, in its own commit with the `contract` type.
2. **Commits and credit.** Follow C-0002. The author is the local git identity. Every commit Claude takes part in ends with `Co-Authored-By: Claude <actual model name> <noreply@anthropic.com>`.
3. **Branches and pushes.** Changes go on short-lived branches by default, and recelilious merges them. Push to `origin` before every development session ends.
4. **Licensing.** Follow C-0003. Register a dependency's license before adding it. GPL and AGPL code never enters the repository.
5. **Originality.** Follow section 6 of C-0001. Never copy, translate or rewrite code from existing products.
6. **Compliance.** Never implement or add any DRM bypass or steam_api emulation. Never commit third-party binaries, signing material, pairing files or credentials.
7. **Language.** All documentation in the repository is in English, and so are code identifiers and commit messages. Create a `_zh_CN` Simplified Chinese version of a document only when recelilious asks for one, and link each file of the pair to the other at the top.
