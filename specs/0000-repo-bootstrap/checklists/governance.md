# Governance Requirements-Quality Checklist: ISK-0000

**Purpose**: Validate the *requirements quality* of human-governed roadmap review, the approval
ledger, anti-fabrication, and constitution precedence (US5 + Principles XI/X). This tests the spec,
not the code.
**Created**: 2026-11-05
**Feature**: [spec.md](../spec.md)

**Review Ownership**: Reviewer-owned requirements-quality artifact. Mark an item `[x]` only when the
reviewer determines the requirements-quality criterion is satisfied. `[x]` ≠ implementation complete.

## Requirement Completeness

- [x] CHK001 Is the roadmap change proposal's required field set fully enumerated (evidence, issue, affected spec IDs, diffs, dependency/milestone impact, requirement-change assessment, rollback, decision owner, classification)? [Completeness, Spec §FR-036]
- [x] CHK002 Are the approval ledger's required fields fully enumerated (decision ID, timestamp, approved diff hash, reviewer, rationale, outcome)? [Completeness, Spec §FR-037, Data-model §8]
- [x] CHK003 Are requirements defined for *where* approvals and rejections are durably recorded and how they relate to workflow state? [Completeness, Spec §FR-037/§FR-038]
- [x] CHK004 Is the "routine non-material doc sync that reflects approved implementation" that is *required before merge* defined distinctly from material changes? [Completeness, Spec §FR-038]

## Requirement Clarity

- [x] CHK005 Is the material-vs-non-material boundary enumerated completely and unambiguously (scope, inclusion/exclusion, new/deleted specs, prompt intent, security boundaries, MVP cutline, new ADR, constitution change)? [Clarity, Spec §FR-037]
- [x] CHK006 Is "diff-only" defined precisely (exact unified diffs against named authoritative files), leaving no room for prose-only proposals? [Clarity, Spec §FR-036]
- [x] CHK007 Is the `approvedDiffHash` hashing method/scope specified so an approval provably binds to one exact diff? [Clarity, Spec §FR-037, Data-model §8]
- [x] CHK008 Is the "decision owner / reviewer" role defined with enough precision to identify who may approve a given proposal? [Clarity, Spec §FR-036/§FR-037]

## Requirement Consistency

- [x] CHK009 Is the "Roadmap Steward reviews only, never orchestrates; only `drive-speckit` orchestrates" boundary stated consistently across §FR-036, §FR-046, and constitution §XI? [Consistency]
- [x] CHK010 Does "roadmap-review eligible only after successful convergence, never auto-run" agree across §FR-033, §FR-036, and data-model §4? [Consistency]
- [x] CHK011 Is the constitution-precedence rule (`.specify/memory/constitution.md` > `docs/15-constitution-prompt.md`) stated consistently across §FR-018, data-model §3, and the constitution preamble? [Consistency]

## Acceptance Criteria Quality

- [x] CHK012 Is "material changes are 100% diff-only and withheld pending approval" (SC-009) objectively verifiable from recorded proposals/approvals? [Measurability, Spec §SC-009]
- [x] CHK013 Is "no authoritative file modified without approval" measurable via a defined, closed list of "authoritative files"? [Measurability, Spec §FR-037]
- [x] CHK014 Can "completed-spec history (records, revision IDs, real merge hashes) preserved on rejection" be objectively verified? [Measurability, Spec §FR-038]

## Scenario & Edge-Case Coverage

- [x] CHK015 Are requirements defined for a proposal mixing material and non-material changes (partial approval / split handling)? [Gap, Coverage]
- [x] CHK016 Are requirements defined for an attempt to overwrite the active constitution without approval (explicit block + preserve prior)? [Coverage, Spec §FR-018, Edge "Constitution absent vs present"]
- [x] CHK017 Are requirements defined for `/review-roadmap` resuming across an approval that arrives between CI/PR transitions? [Coverage, Spec §FR-039]
- [x] CHK018 Are anti-fabrication requirements explicit that only *real* Git merge hashes/refs may be recorded, with no synthesized progress on any path? [Coverage, Spec §FR-038, Constitution §XI]

## Traceability

- [x] CHK019 Does each governance requirement (FR-036…FR-039) map to an acceptance scenario, a data-model entity, and a task? [Traceability]
