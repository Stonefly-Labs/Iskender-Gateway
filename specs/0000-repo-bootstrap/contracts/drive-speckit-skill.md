# Contract: `/drive-speckit` Skill

**Applies to**: the `/drive-speckit` Claude Code skill, a plain skill with no CLI or compiled tooling
behind it (FR-003a). Traces: FR-019..FR-024b, FR-034, Principle X.

## Invocation

- Invoked with **no stage argument**; printed stage names are informational only (FR-019).
- Every invocation first runs preflight: repo root, manifest validation, inventory/status, active
  constitution, Spec Kit skills, current branch, last checkpoint, pending approvals (FR-019). A
  failure blocks and no stage runs; `bootstrap/detect` consumes an invocation only when the spec has
  no state record (FR-019a).
- It selects the spec: an incomplete state record first, otherwise the lowest-numbered `ISK-xxxx` whose
  dependencies record a real final merge commit and whose approved prompt is present (FR-020). A branch
  holds a record only if it changed that file since its merge base with `origin/main`; a copy inherited
  from `main` neither outranks `main` nor counts toward the two-branch block (FR-020, FR-022).
- It selects or resumes **exactly one** eligible stage in dependency order (FR-020), does all the work
  in that stage, and stops at the stage boundary.
- It commits after each task in `implement` and at the end of every other stage, pushes after every
  commit, opens the PR right after the first push, and keeps the PR notes current (FR-040a).

## Stages

Spec Kit skills are **discovered** from `.claude/skills/speckit-*`, never assumed or reinstalled.

| Stage | Runs | Rule |
|---|---|---|
| `bootstrap/detect` | Preflight only | Creates the first state record, starting at the first stage whose output is missing (FR-019a) |
| `specify` | `speckit-specify` | Input is the **verbatim** approved prompt from the inventory; never regenerated or substituted (FR-024) |
| `clarify` | `speckit-clarify` | |
| `plan` | `speckit-plan` | |
| `tasks` | `speckit-tasks` | |
| `analyze` | `speckit-analyze` | |
| `checklist` | `speckit-checklist` | Agent reviews requirements, marks what is satisfied, and blocks only on items needing human input; no code changes (FR-024a) |
| `implement` | `speckit-implement` | Works all remaining tasks until complete/blocked/input-needed; MUST NOT launch `converge` (FR-023) |
| `converge` | `speckit-converge` | Records findings in the ledger; no code changes; severity rules apply (FR-029..033) |
| `roadmap-review` | `/review-roadmap` | Eligible only after successful convergence (FR-033) |
| `pr-merge-checkpoint` | Skill text | Completes the notes of the spec's PR, which is its post-merge PR if it has one or otherwise its feature PR, and waits for the human merge. After it, tags that merge commit `v0.0.<spec>-alpha`, then opens the final records-only `chore/` PR with the real merge commit, and reports that PR until a human merges it; never merges or closes either PR (FR-024b, FR-040a, FR-041a) |

## Mandatory summary (every invocation)

Headed `drive-speckit (<stage>)` and containing: spec ID + title, executed stage, work done, current
status, next stage/action, and the re-invocation instruction when appropriate (FR-034). Blocked
results additionally satisfy `blocker-and-summary.contract.md`.
