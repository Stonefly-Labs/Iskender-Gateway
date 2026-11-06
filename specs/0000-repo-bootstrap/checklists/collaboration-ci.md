# Collaboration & CI Requirements-Quality Checklist: ISK-0000

**Purpose**: Validate the *requirements quality* of protected collaboration, the GitHub Actions CI
skeleton, the documentation merge gate, and secure-development standards (US6 + Principles V/IX).
This tests the spec, not the code.
**Created**: 2026-11-05
**Feature**: [spec.md](../spec.md)

**Review Ownership**: Reviewer-owned requirements-quality artifact. Mark an item `[x]` only when the
reviewer determines the requirements-quality criterion is satisfied. `[x]` ≠ implementation complete.

## Requirement Completeness

- [x] CHK001 Is the full set of required CI checks enumerated (restore, format, build, all tests, architecture/dependency, docs/link/manifest/inventory consistency, secret scan, dependency hygiene, SBOM/provenance)? [Completeness, Spec §FR-042]
- [x] CHK002 Are the branch-protection deliverables fully specified (required reviews, required checks, CODEOWNERS enforcement, no-direct-push)? [Completeness, Spec §FR-041]
- [x] CHK003 Are the collaboration templates fully enumerated (CODEOWNERS, PR template, issue templates, SECURITY.md, release/versioning conventions)? [Completeness, Spec §FR-041]
- [x] CHK004 Are the secure-development standards fully listed and each mapped to an enforcing CI check or documented guidance (least privilege, secret scanning, dependency integrity, no untrusted plugin execution, threat review of new endpoints/identity/serialization)? [Completeness, Spec §FR-045]

## Requirement Clarity

- [x] CHK005 Is the documentation merge gate's "matching documentation change" defined precisely (which docs satisfy a given behavior-bearing path change)? [Ambiguity, Spec §FR-043, Research §D10]
- [x] CHK006 Is the skip mechanism fully specified (exact label, required written justification, and who may apply it)? [Clarity, Spec §FR-043]
- [x] CHK007 Is "SBOM/provenance hooks present" defined concretely enough to confirm the check exists vs. merely being a placeholder? [Clarity, Spec §FR-042]
- [x] CHK008 Is the "pending rule" reporting format specified (owner + exact verification command + explicit not-applied status) so it can never be mistaken for applied? [Clarity, Spec §FR-044, §SC-010]

## Requirement Consistency

- [x] CHK009 Is the list of behavior-bearing paths consistent between §FR-043, research §D10, and task T095? [Consistency]
- [x] CHK010 Does §FR-043's "public contracts/SDK surfaces" match the concrete path list — in particular, is the omission of `src/Iskender.Providers.Abstractions/**` (a public abstraction surface) intentional? [Conflict, Spec §FR-043 vs Research §D10/Task T095]
- [x] CHK011 Is the branch policy (`feature/`/`chore/`/`bugfix/` off protected `main`) stated consistently across §FR-040, constitution §IX, and CONTRIBUTING guidance (task T101)? [Consistency]
- [x] CHK012 Are the CODEOWNERS "high-risk paths" the same set as the docs-gate behavior-bearing paths, or is any divergence intentional and documented? [Consistency, Spec §FR-041/§FR-042]

## Acceptance Criteria Quality

- [x] CHK013 Is "every check executes on a sample change" (SC-010) backed by an enumerable, closed list of checks so it is objectively verifiable? [Measurability, Spec §SC-010]
- [x] CHK014 Can "a docs-missing PR is blocked in 100% of trials" be objectively measured given the path+label rule? [Measurability, Spec §FR-043/§SC-010]

## Scenario & Edge-Case Coverage

- [x] CHK015 Are requirements defined for a PR touching a behavior-bearing path *and* carrying the `docs-not-required` label without justification (must still block)? [Edge Case, Spec §FR-043]
- [x] CHK016 Are requirements defined for test-only/refactor PRs outside behavior-bearing paths (must NOT be blocked by the docs gate)? [Coverage, Spec §FR-043]
- [x] CHK017 Are requirements defined for CI behavior when run on a clean clone with no cloud secrets (all checks still executable)? [Coverage, Spec §FR-042/§FR-007]
- [x] CHK018 Are requirements defined for the case where admin rights exist for some rules but not others (partial application + partial pending)? [Edge Case, Spec §FR-044]

## Dependencies & Assumptions

- [x] CHK019 Are CI tool names (Gitleaks, CycloneDX, Dependabot) treated as recommendations rather than binding constitutional facts, consistent with constitution §IX? [Assumption, Research §D9]

## Traceability

- [x] CHK020 Does each collaboration/CI/security requirement (FR-040…FR-045) map to an acceptance scenario and a task? [Traceability]
