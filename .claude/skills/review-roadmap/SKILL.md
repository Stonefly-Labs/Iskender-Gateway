---
name: "review-roadmap"
description: "Review a converged Iskender spec against the roadmap and produce evidence-backed, diff-only change proposals gated on human approval. Use when the user runs /review-roadmap or when drive-speckit reaches roadmap-review."
compatibility: "Requires the Iskender grounding manifest"
user-invocable: true
disable-model-invocation: false
---

# review-roadmap

Compare what a converged spec actually built against the roadmap, then propose exact diffs. This skill is a reviewer:
it never moves a spec between stages, and it never silently writes a change that needs approval.

Run `bash .github/scripts/check-grounding.sh` first, and take every path from `.iskender/grounding.yaml`. If it
fails, stop and report the failure. Run this only after successful convergence: the spec's
`.iskender/state/<specId>.converge.json` must exist with no open, scheduled, or Medium-or-above finding.

## 1. Resume pending work first

Read `.iskender/state/proposals/` and `.iskender/state/approvals.json`. For each proposal that has not been applied or
rejected:

- **No decision yet**: ask its owner for one (section 4). Do not review again or draft a duplicate.
- **Approved**: apply it (section 5).
- **Rejected**: leave the files as they are.

## 2. Review

Use the `roadmap-steward` agent for the comparison, giving it the spec ID. Ask it to compare code, contracts, and tests
with the approved ADRs, the active constitution, the remaining specs and their prompts, the milestone and dependency
graph, and the grounding docs. It returns proposals only, and it writes nothing.

Reject a proposal that has no evidence, or that describes a change in prose with no exact diff.

## 3. Classify and store

A change is **material** if it alters any of: scope, inclusion or exclusion, new or deleted specs, approved prompt
intent, security boundaries, milestones (including the MVP cutline), a new architectural decision, or the constitution.

**Authoritative files**: `.specify/memory/constitution.md`, `docs/17-master-spec-inventory.md` (including approved
prompts), `docs/16-architecture-decision-register.md`, `.iskender/grounding.yaml`, and any committed spec's `spec.md` or
`plan.md`.

These are **not** gated unless they alter a material category: a Spec Kit stage's own edits to the in-progress spec's
`spec.md` or `plan.md` before its PR merges, and inventory status and merge-commit updates that record real evidence.

- A proposal with **any** material change is material as a whole.
- Purely non-material changes go in their **own** separate proposal, so routine doc sync is never bundled with a
  material change.

Store each proposal atomically at `.iskender/state/proposals/<proposalId>.json`, writing a `.tmp` file and then renaming it:

```json
{
  "proposalId": "RC-YYYYMMDD-NNN",
  "specId": "0000",
  "classification": "material",
  "evidence": ["commit, file, or test ID"],
  "issue": "what is wrong",
  "affectedSpecIds": ["ISK-0001"],
  "diff": "exact unified diff, against named files",
  "diffHash": "sha256 of the exact diff bytes",
  "dependencyMilestoneImpact": "required",
  "changesApprovedRequirements": true,
  "rollbackOption": "required",
  "decisionOwner": "role from the constitution's review owners table",
  "appliedAt": null
}
```

Compute `diffHash` over the exact diff bytes, with no normalization. Write the `diff` value to a temp file, then run
`git apply --check` on it and `sha256sum` it. A diff that does not apply cleanly is not a proposal.

Non-material proposals that only describe already-approved, already-implemented behavior are required before merge.
Apply them under section 5 with no approval entry.

## 4. Decide (material proposals)

Show the owner the exact diff and hash, and stop. **Change no authoritative file until a decision is recorded.** When
drive-speckit called this skill, report `decisionOwner` so that it can block.

The decision belongs to the human review owner of each affected principle, in the constitution's "Quality Gates &
Review Owners" table. If several principles are affected, each owner must approve. Role holders and delegates are in
`CONTRIBUTING.md`. Never approve on anyone's behalf, and never treat silence as approval.

Append the decision to `.iskender/state/approvals.json`, atomically, **before** changing any file:

```json
{
  "decisions": [
    {
      "decisionId": "DEC-YYYYMMDD-NNN",
      "proposalId": "RC-YYYYMMDD-NNN",
      "timestamp": "2026-11-05T12:00:00Z",
      "approvedDiffHash": "sha256 of the exact diff the reviewer saw",
      "reviewer": "GitHub login",
      "rationale": "why",
      "outcome": "approved"
    }
  ]
}
```

**On rejection**: record it with `outcome: rejected`, and leave the authoritative files unchanged. Preserve completed-spec
history, including records, revision IDs, and real merge hashes.

## 5. Apply

Apply a proposal only if **all** of these hold. Otherwise block and write nothing:

1. The SHA-256 of its stored diff equals the recorded `approvedDiffHash`. Skip this for non-material proposals.
2. `git apply --check` passes against the current tree.

Then run `git apply` and set `appliedAt`. For a material change to the inventory, append an `RC-YYYYMMDD-NNN` row to its roadmap change history,
as part of the approved diff. Prompts and revisions of completed specs stay immutable: amendments become new specs or
approved superseding records.

## 6. Report

List each proposal with its ID, classification, owner, and state: pending, applied, or rejected. Then say what the owner must do
next, if anything.
