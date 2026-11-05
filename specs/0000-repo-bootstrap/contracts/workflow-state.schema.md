# Contract: Workflow State Record

**Applies to**: `.iskender/state/<specId>.state.json`. Consumer/producer: `Iskender.Workflow` state
engine, invoked by `/drive-speckit`. Traces: FR-019..FR-023, FR-026, FR-028, SC-005.

## JSON shape

```json
{
  "stateVersion": 1,
  "specId": "0000",
  "currentStage": "implement",
  "stageStatus": "in_progress",
  "enteredAt": "2026-11-05T12:00:00Z",
  "runnerId": "host:user:pid",
  "sourceHashes": { "manifest": "sha256:…", "approvedPrompt": "sha256:…", "constitution": "sha256:…" },
  "artifactPaths": ["specs/0000-repo-bootstrap/spec.md", "…/plan.md", "…/tasks.md"],
  "lastConfirmedTask": "T017",
  "gitEvidence": { "commit": "abc1234", "ref": "feature/0000-repo-bootstrap" },
  "budgets": {
    "runFailedRepairs": 0,
    "consecutiveNoProgress": 0,
    "perProblem": { "<fingerprint>": { "meaningfulFailures": 0 } },
    "resetJustifications": []
  },
  "convergeLedgerRef": ".iskender/state/0000.converge.json",
  "approvalLedgerRef": ".iskender/state/approvals.json"
}
```

## Stage enum (dependency order)

`bootstrap/detect`, `specify`, `clarify`, `plan`, `tasks`, `analyze`, `checklist`, `implement`,
`converge`, `roadmap-review`, `pr-merge-checkpoint`.

## Invariants

| # | Invariant |
|---|---|
| S1 | Exactly **one** `currentStage` is active per invocation; selection follows dependency order + approved status, never CWD scanning (FR-020). |
| S2 | Writes are atomic: temp file → fsync → rename-over. A crash mid-write never yields a partial record (FR-021/FR-028). |
| S3 | A per-spec advisory lock (`.iskender/state/<specId>.lock`, runnerId+timestamp) is required to write; a second driver that cannot acquire it **blocks** with a contention error — it does not write (FR-022). |
| S4 | `implement` works all remaining tasks until complete/blocked/input-needed, then stops; it never sets `currentStage` to `converge` itself (FR-023). |
| S5 | Resume reads the record, verifies `artifactPaths` + `gitEvidence`, and continues from `lastConfirmedTask` without repeating completed work (FR-022, SC-005). |
| S6 | `budgets.*` counters are read from the committed record and are **never** reset by a restart (FR-026, SC-005). |
| S7 | `gitEvidence` records **actual** commit/ref; fabricated progress is rejected (Principle XI). |

## CLI surface

- `state read --spec <id>` → prints the record (or "no state").
- `state select-stage --spec <id>` → returns the single eligible stage.
- `state write --spec <id> --stage <s> --status <st> [--task <t>] [--commit <sha>]` → atomic+locked write.
- `state lock|unlock --spec <id> --runner <id>` → acquire/release advisory lock (blocks on contention).
