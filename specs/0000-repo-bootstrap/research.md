# Phase 0 Research: ISK-0000 Repository Bootstrap

All spec clarifications (Session 2026-11-05) were already resolved in `spec.md` (canonical stage
set, docs-merge-gate rule, `schemaVersion` acceptance rule). The decisions below resolve the
remaining planning unknowns. No `NEEDS CLARIFICATION` markers remain in Technical Context.

---

## D1 — Workflow logic lives in plain Claude Code skills

**Decision**: `/drive-speckit` and `/review-roadmap` are plain Claude Code skills. Their rules are
written in the skill text, their state is plain JSON files, and they automate the installed Spec Kit
skills. There is no CLI, compiled library, or unit-test suite behind them; they are verified by use.

**Rationale**: This is what the workflow needs, and the maintainer has built `drive-speckit` this way
on other projects. A skill can read and write JSON, run Git, and write a temp file and rename it.

**Alternatives considered**: A .NET library + CLI for unit-testable rules (the original decision here).
Rejected by the maintainer on 2026-11-05 as unnecessary.

---

## D2 — Repository-root resolution

**Decision**: Resolve root by walking upward from the process CWD to the first directory containing
both a `.git` entry (directory **or** gitfile, to support worktrees) and `.iskender/grounding.yaml`;
fall back to `git rev-parse --show-toplevel` when available. Fail with an actionable error if no
marker is found.

**Rationale**: FR-012 requires robustness from nested directories and Git worktrees, independent of
shell CWD. Requiring *both* markers avoids resolving to an unrelated parent repo. The gitfile case
(`.git` as a file pointing at the worktree) is what makes worktrees work.

**Alternatives considered**: CWD-relative paths (fails FR-012); environment variable only (fragile,
not present on clean clone). Rejected.

---

## D3 — Grounding manifest validation & `schemaVersion`

**Decision**: `.github/scripts/check-grounding.sh` validates `.iskender/grounding.yaml`, and both the
skill and CI run it. Validate:
single-integer `schemaVersion` equals a supported major (currently `1`); all required keys present
(`grounding.root`, `grounding.inventory`, `grounding.constitutionPrompt`, `grounding.bootstrapBrief`,
`grounding.decisionRegister`, `speckit.constitution`, `speckit.specificationsRoot`,
`workflow.initialSpec`); `grounding.root` resolves under repo root; every `grounding.*` doc path
resolves under `grounding.root`; `speckit.*` paths resolve under repo root; every referenced target
file exists. Unknown keys are tolerated (warn, not fail). **Fail closed** on any unsupported major,
missing key, absent/unparseable manifest, absent/empty/unreadable target file, path traversal (`..`),
disallowed absolute path, or **symlink whose
resolved (real) path escapes the repository root** — emitting the exact offending key/value and owner
instruction. Path-safety checks evaluate the fully resolved real path (symlinks expanded) before the
under-root containment test, so an in-repo symlink pointing outside the root is rejected as unsafe.

**Rationale**: Directly encodes FR-013/FR-014/FR-015 and the clarified `schemaVersion` rule
(integer major; tolerant additive parsing; reserve major bump for breaking changes).

**Alternatives considered**: SemVer string `schemaVersion` (contradicts the clarification); strict
reject-on-unknown-key (breaks additive compatibility). Rejected.

---

## D4 — Verbatim approved-prompt retrieval

**Decision**: Locate the `ISK-xxxx` entry in `docs/17-master-spec-inventory.md` by its literal
heading anchor, then extract the content of the fenced code block labeled as the approved
`/speckit.specify` prompt **byte-for-byte** (no trimming, no re-wrapping). If the heading or fenced
prompt is absent, block — never synthesize.

**Rationale**: FR-016/FR-017, SC-004. Byte-for-byte fidelity is the trust anchor; retrieval keys off
structure (heading + fence label), not the human title.

