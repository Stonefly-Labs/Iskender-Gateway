# Contract: CI Skeleton & Merge Gates

**Applies to**: `.github/workflows/ci.yml`, CODEOWNERS, branch-protection docs. Traces:
FR-040..FR-045, SC-010, Principles V/VIII/IX.

## Required CI checks (ordered)

| # | Check | Tool (recommendation, pin in impl) | Trace |
|---|---|---|---|
| 1 | Restore | `dotnet restore` (locked) | FR-042 |
| 2 | Format | `dotnet format --verify-no-changes` | FR-042 |
| 3 | Build | `dotnet build -warnaserror` | FR-042 |
| 4 | Test (all assemblies) | `dotnet test` | FR-042 |
| 5 | Architecture/dependency | `Iskender.Architecture.Tests` | FR-006/042 |
| 6 | Docs/link/manifest/inventory consistency | `Iskender.Workflow.Cli validate-manifest` + link/inventory check | FR-042 |
| 7 | **Documentation merge gate** | path+label rule (below) | FR-043 |
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
- `.github/workflows/**` (CI)
- `.iskender/grounding.yaml`, `docs/17-master-spec-inventory.md` (grounding manifest/inventory)

A **matching documentation change** = ≥1 added/modified file in the PR under `docs/**` **or** a root
onboarding doc (`README.md`/`CLAUDE.md`). A changed behavior-bearing path with no such file blocks.

**Skip**: a `docs-not-required` label is honored **only** when (a) its justification is non-empty —
label present with empty justification **fails** the gate — and (b) it is applied by a CODEOWNER of
the affected path. Pure refactor/test-only changes **outside** those paths are never blocked (FR-043).

## Protected `main` & branch rules

- All work on `feature/`, `chore/`, or `bugfix/` branches; direct pushes to `main` prohibited;
  PRs require passing CI + review (FR-040).
- Provide IaC-compatible docs + exact `gh api` commands for required reviews/checks, CODEOWNERS,
  PR/issue templates, SECURITY policy, release/versioning conventions (FR-041).
- **Where admin rights/tokens are absent**: document the exact required action, owner, and
  verification command as **pending**; perform **no** remote mutation and never report the rule as
  applied (FR-044, SC-010).
- **Per-rule reporting**: where admin rights cover some rules but not others, each rule is reported
  individually as **applied** (with verification evidence) or **pending**; a partial set is never
  summarized as fully applied (FR-044).
