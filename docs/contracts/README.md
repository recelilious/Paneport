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
| [C-0003](C-0003-licensing.md) | Open-source licensing | Accepted | 1.1 | 2026-10-28 |
| [C-0004](C-0004-architecture-and-layout.md) | Architecture, components and layout | Accepted | 1.0 | 2026-10-28 |
| [C-0005](C-0005-native-host-interface.md) | Native runtime host interface | Accepted | 1.0 | 2026-10-28 |
| [C-0006](C-0006-jit-request-protocol.md) | JIT request protocol | Accepted | 1.0 | 2026-10-28 |
| [C-0007](C-0007-steam-integration.md) | Steam integration | Accepted | 1.0 | 2026-10-28 |
| [C-0008](C-0008-compatibility-database.md) | Compatibility database | Accepted | 1.0 | 2026-10-28 |
| [C-0009](C-0009-build-signing-release.md) | Build, signing and release | Accepted | 1.0 | 2026-10-28 |
| [C-0010](C-0010-languages-and-style.md) | Languages and code style | Accepted | 1.0 | 2026-10-28 |
| [C-0011](C-0011-testing-review-quality.md) | Testing, review and quality tools | Accepted | 1.0 | 2026-10-28 |
| [C-0012](C-0012-acceptance-process.md) | Acceptance process | Accepted | 1.0 | 2026-10-28 |

## Planned contracts

None at the moment. A new contract is proposed as a Draft and listed above.
