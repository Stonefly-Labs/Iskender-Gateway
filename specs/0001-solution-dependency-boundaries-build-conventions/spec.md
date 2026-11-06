# Feature Specification: ISK-0001 — Solution Dependency Boundaries & Build Conventions

**Feature Branch**: `feature/0001-solution-dependency-boundaries-build-conventions`

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0001 — Solution Dependency Boundaries & Build Conventions** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/13-development-standards-and-agent-workflows.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Extend the compilable modular project graph and dependency direction delivered by ISK-0000 across standalone and embeddable components, and map the docs/03 module table onto them**. In-scope behaviors and artifacts: **Mapping of docs/03 modules to approved assemblies or namespaces; refined module assembly references that supersede the ISK-0000 baseline, including the Domain, Management and host edges; additions to the existing central version pins, analyzers and nullability settings; extended dependency architecture tests**. Explicitly exclude: **No provider business functionality; no premature database tables; no recreation of what ISK-0000 delivered**. Prerequisites: **ISK-0000**; do not assume unavailable implementation already exists. ISK-0000 already delivered the seven-project skeleton under `src/`, `Directory.Packages.props`, the analyzer and nullability settings in `Directory.Build.props`, and `tests/Iskender.Architecture.Tests`, so start from the repository as it stands and do not recreate them. The rules in `specs/0000-repo-bootstrap/contracts/module-dependency-rules.md` are the baseline; this spec's own contract supersedes them and leaves that completed record unedited. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Fresh build passes, every docs/03 module maps to an approved assembly or namespace, and forbidden cross-module references are rejected by architecture tests**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

ISK-0000 delivered a seven-project skeleton, central version pins, analyzer and nullability settings, and architecture tests that enforce an approved reference direction. It left two things open, which RC-20261106-004 assigns to this spec:

1. `docs/03` lists 12 modules, and nothing says which assembly or namespace each one lives in.
2. The approved references are narrower than `docs/03` and the independent-plane requirement imply. `docs/03` says Policy Engine depends on the handler SDK, Routing and Conversations depend on provider capability, and Management depends on authorized services, yet the baseline limits both `Iskender.Domain` and `Iskender.Management` to `Iskender.Core.Contracts`. An ISK-0000 test also requires the one host to reference every project, including Management, which conflicts with a data plane that deploys without the control plane.

This spec settles both, and hardens the rules so that later specs inherit a boundary that cannot be bypassed quietly. Its contract, `contracts/module-dependency-rules.md` in this directory (version 2), supersedes `specs/0000-repo-bootstrap/contracts/module-dependency-rules.md` (the version 1 baseline), which stays unedited as a completed record.

This spec adds no gateway behavior. Project and assembly names appear throughout because they are the subject of the spec, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Modular monolith with explicit modules; control and data planes host independently; dependency direction is preserved | ADR-0003, Constitution II |
| Container and `AddIskender(...)` embedding are both shipped, with the same pipeline contracts; embedding must not require global service ownership | ADR-0004, Constitution I and II |
| .NET 10 / ASP.NET Core is the runtime; native interop must not rewrite domain or safety contracts | ADR-0002 |
| Organization → project → resource ownership is non-bypassable; every boundary carries authorization | ADR-0005, Constitution V |
| The module table, and each module's "Owns", "May depend on" and "Must not do" columns | `docs/03` |
| No cloud-specific classes in domain or public SDK; no private storage models in public packages; provider-specific registration stays conditional | `docs/03`, `docs/04` |
| Architecture tests enforce module boundaries and dependency direction | Constitution II gate, `docs/03` |
| Merged specs publish only `-alpha` prereleases with no compatibility commitment until ISK-0086 | ADR-0030 |

