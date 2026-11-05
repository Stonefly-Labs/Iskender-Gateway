# 13 — Development standards and agent workflows

**Authority:** repository engineering rules, `/drive-speckit` orchestration and Roadmap Steward. Bootstrap deliverable: [14](14-spec-0000-bootstrap-brief.md). Inventory: [17](17-master-spec-inventory.md). Discovery: [README](README.md) and `../.iskender/grounding.yaml`.

## Development standards

- **Branches:** all work off protected `main`; branch names `feature/<topic>`, `chore/<topic>` or `bugfix/<topic>`. PRs require successful CI, review and documentation alignment; no direct commits to main.
- **Architecture:** modular monolith with clear dependency direction [03](03-domain-and-technical-architecture.md). Vertical slices are appropriate for cohesive feature contracts/use cases, **not** a mandate to duplicate storage/client abstractions per endpoint. Keep shared infrastructure small and contract-bound. DI/composable interfaces; no service locator or static hidden global state.
- **Code:** modern .NET/C# analyzers, nullable enabled, async/cancellation, bounded allocation on hot path, explicit errors, immutable public settings, guarded sensitive data, versioned APIs/SDKs. Avoid unreviewed reflection/dynamic code in request path.
- **Tests:** unit/property tests, integration via **Testcontainers**, architecture and provider conformance, negative auth/tenancy, security/tamper, SSE fault/chaos, load/perf. Reproducible local commands and no live secret dependencies in default CI.
- **CI:** GitHub Actions for restore, formatting, build, all tests, scan/SBOM, artifact validation and docs/link/manifest/inventory consistency; protect release packages with provenance and pinning. Owners and CODEOWNERS govern high-risk modules.
- **Design decisions:** ADRs with stable IDs and a decision ledger [16](16-architecture-decision-register.md); separate suggestions from approvals. Update architecture, requirements, docs, examples and inventory as part of the same PR. **Documentation maintenance is a mandatory merge gate.**
- **Security:** least privileges, secret scanning, dependency integrity, no untrusted plugin execution, explicit threat review of new public endpoints/identity flows/serialization.

## Source-of-truth hierarchy

1. Human-approved product decisions and later approved ADRs/roadmap changes (with history).
2. Active generated constitution `.specify/memory/constitution.md` once created; amendments require human approval and impact review.
3. Authoritative topic documents under `iskender-grounding/`; [17](17-master-spec-inventory.md) is authoritative for spec order/prompts/status.
4. Individual current Spec Kit spec/plan/tasks and actual code/tests; conflicts require documented reconciliation, **never** an agent's silent reinterpretation of approved scope.
5. Bootstrap prompts and examples are *inputs*, not substitutes for activated constitution or approved implementation.

The manifest `.iskender/grounding.yaml` is **authoritative for path discovery**, not a license to change approval precedence. Repository root is found independently of shell CWD; file paths resolve relative to that root as specified by manifest.

## `drive-speckit`: one eligible stage per invocation

The Claude Code skill `/drive-speckit` is created by **ISK-0000**, and is **not present before bootstrap**. User invokes with no stage argument; printed stage names are informational only. It discovers root, manifest/schema, inventory/status, active constitution, Spec Kit artifacts, current worktree/branch, last checkpoint and pending approval. It validates invariants; selects or resumes exactly the currently eligible **workflow stage**. Spec selection follows dependency order and approved status, never speculative CWD scanning.

### State machine and stage contract

Suggested stages: `bootstrap/detect` (preflight), `specify`, `clarify`, `plan`, `tasks`, `analyze`, `implement`, `converge`, `roadmap-review`, `pr-merge-checkpoint`. Exact Spec Kit commands are discovered from existing installation; don't assume commands not installed. A stage records entered timestamp/runner ID, source hashes, artifact paths, attempt counters, last failed evidence, work completed, Git state and proposed next stage. State is durable, versioned, small and checked into repo or persisted in a clearly documented recovery path with lock/atomic write. Worktree contention must block, not corrupt state.

**One stage per invocation:** a call can perform all work **within that stage** then stop at its boundary; e.g. *implement* processes remaining tasks until complete, blocked or user input, but does **not** launch converge on its own. A later invocation resumes converge. Stages re-entrantly resume from verified artifacts, avoiding repeated operations where evidence is already sufficient. On new spec, `specify` loads the approved **exact prompt from inventory**; do not regenerate missing prompts or silently substitute templates.

### Implement-stage bounded repair algorithm

1. Read current task ledger, working-tree diff, diagnostics, repair history and last checkpoint; prioritize incomplete/failed tasks.
2. Execute tasks sequentially/dependency-aware with tests and local feedback; keep working until **all remaining tasks finished**, user intervention, or a real blocker. Never return after each individual task by default.
3. On failure identify a **problem fingerprint** (spec/task/stage, error class, dependency context, relevant log hash) and attempt a meaningful evidence-based repair. A meaningful attempt changes a hypothesis or action and records expected observation + result. Repeating the same command unchanged does not count as progress.
4. Limit to **3 meaningful failed repairs per problem**, **10 failed repairs per invocation run**, and **2 consecutive no-progress cycles**. These are the previously agreed defaults. Persist per-problem repair count and no-progress state across invocations; do not reset counters by restarting the skill. A *new* authorized hypothesis or externally changed cause may warrant an explicit documented reset requiring bounded approval policy, not an automatic loop.
5. Persist edits, results and checkpoint atomically after each task/attempt. On blocked budget, stop and issue an actionable diagnosis and who may authorize escalation. Do not automatically skip acceptance, disable tests or alter approved requirements to make progress.
6. Finished implementation transitions to **converge eligible**, but does not run convergence itself.

