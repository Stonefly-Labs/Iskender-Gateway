# Contract: CI Skeleton & Merge Gates

**Applies to**: `.github/workflows/ci.yml`, CODEOWNERS, branch-protection docs. Traces:
FR-040..FR-045, SC-010, Principles V/VIII/IX.

## Required CI checks (ordered)

| # | Check | Tool (recommendation, pin in impl) | Trace |
|---|---|---|---|
| 1 | Restore | `dotnet restore --locked-mode`, against committed `packages.lock.json` files | FR-042 |
| 2 | Format | `dotnet format --verify-no-changes` | FR-042 |
| 3 | Build | `dotnet build -warnaserror` | FR-042 |
| 4 | Test (all assemblies) | `dotnet test` | FR-042 |
| 5 | Architecture/dependency | `Iskender.Architecture.Tests` | FR-006/042 |
| 6 | Docs/link/manifest/inventory consistency, and license scan | `.github/scripts/check-grounding.sh`, `.github/scripts/check-license.sh` | FR-042, Constitution I |
| 7 | **Documentation merge gate** | `.github/scripts/docs-gate.sh`, path+label rule (below) | FR-043 |
| 8 | Secret scanning | Gitleaks | FR-042/045 |
| 9 | Dependency hygiene | `dotnet list package --vulnerable` + Dependabot | FR-042/045 |
| 10 | SBOM + provenance | CycloneDX + build-provenance attestation hook | FR-042 |

Tool names are *recommendations*, not constitutional facts (Principle IX); they are pinned during
implementation. No live cloud secrets are required (SC-001). Every check is a **real executing step**
— in particular, check 10 MUST emit an SBOM artifact and a build-provenance attestation (or a
documented local equivalent where a hosted attestation service is unavailable), not a placeholder.

## Documentation merge gate (deterministic)

Behavior-bearing paths requiring a concurrent **matching documentation change** in the **same PR**:

- `src/Iskender.Core.Contracts/**`, `src/Iskender.Providers.Abstractions/**`, `src/Iskender.Policy.Sdk/**` (public contract/SDK surfaces)
- `src/Iskender.Gateway.Host/**` (host/config)
- `.github/workflows/**`, `.github/scripts/**` (CI)
- `.iskender/grounding.yaml` (grounding manifest)

A **matching documentation change** = ≥1 added/modified file in the PR under `docs/**` **or** a root
onboarding doc (`README.md`/`CLAUDE.md`). Deleting a doc, or renaming one without changing it, does not
count. A changed behavior-bearing path with no such file blocks.

**The inventory**: `docs/17-master-spec-inventory.md` is its own documentation, so a PR that changes only
that file is not blocked. Its status values change on every spec PR, so it does not count as the matching
documentation change for any other path.

**Skip**: a `docs-not-required` label is honored **only** when (a) the text under a
`## Docs-not-required justification` heading in the PR description is non-empty, ignoring HTML comments
and whitespace — label present with empty justification **fails** the gate — and (b) its most recent
application, read from the PR's label events, was by a CODEOWNER of every affected path. A label that
meets neither condition fails the gate even if the PR also has a matching documentation change. Pure
refactor/test-only changes **outside** those paths are never blocked (FR-043).

## Releases (FR-041a)

`.github/workflows/release.yml` runs on a version tag. It rejects a malformed tag or one not on `main`,
then restores, builds, and tests with the version from the tag, packs the projects marked packable,
generates an SBOM, attests both, and publishes a GitHub release with them attached. A tag with a
prerelease suffix publishes a prerelease. Nothing is pushed to a NuGet feed.

Current tag rule: `v0.0.<spec number>-alpha`, set by `/drive-speckit` on the merge commit of the spec's PR: its latest post-merge PR if it has one, or otherwise its feature PR.
The rule lives in the skill and `CONTRIBUTING.md`, so it can change without changing the workflow.

## Protected `main` & branch rules

- All work on `feature/`, `chore/`, or `bugfix/` branches; direct pushes to `main` prohibited;
  PRs require passing CI + review (FR-040).
- Commit as each task is implemented, push after every commit, open the PR right after the first
  push, and keep its notes current and complete before merge (FR-040a).
- Provide IaC-compatible docs + exact `gh api` commands for required reviews/checks, CODEOWNERS,
  PR/issue templates, SECURITY policy, release/versioning conventions (FR-041).
- **Where admin rights/tokens are absent**: document the exact required action, owner, and
  verification command as **pending**; perform **no** remote mutation and never report the rule as
  applied (FR-044, SC-010).
- **Self-approval**: GitHub does not count a PR author's own approval, so a required-review rule cannot
  be met on a PR opened under the only approver's account. The docs state this, and the rule is
  reported per the next item until it can be met (FR-041).
- **Per-rule reporting**: where admin rights cover some rules but not others, each rule is reported
  individually as **applied** (with verification evidence) or **pending**; a partial set is never
  summarized as fully applied (FR-044).