### Technical recommendations (proposed by this spec; open to change in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | Add `Iskender.Hosting`, an empty embeddable library that is the home of Hosting/Ingress | `AddIskender(...)` cannot live in `Iskender.Gateway.Host`, which is an executable that nothing may reference | Mapping Hosting/Ingress to the host, which would make it impossible to embed |
| TR-2 | Add `Iskender.ControlPlane.Host`, an empty second executable, and make `Iskender.Gateway.Host` the data-plane host | Plane independence can only be checked by the graph if both planes exist in it | One host holding both planes, which is the conflict this spec exists to remove |
| TR-3 | Host most modules as namespaces inside `Iskender.Domain`, separated by namespace rules | `docs/03` allows assemblies or namespaces, and one project per module adds build cost with no current benefit | One project per module |
| TR-4 | Module namespaces may not reference one another; the initial module-to-module allow-list is empty | The `docs/03` "May depend on" column names no other Domain module | Allowing references and relying on review |
| TR-5 | Keep the rules, the allow-list, and the module map in one machine-readable source that the tests consume | ISK-0000 repeats the allow-list in contract prose and test code, which can drift apart | Keeping both by hand |
| TR-6 | Treat a package as embeddable only if everything it references is packable too | An embedded consumer cannot restore a package whose dependency was never packed | Deciding this in ISK-0006, which would ship broken packages in prereleases until then |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | `Iskender.Domain` can stay transport-neutral, with the ASP.NET Core Identity store adapter in `Iskender.Infrastructure` | ISK-0013; if false, a contract revision moves the edge |
| AV-2 | Policy handlers and provider adapters need no ASP.NET Core types in their public contracts | ISK-0030, ISK-0052 |
| AV-3 | Later modules can work through contracts and ports alone. Where one truly needs another (for example, Registry and Identity), a reviewed contract revision adds the edge | ISK-0011, ISK-0012, ISK-0015 |
| AV-4 | The raised analyzer baseline can be met by the existing code with fixes rather than suppressions | This spec's implementation; a rule that cannot be met is reported, not silently suppressed |

### Post-MVP work (named so that it is not designed here)

Native or Rust hot-path bridges (ADR-0002), out-of-process plugin isolation, general HTTP gateway modules (such as ISK-0113), and any split of `Iskender.Domain` into per-module assemblies. Each would arrive through its own spec and a revision of this contract.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Every module has one known home (Priority: P1)

An author starting a later spec reads one table and knows which assembly and namespace their module belongs to, what it may reference, and who reviews a change to it.

**Why this priority**: Every later spec places code according to this map. Without it, the first few specs would each decide independently and the boundaries would drift. It is also named acceptance evidence for this spec.

**Independent Test**: Compare the module map with the `docs/03` Module boundaries table. Delete an entry, or add a row to a copy of the table, and confirm the architecture tests fail and name the module.

**Acceptance Scenarios**:

1. **Given** the `docs/03` module table, **When** the architecture tests run, **Then** each of its 12 modules resolves to exactly one home that exists in the project graph.
2. **Given** a `docs/03` module with no map entry, **When** the tests run, **Then** they fail and name that module.
3. **Given** a map entry naming a project that does not exist, or a module that `docs/03` does not list, **When** the tests run, **Then** they fail and name the entry.
4. **Given** the `docs/03` table cannot be found or yields no modules, **When** the tests run, **Then** they fail rather than pass with nothing checked.

---

### User Story 2 - Forbidden references are rejected (Priority: P1)

An implementer who adds a reference that crosses a module boundary is stopped by the test run before review, with a message saying which rule was broken and how to fix it.

**Why this priority**: This is the enforcement behind the Constitution II gate and the spec's named acceptance evidence. A boundary that only reviewers check will not hold across 125 specs.

**Independent Test**: For every forbidden edge in the contract, apply the violation to a fixture and confirm that a test fails with the expected rule ID.

**Acceptance Scenarios**:

1. **Given** a production project with a reference that is not on the allow-list, **When** the tests run, **Then** they fail with the rule ID, the source, the target, and the contract's location.
2. **Given** several violations, **When** the tests run, **Then** all of them are reported in one run, in a stable order.
3. **Given** a type in one `Iskender.Domain` module namespace that uses a type in another, **When** the tests run against a fixture containing it, **Then** they fail and name both namespaces.
4. **Given** a reference disguised by a build condition, an imported build file, a direct assembly reference, a package name matching an `Iskender.*` project, or a project placed outside `src/` and `tests/`, **When** the tests run, **Then** it is detected as a violation.
5. **Given** the real repository at this spec's merge, **When** the tests run, **Then** no violation is reported.

---

### User Story 3 - Refined edges let the planes deploy apart (Priority: P2)

