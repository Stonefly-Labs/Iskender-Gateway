# 18 — Branch protection and repository rules

**Authority:** the required GitHub settings for `Stonefly-Labs/Iskender-Gateway`, and whether each one is applied. Requirements: FR-040, FR-041, FR-044.

Tooling does not apply these rules. The owner runs the commands below, then the table is updated with the evidence.

## Rule status

Each rule is reported on its own. A rule is **applied** only when its verification command has shown it to be in force.

Last checked 2026-11-05: R1–R4 are in force on `main`. A direct push of a test commit was rejected with `GH006: Protected branch update failed`, citing the pull request and `ci` requirements.

| # | Rule | Status | Owner | Verify |
|---|---|---|---|---|
| R1 | Pull request required; no direct push to `main` | **Applied** 2026-11-05 | Architecture (ADR) owner | `required_pull_request_reviews` is set |
| R2 | Required status check `ci`, up to date with `main` | **Applied** 2026-11-05 | Architecture (ADR) owner | `required_status_checks.contexts` contains `ci` |
| R3 | No force pushes or branch deletion | **Applied** 2026-11-05 | Architecture (ADR) owner | `allow_force_pushes.enabled` and `allow_deletions.enabled` are `false` |
| R4 | Rules also apply to administrators | **Applied** 2026-11-05 | Architecture (ADR) owner | `enforce_admins.enabled` is `true` |
| R5 | One approving review required | **Pending: cannot be met yet** | Architecture (ADR) owner | `required_approving_review_count` is `1` |
| R6 | CODEOWNERS review required | **Pending: cannot be met yet** | Architecture (ADR) owner | `require_code_owner_reviews` is `true` |
| R7 | `docs-not-required` label exists | **Applied** 2026-11-05 | Spec Kit maintainer | `gh api repos/Stonefly-Labs/Iskender-Gateway/labels/docs-not-required` succeeds |
| R8 | Pull requests merge only with a merge commit | **Applied** 2026-11-06 | Architecture (ADR) owner | `gh api repos/Stonefly-Labs/Iskender-Gateway --jq '{allow_merge_commit,allow_squash_merge,allow_rebase_merge}'` shows `true`, `false`, `false` |

Verification command for R1–R6:

```bash
gh api repos/Stonefly-Labs/Iskender-Gateway/branches/main/protection
```

## Self-approval limitation

GitHub does not count a pull request author's own approval. Every review role is held by one person, so **a required-review rule cannot be met on a pull request opened under that person's account**. With R4 on as well, such a pull request could never merge.

R5 and R6 therefore stay pending until pull requests can come from a different identity, such as a bot account or a second maintainer. Until then, applying R1–R4 gives protected `main` with passing CI, but no enforced independent review. That gap should be reported rather than described as full protection.

### Logged exception to the Principle IX gate

| Field | Value |
|---|---|
| Decision | `DEC-20261105-002` in `.iskender/state/approvals.json` |
| Principle | IX, whose gate requires required reviews to be enforced |
| Deviation | Pull requests need 0 approving reviews, so R5 and R6 stay unapplied. R1–R4 and the `ci` check still apply |
| Scope | All pull requests to `main` in this repository |
| Expiry | When pull requests can come from a second identity. R5 and R6 are then applied and this exception ends |
| Approved by | Architecture (ADR) owner, 2026-11-05 |

## Apply R1–R4

This requires pull requests with zero required approvals:

```bash
gh api --method PUT repos/Stonefly-Labs/Iskender-Gateway/branches/main/protection --input - <<'JSON'
{
  "required_status_checks": { "strict": true, "contexts": ["ci"] },
  "enforce_admins": true,
  "required_pull_request_reviews": {
    "required_approving_review_count": 0,
    "require_code_owner_reviews": false,
    "dismiss_stale_reviews": true
  },
  "restrictions": null,
  "allow_force_pushes": false,
  "allow_deletions": false
}
JSON
```

The `ci` check must have run at least once on GitHub before it can be selected in the settings UI. The API accepts the name before that, but until the check has run, a pull request cannot satisfy it.

## Apply R5 and R6

Run this once pull requests come from a different identity:

```bash
gh api --method PATCH repos/Stonefly-Labs/Iskender-Gateway/branches/main/protection/required_pull_request_reviews --input - <<'JSON'
{
  "required_approving_review_count": 1,
  "require_code_owner_reviews": true,
  "dismiss_stale_reviews": true
}
JSON
```

## Apply R7

```bash
gh label create docs-not-required --color BFD4F2 --description "CODEOWNER skip for the documentation merge gate; needs a written justification"
```

## Apply R8

```bash
gh api --method PATCH repos/Stonefly-Labs/Iskender-Gateway -F allow_merge_commit=true -F allow_squash_merge=false -F allow_rebase_merge=false
```

`/drive-speckit` resumes only when a record's `gitEvidence.commit` is HEAD or an ancestor of it (FR-022). A squash or rebase merge rewrites that commit, so the check would block once the spec's PR had merged.

If R8 is ever turned off and a spec's PR merges another way, the block is correct, and recovery is the owner's call:

1. Log an `exception` decision in `.iskender/state/approvals.json` that names the PR and the real commit it produced on `main`.
2. Set `gitEvidence.commit` in the state record to that commit, confirmed with `git merge-base --is-ancestor <hash> origin/main`.
3. Run `/drive-speckit` again.

## CI checks and provenance

The `ci` job in `.github/workflows/ci.yml` runs the 10 ordered checks. Check 10 attests the SBOM and host assembly with GitHub's attestation service. Pull requests from forks get no `id-token` permission, so they write SHA-256 checksums to the job summary instead. That is the documented local equivalent, and it is weaker than an attestation because nothing signs it.