**Byte-for-byte reference**: the comparison is against the fenced block body as stored on disk after
checkout. Because `.gitattributes` normalizes source/Markdown to LF, both the inventory file and any
comparison are subject to the same normalization, so the guarantee is defined over post-checkout
bytes (UTF-8, LF). The extractor returns the interior bytes verbatim — no trim, re-wrap, or
trailing-newline change — and strips only the fence delimiter lines.

**Alternatives considered**: Regenerating a prompt from the title (explicitly forbidden); fuzzy
heading match (risks wrong entry). Rejected.

---

## D5 — Workflow state: storage, atomicity, locking

**Decision**: One JSON record per spec at `.iskender/state/<specId>.state.json`, committed to the
repo (durable + auditable). Each record carries a `stateVersion` integer. Writes are atomic: write
to `<specId>.state.json.tmp`, `fsync`, then rename over the target. Concurrency is guarded by an
advisory lock file `.iskender/state/<specId>.lock` containing the runner ID + timestamp; a second
driver that cannot acquire the lock **blocks with a contention error** rather than writing.

**Lock semantics**: contention is **block-and-exit** (report owner + verification steps and stop) —
never wait/spin/steal. The lock file records `runnerId` (`host:user:startedAtUtc`) + an ISO-8601
timestamp, which the owner refreshes on every state write and before long-running steps. A skill has
no long-lived process whose liveness could be checked, so a lock is **stale and reclaimable** when the
recorded runner is on this host **and** the timestamp is more than 15 minutes old; reclaiming writes
a record of the reclaim. A crash between the atomic state write and lock release
leaves committed state intact and the lock reclaimable under the stale rule, so completed work is
never redone. With one worktree and one implementer, the lock stays in the worktree; cross-worktree
contention is out of scope.

**State versioning**: each record carries an integer `stateVersion`. A record newer than the engine
supports fails closed (block + owner action); older supported versions are read without loss. The
`.iskender/state/` directory and its `*.state.json`/`*.converge.json`/`approvals.json` records are
**committed** (durable + auditable) and MUST NOT be excluded by `.gitignore`. The transient `*.lock`
and `*.tmp` files are ignored so a lock can never be committed.

**Rationale**: FR-021/FR-022, Principle X/XI. Rename-over is atomic on NTFS and POSIX; committing
state gives tamper-evident audit history with real Git evidence. Advisory lock satisfies the
"block, not corrupt" contention requirement (US2 scenario 4).

**Alternatives considered**: Database/Redis (violates no-cloud/offline constraint and adds runtime
deps); in-place mutation (non-atomic, corrupts on crash); OS mandatory file locks (poor
cross-platform behavior). Rejected.

---

## D6 — Bounded repair budgets & persistence

**Decision**: Model three counters in the state record — per-problem meaningful-failure count keyed
by a **problem fingerprint** (specId/taskId/stage + error class + dependency context + log hash),
per-run failed-repair count, and consecutive no-progress count. A repair counts only if it changes
hypothesis or action and records an expected observation + result; an unchanged re-run is "no
progress". Limits: **3** meaningful failures/problem, **10** failed repairs/run, **2** consecutive
no-progress cycles. Counters persist in the committed state and are **never** reset by a
process/skill restart; a reset requires a recorded, approval-bounded new-hypothesis entry authorized
by the Spec Kit maintainer (Principle X owner).

**Mechanical classification**: an attempt is **meaningful** only if its normalized command/action
differs from the immediately preceding attempt for the same problem *and* it records an
expected-observation; an unchanged normalized invocation is **no-progress**. One no-progress cycle =
one no-progress attempt; `consecutiveNoProgress` increments per such attempt and resets on the next
meaningful one. Fingerprint normalization: `errorClass` is the tool/exit-code class (not the full
message), `depContext` is the resolved dependency/toolchain identifiers in play, and `logHash` is a
hash over the **normalized** failing-output lines (volatile tokens — timestamps, paths, PIDs —
stripped) so the same recurring failure hashes stably across runs.

**Rationale**: FR-025/FR-026/FR-027, Principle X, SC-006. Fingerprinting is what distinguishes
"meaningful" repairs from loops.

