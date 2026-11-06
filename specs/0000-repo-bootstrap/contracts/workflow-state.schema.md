# Contract: Workflow State Record

**Applies to**: `.iskender/state/<specId>.state.json`. Consumer/producer: the `/drive-speckit`
skill. Traces: FR-019..FR-023, FR-026, FR-028, SC-005.

## JSON shape

```json
{
  "stateVersion": 1,
  "specId": "0000",
  "currentStage": "implement",
  "stageStatus": "in_progress",
  "enteredAt": "2026-11-05T12:00:00Z",
  "runnerId": "host:user:startedAtUtc",
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
  "partialFailureDiagnostics": { "errorClass": "NU1101", "command": "dotnet restore", "outcome": "failed" },
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
| S3 | A per-spec advisory lock (`.iskender/state/<specId>.lock`, runnerId+timestamp) is required to write; a second driver that cannot acquire it **blocks-and-exits** with a contention error naming the owner — it never waits, spins, steals, or writes (FR-022). |
| S3a | A lock is **stale/reclaimable** only when the recorded runner is on this host AND its timestamp, refreshed on every state write and before long-running steps, is more than 15 minutes old; a reclaim is recorded. A crash between the atomic write (S2) and lock release leaves committed state intact and the lock reclaimable — completed work is not redone (FR-022). |
| S8 | `stateVersion` newer than the skill supports **fails closed** (block + owner action); older supported versions are read without loss. `.iskender/state/` records are committed and MUST NOT be `.gitignore`d; only transient `*.lock` and `*.tmp` files are ignored (FR-021). |
| S4 | `implement` works all remaining tasks until complete/blocked/input-needed, then stops; it never sets `currentStage` to `converge` itself (FR-023). |
| S5 | Resume reads the record and verifies that every `artifactPaths` entry exists, any `gitEvidence.commit` is HEAD or its ancestor, and `sourceHashes` match; any mismatch blocks. It then continues after `lastConfirmedTask`, skipping tasks marked complete in `tasks.md`, without repeating completed work (FR-022, SC-005). |
| S6 | `budgets.*` counters are read from the committed record and are **never** reset by a restart (FR-026, SC-005). |
| S7 | `gitEvidence` records **actual** commit/ref; fabricated progress is rejected (Principle XI). |
