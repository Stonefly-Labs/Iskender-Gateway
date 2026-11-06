# Iskender Gateway — Project grounding package

**Iskender Gateway:** MIT-licensed .NET 10/ASP.NET Core AI-first gateway with cloud-portable runtime, independent control/data planes, deployable OCI containers and embeddable `AddIskender(...)` services. This directory is the **project's initial authoritative grounding package for Claude Code and GitHub Spec Kit**, not a claim that any gateway implementation already exists.

## Start here: authoritative discovery and source hierarchy

- `.iskender/grounding.yaml` at **repository root** is the machine-readable path discovery manifest. `grounding.root` is repo-relative; fields under `grounding` are relative to that root; fields under `speckit` are repo-relative. **Never resolve against current working directory or invent a missing path.** Validate `schemaVersion: 1`, required keys and in-repo path safety before use.
- The **active constitution**, when it exists, lives at `.specify/memory/constitution.md` and supersedes the *bootstrap generation prompt* [15](15-constitution-prompt.md). Amendments require explicit approval and impact review.
- Settled product decisions, later approved ADRs and approved roadmap changes outrank proposals; [16](16-architecture-decision-register.md) records status. Each topic has one authoritative grounding document; use its links rather than duplicating divergent requirements.
- [17](17-master-spec-inventory.md) is authoritative for **planned spec ID, status, revision, dependencies, MVP cutline, actual merge hash and verbatim `/speckit.specify` prompts**. Current entries are planned; no commits or implementations are claimed. [02](02-product-requirements-and-roadmap.md) is the definitive MVP feature contract.
- Once code exists, the current approved Spec Kit artifact and code/test evidence inform implementation review; they do **not** silently override a settled product requirement or approved milestone.

## Complete document index and purpose

| File | Purpose | Suggested order |
|---|---|---|
| [01 — Charter and vision](01-product-charter-and-vision.md) | Product intent, values, MIT and audience | 1 |
| [02 — Requirements and roadmap](02-product-requirements-and-roadmap.md) | MVP capabilities, release acceptance and exclusions | 2 |
| [03 — Domain and architecture](03-domain-and-technical-architecture.md) | Resource nouns, modules, request pipeline and invariants | 3 |
| [04 — Stack and deployment](04-technology-stack-and-deployment.md) | Approved technology, containers, NuGet, cloud-neutral topology | 4 |
| [05 — Identity, security, privacy](05-identity-security-and-privacy.md) | Local/OIDC auth, API keys/JWT, ownership, credentials and retention | 5 |
| [06 — Conversation state](06-conversation-state-architecture.md) | Three required modes, canonical history, safe continuity | 6 |
| [07 — Providers and APIs](07-provider-adapters-and-api-compatibility.md) | Provider contracts, ingress compatibility and limits | 7 |
| [08 — Routing and governance](08-routing-resilience-and-governance.md) | Pools, failover, distributed quotas and spend | 8 |
| [09 — Policy runtime](09-policy-language-and-execution.md) | Strongly typed handlers, CEL, hierarchy and plan activation | 9 |
| [10 — First-party policy catalog](10-first-party-policy-catalog.md) | Complete proposed built-in handler definitions and stable namespaces | 10 |
| [11 — Plugins and SDK](11-plugin-extensibility-and-sdk.md) | Template, pack manifests, CLI, operator approval, safe loading | 11 |
| [12 — Observability and quality](12-observability-and-nonfunctional-requirements.md) | OTel, performance, multi-replica and release test gates | 12 |
| [13 — Agent workflows](13-development-standards-and-agent-workflows.md) | Engineering standards, drive-speckit, convergence and roadmap review | 13 |
| [16 — ADR register](16-architecture-decision-register.md) | Approved decisions, consequences and unresolved candidates | 14 |
| [15 — Constitution prompt](15-constitution-prompt.md) | Bootstrap `/speckit.constitution` prompt, not active constitution | 15 |
| [14 — Spec 0000 brief](14-spec-0000-bootstrap-brief.md) | Complete initial repo/agent bootstrap specification source | 16 |
| [17 — Master spec inventory](17-master-spec-inventory.md) | Full executable Spec Kit prompts, dependencies, status and roadmap | 17 |
| [18 — Branch protection](18-branch-protection.md) | Required GitHub rules, and whether each is applied or pending | 18 |

## Initial Claude Code + Spec Kit sequence

1. Extract ZIP at **repository root**, preserving `.iskender/grounding.yaml` and `docs/` exactly; do not overwrite `.specify` or install Spec Kit again.
2. Check existing Spec Kit installation/commands in the repo. Read this index, approved decisions, [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md), [15](15-constitution-prompt.md) and ISK-0000 in [17](17-master-spec-inventory.md).
3. If no active constitution exists, use the installed `/speckit.constitution` with the complete prompt in [15](15-constitution-prompt.md). If it exists, **review rather than overwrite**; amendment requires approval.
4. Invoke the **installed** `/speckit.specify` manually with **ISK-0000's literal starting prompt** in [17](17-master-spec-inventory.md#isk-0000--repository-bootstrap--agentic-development-framework), using `@docs/14-spec-0000-bootstrap-brief.md`. Specify creates a spec only; it **does not implement**.
5. Use the installed Spec Kit plan/tasks/implementation workflow for ISK-0000 (the new orchestration skill does not exist yet), review/test/merge on a feature branch and preserve existing Spec Kit state.
6. After ISK-0000 implements it, invoke **`/drive-speckit`** without stage names for subsequent stages/specs. One eligible stage runs per invocation; implement completes all remaining tasks until done/blocked, convergence is separate and Roadmap Steward proposals wait for explicit human approval.

## Operator and agent safeguards

Never reinitialize the existing Spec Kit installation. Never infer a missing manifest path/prompt. Never execute unknown plugin binaries; verified operator lockfiles are mandatory. Never claim a completed task, benchmark or final merge hash without evidence. Never weaken mandatory auth/tenant rules via optional policies. Material roadmap changes require explicit approval; routine docs changes are mandatory before merge. If blocked, report what/why/evidence/tried steps/owner/exact unblock/resume stage. The initial discovery manifest is part of this package and must be **preserved**, not recreated independently.

## Reconciliation note

Earlier project planning included a 109-spec outline; its broad category boundaries were recovered, but **not every original title and prompt**. [17](17-master-spec-inventory.md) therefore presents a complete **125-spec revised planning decomposition** incorporating later mandatory state, plugin, policy and bootstrap decisions, with an explicit revised MVP cutline after **ISK-0086**. The feature commitments are approved; individual reconstructed spec IDs are a newly authored plan, not claims of historical verbatim approval. No spec is marked complete.

## License and use

The Iskender project is intended for MIT open-source distribution. Add a real LICENSE file as part of repository bootstrap; this documentation package is not a substitute for code, implementation, provider contract verification, a released artifact or applied GitHub security settings.