**Alternatives considered**: In-memory counters (reset on restart — forbidden); time-based budgets
(non-deterministic, untestable). Rejected.

---

## D7 — Convergence ledger & severity governance

**Decision**: A versioned finding ledger in a sibling file
`.iskender/state/<specId>.converge.json` (referenced from the state record via `convergeLedgerRef`), each finding with stable ID, severity
(Low/Medium/High/Critical), evidence, pass number, and status. `converge` only **evaluates and
records**; it never edits code. Severity rules: passes 1–3 schedule remediation of *all* findings
(remediation happens in a *separate* `implement` invocation); pass 4+ may accept **Low-only**
findings with recorded evidence/owner/rationale; any Medium+ on any pass blocks; zero findings
succeed immediately even on pass 1; severities are never silently downgraded: each finding carries an
append-only `severityHistory`, and a lower severity without a recorded reason from the Spec Kit
maintainer is rejected. Roadmap Steward becomes eligible only after successful convergence and is
never auto-run.

**Rationale**: FR-029..FR-033, Principle X, SC-007.

**Alternatives considered**: Letting `converge` auto-remediate (forbidden hidden second stage);
auto-accepting Low on pass 1 (violates the pass-4 rule). Rejected.

---

## D8 — Architecture / reference-direction enforcement

**Decision**: `tests/Iskender.Architecture.Tests` uses `NetArchTest.Rules` to assert the approved
dependency direction (e.g., `Core.Contracts` depends on nothing internal; `Domain` →
`Core.Contracts`; `Infrastructure` → `Domain`/`Contracts`; `Providers.Abstractions` →
`Core.Contracts`; `Host` composes but is referenced by none; no project references `Host`). The same
assertions run in CI as a required check. Exact edges are codified in
`contracts/module-dependency-rules.md`.

**Project-graph check**: `NetArchTest` alone would pass without checking anything on this skeleton.
The compiler drops references an assembly does not use, so a probe of an empty project with a
`ProjectReference` produced an assembly with zero types and no reference to its dependency. The
architecture tests therefore also parse each `.csproj`'s `ProjectReference` graph, and a test proves
a deliberate violation fails. Type-level `NetArchTest` assertions stay in place for when real types
arrive.

**Rationale**: FR-006, Principle II gate. A test-based guard is reviewable and runs offline.

**Alternatives considered**: Hand-maintained diagrams only (not enforced); MSBuild
`ProjectReference` bans only (misses transitive/namespace rules). NetArchTest chosen; a custom
reflection check is an acceptable fallback if the package is unavailable.

---

## D9 — CI skeleton, secret scanning, SBOM/provenance

**Decision**: Single GitHub Actions workflow `ci.yml` with ordered jobs: restore → `dotnet format
--verify-no-changes` → build (`-warnaserror`) → `dotnet test` (all assemblies) → architecture tests
→ docs/link/manifest/inventory consistency (`.github/scripts/check-grounding.sh`) → **documentation
merge gate** (path+label rule, `.github/scripts/docs-gate.sh`) → secret scanning
(Gitleaks) → dependency hygiene (`dotnet list package --vulnerable`/Dependabot) → SBOM generation
(`dotnet CycloneDX`) + build-provenance attestation hook. CODEOWNERS gates high-risk paths. Every
job is a real executing step: the SBOM/provenance job MUST actually emit an SBOM artifact and a
build-provenance attestation (or a documented local equivalent where a hosted attestation service is
absent), not a placeholder comment.

**Rationale**: FR-042/FR-043/FR-045, Principle IX. Tools are OSS and run without cloud secrets.

**Alternatives considered**: Multiple fragmented workflows (harder to gate); paid scanners
(unnecessary, non-portable). Rejected. Tool *names* are recommendations to be pinned in
implementation, not constitutional facts (Principle IX).

---

## D10 — Documentation merge gate (path + label)