An architecture owner can see from the graph that the data plane builds without the Management surface and the control plane builds without the ingress surface, and that the Domain and Management edges match `docs/03`.

**Why this priority**: ISK-0006, ISK-0007, ISK-0030, ISK-0052, and ISK-0080 build on these edges, and without this work each would inherit the conflict. It depends on the rules in User Story 2 to stay true.

**Independent Test**: Build each host from a clean tree. Compute each host's transitive references and compare them with the contract.

**Acceptance Scenarios**:

1. **Given** the refined graph, **When** the solution is built from a fresh clone, **Then** the build passes with zero warnings.
2. **Given** `Iskender.Gateway.Host`, **When** its transitive references are computed, **Then** they do not include `Iskender.Management` or `Iskender.ControlPlane.Host`.
3. **Given** `Iskender.ControlPlane.Host`, **When** its transitive references are computed, **Then** they do not include `Iskender.Hosting` or `Iskender.Gateway.Host`.
4. **Given** `Iskender.Management`, **When** a fixture adds a reference to `Iskender.Infrastructure`, **Then** the tests fail, because Management must not bypass domain validation with raw storage updates.

---

### User Story 4 - Build conventions cannot be weakened quietly (Priority: P2)

A maintainer can rely on every project using the same pinned versions, analyzers, and nullability settings, because a project that opts out fails the tests.

**Why this priority**: These settings already exist, but nothing stops one project from overriding them. Later specs add packages and code quickly, which is when overrides tend to appear.

**Independent Test**: Apply each protected-setting violation to a fixture project and confirm the tests fail with the expected rule ID.

**Acceptance Scenarios**:

1. **Given** a project that states a package version itself, **When** the tests run, **Then** they fail and name the project and package.
2. **Given** a project that turns off nullability, warnings as errors, or analyzers, or that changes the target framework, **When** the tests run, **Then** they fail and name the project and setting.
3. **Given** a source file that disables nullable checks, **When** the tests run, **Then** they fail and name the file.
4. **Given** a transport-neutral project that takes a cloud SDK, provider SDK, or database driver, **When** the tests run, **Then** they fail and name the project and package.

---

### User Story 5 - Embeddable packages are complete (Priority: P3)

A consumer embedding Iskender gets packages whose dependencies were all packed, and a maintainer can see which projects are executables, which are embeddable, and which public contract surfaces fall under ISK-0002's compatibility rules.

**Why this priority**: The first working `AddIskender(...)` arrives in ISK-0006, so this is groundwork, but the release workflow already packs every packable project and attaches the packages to the prerelease for each merged spec.

**Independent Test**: Pack the solution and confirm that each package's `Iskender.*` dependencies were also produced.

**Acceptance Scenarios**:

1. **Given** a packable project that references a non-packable one, **When** the tests run, **Then** they fail and name both.
2. **Given** an executable host or a test project marked packable, **When** the tests run, **Then** they fail and name it.
3. **Given** a pack of the solution, **When** its packages are inspected, **Then** every `Iskender.*` dependency of every package is also in the output.

---

### Edge Cases

- **A production project escapes by name**: a project under `src/` named `*.Tests` must not gain the test-project exemption. The exemption depends on the project being under `tests/`.
- **Assembly identity is changed**: a project whose assembly name or root namespace differs from its project name is a violation, because every rule is written in terms of those names.
- **Internals are shared across a boundary**: a production assembly that grants `InternalsVisibleTo` to another production assembly is a violation. Granting it to a test project is allowed.
- **A rule passes because nothing exists yet**: type-level rules pass on an empty namespace, so an empty assembly is never counted as evidence. Each rule also has a fixture that proves it fails on a real violation.
- **A cycle arrives through a new edge**: the refined graph adds edges, so cycle detection must cover the whole graph, including `Iskender.Hosting` and `Iskender.ControlPlane.Host`.
- **A test project references a host**: this remains forbidden. Later host tests must test through a library or run the process, as ISK-0006 and ISK-0007 decide.
- **Case differences in project names or paths**: Windows and Linux must get the same result. A reference written with backslashes or different casing resolves to the same project.
- **Unparseable project file**: it fails the test run and names the file, rather than being skipped.
- **A new provider adapter project**: a project named `Iskender.Providers.<Provider>` is approved by pattern, with its references limited as in FR-009. Any other new production project fails until the contract adds it.
- **Runtime reflection**: `Assembly.Load` and similar calls cannot be seen by reference checks. This limit is recorded in the contract, and `docs/13` keeps unreviewed reflection out of the request path by review.

