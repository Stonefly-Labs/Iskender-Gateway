# Skeleton & Onboarding Requirements-Quality Checklist: ISK-0000

**Purpose**: Validate the *requirements quality* of the compilable .NET skeleton, module dependency
direction, offline build/test, and onboarding/identity assets (US1 + Principles I/II/VIII). This
tests the spec, not the code.
**Created**: 2026-11-05
**Feature**: [spec.md](../spec.md)

**Review Ownership**: Reviewer-owned requirements-quality artifact. Mark an item `[x]` only when the
reviewer determines the requirements-quality criterion is satisfied. `[x]` ≠ implementation complete.

## Requirement Completeness

- [x] CHK001 Is the full set of skeleton projects enumerated (host, core contracts, domain, infrastructure, provider abstractions, policy SDK, management, + test assemblies)? [Completeness, Spec §FR-005]
- [x] CHK002 Are the required root config files fully specified (`.gitignore`, `.gitattributes`, `.editorconfig`, `global.json`, central package management)? [Completeness, Spec §FR-004/§FR-008]
- [x] CHK003 Are the README's required sections fully enumerated (mission, license, architecture, build/test, Spec Kit how-to, future plugin/policy scope, contributor workflow, docs/README link)? [Completeness, Spec §FR-009]
- [x] CHK004 Are the `CLAUDE.md` required sections fully enumerated, including the explicit statement that `/drive-speckit`, Roadmap Steward, and `/review-roadmap` do not exist until ISK-0000 is implemented? [Completeness, Spec §FR-010/§FR-003]
- [x] CHK005 Is the existence and required content of `docs/README.md` (the prominent link target) specified, or only assumed? [Gap, Spec §FR-009, Assumptions]

## Requirement Clarity

- [x] CHK006 Is "empty but compilable" defined precisely (no business logic, yet each project builds and the host has a runnable but endpoint-free `Program.cs`)? [Clarity, Spec §FR-005/§FR-047]
- [x] CHK007 Are the "predictable namespace conventions" defined concretely (root namespace per project)? [Clarity, Spec §FR-005, Contract module-dependency-rules]
- [x] CHK008 Is "no cloud provisioning attempted" measurable (what counts as an attempt)? [Measurability, Spec §FR-007/§SC-001]
- [x] CHK009 Are the SVG logo requirements specific enough to verify (original, self-contained, no external refs/fonts/raster, `<title>`/`<desc>`, `role="img"`)? [Clarity, Spec §FR-011]

## Requirement Consistency

- [x] CHK010 Is the approved module dependency direction consistent between spec §FR-006, the module-dependency-rules contract (A1–A5), and research §D8? [Consistency]
- [x] CHK011 Is the project list in US1 acceptance scenario 2 identical to the project list in §FR-005 and the dependency-rules contract? [Consistency]
- [x] CHK012 Is the ".NET 10 / ASP.NET Core" runtime stated consistently across spec Assumptions, plan Technical Context, and constitution §I/§II? [Consistency]

## Acceptance Criteria Quality

- [x] CHK013 Is "clean clone builds/tests green on first attempt" (SC-001) objectively verifiable with the documented command sequence? [Measurability, Spec §SC-001]
- [x] CHK014 Is "Spec Kit preserved byte-for-byte" (SC-002) measurable via a defined check (e.g., empty `git status --porcelain .specify`)? [Measurability, Spec §SC-002]
- [x] CHK015 Can the architecture/reference-direction rules be objectively verified by an automated check rather than reviewer judgment? [Measurability, Spec §FR-006]

## Scenario & Edge-Case Coverage

- [x] CHK016 Are requirements defined for the optional local/Compose dependency scaffolding being present but never *required* for build/test? [Coverage, Spec §FR-007]
- [x] CHK017 Are accessibility/rendering requirements for the logo defined for both light and dark backgrounds (contrast/theme), or is that intentionally out of scope? [Gap, Spec §FR-011]
- [x] CHK018 Are requirements defined to prevent the workflow-state directory (`.iskender/state/`) from being accidentally excluded by `.gitignore` given state must be durable/committed? [Edge Case, Spec §FR-004 vs Research §D5]

## Dependencies & Assumptions

- [x] CHK019 Is the assumption that grounding documents live under `docs/` (per `grounding.root: docs`) stated and validated? [Assumption, Spec Assumptions]
- [x] CHK020 Is the four-digit `0000` spec-directory decision documented as an intentional override of the three-digit default? [Assumption, Spec Assumptions]

## Traceability

- [x] CHK021 Does each US1 requirement (FR-004…FR-011, FR-047) map to an acceptance scenario and a task? [Traceability]