**Decision**: Implement as a CI job + CODEOWNERS. Behavior-bearing paths —
`src/Iskender.Core.Contracts/**`, `src/Iskender.Providers.Abstractions/**`, `src/Iskender.Policy.Sdk/**`
(public contract/SDK surfaces), `src/Iskender.Gateway.Host/**` (config/host), `.github/workflows/**`,
and `.iskender/grounding.yaml` / `docs/17-master-spec-inventory.md` — require a matching
documentation change in the same PR, unless a `docs-not-required` label carrying written
justification is present. A **matching documentation change** is deterministically defined as ≥1
added/modified file in the PR under `docs/**` **or** a root onboarding doc (`README.md`/`CLAUDE.md`).
The justification is the text under a `## Docs-not-required justification` heading in the PR
description, because a label cannot hold text. The skip label is honored only when that text is
non-empty (label present + empty justification → the gate fails) and only when its most recent
application, read from the PR's label events, was by a CODEOWNER of every affected path. Pure
refactor/test-only changes outside the behavior-bearing paths are not blocked.

`Providers.Abstractions` is included because it is a public abstraction surface in the same class as
`Core.Contracts`/`Policy.Sdk`; its earlier omission was an oversight, not a scope decision.

**Rationale**: Encodes the clarified gate rule and FR-043, Principle IX.

**Alternatives considered**: Reviewer-discretion only (not deterministic); block-everything
(blocks test-only PRs — rejected).

---

## D11 — Protected main where admin rights may be absent

**Decision**: Provide branch-protection as IaC-compatible documentation (`docs/` +
`CONTRIBUTING.md`) plus the exact `gh api` commands to apply required reviews, required status
checks, and no-direct-push. When admin permissions/tokens are unavailable, the driver/report marks
the rule **pending** with owner + verification command and performs **no** remote mutation. Rules are
reported **per-rule**, not in aggregate: where admin rights cover some rules but not others, each is
independently marked **applied** (with verification evidence) or **pending**, and a partial set is
never summarized as fully applied.

**Rationale**: FR-040/FR-041/FR-044, Principle IX, SC-010. Prevents falsely reporting remote rules
as applied.

**Alternatives considered**: Silent `gh` mutation during bootstrap (forbidden without authorized
tooling). Rejected.

---

## D12 — Original kebab-inspired SVG logo

**Decision**: Hand-author `assets/iskender-logo.svg` from original vector paths (stylized
skewer + kebab layers motif), self-contained (no external refs/fonts/raster), with `<title>` and
`<desc>` for accessibility and a `role="img"`/`aria` hook. Not derived from trademarked or
downloaded artwork. The palette MUST stay legible on both light and dark backgrounds — achieved via
`currentColor` or an explicit stroke/contrast treatment (or a declared safe background) so it renders
acceptably in common light/dark README and host themes.

**Rationale**: FR-011, US1 scenario 4.

**Alternatives considered**: Downloaded stock / AI-raster-then-trace (provenance risk — forbidden).
Rejected.

---

## D13 — .NET SDK & dependency pinning

**Decision**: `global.json` pins the .NET 10 SDK; `Directory.Packages.props` enables Central Package
Management so every NuGet version is pinned in one place; `Directory.Build.props` enables
`Nullable`, analyzers, `TreatWarningsAsErrors`, deterministic builds, `RestorePackagesWithLockFile`
(committed `packages.lock.json` files back CI's locked restore), and the shared `Iskender.*`
namespace root. `.editorconfig` governs C#/Markdown/JSON/YAML; `.gitattributes` forces LF for
source and normalizes line endings deterministically. The solution stays `Iskender.sln`, created with
`dotnet new sln --format sln` because SDK 10 defaults to `.slnx`. `global.json` selects
Microsoft.Testing.Platform as the test runner, which `xunit.v3` requires for `dotnet test` on SDK 10.

**Rationale**: FR-004/FR-008, Principles VIII/IX (reproducible builds, supply-chain hygiene).

**Alternatives considered**: Per-project `PackageReference` versions (drift-prone). Rejected.
