# 16 — Architecture decision register

**Authority:** decision status/traceability. This register reflects recoverable approved user decisions through 2026-11-05; new implementation choices should be added through ADR workflow. Exact original 109-spec title list was not recoverable verbatim, so new spec numbering in [17](17-master-spec-inventory.md) is a proposed reconciliation, not historical evidence.

## Decision status rules

`Approved`: explicitly settled design, superseded only by later approved ADR. `Proposed`: recommendation awaiting decision, not binding architectural choice. `Superseded`: preserve original text/ID and reference successor. `Rejected`: record rationale and prevent accidental resurrection. ADR identifiers are never reused. Any change crossing a product scope/security/release boundary requires human approval and roadmap impact analysis. Implementation choices that merely satisfy approved contracts may be recorded as proposed technical ADRs and reviewed with PR owners.

### ADR-0001

**AI-first MIT gateway with later general HTTP evolution**

- **Status:** Approved.
- **Context:** Platform teams need governed AI traffic and cloud choice.
- **Decision:** Open-source Iskender core MIT; general HTTP API surface follows AI MVP.
- **Rationale:** Licensing and an AI-first entry point reduce adoption friction while keeping the neutral pipeline available for later HTTP expansion.
- **Consequences / trade-offs:** Keeps MVP scope credible; forbids hidden open-core assumptions.
- **References:** [01](01-product-charter-and-vision.md), [02](02-product-requirements-and-roadmap.md).

### ADR-0002

**.NET 10/ASP.NET Core primary runtime**

- **Status:** Approved.
- **Context:** Composable hosting and productivity with a hot-path optimization path.
- **Decision:** Use .NET/C# for MVP; isolate potential native/Rust hot paths.
- **Rationale:** The .NET ecosystem and ASP.NET hosting model optimize composability; measurements rather than speculative Rust rewrites should dictate native hot-path changes.
- **Consequences / trade-offs:** Native interop must not rewrite domain or safety contracts.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0003

**Modular monolith and split deployment**

- **Status:** Approved.
- **Context:** Shared domain correctness but different operational scale.
- **Decision:** Explicit modules; independently host control/data planes.
- **Rationale:** A shared modular codebase reduces drift while separate process boundaries permit independent scaling and operator security policies.
- **Consequences / trade-offs:** Version config and IPC/storage contracts; preserve dependency direction.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0004

**Container and NuGet embedded consumption**

- **Status:** Approved.
- **Context:** Users want both a self-hosted image and AddIskender integration.
- **Decision:** Ship both modes with same pipeline contracts.
- **Rationale:** Embedding requires first-class DI/middleware contracts; containers simplify deployment for operators who do not own application code.
- **Consequences / trade-offs:** Embedded must not require global service ownership.
- **References:** [01](01-product-charter-and-vision.md), [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md).

### ADR-0005

**Organization/project/resource ownership**

- **Status:** Approved.
- **Context:** Teams need scoped governance and isolation.
- **Decision:** Use org→project→resource scopes, RBAC, ownership-bound references.
- **Rationale:** Clear ownership scopes make multi-team governance and adversarial isolation testable at every persistence and authorization boundary.
- **Consequences / trade-offs:** Every query/worker/job/stream requires authorization checks.
- **References:** [03](03-domain-and-technical-architecture.md), [05](05-identity-security-and-privacy.md).

### ADR-0006

**PostgreSQL durability, Redis coordination option**

- **Status:** Approved.
- **Context:** Portability and durable history without cloud coupling.
- **Decision:** PG source of truth; Redis for hot/ephemeral coordination when enabled.
- **Rationale:** Postgres supplies transactional durability; optional Redis speeds distributed hot paths without replacing authoritative records.
- **Consequences / trade-offs:** Strong distributed controls require capable backend; no silent weakening.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [06](06-conversation-state-architecture.md), [08](08-routing-resilience-and-governance.md).

### ADR-0007

**Azure first reference; portable clouds/on-prem**

- **Status:** Approved.
- **Context:** Initial launch targets ACA/PG/Redis.
- **Decision:** Cloud dependencies behind interfaces with Compose/local path.
- **Rationale:** Cloud-native initial deployment is pragmatic, but interchangeable service interfaces keep self-hosted and other-cloud installations viable.
- **Consequences / trade-offs:** Provider identity adapters separate from domain logic.
- **References:** [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md).

### ADR-0008

