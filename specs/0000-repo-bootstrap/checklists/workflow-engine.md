# Workflow Engine Requirements-Quality Checklist: ISK-0000

**Purpose**: Validate the *requirements quality* (completeness, clarity, consistency, measurability,
coverage) of the deterministic workflow core — stage engine, grounding discovery, manifest
validation, verbatim prompt retrieval, state/resume, repair budgets, and convergence governance
(US2/US3/US4). This tests the spec, not the code.
**Created**: 2026-11-05
**Feature**: [spec.md](../spec.md)

**Review Ownership**: Reviewer-owned requirements-quality artifact. Mark an item `[x]` only when the
reviewer determines the requirements-quality criterion is satisfied. `[x]` ≠ implementation complete.

## Requirement Completeness

- [x] CHK001 Are repo-root resolution failure requirements defined for the case where neither a `.git` entry nor `.iskender/grounding.yaml` is found up to the filesystem root? [Completeness, Spec §FR-012]
- [x] CHK002 Is lock-contention resolution specified as *block-and-wait* vs *block-and-exit* (and, if waiting, a timeout)? [Gap, Spec §FR-022, US2 scenario 4]
- [x] CHK003 Are stale/orphaned lock requirements defined for when a runner dies while holding `.iskender/state/<specId>.lock`? [Gap]
- [x] CHK004 Are `stateVersion` migration/compatibility requirements defined for reading a record written by an older/newer engine version? [Gap, Data-model §4]
- [x] CHK005 When multiple specs are simultaneously eligible, are requirements defined for which spec `/drive-speckit` selects (and the ordering source)? [Coverage, Spec §FR-020]
- [x] CHK006 Are requirements defined for behavior when an installed Spec Kit command for a canonical stage is absent or named differently? [Coverage, Spec §FR-002, Edge "Spec Kit command mismatch"]
- [x] CHK007 Is the set of required workflow-state fields complete and traceable to FR-021 (including `partialFailureDiagnostics`)? [Completeness, Spec §FR-021, Data-model §4]
- [x] CHK008 Are requirements defined for the `bootstrap/detect` preflight stage's pass/fail criteria distinct from the other stages? [Completeness, Spec §FR-019]

## Requirement Clarity

- [x] CHK009 Is "meaningful repair" (hypothesis- or action-changing) defined precisely enough to be classified deterministically rather than by judgment? [Clarity, Spec §FR-025]
- [x] CHK010 Is the problem fingerprint (`specId/taskId/stage + errorClass + depContext + logHash`) specified with enough normalization detail (what `errorClass` and `logHash` cover) to be stable across runs? [Clarity, Research §D6]
- [x] CHK011 Is a "no-progress cycle" defined unambiguously (what a single cycle is, and when it increments)? [Ambiguity, Spec §FR-025]
- [x] CHK012 Is "byte-for-byte" prompt retrieval defined w.r.t. character encoding, line-ending normalization, and trailing-newline handling of the fenced block? [Clarity, Spec §FR-016, Research §D4]
- [x] CHK013 Is "verified artifacts / sufficient evidence" for resume defined concretely (which artifacts + what makes evidence sufficient)? [Clarity, Spec §FR-022]
- [x] CHK014 Is `runnerId` derivation/format specified so lock ownership and contention are unambiguous? [Clarity, Data-model §4]

## Requirement Consistency

- [x] CHK015 Are the repair budget limits stated identically (3 per problem / 10 per run / 2 no-progress) across spec, plan, data-model, and constitution §X? [Consistency, Spec §FR-025]
- [x] CHK016 Is the canonical stage order identical everywhere it appears (spec §FR-020, data-model §4, task T044)? [Consistency]
- [x] CHK017 Do the convergence severity rules agree across spec §FR-030–032, data-model §5, and constitution §X (pass 1–3 schedule all; pass 4+ Low-only accept; Medium+ always blocks)? [Consistency]
- [x] CHK018 Is the `implement`-stops-before-`converge` rule stated consistently and without exception across spec §FR-023, §FR-033, and constitution §X? [Consistency]

## Acceptance Criteria Quality

- [x] CHK019 Is "zero repeated completed work" after resume (SC-005) objectively measurable from recorded state (e.g., via `lastConfirmedTask` + `gitEvidence`)? [Measurability, Spec §SC-005]
- [x] CHK020 Are the manifest failure cases enumerated as a closed, testable set so "100% blocked" (SC-003) is verifiable? [Measurability, Spec §SC-003]
- [x] CHK021 Can "severities are never silently downgraded" be objectively verified from the ledger history? [Measurability, Spec §FR-032, §SC-007]

## Scenario & Edge-Case Coverage

- [x] CHK022 Are path-safety requirements defined for symlink-based escape of the repo root (not only `..` traversal and absolute paths)? [Gap, Security, Spec §FR-014]
- [x] CHK023 Are requirements defined for an approval-bounded budget reset: who authorizes it and what evidence is recorded? [Coverage, Spec §FR-026/§FR-027]
- [x] CHK024 Are requirements defined for a manifest that parses but references a file that exists yet is empty/unreadable (vs merely absent)? [Edge Case, Spec §FR-014]
- [x] CHK025 Is the recovery requirement for a crash *between* atomic state write and lock release specified? [Edge Case, Spec §FR-021/§FR-028]

## Traceability

- [x] CHK026 Does every workflow-core requirement (FR-012…FR-035) map to at least one acceptance scenario and one task? [Traceability]
