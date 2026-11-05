# Phase 1 Data Model: ISK-0000

These are the durable/file-backed entities the workflow core manages. There is no database; every
entity is a file (JSON/YAML/Markdown) under the repository. Field-level JSON schemas live in
[`contracts/`](./contracts/). Validation rules trace to the FRs noted in parentheses.

---

## 1. Grounding Manifest (`.iskender/grounding.yaml`) — *read-only, preserved*

| Field | Type | Rules |
|---|---|---|
| `schemaVersion` | int | MUST equal a supported major (currently `1`); else fail closed (FR-013). |
| `grounding.root` | path | Resolves under repo root; no traversal/absolute escape (FR-014). |
| `grounding.inventory` | path | Relative to `grounding.root`; target MUST exist. |
| `grounding.constitutionPrompt` | path | Relative to `grounding.root`; target MUST exist (bootstrap input only — FR-018). |
| `grounding.bootstrapBrief` | path | Relative to `grounding.root`; target MUST exist. |
| `grounding.decisionRegister` | path | Relative to `grounding.root`; target MUST exist. |
| `speckit.constitution` | path | Relative to repo root; the authoritative constitution once present. |
| `speckit.specificationsRoot` | path | Relative to repo root; holds `specs/`. |
| `workflow.initialSpec` | string | Spec ID of the first spec (`'0000'`). |
| *(unknown keys)* | any | Tolerated — warn, do not fail (FR-013 tolerant parsing). |

**Relationships**: points to the Inventory, Constitution Prompt, Decision Register, and active
Constitution. It is the single registry — a second registry MUST NOT be invented (FR-015).

---

## 2. Master Inventory Entry (`docs/17-master-spec-inventory.md`, per `ISK-xxxx`)

| Field | Type | Rules |
|---|---|---|
| `id` | string | Literal `ISK-xxxx` heading anchor. |
| `title`, `scope`, `inclusions`, `exclusions` | text | Informational. |
| `milestone`, `dependencies` | text | Drives stage/spec eligibility ordering. |
| `status`, `planningRevision`, `finalMergeCommit` | text | Real Git evidence; never fabricated (FR/Principle XI). |
| `approvedSpecifyPrompt` | fenced code block | Retrieved **byte-for-byte**; absence blocks `specify` (FR-016/FR-017). |

**State transition**: an entry is *eligible* for `specify` only when its dependencies are satisfied
and its approved prompt is present.

---

## 3. Active Constitution (`.specify/memory/constitution.md`)

| Field | Type | Rules |
|---|---|---|
| content | Markdown | Authoritative governance; supersedes the bootstrap prompt once it exists (FR-018). |
| `Version` / `Ratified` / `Last Amended` | metadata | Present (currently v1.0.0). Never overwritten without recorded human approval (FR-018, Principle XI). |

**Precedence**: `constitution.md` > `docs/15-constitution-prompt.md` (historical input).

---

## 4. Workflow State Record (`.iskender/state/<specId>.state.json`)

| Field | Type | Rules |
|---|---|---|
| `stateVersion` | int | Schema version for the record; bumped on breaking change. |
| `specId` | string | e.g. `0000`. |
| `currentStage` | enum | One of the canonical stages (see below); exactly one is active. |
| `stageStatus` | enum | `pending` \| `in_progress` \| `blocked` \| `complete`. |
| `enteredAt` | ISO-8601 | Stage entry timestamp. |
| `runnerId` | string | Identity holding the lock. |
| `sourceHashes` | map | Hashes of source prompt/config/manifest (tamper evidence) (FR-021). |
| `artifactPaths` | string[] | spec/plan/tasks/etc. produced. |
| `lastConfirmedTask` | string | Resume anchor (FR-022). |
| `gitEvidence` | object | Actual commit SHA / ref — real, never fabricated (Principle XI). |
| `budgets` | object | See entity 6 (FR-025/FR-026). |
| `partialFailureDiagnostics` | object | Last-attempt diagnostics (error class, command, outcome) captured for resume + blocker reporting (FR-021/FR-035). |
| `approvalLedgerRef` | string | Pointer to approval entries (entity 8). |
| `convergeLedgerRef` | string | Pointer to the finding ledger (entity 5). |