**Local Identity plus external OIDC control auth**

- **Status:** Approved.
- **Context:** Both self-hosted teams and enterprise IdPs.
- **Decision:** Support local ASP.NET Core Identity and BYO OIDC.
- **Rationale:** Local identity keeps self-hosting accessible, while OIDC makes enterprise user lifecycle and SSO feasible without operating a new authorization server.
- **Consequences / trade-offs:** Internal principal/role mapping; no automatic cross-IdP trust.
- **References:** [05](05-identity-security-and-privacy.md).

### ADR-0009

**Data-plane API keys and OAuth bearer**

- **Status:** Approved.
- **Context:** Workloads require flexible ingress auth.
- **Decision:** Keys and validated JWT/OAuth; independently disable key issue/acceptance.
- **Rationale:** Key-based clients and federated OAuth workloads have distinct needs, so operators must control issuance and validation independently.
- **Consequences / trade-offs:** Revocation/tenant mapping and per-scope checks remain mandatory.
- **References:** [05](05-identity-security-and-privacy.md).

### ADR-0010

**Scoped upstream credential profiles**

- **Status:** Approved.
- **Context:** Backends/accounts need independent secrets/workload identities.
- **Decision:** Deployment-referenced credentials; Azure, Google, OpenAI federation where supported.
- **Rationale:** Per-deployment credential separation prevents accidental account/tenant token reuse and enables short-lived workload identities where supported.
- **Consequences / trade-offs:** Some providers only accept keys; never expose secrets in client config.
- **References:** [05](05-identity-security-and-privacy.md), [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0011

**Three mandatory conversation modes**

- **Status:** Approved.
- **Context:** User explicitly requires gateway-owned durable state in MVP.
- **Decision:** Stateless, provider-managed, gateway-managed history with PG ownership.
- **Rationale:** Gateway ownership is necessary to survive provider changes and restarts, and it cannot be deferred if durable conversations are part of MVP value.
- **Consequences / trade-offs:** Compaction/branching delayed; replica-local state non-authoritative.
- **References:** [06](06-conversation-state-architecture.md).

### ADR-0012

**Responses and Conversations ingress in MVP**

- **Status:** Approved.
- **Context:** Portable continuation must be an API feature not internal only.
- **Decision:** OpenAI-compatible endpoint set with typed items/tool history/stream safety.
- **Rationale:** Standards-shaped ingress makes continuation useful to application developers while explicit compatibility limits prevent false parity.
- **Consequences / trade-offs:** Provider-specific limitations and native IDs must be explicit.
- **References:** [06](06-conversation-state-architecture.md), [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0013

**Five MVP provider categories**

- **Status:** Approved.
- **Context:** Practical heterogeneous model support.
- **Decision:** OpenAI, Azure OpenAI/Foundry, Anthropic, Gemini, generic compatible.
- **Rationale:** Heterogeneous adapters minimize provider lock-in and capability flags block invalid backend routing before irreversible execution.
- **Consequences / trade-offs:** Publish evidence-backed capability matrix; no fake universal support.
- **References:** [07](07-provider-adapters-and-api-compatibility.md).

### ADR-0014

**Multiple independent backend deployments/pool**

- **Status:** Approved.
- **Context:** Resilience and accounts/regions demand separate failure domains.
- **Decision:** Pool candidates individually own credentials, health, breakers and selection.
- **Rationale:** Separate deployment identities allow real failover, health and cost governance instead of treating a model name as a single endpoint.
- **Consequences / trade-offs:** No single-global model/backend assumption.
- **References:** [03](03-domain-and-technical-architecture.md), [08](08-routing-resilience-and-governance.md).

### ADR-0015

**Distributed governance and financial accounting**

- **Status:** Approved.
- **Context:** Enterprise multi-replica budgets/quotas need correctness.
- **Decision:** Request/token limits, quotas, concurrency, usage and spend reservation/reconciliation.
- **Rationale:** Token and spend reservations must coordinate across replicas and reconcile attempts to avoid bypassing enterprise guardrails.
- **Consequences / trade-offs:** No invoice-exact claim or fail-open hard budget.
- **References:** [08](08-routing-resilience-and-governance.md).

### ADR-0016

**Typed policy + CEL applicability + C# effects**

- **Status:** Approved.
- **Context:** Flexible rules without inventing a procedural language.
- **Decision:** Typed versioned handlers, CEL when, JSON/YAML and fluent forms.
- **Rationale:** CEL gives a reviewable declarative predicate while typed handlers keep side effects testable and strongly versioned.
- **Consequences / trade-offs:** No Rego/substitute DSL; validate before activation.
- **References:** [09](09-policy-language-and-execution.md).

### ADR-0017

**Policy scopes and atomic compiled plans**

- **Status:** Approved.
- **Context:** Inheritance/security changes must be deterministic and safe.
- **Decision:** Org/project/resource, mandatory guardrails, immutable plans, atomic activation.
- **Rationale:** Immutable compiled snapshots avoid partially applied policy changes and permit request-level traceability and safe rollback.
- **Consequences / trade-offs:** Stale/missing mandatory security plan fails closed.
- **References:** [09](09-policy-language-and-execution.md).

### ADR-0018

**Namespaced first-party policy identifiers**

- **Status:** Approved.
- **Context:** Avoid collisions/confusion with Azure APIM policy identifiers.
- **Decision:** Use iskender-domain semantic namespace, owned partner namespaces.
- **Rationale:** Namespaced identifiers protect brand clarity and SDK stability; they also avoid misleading equivalence with Azure APIM XML policy names.
- **Consequences / trade-offs:** Catalog identity/version migration required.
- **References:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md).

### ADR-0019

**First-party parity with SDK handlers**

- **Status:** Approved.
- **Context:** Extensibility should be a core platform capability.
- **Decision:** Built-ins share typed plugin handler contracts where feasible.
- **Rationale:** Using common handler contracts reduces built-in/custom semantic drift while non-optional authentication stays outside discretionary extensions.
- **Consequences / trade-offs:** Mandatory auth stays non-optional platform invariant.
- **References:** [09](09-policy-language-and-execution.md), [10](10-first-party-policy-catalog.md), [11](11-plugin-extensibility-and-sdk.md).

### ADR-0020

**NuGet plugin SDK and template**

- **Status:** Approved.
- **Context:** Third parties should use normal dotnet tooling.
- **Decision:** Public SDK, dotnet new iskender-policy, pack-time manifests/hashes/schemas.
- **Rationale:** NuGet and project templates meet .NET authors where they work and automate error-prone manifest/schema/hash production.
- **Consequences / trade-offs:** Manual manifest valid but must meet exact integrity schema.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0021

**Plugin approval lockfile and safe delivery**

- **Status:** Approved.
- **Context:** No custom container rebuild required.
- **Decision:** Approved read-only bundles or startup NuGet download, digest/dep lockfiles.
- **Rationale:** Digest-pinned, operator-approved artifacts make stock images extensible without permitting opportunistic startup code execution.
- **Consequences / trade-offs:** No unapproved directory sweep; missing required bundle blocks readiness.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0022

**Trusted in-process plugins with restarts**

- **Status:** Approved.
- **Context:** Performance/composition prioritize .NET handlers.
- **Decision:** Load approved trusted assemblies in process; binaries restart/redeploy.
- **Rationale:** In-process handlers achieve low overhead and share DI but must be trusted because .NET assembly load isolation cannot constrain malicious code.
- **Consequences / trade-offs:** AssemblyLoadContext is not a sandbox; out-of-process later.
- **References:** [11](11-plugin-extensibility-and-sdk.md).

### ADR-0023

**OpenTelemetry and safe defaults**

- **Status:** Approved.
- **Context:** Operators need correlatable inference observability.
- **Decision:** OTel auto-instrumentation, safe policy telemetry, payload off by default.
- **Rationale:** OpenTelemetry retains vendor-agnostic operations and avoids making prompt capture an accidental observability default.
- **Consequences / trade-offs:** Control metric cardinality and avoid sensitive logging.
- **References:** [05](05-identity-security-and-privacy.md), [12](12-observability-and-nonfunctional-requirements.md).

### ADR-0024

**Next.js and shadcn/ui management**

- **Status:** Approved.
- **Context:** Practical operational UI separate from hot path.
- **Decision:** Use Next.js/shadcn management application.
- **Rationale:** Admin UI is a thin authorized client, leaving management APIs reusable by automation and keeping JavaScript off the request hot path.
- **Consequences / trade-offs:** Keep core gateway usable via API and embedded host.
- **References:** [04](04-technology-stack-and-deployment.md).

### ADR-0025

**Spec Kit and Claude Code stage discipline**

- **Status:** Approved.
- **Context:** Agent needs repeatable controlled implementation.
- **Decision:** Spec 0000 creates resumable drive-speckit, stage-per-invocation.
- **Rationale:** Resumable stage boundaries avoid lost agent progress and accidental unreviewed transitions across build, converge and merge activities.
- **Consequences / trade-offs:** Implement continues tasks but never auto-enters converge.
- **References:** [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md).

### ADR-0026

**Repair/convergence bounds**

- **Status:** Approved.
- **Context:** Prevent infinite retries and hidden severity downgrades.
- **Decision:** 3/problem,10/run,2 no-progress; passes1-3 fix Low, pass4+ Low accepted.
- **Rationale:** Persisted repair budgets and strict severity rules bound coding agents and prevent restart-based loop or quiet issue suppression.
- **Consequences / trade-offs:** Medium+ blocks; separate implement and converge stages.
- **References:** [13](13-development-standards-and-agent-workflows.md).

### ADR-0027

**Human-approved Roadmap Steward**

- **Status:** Approved.
- **Context:** Implementation may invalidate future assumptions.
- **Decision:** Review after convergence, propose diffs/evidence, await material approval.
- **Rationale:** An independent roadmap review catches implementation drift without permitting an agent to rewrite product commitments on its own.
- **Consequences / trade-offs:** Preserve spec history and commitments.
- **References:** [13](13-development-standards-and-agent-workflows.md), [17](17-master-spec-inventory.md).

### ADR-0028

**Grounding manifest and constitution hierarchy**

- **Status:** Approved.
- **Context:** Reliable discovery across shell paths and bootstrap.
- **Decision:** Use .iskender/grounding.yaml and initial constitution prompt; active constitution wins.
- **Rationale:** A small versioned manifest makes bootstrap and long-term path discovery deterministic, and active constitution precedence prevents prompt-driven resets.
- **Consequences / trade-offs:** Never guess missing paths or overwrite existing constitution.
- **References:** [13](13-development-standards-and-agent-workflows.md), [14](14-spec-0000-bootstrap-brief.md), [15](15-constitution-prompt.md), [17](17-master-spec-inventory.md).

### ADR-0029

**Release after full MVP validation**

- **Status:** Approved.
- **Context:** Broadly credible release requires correctness and operational evidence.
- **Decision:** Exact reconstructed inventory gate after ISK-0086; acceptance requirements in 02/12.
- **Rationale:** A named release spec makes the cumulative quality gate explicit; revised ID is an operational planning choice, not evidence of prior implementation.
- **Consequences / trade-offs:** Gate ID is a planning decomposition, not an earlier verbatim approval.
- **References:** [02](02-product-requirements-and-roadmap.md), [12](12-observability-and-nonfunctional-requirements.md), [17](17-master-spec-inventory.md).

### ADR-0030

**Pre-MVP alpha prereleases from merged specs**

- **Status:** Approved.
- **Context:** ISK-0000 added a workflow that publishes a public GitHub release, with NuGet packages attached, for each version tag on `main`, and `/drive-speckit` tags every merged spec `v0.0.<spec number>-alpha`. Milestones before E5 are internal, and the public MVP ships only after ISK-0086.
- **Decision:** Until ISK-0086 completes, a merged spec publishes only a `-alpha` GitHub prerelease, with nothing pushed to a NuGet feed. Such a prerelease is a development build. It is not an MVP release, carries no support or compatibility commitment, and is not evidence that a release gate passed. ISK-0086 sets the version and tag rule for the public MVP and later releases.
- **Rationale:** Tagged prereleases exercise the release path and give each merged spec a reproducible artifact long before the release gate, as long as nobody can mistake one for the MVP.
- **Consequences / trade-offs:** Internal milestones produce public build artifacts. The ISK-0002 versioning policy must allow for package versions derived from spec numbers until ISK-0086 replaces the rule.
- **References:** [02](02-product-requirements-and-roadmap.md), [17](17-master-spec-inventory.md).

### ADR-0031

**Project topology for plane separation and module homes**

- **Status:** Approved.
- **Context:** ADR-0003 and ADR-0004 require explicit modules, independently hosted control and data planes, and `AddIskender(...)` embedding, but name no projects. ISK-0000 left one host that referenced `Iskender.Management`, and the 12 modules in [03](03-domain-and-technical-architecture.md) had no home.
- **Decision:** `Iskender.Gateway.Host` is the data-plane host and `Iskender.ControlPlane.Host` the control-plane host. Neither reaches the other's plane, and no project references a host. Hosting/Ingress lives in the embeddable library `Iskender.Hosting`, which reaches neither `Iskender.Infrastructure` nor `Iskender.Management`. The eight domain modules share `Iskender.Domain`, kept apart by namespace, with an initially empty module-to-module allow-list and shared port types in `Iskender.Domain.Ports`. Provider SDKs live only in `Iskender.Providers.<Provider>`, and database drivers only in `Iskender.Infrastructure.<Adapter>`. Public contract and embeddable projects are packable. The current module dependency rules contract, linked from the Module boundaries section of [03](03-domain-and-technical-architecture.md) and first published by ISK-0001, holds the exact edges and rules, and architecture tests enforce it.
- **Rationale:** Plane independence and embeddability can only be checked when both planes and the embeddable hosting library exist in the project graph. One domain assembly avoids per-module build cost while namespace rules keep modules apart, and per-adapter projects stop one driver or cloud SDK being imposed on every embedder.
- **Consequences / trade-offs:** `internal` does not separate modules inside `Iskender.Domain`, so the namespace rule is their only compile-independent guard, and every module's packages reach every consumer of that assembly. An embedder opts into storage or control-plane services by referencing them itself. A new project kind, a module split, or a new cross-module edge needs a revision of the contract.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [17](17-master-spec-inventory.md).

### ADR-0032

**Public contract identifiers, errors, and versioning discipline**

- **Status:** Approved.
- **Context:** ADR-0003 and ADR-0005 require versioned contracts and opaque, owner-scoped resource IDs, and the constitution requires versioned SDK and API surfaces. ADR-0030 gives pre-MVP prereleases no compatibility commitment. No ADR said how IDs are formed, how errors are classified, or how a breaking change to a public contract is caught.
- **Decision:** A resource ID is `<prefix>_<26 Crockford base32 characters>` holding 128 random bits, with each prefix registered in `Iskender.Core.Contracts` and never reused. Its text has no version marker, and any change to that text form is a break. Every error has a registered, dot-namespaced code and safe message and belongs to one of 13 transport-neutral categories, each with a fixed retry rule. A later spec may add codes, but a new category needs a revision of the contract. The `error-envelope` wire contract never carries credentials, content, or another tenant's identifiers, and is always `withheld` for `not_found`. `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, and `Iskender.Policy.Sdk` are versioned as separate contracts, every public type is tagged with its contract and version, and each wire contract has its own version. Until ISK-0086 completes, a break passes only when it is declared in the break record, approved by the Architecture (ADR) owner, and its contract version is incremented. `tests/Iskender.Contracts.Compatibility.Tests` compares each change with its merge base on `origin/main` and fails on any other break. The current [public contracts and versioning contract](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md), first published by ISK-0002, holds the exact registries, rules, and wire formats.
- **Rationale:** One ID format and one error contract stop each module from inventing its own. Per-contract versions show which contract changed while package versions follow spec numbers under ADR-0030. Declared breaks keep the pre-MVP surface changeable without letting a break go unnoticed.
- **Consequences / trade-offs:** Any change to a package's public surface, an addition included, increments that contract's version. The harness needs `origin/main` fetched. `.github/CODEOWNERS` requests the owner's review of a declared break, but GitHub does not require it while DEC-20261105-002 holds. A resource ID shows its type through its prefix. ISK-0086 must replace the pre-MVP declared-break rule with its release rule.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [07](07-provider-adapters-and-api-compatibility.md), [17](17-master-spec-inventory.md).

### ADR-0033

**Solution file format**

- **Status:** Approved.
- **Context:** ISK-0000 created `Iskender.sln` with `dotnet new sln --format sln`, keeping the older text format although SDK 10 defaults to `.slnx`. That was a bootstrap choice, not an ADR. Rule A13 in the module dependency rules contract names the solution file, and the architecture tests read it as text.
- **Decision:** The solution file is `Iskender.slnx`. `Iskender.sln` is removed, and every workflow, test, and build convention names `Iskender.slnx`. The architecture tests read project paths from the `Path` attributes of its `Project` elements, as XML with no MSBuild. The module dependency rules contract was revised in place to version 3 under RC-20261108-009, with this owner's approval, rather than republished by a new spec.
- **Rationale:** `.slnx` is the SDK 10 default and is plain XML, so it merges cleanly and can be read without parsing the older format's GUID blocks. With SDK 10.0.403, `dotnet restore --locked-mode`, `dotnet build -warnaserror`, `dotnet list package --vulnerable`, and CycloneDX 6.3.1 all accept it, and the SBOM's components were identical to the `.sln` run.
- **Consequences / trade-offs:** Tooling older than SDK 9.0.200 cannot open the solution, so `global.json` keeps SDK 10. A branch created before the switch that names `Iskender.sln` must be updated before it merges. Completed specs' records that name `Iskender.sln` stay unedited as history.
- **References:** [04](04-technology-stack-and-deployment.md), [13](13-development-standards-and-agent-workflows.md), [17](17-master-spec-inventory.md).

### ADR-0034

**Configuration declaration, validation, and revisions**

- **Status:** Approved.
- **Context:** ADR-0003 requires versioned configuration contracts, [03](03-domain-and-technical-architecture.md) runs data planes on immutable validated snapshots with a version marker, and [04](04-technology-stack-and-deployment.md) forbids development-only defaults in production and asks that chosen libraries be documented in approved ADRs. ISK-0002 published `ConfigurationRevision` without saying what it names. No ADR said how operator configuration is declared, read, layered, checked, or named.
- **Decision:** System and operator configuration sits under the root key `iskender`, in JSON or a JSON-equivalent YAML subset that rejects anchors, aliases, tags, merge keys, directives, and more than one document. Each namespace under the root is owned by one module through a `SectionSchema` registered from `Iskender.Core.Contracts`, every scalar is typed by its schema rather than by YAML, and validation is closed, so an undeclared namespace or field is an error. Layers apply in the order schema defaults, base document, environment overlay document, then `ISKENDER__` environment variables. Only the active environment `development` permits development-only values, and any other name, or none, is production-grade. A secret field holds only a `secretRef` placeholder naming the secret, and configuration never resolves it. Every error is collected before anything runs, carries a registered `config.*` code, a path, a location, and a fix, and never contains a configuration value. A validated snapshot is immutable, and its revision is `cfg-` followed by the lower-case base32 SHA-256 of its canonical form, held in `ConfigurationRevision`. Publication, signing, and activation stay with ISK-0057. The engine lives in `Iskender.Domain.Registry` and reads YAML with YamlDotNet (MIT). The current [configuration contract](../specs/0003-configuration-schema-validation-foundation/contracts/configuration.md), first published by ISK-0003, holds the exact rules, limits, canonical form, codes, and events.
- **Rationale:** One closed, schema-typed format with a fixed precedence gives the same result in container and embedded hosts and catches misspelled or unsafe settings before anything runs. YAML's implicit typing and expansion features are where hostile or mistaken documents go wrong, so the subset bans them. A content-derived revision is the same on every replica and host without a coordinator, and hashing reference names keeps secrets out of it.
- **Consequences / trade-offs:** Configuration here defines no tenant-owned resource; whether policy configuration reuses the schema mechanism is left to ISK-0054. A change to the canonical form changes every revision. YamlDotNet and `Microsoft.Extensions.Logging.Abstractions` reach every consumer of `Iskender.Domain` (ADR-0031). A secret pasted into a field not declared as a secret is not detected. Refusing to start or reporting not ready on a rejected configuration is left to the hosts (ISK-0006, ISK-0007), and reloading configuration while running is post-MVP.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md), [09](09-policy-language-and-execution.md), [17](17-master-spec-inventory.md).