## Requirements *(mandatory)*

### Functional Requirements

**Contract and ownership**

- **FR-001**: This spec MUST publish `contracts/module-dependency-rules.md`, labelled version 2, stating that it supersedes the ISK-0000 version 1 baseline. Nothing under `specs/0000-repo-bootstrap/` may change.
- **FR-002**: The rules, the allow-list, and the module map MUST have exactly one machine-readable source, which the architecture tests consume. The contract's prose MUST either be generated from that source or be checked against it by a test.
- **FR-003**: That source MUST be covered by `.github/CODEOWNERS`, along with every new or currently uncovered `src/` project, so that loosening a rule needs Architecture (ADR) owner review.
- **FR-004**: A change to an approved edge or module home MUST be made through a spec's contract revision that increments the version and records the edge, its reason, and its review owner. A later spec may make this change.

**Module map**

- **FR-005**: Every module in the `docs/03` Module boundaries table MUST map to exactly one home, as follows:

  | `docs/03` module | Assembly | Namespace root |
  |---|---|---|
  | Hosting/Ingress | `Iskender.Hosting` | `Iskender.Hosting` |
  | Identity/Authorization | `Iskender.Domain` | `Iskender.Domain.Identity` |
  | Registry/Configuration | `Iskender.Domain` | `Iskender.Domain.Registry` |
  | Policy Engine | `Iskender.Domain` | `Iskender.Domain.Policy` |
  | Routing/Resilience | `Iskender.Domain` | `Iskender.Domain.Routing` |
  | Providers | `Iskender.Providers.Abstractions`, with adapters in `Iskender.Providers.<Provider>` | `Iskender.Providers` |
  | Conversations | `Iskender.Domain` | `Iskender.Domain.Conversations` |
  | Governance | `Iskender.Domain` | `Iskender.Domain.Governance` |
  | Telemetry/Audit | `Iskender.Domain` | `Iskender.Domain.Telemetry` |
  | Management | `Iskender.Management` | `Iskender.Management` |
  | Plugins | `Iskender.Domain` | `Iskender.Domain.Plugins` |
  | Infrastructure | `Iskender.Infrastructure` | `Iskender.Infrastructure` |

- **FR-006**: The map MUST also place the `docs/03` illustrative ports and the request context, without defining their signatures, which later specs freeze:

  | Item | Home | Signature owner |
  |---|---|---|
  | `GatewayExecutionContext` and capability-bounded views | `Iskender.Core.Contracts` | ISK-0002 |
  | `ICredentialResolver` (secret handles) | `Iskender.Core.Contracts` | Later identity and credential specs |
  | `IProviderAdapter` | `Iskender.Providers.Abstractions` | ISK-0030 |
  | `IPolicyHandler` | `Iskender.Policy.Sdk` | ISK-0052 |
  | `IBackendSelector` | `Iskender.Domain.Routing` | Later routing specs |
  | `IConversationStore` | `Iskender.Domain.Conversations` | Later conversation specs |
  | `IQuotaCoordinator`, `IUsageLedger` | `Iskender.Domain.Governance` | Later governance specs |
  | `IPolicyPlanPublisher` | `Iskender.Domain.Policy` | Later policy specs |
  | `IPluginTrustStore` | `Iskender.Domain.Plugins` | Later plugin specs |
  | Ports shared across modules, such as persistence (ISK-0004) and coordination (ISK-0005) | `Iskender.Domain.Ports` | Those specs |

- **FR-007**: The tests MUST compare the map with the `docs/03` table on every run, and fail with the module's name on any missing, extra, or duplicate entry, or if the table cannot be read.

**Refined graph**

