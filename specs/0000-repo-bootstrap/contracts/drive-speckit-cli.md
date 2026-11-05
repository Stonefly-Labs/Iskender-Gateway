# Contract: `/drive-speckit` Skill ↔ Workflow CLI

**Applies to**: the `/drive-speckit` Claude Code skill and the `Iskender.Workflow.Cli` it delegates
to. Traces: FR-019..FR-024a, FR-034, Principle X.

## Skill invocation contract

- Invoked with **no stage argument**; printed stage names are informational only (FR-019).
- On each invocation the skill performs preflight discovery (via the CLI): repo root, manifest/schema
  validation, inventory/status, active constitution, Spec Kit artifacts, current worktree/branch,
  last checkpoint, pending approvals (FR-019).
- It selects or resumes **exactly one** eligible stage in dependency order (FR-020) and executes all
  work within that stage, then stops at the stage boundary.
- Underlying Spec Kit commands are **discovered from the installed toolkit**
  (`speckit-{specify,clarify,plan,tasks,analyze,checklist,implement,converge}` observed at bootstrap)
  — never assumed or reinstalled (FR-020, Principle X).
- `implement`: works all remaining tasks until complete/blocked/input-needed; MUST NOT launch
  `converge` (FR-023).
- `specify`: loads the approved **verbatim** prompt from the inventory; MUST NOT regenerate or
  substitute (FR-024).
- `checklist` (after `analyze`, before `implement`): generates an acceptance/quality checklist and
  reviews the implementation against it; any item it cannot confidently discharge is surfaced for
  explicit human review; it MUST NOT modify code — remediation happens in a separate `implement`
  (FR-024a).

## CLI commands the skill relies on

| Command | Purpose | Trace |
|---|---|---|
| `preflight --spec <id>` | root + manifest + constitution + artifacts + checkpoint + lock discovery | FR-019 |
| `validate-manifest` | fail-closed manifest validation | FR-013/014 |
| `get-prompt --spec <id>` | verbatim approved-prompt retrieval (blocks if absent) | FR-016/017 |
| `state select-stage --spec <id>` | returns the single eligible stage | FR-020 |
| `state read/write/lock/unlock` | durable atomic+locked state | FR-021/022 |
| `budget check --spec <id> --fingerprint <f>` | enforce 3/10/2 limits | FR-025/026 |
| `converge evaluate/record` | severity governance + ledger | FR-029..033 |
| `summary emit --spec <id>` | mandatory human summary payload | FR-034 |

## Mandatory summary (every invocation)

Headed `drive-speckit (<stage>)` and containing: spec ID + title, executed stage, work done, current
status, next stage/action, and the re-invocation instruction when appropriate (FR-034). Blocked
results additionally satisfy `blocker-and-summary.contract.md`.