### ADR-0035

**PostgreSQL persistence foundation**

- **Status:** Approved.
- **Context:** ADR-0006 makes PostgreSQL the durable source of truth and ADR-0005 requires organization, project, and resource ownership, but no ADR said how modules reach the database, how schema changes are applied and reversed, or how one tenant's rows are kept from another. [04](04-technology-stack-and-deployment.md) lists EF Core, Npgsql, and Dapper as candidates and asks that the chosen relational mapper and migration strategy be documented in an approved ADR, and this register's recommendations table left the EF Core / Dapper split and migrations to a storage spec.
- **Decision:** Modules reach the database only through the ports in `Iskender.Domain.Ports`: a unit of work, which is one connection and one transaction for one owner scope, transaction-scoped advisory locks, a migration runner, a health probe, and a database credential port that the host implements. `Iskender.Infrastructure.PostgreSql` implements them on Npgsql (SPDX `PostgreSQL`, added to the license allow-list by the Product/Charter owner) with positional-parameter SQL, and no mapper or migration library. Each module owns one schema, `isk_<module>`, and ships paired forward and reverse SQL scripts, which are checksummed and applied in dependency order, one transaction each, under a separate migration login and one session advisory lock, and only when a host calls the runner, never on start-up. A tenant-owned table carries its owner IDs, and isolation is enforced twice: by the owner predicate in repository code, and by forced row-level security keyed on transaction-local settings that only the unit of work sets, for a runtime login that owns nothing and cannot bypass it. A row of another scope reads as missing. Database failures map onto 12 `persistence.*` codes under existing ISK-0002 categories, and only a whole unit that its caller marks safe to re-run is retried after a transient conflict. Within a one-release compatibility window, migrations are expand-only. PostgreSQL 17 is the tested and oldest supported major version. The current persistence contract, linked from the Persistence, publication and distributed coordination section of [03](03-domain-and-technical-architecture.md) and first published by ISK-0004, holds the exact rules, keys, codes, and events.
- **Rationale:** One unit-of-work seam keeps scope, bounds, and failure handling the same in every module, and keeps the driver out of `Iskender.Domain` (ADR-0031). With isolation enforced by the database as well, one forgotten predicate does not leak another tenant's rows. Explicit migrations under a separate identity let replicas start together without racing, and limit what a compromised data plane can change. Plain SQL needs no mapping for this surface, and a later spec can still add a mapper inside the adapter.
- **Consequences / trade-offs:** Every module writes its own SQL and reverse scripts, and a statement PostgreSQL refuses inside a transaction, such as `CREATE INDEX CONCURRENTLY`, cannot be used in a migration. Operators provision the `iskender_runtime` group role and two logins before the first migration. Advisory locks are mutual exclusion within one database and one transaction, not leases or fencing, which stay with ISK-0005. No production host can connect until ISK-0021 implements the credential port, and ISK-0007 owns the migration command and the readiness mapping. Outside `development`, `tlsMode: require` cannot use a server certificate from a private CA until a later spec adds a CA setting. The schema check reads script text, so dynamic SQL in a migration is left to review.
- **References:** [03](03-domain-and-technical-architecture.md), [04](04-technology-stack-and-deployment.md), [05](05-identity-security-and-privacy.md), [17](17-master-spec-inventory.md).