### Convergence: repeated analysis with separate remediation

- A **converge stage** evaluates implementation, design constraints, security, tests and acceptance against the current spec; writes a versioned finding ledger, each with stable ID, severity and evidence.
- **Passes 1–3:** attempt to remediate **all findings including Low**. Remediation happens only after a **separate `implement` invocation**; converge itself does not modify code as its own hidden second stage.
- **Pass 4 onward:** Low-only documented findings can be explicitly accepted with evidence/owner/rationale if no remaining release-specific gate prohibits them. Medium, High, Critical **always block** and return to implement/escalation. **Zero findings** permit immediate success even on pass 1.
- Persistent failures trigger the same bounded repair/no-progress escalation; do not loop forever or **silently downgrade severity**. Track pass numbers across restarts and preserve earlier findings and repair evidence.
- After successful convergence, Roadmap Steward **review** becomes eligible as an independent stage; material proposals await human approval before authoritative edits.

### Mandatory invocation summary and blocker contract

Every call emits a concise human summary headed `drive-speckit (<stage>)` with **Spec ID and title; executed stage; work done; current status; next stage/action; and `/drive-speckit` re-invocation instruction when appropriate**. A blocked result **always** includes what stage/spec/task is blocked; precise reason; evidence/error; tried hypotheses/commands/outcomes; exact unblock steps; responsible actor; and what stage/task the skill will resume afterward. Unknown cause is labeled unknown with diagnostic commands and evidence collection, not a vague `blocked`. Preserve machine-readable state for audit and resumption.

Example **format only**, not an assertion of actual progress:

```text
drive-speckit (implement)
Spec: ISK-0048 — <title from inventory>
Completed this invocation: <task IDs + evidence>
Status: BLOCKED (task ...); reason ...; evidence/log ...
Attempted: hypothesis A ...; B ...; counts: ...
Unblock: <exact owner action and verification command>; owner: <role>
Resumes: implement task ... after evidence confirms remediation.
Run /drive-speckit again after unblock.
```

## Roadmap Steward + `/review-roadmap`

Roadmap Steward (agent) and `review-roadmap` (skill) are created in ISK-0000. They are **reviewers, not competing development orchestrators**. Run after implementation and convergence, then compare actual code/contracts/tests, approved ADRs, constitution, all remaining scopes, milestone/dependency graph, future `/speckit.specify` prompts and grounding docs. Output a structured change proposal: evidence from commits/files/test IDs, issue, affected spec IDs, exact proposed diffs, dependency/milestone impact, why it does/doesn't change approved requirements, rollback option and decision owner.

**Material changes** (scope or inclusion/exclusion; new/deleted specs; prompt intent; dependencies affecting scope, policy security boundaries, MVP ship cutline, new architectural decision, constitution changes) **must receive explicit user approval** before any authoritative file is modified. Approval records decision ID, timestamp, approved diff hash, reviewer and rationale. On rejection, leave documents unchanged, record decision and preserve history. Routine non-material docs changes that accurately reflect approved implementation are **required** before merge. Revisions/prompts of completed specs stay historically immutable; future amendments are new specs or approved superseding records.

Review is resumable across approvals and CI/PR transitions. `review-roadmap` produces proposals; it never silently writes approval-required changes. See [17](17-master-spec-inventory.md) for status and change-history conventions.

## Specialized helpers (bounded responsibility)

Recommend helpers `dotnet-gateway-reviewer` (hot-path/DI/hosting), `security-isolation-reviewer` (auth/tenancy/threat model), `provider-conformance-reviewer` (compat matrices/stream), `policy-plugin-reviewer` (SDK/manifests/locks), `test-reliability-reviewer` (fault/perf plans), `documentation-auditor` (links/cross-doc drift), `architecture-guard` (module dependencies); skills can invoke them for narrow reviews. Only **drive-speckit** orchestrates stage transitions and only Roadmap Steward proposes material roadmap diffs. No recursive agent spawning or overlapping orchestration.

## Initial bootstrap limitation

Before ISK-0000 exists, use the **currently installed** Spec Kit commands manually: inspect and preserve installation; review [15](15-constitution-prompt.md); generate active constitution; run `/speckit.specify` on ISK-0000's [approved inventory prompt](17-master-spec-inventory.md#isk-0000--repository-bootstrap--agentic-development-framework) with [14](14-spec-0000-bootstrap-brief.md) as source; follow installed Spec Kit planning/tasks/implementation commands; verify generated skill before handing control to `/drive-speckit`. **Never reinstall/reinitialize Spec Kit** as part of bootstrap. More detail [14](14-spec-0000-bootstrap-brief.md).
