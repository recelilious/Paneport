# Contracts

Contracts are the binding documents of this project. Contracts come first; implementation follows.

## Rules

1. **Contracts win.** When the implementation and a contract disagree, the contract wins. To change behavior, change the contract first.
2. **Number and status.** Every contract has a number, a status, a version and a date. The status is Draft, Accepted or Superseded.
3. **Acceptance needs approval.** A new contract, or a substantive change to an existing one, becomes Accepted only after recelilious explicitly approves it. Accepted versions are 1.0 or higher.
4. **Separate commits.** Any change under `docs/contracts/` goes in its own commit with the `contract` type, and the contract's change log records why.
5. **Read before working.** At the start of every development session, Claude reads this index and the contracts relevant to the task.

## Current contracts

| Number | Title | Status | Version | Date |
|---|---|---|---|---|
| [C-0001](C-0001-project-charter.md) | Project charter | Accepted | 1.0 | 2026-10-28 |
| [C-0002](C-0002-repository-and-commits.md) | Repository and commits | Accepted | 1.0 | 2026-10-28 |
| [C-0003](C-0003-licensing.md) | Open-source licensing | Accepted | 1.0 | 2026-10-28 |

## Planned contracts

To be drafted next, in dependency order.

| Number | Title | Scope |
|---|---|---|
| C-0004 | Component boundaries and layout | What each module is responsible for and which repository it lives in |
| C-0005 | Native runtime host interface | The C ABI between the Avalonia shell and the native core |
| C-0006 | JIT request protocol | How the app asks an external debugger for executable memory |
| C-0007 | Steam integration | Sign-in, library, download and launch design, and its compliance boundary |
| C-0008 | Compatibility database | Format of per-game configuration and test results |
| C-0009 | Build and signing | The pipeline from a Windows 11 workstation to an installable .ipa |