- **FR-008**: The solution MUST add `Iskender.Hosting` (library) and `Iskender.ControlPlane.Host` (executable), neither containing gateway behavior. `Iskender.Gateway.Host` MUST become the data-plane host. Entry-point behavior, containers, and `AddIskender(...)` remain with ISK-0006 and ISK-0007.
- **FR-009**: The approved direct references MUST be exactly the following, with anything else rejected:

  | Project | May reference |
  |---|---|
  | `Iskender.Core.Contracts` | (nothing internal) |
  | `Iskender.Providers.Abstractions` | `Core.Contracts` |
  | `Iskender.Policy.Sdk` | `Core.Contracts` |
  | `Iskender.Providers.<Provider>` (pattern; none exist yet) | `Providers.Abstractions`, `Core.Contracts` |
  | `Iskender.Domain` | `Core.Contracts`, `Providers.Abstractions`, `Policy.Sdk` |
  | `Iskender.Infrastructure` | `Domain`, `Core.Contracts` |
  | `Iskender.Management` | `Domain`, `Core.Contracts` |
  | `Iskender.Hosting` | `Domain`, `Core.Contracts` |
  | `Iskender.Gateway.Host` (data plane) | `Hosting`, `Domain`, `Infrastructure`, `Providers.Abstractions`, `Policy.Sdk`, `Core.Contracts`, `Iskender.Providers.<Provider>` |
  | `Iskender.ControlPlane.Host` | `Management`, `Domain`, `Infrastructure`, `Core.Contracts` |

- **FR-010**: Rules A1 to A5 from the baseline MUST be kept under their existing IDs, updated to the new graph. New rules MUST take new IDs, and an ID MUST never be reused.
- **FR-011**: The rules MUST check transitive references as well as direct ones, so that:
  - `Iskender.Gateway.Host` reaches neither `Iskender.Management` nor `Iskender.ControlPlane.Host`.
  - `Iskender.ControlPlane.Host` reaches neither `Iskender.Hosting` nor `Iskender.Gateway.Host`.
  - `Iskender.Hosting` reaches neither `Iskender.Infrastructure` nor `Iskender.Management`, because embedding must not impose storage or the control plane.
  - `Iskender.Domain`, `Iskender.Policy.Sdk`, and `Iskender.Providers.*` do not reach `Iskender.Infrastructure`, `Iskender.Management`, `Iskender.Hosting`, or a host.
  - `Iskender.Domain` and `Iskender.Hosting` do not reach a provider adapter, because provider registration stays conditional.
- **FR-012**: Each host MUST be required to reference what its role composes: `Iskender.Hosting` and `Iskender.Infrastructure` for the data plane, and `Iskender.Management` and `Iskender.Infrastructure` for the control plane. This replaces the ISK-0000 requirement that the host reference every project.

**Boundary hardening**

- **FR-013**: The checks MUST cover every project file in the repository, except build output, regardless of directory. They MUST see references added through build conditions and imported build files, and MUST reject a direct assembly reference to, or a package reference named like, an `Iskender.*` project.
- **FR-014**: Only projects under `tests/` get the test-project exemption, and it never covers rule A2.
- **FR-015**: Each production project's assembly name and root namespace MUST equal its project name.
- **FR-016**: A production assembly MUST NOT grant `InternalsVisibleTo` to another production assembly.
- **FR-017**: Within `Iskender.Domain`, a type in a module namespace MUST NOT use a type in another module's namespace unless the contract's module allow-list permits it. That list starts empty (TR-4). `Iskender.Domain.Ports` MAY be used by every module and MUST NOT use any module namespace.
- **FR-018**: `Iskender.Core.Contracts`, `Iskender.Policy.Sdk`, `Iskender.Providers.Abstractions`, and `Iskender.Domain` MUST stay transport-neutral and cloud-neutral, so they MUST NOT take the ASP.NET Core framework reference, a cloud SDK, a provider SDK, a database driver, or a data-access library. Provider adapters MAY take their own provider's SDK, which MUST NOT appear in any other project. The contract holds the denied package prefixes.

**Build conventions**

