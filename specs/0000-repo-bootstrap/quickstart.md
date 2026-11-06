# Quickstart: ISK-0000 Validation Guide

Scenarios that show the bootstrap works. Scenarios 1–3 and 10 are runnable commands. The workflow
skills are plain skills verified by use (FR-003a), so scenarios 4–8 describe what to expect when they
run on a real spec. Scenario 9 needs a GitHub PR.

## Prerequisites

- .NET SDK 10 (version pinned by `global.json`)
- Git 2.40+
- Bash (Git Bash on Windows) for the check scripts
- PowerShell 7+ (existing Spec Kit scripts)
- **No cloud credentials** — clean-clone validation must succeed without them. "Offline" follows
  FR-007: `dotnet restore` may reach a public NuGet feed without credentials or use a primed cache;
  build and test need no network afterward

## Scenario 1 — Clean-clone compilable skeleton (US1, SC-001/002)

```bash
git clone <repo> fresh && cd fresh
dotnet restore
dotnet build -warnaserror
dotnet test
```

**Expected**: all steps succeed; no cloud provisioning attempted (no build, test, or CI step invokes
`az`, `azd`, `terraform`, `bicep`, or `pulumi`, or creates a cloud resource); every skeleton project
compiles with no gateway/provider/policy/DB business logic. Confirm `.specify/` is byte-for-byte
unchanged (`git diff --exit-code <baseline> -- .specify`, using the baseline commit recorded in
`specs/0000-repo-bootstrap/speckit-preservation.md`) and README, `CLAUDE.md`, and
`assets/iskender-logo.svg` exist and are valid (SVG has `<title>`/`<desc>`).

## Scenario 2 — Manifest validation, fail-closed (US3, SC-003)

```bash
bash .github/scripts/check-grounding.sh
```

**Expected**: exit 0 on the supplied manifest. Point it at a copy of the repo with a broken manifest
(absent, unparseable, unsupported `schemaVersion`, missing key, invalid `grounding.root`,
absent/empty target file, out-of-root `speckit.*` path, `..` traversal, absolute path, symlink escape)
and each case exits non-zero naming the rule, the offending key/value, and the owner action.

## Scenario 3 — Verbatim approved-prompt retrieval (US3, SC-004)

Run the prompt-extraction command from `.claude/skills/drive-speckit/SKILL.md` for `ISK-0000`.

**Expected**: output is byte-for-byte identical to the fenced `/speckit.specify` prompt under the
`ISK-0000` heading in `docs/17-master-spec-inventory.md`. An entry with no fenced prompt produces no
output, and the skill blocks with no synthesized prompt.

## Scenario 4 — One stage per invocation + resume (US2, SC-005)

Run `/drive-speckit` repeatedly on a real spec. Each invocation advances **exactly one** eligible stage
in dependency order; `implement` works all remaining tasks and then **stops before `converge`**;
re-invoking after an interruption resumes the same stage with **zero** repeated completed work and
**zero** counter reset; a second concurrent invocation **blocks** on the lock.

## Scenario 5 — Repair budgets (US4, SC-006)

The skill stops at **3** meaningful failed repairs/problem, **10** failed repairs/run, and **2**
consecutive no-progress cycles, with the counts in the state record surviving a restart. An unchanged
re-run does not count as progress.

## Scenario 6 — Convergence severity governance (US4, SC-007)

- Passes 1–3: all findings (incl. Low) scheduled; remediation only via a **separate** `implement`;
  `converge` makes no code edits.
- Pass 4+: Low-only accepted with recorded owner/rationale.
- Any Medium+ blocks on any pass; zero findings succeed immediately even on pass 1; no silent
  downgrade.

## Scenario 7 — Summary & blocker contract (SC-008)

Every invocation emits the `drive-speckit (<stage>)` summary with all required fields, and every blocked
result includes the full blocker contract (stage/spec/task, reason, evidence, attempts, unblock steps,
owner, resume point; unknown-cause labeled with diagnostics). See
`contracts/blocker-and-summary.contract.md`.

## Scenario 8 — Roadmap review, human-governed (US5, SC-009)

Run `/review-roadmap` after a real convergence. **Expected**: a structured, evidence-backed,
**diff-only** proposal; material changes **block pending approval** (no authoritative file changes);
approving records decision ID/proposal ID/timestamp/diff-hash/reviewer/rationale; rejecting leaves
files unchanged and preserves completed-spec history. Re-invoking after an approval reloads the stored
proposal and applies it only if the hash matches and the diff still applies cleanly.

## Scenario 9 — Protected collaboration, CI & docs gate (US6, SC-010)

- Attempt a direct push to `main` → rejected (or documented **pending** with owner + verification
  command where a rule is not applied; never falsely reported as applied).
- Open a PR touching a behavior-bearing path without a `docs/**` change → the documentation merge
  gate **blocks** it (unless `docs-not-required` was applied by a CODEOWNER and the PR description has
  a non-empty `## Docs-not-required justification` section).
- Run `ci.yml` on a sample change → every check (restore, format, build, test, architecture,
  docs/manifest/inventory, docs gate, secret scan, dependency hygiene, SBOM/provenance) executes.

## Scenario 10 — Active constitution precedence (SC-011)

Confirm that with both `docs/15-constitution-prompt.md` and `.specify/memory/constitution.md`
present, governance resolution treats `.specify/memory/constitution.md` as authoritative, and it is
never overwritten without a recorded human approval.
