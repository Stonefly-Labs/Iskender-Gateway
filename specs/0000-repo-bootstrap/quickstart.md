# Quickstart: ISK-0000 Validation Guide

Runnable scenarios that prove the bootstrap works end-to-end. Each maps to spec acceptance criteria.
This guide validates; it does not contain implementation code (see `tasks.md` + implementation).

## Prerequisites

- .NET SDK 10 (version pinned by `global.json`)
- Git 2.40+ (worktree support)
- PowerShell 7+ (existing Spec Kit scripts)
- **No cloud credentials** — clean-clone validation must succeed offline

## Scenario 1 — Clean-clone compilable skeleton (US1, SC-001/002)

```bash
git clone <repo> fresh && cd fresh
dotnet restore
dotnet build -warnaserror
dotnet test
```

**Expected**: all steps succeed; no cloud provisioning attempted; every skeleton project compiles
with no gateway/provider/policy/DB business logic. Confirm `.specify/` is byte-for-byte unchanged
(`git status --porcelain .specify` is empty) and README, `CLAUDE.md`, and `assets/iskender-logo.svg`
exist and are valid (SVG has `<title>`/`<desc>`).

## Scenario 2 — Manifest validation, fail-closed (US3, SC-003)

```bash
dotnet run --project tools/Iskender.Workflow.Cli -- validate-manifest
# → exit 0 for the supplied .iskender/grounding.yaml
```

Then run against fixtures in `tests/Iskender.Workflow.Tests` (unsupported `schemaVersion`, missing
key, absent target file, `..` traversal, absolute path). **Expected**: each blocks non-zero with the
exact offending key/value + owner action; zero proceed silently. Covered by `dotnet test`.

## Scenario 3 — Verbatim approved-prompt retrieval (US3, SC-004)

```bash
dotnet run --project tools/Iskender.Workflow.Cli -- get-prompt --spec 0000
```

**Expected**: output is byte-for-byte identical to the fenced `/speckit.specify` prompt under the
`ISK-0000` heading in `docs/17-master-spec-inventory.md`. A missing-prompt fixture **blocks** with no
synthesized prompt.

## Scenario 4 — One stage per invocation + resume (US2, SC-005)

Using the mock Spec Kit fixtures in `tests/Iskender.Workflow.Tests`:

1. Drive a sequence of stages; confirm each invocation advances **exactly one** eligible stage in
   dependency order.
2. Confirm `implement` works all remaining tasks and then **stops before `converge`**.
3. Interrupt mid-stage, re-invoke; confirm the same stage resumes from the last verified checkpoint
   with **zero** repeated completed work and **zero** counter reset.
4. Simulate a second concurrent driver; confirm it **blocks** on the lock rather than corrupting
   state. Covered by `dotnet test`.

## Scenario 5 — Repair budgets (US4, SC-006)

Synthetic looping/failing fixtures assert the exact limits: **3** meaningful failed repairs/problem,
**10** failed repairs/run, **2** consecutive no-progress cycles — and that counters **persist across
a simulated restart** (not reset). An unchanged re-run does not count as progress. Covered by
`dotnet test`.

## Scenario 6 — Convergence severity governance (US4, SC-007)

Drive the ledger through passes 1–4 with mixed-severity fixtures:

- Passes 1–3: all findings (incl. Low) scheduled; remediation only via a **separate** `implement`;
  `converge` makes no code edits.
- Pass 4+: Low-only accepted with recorded owner/rationale.
- Any Medium+ blocks on any pass; zero findings succeed immediately even on pass 1; no silent
  downgrade. Covered by `dotnet test`.

## Scenario 7 — Summary & blocker contract (SC-008)

Assert every driver invocation emits the `drive-speckit (<stage>)` summary with all required fields,
and every blocked path includes the full blocker contract (stage/spec/task, reason, evidence,
attempts, unblock steps, owner, resume point; unknown-cause labeled with diagnostics). See
`contracts/blocker-and-summary.contract.md`.

## Scenario 8 — Roadmap review, human-governed (US5, SC-009)

Run `/review-roadmap` against a drift fixture. **Expected**: a structured, evidence-backed,
**diff-only** proposal; material changes **block pending approval** (no authoritative file changes);
approving records decision ID/timestamp/diff-hash/reviewer/rationale; rejecting leaves files
unchanged and preserves completed-spec history.

## Scenario 9 — Protected collaboration, CI & docs gate (US6, SC-010)

- Attempt a direct push to `main` → rejected (or documented **pending** with owner + verification
  command where admin rights are absent; never falsely reported as applied).
- Open a PR touching a behavior-bearing path without a `docs/**` change → the documentation merge
  gate **blocks** it (unless `docs-not-required` + justification).
- Run `ci.yml` on a sample change → every check (restore, format, build, test, architecture,
  docs/manifest/inventory, secret scan, dependency hygiene, SBOM/provenance) executes.

## Scenario 10 — Active constitution precedence (SC-011)

Confirm that with both `docs/15-constitution-prompt.md` and `.specify/memory/constitution.md`
present, governance resolution treats `.specify/memory/constitution.md` as authoritative, and it is
never overwritten without a recorded human approval.