- **FR-019**: Every project MUST take package versions only from central management. A version stated in a project, a version override, or disabling central management MUST fail the tests.
- **FR-020**: Central pinning MUST extend to transitive packages, so that a transitive version changes only through a reviewed change.
- **FR-021**: The analyzer baseline MUST be stated explicitly, so that an SDK upgrade cannot change it silently, and MUST include code-style enforcement during the build.
- **FR-022**: No project may override the target framework, language version, nullability, warnings as errors, analyzers, deterministic build, or lock-file settings. No source file may disable nullable checks.
- **FR-023**: Warning suppressions MUST be listed in the contract, each with a reason. The existing `NU1900` exception stays, for the reason recorded in `Directory.Build.props`. An unlisted suppression in a project file or source file MUST fail the tests.
- **FR-024**: New package pins MUST be limited to what this spec's tests need. No provider, database, cloud, or coordination package may be pinned by this spec.

**Packaging and public compatibility**

- **FR-025**: The contract MUST classify every project as one of: *public contract* (`Core.Contracts`, `Providers.Abstractions`, `Policy.Sdk`), *embeddable* (`Hosting`, `Domain`, `Infrastructure`, `Management`, and any `Iskender.Providers.<Provider>`), *executable* (both hosts), or *test*. Only the first two may be packable, and all of them MUST be.
- **FR-026**: Every project reference from a packable project MUST target a packable project.
- **FR-027**: This spec MUST NOT add or change any public type outside the new host's shell. The only change to published output is the set of `-alpha` packages, which ADR-0030 gives no compatibility commitment. Compatibility rules for public contract packages remain with ISK-0002. Being packable does not make an embeddable project's types a supported public API.
- **FR-028**: The new executable MUST get the same CI provenance and checksum treatment as `Iskender.Gateway.Host`, and `src/Iskender.Hosting/**` and `src/Iskender.ControlPlane.Host/**` MUST be behavior-bearing paths in the documentation merge gate and its description in `CONTRIBUTING.md`.

**Tests and diagnostics**