## Recommendations and unresolved approvals (not ADR decisions)

| Candidate decision | Status | Owner/action and evidence required |
|---|---|---|
| Concrete CEL evaluator implementation and CEL limits | Recommended; not approved | Implementation spec benchmarks evaluator cost/security and records selected library/license |
| Redis client | Recommended; not approved | ISK-0005 tests coordination behavior and proposes ADR if it affects public contracts. The PostgreSQL driver, the absence of a mapper, and the migration approach are settled by ADR-0035 |
| Strong-consistency coordinator fallback when Redis is not deployed | Unresolved | Prove semantics in multi-replica cases; either require capable backend or restrict deployment mode |
| Local user MFA minimum and emergency admin recovery | Unresolved | Security owner reviews threat model and operator impact before product release |
| Default retention and encryption key management service/provider | Unresolved | Privacy/operations owner approves documented defaults and local deployment path |
| Numeric SLO, latency/memory/throughput thresholds and RPO/RTO | Unresolved | Performance baseline + reviewer-approved release targets; no invented measurement |
| Hosted/commercial plans, OpenIddict and out-of-process isolation | Post-MVP candidates | Separate product approval if pursued; do not contaminate MVP obligations |
| First-party catalog field names and handler versions | Proposed contract surface | Freeze via implementing specs/ADR before public SDK release |

## ADR submission and supersession procedure

A proposed ADR records decision question, current constraints, 2+ options (where meaningful), evidence/benchmarks, security/compatibility consequences and affected specs. Approval logs approver and date, marks status `Approved`, updates references and test expectations. A superseded ADR retains full historical rationale and points to successor `ADR-xxxx`; successor lists prior ID, migration and impact on deployed configurations. Material roadmap changes also require a Roadmap Steward proposal and explicit approval [13](13-development-standards-and-agent-workflows.md). No ADR records are considered implementation-complete solely because they appear here.