**Canonical stages (dependency order)**: `bootstrap/detect` → `specify` → `clarify` → `plan` →
`tasks` → `analyze` → `checklist` → `implement` → `converge` → `roadmap-review` →
`pr-merge-checkpoint`. **One eligible stage per invocation** (FR-020). `implement` works all
remaining tasks then stops before `converge` (FR-023). `pr-merge-checkpoint` is the **terminal**
stage: after `roadmap-review` it verifies CI/doc-gate/required-reviews, records the **actual** merge
commit/ref into `gitEvidence`, performs no code or remote changes, and completes the spec (FR-020/
FR-044, Principle XI). Writes are atomic + lock-guarded; contention blocks (FR-021/FR-022). Resume
restores from verified artifacts without repeating work (FR-022).

**Naming glossary** (one concept, three surfaces): the workflow **stage** `roadmap-review` is driven
by the **skill** `/review-roadmap` (`.claude/skills/review-roadmap/`), which is backed by the
**agent** `roadmap-steward` (`.claude/agents/roadmap-steward.md`). "Roadmap Steward" in the spec
refers to that agent; `roadmap-review` in stage/state logic refers to the stage.

---

## 5. Convergence Finding Ledger (`.iskender/state/<specId>.converge.json`)

| Field | Type | Rules |
|---|---|---|
| `passNumber` | int | Monotonic; persists across restarts (FR-032). |
| `findings[].id` | string | Stable ID. |
| `findings[].severity` | enum | `Low`\|`Medium`\|`High`\|`Critical`; never silently downgraded (FR-032). |
| `findings[].evidence` | text | Required. |
| `findings[].status` | enum | `open`\|`scheduled`\|`accepted`\|`remediated`. |

**Rules**: passes 1–3 → all findings scheduled; remediation only in a separate `implement`
(FR-030). Pass 4+ → Low-only may be `accepted` with recorded owner/rationale; any Medium+ blocks
(FR-031). Zero findings → immediate success even pass 1 (FR-032). `converge` never edits code.

---

## 6. Repair Budget Counters (embedded in the state record)

| Field | Type | Rules |
|---|---|---|
| `perProblem[fingerprint].meaningfulFailures` | int | Stop at **3** (FR-025). |
| `runFailedRepairs` | int | Stop at **10** per invocation run (FR-025). |
| `consecutiveNoProgress` | int | Stop at **2** (FR-025). |
| `fingerprint` | string | specId/taskId/stage + errorClass + depContext + logHash (FR-025/D6). |
| `resetJustifications[]` | object | Approval-bounded, recorded; never automatic on restart (FR-026). |

A meaningful repair changes hypothesis/action and records expected observation + result; an
unchanged re-run is no-progress (FR-025). Counters persist across invocations (FR-026, SC-005/006).

---

## 7. Roadmap Change Proposal (produced by Roadmap Steward)

| Field | Type | Rules |
|---|---|---|
| `evidence` | refs | Commit/file/test IDs. |
| `issue`, `affectedSpecIds`, `diffs` | data | Exact diffs only (FR-036). |
| `dependencyMilestoneImpact` | text | Required. |
| `changesApprovedRequirements` | bool | If material → requires approval before any file change (FR-037). |
| `rollbackOption`, `decisionOwner` | text | Required. |
| `classification` | enum | `material`\|`non-material`. |

Material changes block pending explicit human approval; `/review-roadmap` never silently writes
approval-required changes (FR-037/FR-039).

---

## 8. Approval Ledger Entry (`.iskender/state/approvals.json`)

| Field | Type | Rules |
|---|---|---|
| `decisionId` | string | Stable. |
| `timestamp` | ISO-8601 | Required. |
| `approvedDiffHash` | string | Hash of the exact approved diff (FR-037). |
| `reviewer`, `rationale` | text | Required. |
| `outcome` | enum | `approved`\|`rejected`. |

On rejection, authoritative files stay unchanged and completed-spec history (records, revision IDs,
real merge hashes) is preserved (FR-038).

---

## 9. Solution / Module Skeleton

Projects and their approved dependency direction are the data captured in
[`contracts/module-dependency-rules.md`](./contracts/module-dependency-rules.md). Each project
compiles empty (no business logic) and the direction is enforced by architecture tests (FR-005/006).

---

## 10. CI Pipeline Definition (`.github/workflows/ci.yml`)

Ordered required checks: restore, format, build, test, architecture/dependency, docs/link/manifest/
inventory consistency, documentation merge gate, secret scan, dependency hygiene, SBOM/provenance.
Contract detailed in [`contracts/ci-and-merge-gates.contract.md`](./contracts/ci-and-merge-gates.contract.md)
(FR-042/043/045).