- **FR-029**: Each rule MUST have at least one negative fixture proving it fails on a real violation. Fixtures MUST live outside `src/`, ship in no package, use no network or credentials, and include type-level and on-disk project examples as well as synthetic graphs.
- **FR-030**: ISK-0000 tests that encode the superseded baseline MUST be changed, not deleted or disabled, and each MUST end up at least as strict. These are `HostComposesEverySkeletonProject`, the `Management → Domain` case in `A5UnapprovedEdgeFails`, and the source list in `SkeletonContainsNoGatewayBusinessCode`.
- **FR-031**: Every violation message MUST state the rule ID, the source, the target or setting, and the contract's path and version. Violations MUST be listed in a stable order, all in one run.
- **FR-032**: Missing inputs (the contract's rule source, the `docs/03` table, or a project file that cannot be read) MUST fail the run and name what is missing. A run that finds no projects or no modules MUST fail.
- **FR-033**: These rules MUST run under the existing CI check "5. Architecture and dependency rules". This spec adds no separate CI check.

**Documentation and scope**

- **FR-034**: `README.md` MUST describe the new projects and link to this spec's contract, and `docs/03` MUST link from its Module boundaries section to the module map. These are non-material changes that reflect approved implementation.
- **FR-035**: The contract MUST trace each `docs/03` architectural test expectation to its status:

  | `docs/03` expectation | Status |
  |---|---|
  | Package dependency direction | Enforced here |
  | No direct provider SDK types in public common contracts | Enforced here at package level (FR-018); type-level follow-up with ISK-0030 |
  | No cross-module database table updates | Deferred to ISK-0004, since no tables exist |
  | Organization scope on every query and event | Deferred to ISK-0004 and the specs that add queries and events |
  | Same request lifecycle in embedded and container hosts | Deferred to ISK-0006 and ISK-0007 |
  | Two independent replicas against one resource snapshot | Deferred to the spec that introduces published snapshots |

- **FR-036**: The implementation MUST NOT add gateway, provider, policy, or database behavior, or any database table, and MUST NOT recreate what ISK-0000 delivered. Any interaction with CEL policies, plugin trust, durable conversations, financial controls, or streaming is limited to naming the home assembly, and those behaviors remain governed by `docs/06`, `docs/08`, `docs/09`, and `docs/11`.

### Negative Security and Failure Scenarios

Each of these is covered by a negative fixture (FR-029):

| Scenario | Risk if allowed | Rule source |
|---|---|---|
| A provider adapter references `Iskender.Domain` | An adapter could own tenancy or conversations, or treat provider IDs as ownership | FR-009, FR-011 |
| `Iskender.Policy.Sdk` references `Iskender.Domain` or `Iskender.Infrastructure` | Plugin authors compile against internals, which defeats capability-bounded views | FR-009, FR-011 |
| `Iskender.Management` references `Iskender.Infrastructure` | Admin operations bypass domain validation and ownership checks | FR-009, FR-011 |
| The data-plane host reaches `Iskender.Management` | Management surface and its dependencies ship in the internet-facing process | FR-011 |
| `Iskender.Domain` takes a cloud SDK or database driver | Cloud lock-in, with storage models leaking into public packages | FR-018 |
| A production assembly grants `InternalsVisibleTo` to another | The boundary holds in the graph but not in practice | FR-016 |
| A project under `src/` is named `*.Tests` | It escapes the allow-list | FR-014 |
| The rule source is loosened without an owner's review | Enforcement is removed by the change it should stop | FR-003 |

### Key Entities

- **Module**: a row of the `docs/03` Module boundaries table, with its ownership and constraints.
- **Module home**: the assembly and namespace root that a module maps to.
- **Project role**: public contract, embeddable, executable, or test.
- **Approved edge**: an allowed direct reference between projects, or between module namespaces.
- **Rule**: a stable ID, a statement, and at least one negative fixture.
- **Violation**: a rule ID, a source, a target or setting, and the contract's path and version.
- **Contract version**: the number identifying a revision of the rules. Version 1 is the ISK-0000 baseline, and this spec publishes version 2.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone with no build output and no cloud credentials, restore, format check, build, and test all pass with zero warnings, and all CI checks pass.
- **SC-002**: 12 of 12 `docs/03` modules map to exactly one home that exists in the project graph, and adding, removing, or duplicating a module or map entry fails the tests.
- **SC-003**: 100% of the rules have a negative fixture, and each fixture's test reports the expected rule ID. Every forbidden edge in the contract is rejected when applied to a fixture.
- **SC-004**: A reader of any violation message can identify the rule, the offending project or file, and the contract section to consult without opening the test code. All violations appear in one run.
- **SC-005**: The transitive references of the data-plane host include 0 control-plane projects, and those of the control-plane host include 0 ingress projects.
- **SC-006**: Every protected setting in FR-019 to FR-023 has a fixture that fails the tests when the setting is weakened.
- **SC-007**: Every `Iskender.*` dependency of every package in a pack of the solution is also in the pack's output.
- **SC-008**: The PR changes 0 files under `specs/0000-repo-bootstrap/`, 0 files under `.specify/` that Git tracks, and 0 in `.iskender/grounding.yaml`. It adds 0 provider, database, cloud, or coordination packages and 0 database tables. The only new production source file is the control-plane host's shell.
- **SC-009**: The full architecture test suite passes with the network disconnected, once packages are restored.

## Assumptions

- ISK-0000 is merged and its record is complete on `main`. The repository as it stands is the starting point.
- "Approved assembly or namespace" in the acceptance evidence means a home recorded in this spec's contract, reviewed by the Architecture (ADR) owner through this spec's PR.
- Namespaces without types cannot be observed in compiled output. Until later specs add types, the module map is checked as data and the namespace rule is proved on fixtures.
- Direct references are checked by allow-list and transitive references by the forbidden rules in FR-011, so code may use a type reachable through an approved reference without declaring a reference of its own.
- The all-in-one developer distribution in `docs/04` runs both hosts in one container and needs no project of its own. ISK-0007 and ISK-0008 own that packaging.
- Items TR-1 to TR-6 and AV-1 to AV-4 above are the intended inputs to the `clarify` stage.

## Out of Scope

- Provider adapters, provider business functionality, and provider SDK packages.
- Database tables, schemas, migrations, and data-access libraries.
- Working `AddIskender(...)` registration (ISK-0006), control-plane and data-plane entry-point behavior and containers (ISK-0007), and local Compose (ISK-0008).
- Contract types, signatures, and compatibility tooling for public surfaces (ISK-0002).
- Changing the constitution, the roadmap, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
- Detecting runtime reflection or dynamic loading.
