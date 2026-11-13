# Feature Specification: ISK-0006 — Host Composition & DI Registration

**Feature Branch**: `feature/0006-host-composition-di-registration`

**Created**: 2026-11-13

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0006 — Host Composition & DI Registration** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Make domain services composable for host-owned ASP.NET Core service collection and runtime options**. In-scope behaviors and artifacts: **AddIskender registration design; service lifetimes; minimal hosting sample; coexisting middleware**. Explicitly exclude: **No fully working provider ingress yet; no hidden singletons**. Prerequisites: **ISK-0001, ISK-0002, ISK-0003**; do not assume unavailable implementation already exists. ISK-0002's compatibility harness covers only projects with the `public contract` role, and `Iskender.Hosting` is not one, so making `AddIskender(...)` a supported public API needs a revision of the module dependency rules contract that brings it under the harness, as AV-5 in the public contracts and versioning contract states. ISK-0001 already created `Iskender.Hosting`, an empty, packable library that is the home of Hosting/Ingress, so build on it rather than creating another project. Follow the current module dependency rules contract, linked from the Module boundaries section of @docs/03-domain-and-technical-architecture.md: `Iskender.Hosting` may reference only `Iskender.Domain` and `Iskender.Core.Contracts`, so an embedder opts into storage or control-plane services by referencing `Iskender.Infrastructure`, an infrastructure adapter, or `Iskender.Management` itself, and the minimal hosting sample needs a location, role and edges added by a revision of that contract. ISK-0003 already published the configuration engine: modules declare sections with `SectionSchema` in `Iskender.Core.Contracts`, and the static `ConfigurationValidator` in `Iskender.Domain.Registry` validates a base document, an overlay document, and `ISKENDER__` environment variables into an immutable snapshot named by a `ConfigurationRevision`, or returns field errors and no snapshot. Follow the current configuration contract, linked from the Persistence, publication and distributed coordination section of @docs/03-domain-and-technical-architecture.md: `AddIskender(...)` supplies those sources and the active environment name without taking over the host's own configuration, and refusing to start on a rejected configuration belongs to the host, so settle that spec's assumptions AV-4 and AV-6 for embedded hosts. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Embedding host resolves modules without requiring standalone process**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Iskender ships in two ways of equal standing: a container image and an embedded library that an ASP.NET Core application composes with `AddIskender(...)` (ADR-0004, Constitution I and II). Embedding must not take global ownership of the host's services, routes, logging, or configuration, and must not need an external control plane (`docs/04` Deployable topologies 3). Today nothing composes Iskender: `Iskender.Hosting` is an empty packable library, both hosts are empty shells, and the configuration engine (ISK-0003), the persistence adapter (ISK-0004), and the coordination ports and coordinators (ISK-0005) exist only as types that tests construct by hand. ISK-0004 and ISK-0005 left their host composition to this spec and ISK-0007.

This spec gives a host application:

1. **One registration entry point**, `AddIskender(...)` on the host's own service collection, with runtime options that name the configuration sources and the active environment.
2. **Opt-in composition of modules and adapters**: an embedder that wants storage or coordination references `Iskender.Infrastructure` or an infrastructure adapter itself and registers it beside `AddIskender(...)`, because `Iskender.Hosting` reaches none of them.
3. **Explicit service lifetimes**: every Iskender service is owned by the host's container, so two containers in one process share nothing, and no state lives in a static or process-wide field.
4. **Host-owned start-up refusal**: configuration is validated through ISK-0003's engine when the host starts, and a rejected configuration stops the host from starting, with ISK-0003's error report and no configuration values.
5. **Coexisting middleware**: Iskender maps its own route group under a prefix the host chooses, adds no global middleware, and leaves every host route, middleware, and setting as it was.
6. **A minimal hosting sample** and an in-process embedded-host test fixture, which together are the acceptance evidence: an embedding host resolves Iskender's modules without a standalone process.
7. **A versioned public surface**: `AddIskender(...)` and its options become a supported public API under ISK-0002's compatibility harness.

This spec adds no provider ingress, inference endpoint, authentication scheme, readiness endpoint, or container entry point. A request under Iskender's route prefix runs through the shared request bootstrap and ends in the ISK-0002 error envelope, because no endpoint can resolve yet.

Project, package, and type names appear in this spec because they are its subject, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Iskender ships as a container and as an embedded NuGet library with `AddIskender(...)`, both honoring the same pipeline contracts. Embedded hosting must not require global service ownership | ADR-0004, Constitution I and II |
| Explicit modules with preserved dependency direction. Control plane and data plane are independently deployable | ADR-0003, Constitution II |
| In embedded use the host provides DI and may opt into control-plane services. The data plane must not take global ownership of routes, logging, or configuration. No hidden external control-plane requirement for configured embedded use. The exact public API signature is finalized in a spec | `docs/04` Deployable topologies 3 |
| A sample ASP.NET Core app demonstrates `AddIskender(...)` alongside user middleware and services with no fork of Iskender source. Provider-specific registration stays conditional | `docs/04` Package boundaries and composition |
| Hosting/Ingress owns host adapters, pipeline bootstrap, and protocol dispatch, and must not assume a standalone container or ASP.NET global static state | `docs/03` Module boundaries |
| A request-local scoped `GatewayExecutionContext` carries the request's state. Mutable per-request state is not shared across requests | `docs/03` Module boundaries, ISK-0002 contract Request context |
| `Iskender.Hosting` is the Hosting/Ingress assembly, may reference only `Iskender.Domain` and `Iskender.Core.Contracts`, and must reach neither `Iskender.Infrastructure`, an infrastructure adapter, `Iskender.Management`, nor a provider adapter (rules A5, A8, A10). It may take ASP.NET Core, and is denied the `cloud`, `provider`, `driver`, and `data-access` categories | ISK-0001 module dependency rules contract version 3 |
| A project of a kind the rules contract does not name, such as the `AddIskender(...)` hosting sample, gets a location, role, and edges through a revision of that contract by the spec that adds it | ISK-0001 contract, Left to later specs |
| The compatibility harness covers only `public contract` projects. Making `AddIskender(...)` a supported public API needs a revision of the module dependency rules contract that brings `Iskender.Hosting` under the harness | ISK-0002 contract, Harness and Known limits (AV-5) |
| New error codes go in `ErrorCodes` under existing categories, and a change to `Iskender.Core.Contracts`' surface or registries increments `core-contracts` | ISK-0002 contract, Versioning policy |
| Configuration is validated by `ConfigurationValidator` from at most one base document, one overlay document, and `ISKENDER__` environment variables, with the active environment name supplied by the host. Validation reads no host configuration and never changes it. Refusing to start on a rejected configuration is the host's job | ISK-0003 configuration contract, Inputs, Overlays and environment variables, Embedding |
| Only `development` permits development-only values. Any other name, and no name, is production-grade | ISK-0003 contract, Active environment |
| Persistence and coordination credentials come through host-implemented ports (`IDatabaseCredentialProvider`, `ICoordinationCredentialProvider`), with no production implementation until ISK-0021 | ISK-0004 and ISK-0005 contracts |
| Migrations run only as an explicit operation, never on start-up. Liveness never calls a health probe. Readiness mapping belongs to ISK-0007 | ISK-0004 and ISK-0005 contracts, `docs/04` |
| Mandatory authentication is a non-optional platform invariant, and fail-closed controls never degrade to allow-all | Constitution V and VI, `docs/03` Stream invariants |
| Payload logging is off by default, metric cardinality is bounded and sensitive-safe, and no performance figure is asserted without a reviewer-approved baseline | Constitution V and VIII |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | `AddIskender(...)` is an extension method on the host's `IServiceCollection`, in `Iskender.Hosting`. It takes a delegate that sets **Iskender runtime options**, and returns a builder through which the host adds Iskender-specific registrations. A companion `MapIskender(...)` on the host's endpoint route builder maps Iskender's route group (TR-8). Both are called by the host, in its own `Program.cs`, and neither builds or starts a host | The ASP.NET Core convention for libraries. The host keeps control of its container and pipeline (ADR-0004) | A custom host builder or a `UseIskender()` that owns `WebApplication`, which takes over the host |
| TR-2 | **Configuration sources** (settles ISK-0003 AV-4 for embedded hosts). **Decided by the maintainer in `specify` on 2026-11-13:** `AddIskender(...)` reads only the sources the host names explicitly in the options: at most one base document and at most one overlay document, each given as a file path, text, or stream with its source name and format, and, only when the host opts in, the process's `ISKENDER__` environment variables. It never reads the host's `IConfiguration`, offers no bridge from it, and never changes the host's configuration | The configuration contract types scalars by schema and reports source, line, and column. A host's `IConfiguration` holds every value as text and drops locations and nulls, so a bridge would be lossy, and reading it would let an unrelated host setting change Iskender | A bridge from the `iskender` section of the host's `IConfiguration`, alone or beside explicit sources |
| TR-3 | **Active environment name**. **Decided by the maintainer in `specify` on 2026-11-13:** the host sets Iskender's active environment name explicitly in the options. When it is unset, there is no name, which ISK-0003 treats as production-grade. It is never derived from the host's own environment name, so a host running in `Development` does not permit Iskender's development-only values unless the host also sets Iskender's name to `development`. The name is checked by ISK-0003's rules, and an invalid name rejects the configuration | Only `development` permits development-only values such as TLS `disable`, so a host setting must never relax Iskender's security defaults by accident | Deriving the name from the host's environment, with or without an extra opt-in for `development` |
| TR-4 | **Precedence** (settles ISK-0003 AV-6 for embedded hosts): the embedded host uses ISK-0003's precedence, defaults, base, overlay, environment, with no further layer. Values a host builds in code reach Iskender as the text of the base or overlay document, never as a fifth layer | One precedence for every topology keeps container and embedded hosts producing the same revision from the same inputs | A code-options layer above environment variables, which would need a revision of the configuration contract and would make the revision depend on code |
| TR-5 | **Composition of modules and adapters**: each module or adapter assembly that has services to register offers its own registration method, called by the host beside `AddIskender(...)`, for example to add the PostgreSQL persistence adapter, the Redis coordinator, or the local coordination substitutes. Each registration contributes its `SectionSchema` and its services. The calls may come in any order, and the schema registry is built once from every contributed schema. No module or adapter is registered unless the host asks for it, and `Iskender.Hosting` gains no edge to `Iskender.Infrastructure`, an adapter, or `Iskender.Management`. The seam a registration uses is reachable through each assembly's existing approved references, so the rules contract adds no edge to `Iskender.Hosting` from any other project | `Iskender.Hosting` cannot reach storage or control-plane assemblies (A8), so the embedder opts in by referencing them, as ISK-0001 decided. Provider-specific and storage-specific registration stays conditional (`docs/04`) | Discovering modules by assembly scanning, which loads code the host did not ask for (`docs/03` Plugins: no scanning of arbitrary folders) |
| TR-6 | **Lifetimes**: (a) container-scoped singletons, owned and disposed by the host's container: the schema registry, the validated snapshot, the capability check results, the coordinators, connection pools, and data sources; (b) request-scoped: `GatewayExecutionContext`, created by the request bootstrap for one request and never shared; (c) no Iskender service is transient unless it is stateless. **No hidden singletons**: no Iskender type holds a service, configuration value, connection, cache, or counter in a static or process-wide field, and no service is created outside the host's container. A singleton never captures a scoped service. The host's container validation (scope validation and build-time validation) passes with every Iskender registration | Two hosts in one process, such as two test servers or a host with two independently configured gateways, must not share state. A static would also outlive the container that configured it | Static caches or ambient accessors, which `docs/03` forbids ("ASP.NET global static state") |
| TR-7 | **Start-up validation**: registration validates nothing and opens nothing. When the host starts, Iskender validates its configuration once, through `ConfigurationValidator`, with the host's `ILogger`. A valid configuration yields one snapshot, and its revision is the one every Iskender service of that container sees. A rejected configuration makes the host's start fail with a `GatewayException` whose diagnostics carry ISK-0003's text rendering, so the host's own start-up handling decides what happens next. Resolving an Iskender service that needs the snapshot before start-up validation has succeeded fails the same way, never with a partial configuration. Registration and validation open no network connection and read no secret | The configuration contract assigns start-up refusal to the host. Failing the host's own start keeps that decision in the host's code and tooling | Failing inside `AddIskender(...)`, which runs before the host can log; serving with defaults, which is a partly validated configuration |
| TR-8 | **Coexisting middleware**: `MapIskender(...)` maps one route group under a prefix the host chooses, and returns it so the host can add its own conventions to it. Iskender adds no global middleware, exception handler, authentication or authorization scheme, CORS policy, logging provider, telemetry exporter, or fallback route, and changes no host option. Requests outside the prefix never reach Iskender code. Inside the group, the request bootstrap establishes the correlation ID and a `GatewayExecutionContext`, and, since no endpoint can resolve yet, answers with the ISK-0002 error envelope for `core.resource.not_found` and the gateway correlation ID. Host middleware that runs before the group, such as the host's own authentication, runs as usual, but never replaces Iskender's mandatory authentication, which the identity specs add inside the group | A host must keep its own routes, error pages, and policies (`docs/04`), and the group gives later specs one place to add ingress | Global middleware that inspects every request, which is the "global ownership of routes" `docs/04` forbids |
| TR-9 | **Public surface**: through a revision of the module dependency rules contract (version 4), `Iskender.Hosting` takes the `public contract` role and joins the compatibility harness as contract `hosting`, version 1, with a committed surface baseline. Its public surface exposes only its own types, `Iskender.Core.Contracts` types, and .NET and ASP.NET Core types, never an `Iskender.Domain` type, because `Iskender.Domain` is not versioned. Registration methods in `Iskender.Infrastructure` and its adapters stay outside the harness, as embeddable packages (AV-3) | AV-5 of the public contracts contract asks for exactly this revision. Keeping `Iskender.Domain` types out of the surface stops an unversioned assembly from leaking into a versioned one | Leaving `AddIskender(...)` outside the harness, which makes the embedded API change without notice |
| TR-10 | **Hosting sample**. **Decided by the maintainer in `specify` on 2026-11-13:** the sample lives in a new top-level `samples/` directory, with a new `sample` role, added by the rules contract revision (FR-022). A `sample` project is not packable. It may reference `Iskender.Hosting`, `Iskender.Core.Contracts`, `Iskender.Infrastructure`, and infrastructure adapters, and nothing else internal. Only test projects may reference it, so tests host it in process. It is checked by the same build conventions and deny-list rules as production projects, with the denied categories of an executable. Rule A14 is extended so that a project may live under `samples/`. The sample is an ordinary ASP.NET Core application with its own route, middleware, service, and configuration key, which calls `AddIskender(...)` and `MapIskender(...)` and changes no Iskender source | `docs/04` requires the sample, and the rules contract asks this spec to place it. A separate role keeps it out of packages and out of the hosts' plane rules, while letting the acceptance test run it in process | A test-role project under `tests/`, which escapes the production rules and reads as test code; an executable under `src/`, which rule A2 stops any test from referencing |
| TR-11 | **Embedded-host test fixture**: a reusable in-process fixture, in a test project, that builds a host with `AddIskender(...)` and given configuration sources, starts it on an in-memory server, and exposes its container and client. Later specs use it to prove the same request lifecycle in embedded hosts, and ISK-0007 pairs it with the container hosts | The acceptance evidence must run without a standalone process, and `docs/03` asks for the same lifecycle test in embedded and container hosts | Per-test ad hoc hosts, which drift |
| TR-12 | **Error codes**, added to `ErrorCodes` under existing ISK-0002 categories: `hosting.configuration_rejected` (`internal`, start-up failed because configuration was rejected), `hosting.registration_duplicate` (`internal`, `AddIskender(...)` or one module registration called twice on one container), and `hosting.registration_missing` (`internal`, `MapIskender(...)` or a module registration used without `AddIskender(...)`). Each has a safe message with no value, path, or host detail. This raises `core-contracts` from 4 to 5 with compatible changes only | Hosts and tests rely on codes, never on message text (ISK-0002). No new category is needed | Raw `InvalidOperationException`, which has no stable code |
| TR-13 | **Diagnostics**: fixed log events with stable IDs from 6001, written through the host's `ILogger`: composition summary at start (registered sections with versions, registered modules and adapters, environment name, configuration revision), start-up refused (error count), duplicate registration, and missing registration. No event holds a configuration value, secret reference name, connection detail, or route value from a request. ISK-0003's events 3001 to 3003 are written as that contract defines. Iskender adds no logging provider, metric exporter, or trace exporter | An operator must tell from the log which modules a host composed and why it did not start | Console output, which bypasses the host's logging |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | ISK-0007's data-plane host can be built from `AddIskender(...)` and `MapIskender(...)` with its own configuration roots mapped onto the base and overlay documents, so the embedded and container hosts share one request bootstrap, and ISK-0007 adds the cross-host lifecycle test | ISK-0007 |
| AV-2 | ISK-0007 can map the start-up failure in TR-7, the capability check results, and the persistence and coordination health reasons onto start-up and readiness, without this spec adding readiness endpoints | ISK-0007 |
| AV-3 | The registration methods of `Iskender.Infrastructure` and its adapters can stay outside the compatibility harness until a later spec, or ISK-0086, decides whether embeddable packages gain a compatibility commitment. Until then they are documented as prerelease, under ADR-0030 | ISK-0086 |
| AV-4 | Later identity, endpoint, and ingress specs (ISK-0011 onward, ISK-0030 onward) can add authentication, endpoint resolution, and protocol dispatch inside the route group from TR-8 without changing `AddIskender(...)`'s surface in a breaking way | Those specs |
| AV-5 | `Iskender.Management`'s control-plane services, when they exist, can be composed into an embedded host through the same pattern as TR-5, so that a host opts into them by referencing `Iskender.Management` and calling its registration | The first spec that adds control-plane services |
| AV-6 | The packages this spec adds to a `src/` project, such as the dependency injection abstractions an adapter needs for its registration method and ASP.NET Core in `Iskender.Hosting`, declare an SPDX expression already on the `check-license.sh` list | This spec's plan |

**ISK-0003's AV-4 and AV-6 are settled for embedded hosts by this spec** (TR-2, TR-3, TR-4). ISK-0007 settles them for the container entry points.

**ISK-0002's AV-5 is settled by this spec** through the rules contract revision in TR-9.

### Post-MVP work (named so that it is not designed here)

Several independently configured gateways in one host container. Hot reload of configuration in an embedded host, which belongs to ISK-0057's publication and activation. A bridge to configuration providers beyond those this spec chooses. A compatibility commitment for embeddable packages. Embedding in non-ASP.NET Core hosts, such as a worker service without HTTP. Source-generated or trimmed registration for native ahead-of-time compilation.

## Clarifications

### Session 2026-11-13 (specify)

- Q: Where should `AddIskender(...)` get Iskender's configuration documents? → A: Only from sources the host names explicitly in the options: a base and an overlay document, each a file path, text, or stream, and the process's `ISKENDER__` variables only when the host opts in. The host's `IConfiguration` is never read (TR-2).
- Q: How is Iskender's active environment name set in an embedded host? → A: Only explicitly in the options. Unset means no name, which is production-grade, and the host's own environment name never relaxes Iskender's defaults (TR-3).
- Q: Where does the minimal hosting sample live, and with what role? → A: In a new top-level `samples/` directory with a new `sample` role, which is not packable, may reference Hosting, Core.Contracts, Infrastructure, and adapters, and may be referenced only by tests (TR-10).

## User Scenarios & Testing *(mandatory)*

The users of this feature are .NET developers who embed Iskender in their own ASP.NET Core application, the authors of later specs who add modules and ingress, operators who read why an embedded host did not start, and reviewers who need evidence that embedding takes over nothing.

### User Story 1 - An embedding host resolves Iskender's modules in process (Priority: P1)

A developer adds the Iskender packages to an existing ASP.NET Core application, calls `AddIskender(...)` with a configuration document, opts into the local coordination substitutes, and starts the application. Iskender's modules resolve from the application's own container, in the application's own process, without any Iskender executable or container.

**Why this priority**: It is the acceptance evidence for this spec, and the base of every later embedded-host test.

**Independent Test**: With the embedded-host fixture, start the hosting sample in process with a valid configuration, and resolve each composed module's services from the host's container.

**Acceptance Scenarios**:

1. **Given** the hosting sample with a valid configuration, **When** it starts in process on an in-memory server, **Then** it resolves the configuration snapshot, its revision, the schema registry with every registered section, and the composed coordination services, and no Iskender process or container is running.
2. **Given** a host that registers the PostgreSQL persistence adapter with valid settings, **When** it starts and resolves the unit of work factory, **Then** resolution succeeds and no database connection has been opened.
3. **Given** a host that registers no adapter, **When** it starts, **Then** only the configuration engine and request bootstrap are composed, and resolving an adapter's port reports that it is not registered.
4. **Given** two hosts in one process with different configurations, **When** both start, **Then** each sees its own revision, and disposing one leaves the other working.

---

### User Story 2 - A rejected configuration stops the host from starting (Priority: P1)

A developer misspells a field, puts a plaintext password in a secret field, or sets a development-only value outside `development`. The host does not start, and its log says which field is wrong and where, without echoing any value.

**Why this priority**: Running on a partly valid configuration would weaken security defaults silently. The configuration contract assigns this to the host.

**Independent Test**: Start the fixture with each fixture configuration from ISK-0003's negative set, and check the start-up result and the captured log.

**Acceptance Scenarios**:

1. **Given** a configuration with a misspelled field, **When** the host starts, **Then** start fails with `hosting.configuration_rejected`, the exception's diagnostics carry ISK-0003's text rendering with `config.field.unknown`, and the log holds events 3002, 3003, and the start-up refused event.
2. **Given** a plaintext value in a `secretRef` field, **When** the host starts, **Then** start fails, and the value appears in no exception, diagnostic, or log line.
3. **Given** a valid configuration, **When** the host starts, **Then** start succeeds, event 3001 and the composition summary are logged once, and both name the same revision.
4. **Given** a host that resolves an Iskender service before start, **When** the configuration is rejected, **Then** resolution fails with `hosting.configuration_rejected` and no partial snapshot is returned.
5. **Given** any configuration, **When** `AddIskender(...)` runs, **Then** no document is read, no validation runs, and no connection is opened until the host starts.
6. **Given** a host running in its own `Development` environment that sets no Iskender environment name, **When** the configuration holds a development-only value, **Then** start fails with `hosting.configuration_rejected` and `config.value.unsafe_for_environment`.
7. **Given** `ISKENDER__` variables in the process and a host that did not opt into them, **When** the host starts, **Then** the snapshot records 0 environment variables applied, and none of their values reached the configuration.

---

### User Story 3 - Iskender coexists with the host's own middleware, routes, and configuration (Priority: P1)

The host has its own endpoints, middleware, exception handler, and configuration keys. After adding Iskender, all of them behave exactly as before, and Iskender answers only under the prefix the host chose.

**Why this priority**: Embedding must not take global ownership (ADR-0004, `docs/04`), and a host that changes behavior after adding a library will not adopt it.

**Independent Test**: Run the same requests against the sample with and without the Iskender registration, and compare responses, headers, and the host's configuration.

**Acceptance Scenarios**:

1. **Given** the sample's own route and middleware, **When** they are requested with Iskender registered and mapped, **Then** status, body, and headers are identical to a run without Iskender.
2. **Given** a request under Iskender's prefix, **When** it is handled, **Then** the response is the ISK-0002 error envelope for `core.resource.not_found`, with a gateway correlation ID in the `req_` form, and the host's middleware that runs before the group ran as usual.
3. **Given** a request outside the prefix that matches no host route, **When** it is handled, **Then** the host's own fallback answers, and no Iskender code ran.
4. **Given** the host's configuration, **When** the host has started, **Then** no host configuration key or value was added, removed, or changed, and no host option was changed.
5. **Given** the registered services, **When** they are listed, **Then** Iskender added no global middleware, authentication or authorization scheme, CORS policy, logging provider, or telemetry exporter.

---

### User Story 4 - Service lifetimes are explicit and nothing is hidden (Priority: P2)

An author of a later spec adds a service, and the rules for its lifetime are written down and checked. A reviewer can show that no Iskender service lives outside the host's container.

**Why this priority**: It is the "no hidden singletons" exclusion, and it protects every later module, but no user sees it directly.

**Independent Test**: Build the fixture with container validation on, scan Iskender assemblies for static mutable state, and run concurrent requests checking that no request sees another's context.

**Acceptance Scenarios**:

1. **Given** every Iskender registration, **When** the host's container is built with scope validation and build-time validation on, **Then** it builds with no error and no singleton captures a scoped service.
2. **Given** the production Iskender assemblies, **When** they are scanned, **Then** no type holds a static mutable field or a static service, configuration, connection, cache, or counter, other than the fixed registries and constants the contract lists.
3. **Given** 100 concurrent requests under the prefix, **When** they complete, **Then** each had its own `GatewayExecutionContext` and correlation ID, and none observed another's.
4. **Given** a disposed host, **When** its container is disposed, **Then** every disposable Iskender service it created is disposed, and nothing Iskender started keeps running.

---

### User Story 5 - Registration mistakes fail with a code (Priority: P3)

A developer calls `AddIskender(...)` twice, registers an adapter without `AddIskender(...)`, or maps Iskender without registering it. Each fails early with a stable code and a log event, instead of an obscure dependency injection error.

**Why this priority**: It improves the first-use experience, but correct use does not depend on it.

**Independent Test**: Build fixture hosts with each mistake and check the code and event.

**Acceptance Scenarios**:

1. **Given** `AddIskender(...)` called twice on one service collection, **When** the second call runs, **Then** it fails with `hosting.registration_duplicate`.
2. **Given** an adapter registration or `MapIskender(...)` without `AddIskender(...)`, **When** the host is built or the route is mapped, **Then** it fails with `hosting.registration_missing`.
3. **Given** the same adapter registered twice, **When** the second registration runs, **Then** it fails with `hosting.registration_duplicate`, so two schemas never claim one namespace.

---

### Edge Cases

- The host supplies no configuration source at all: validation runs on schema defaults alone, and a section with a required field rejects the configuration.
- A base or overlay file named in the options does not exist or cannot be read: start fails with ISK-0003's document error for that source, with its source name and no content.
- The host runs in `Development` and sets no Iskender environment name: Iskender has no name, so a development-only value rejects the configuration with `config.value.unsafe_for_environment` (TR-3).
- The host sets an Iskender environment name that is not a valid ISK-0003 name, such as `Staging_EU`: the configuration is rejected with `config.environment.invalid`, and start fails.
- An `ISKENDER__` variable is present in the process but the host did not opt into environment variables: it is ignored, and the snapshot records 0 environment variables applied (TR-2).
- The host's own configuration has a key named `iskender`: it is not read and not changed (TR-2).
- The host maps Iskender under `/` or under a prefix that overlaps one of its own routes: the host's more specific routes keep matching, and the documentation states that ASP.NET Core's routing precedence decides.
- The host calls `MapIskender(...)` twice with different prefixes: both groups share one container and one snapshot. This is allowed, and documented.
- A host stops while start-up validation is running: no snapshot is published, and nothing is left running.
- No credential provider is registered and the PostgreSQL adapter is: registration and start succeed, and the store health probe reports `authentication_failed` when called, as before ISK-0021 (`docs/04`).
- A request under the prefix carries a caller correlation header: it is kept or dropped by ISK-0002's rules, and the gateway ID stays authoritative.
- The host's container is built twice from one service collection: each container validates and holds its own snapshot.

## Requirements *(mandatory)*

### Functional Requirements

**Registration and options**

- **FR-001**: `Iskender.Hosting` MUST provide `AddIskender(...)` on the host's service collection and `MapIskender(...)` on the host's endpoint route builder as TR-1 describes, and MUST NOT build, start, or own a host.
- **FR-002**: The runtime options MUST let the host name at most one base document, at most one overlay document, opt-in reading of `ISKENDER__` variables, and the active environment name, as TR-2 and TR-3 decide. Iskender MUST NOT read or change the host's `IConfiguration`, and MUST NOT derive its environment name from the host's.
- **FR-003**: The embedded host MUST use ISK-0003's precedence with no further layer (TR-4).
- **FR-004**: Every module or adapter registration MUST be explicit and opt-in as TR-5 describes, MUST contribute its section schema, and MUST work in any order relative to `AddIskender(...)`. No assembly MUST be discovered by scanning.
- **FR-005**: This spec MUST provide registrations for the configuration engine and request bootstrap in `Iskender.Hosting`, the local coordination substitutes and capability checker in `Iskender.Infrastructure`, the PostgreSQL persistence adapter and coordinator in `Iskender.Infrastructure.PostgreSql`, and the Redis coordinator in `Iskender.Infrastructure.Redis`, without changing any ISK-0004 or ISK-0005 port signature or contract rule.
- **FR-006**: No project MUST gain an approved reference that the current rules contract does not list, except those the rules contract revision in FR-022 adds for the sample.

**Lifetimes**

- **FR-007**: Every Iskender service MUST have the lifetime TR-6 assigns it, and the host's container MUST build with scope validation and build-time validation on.
- **FR-008**: No production Iskender type MUST hold a service, configuration value, snapshot, connection, cache, or counter in a static or process-wide field. Two containers in one process MUST share no Iskender state.
- **FR-009**: `GatewayExecutionContext` MUST be created once per request by the request bootstrap and MUST NOT be reachable from another request.
- **FR-010**: Disposing the host's container MUST dispose every disposable Iskender service it created, and nothing Iskender started MUST outlive it.

**Start-up validation**

- **FR-011**: Registration MUST NOT read a document, run validation, open a network connection, or read a secret.
- **FR-012**: Configuration MUST be validated once per container when the host starts, through `ConfigurationValidator` and the host's `ILogger`, and every Iskender service in that container MUST see the one resulting snapshot and revision.
- **FR-013**: A rejected configuration MUST make the host's start fail with `hosting.configuration_rejected`, carrying ISK-0003's text rendering in its diagnostics, and MUST NOT leave any snapshot or Iskender service usable. A service resolved before successful validation MUST fail the same way.

**Coexisting middleware and request bootstrap**

- **FR-014**: `MapIskender(...)` MUST map one route group under a host-chosen prefix and return it to the host, and Iskender MUST add none of the global items TR-8 lists and MUST change no host option.
- **FR-015**: A request outside the prefix MUST NOT run any Iskender code. A request inside it MUST receive a gateway correlation ID and its own `GatewayExecutionContext`, and, until an ingress spec adds endpoints, the ISK-0002 error envelope for `core.resource.not_found`.
- **FR-016**: Nothing in this spec MUST let host middleware replace or disable Iskender's mandatory authentication once identity specs add it inside the group.

**Public surface and errors**

- **FR-017**: `Iskender.Hosting` MUST become a `public contract` project, joining the compatibility harness as contract `hosting` at version 1 with a committed baseline, and every public type MUST carry its version tag.
- **FR-018**: No public member of `Iskender.Hosting` MUST expose an `Iskender.Domain` type.
- **FR-019**: The error codes in TR-12 MUST be registered under the stated categories, each with a safe message, and the harness MUST report `core-contracts` raised from 4 to 5 with compatible changes only and no change to `providers-abstractions` or `policy-sdk`.

**Diagnostics**

- **FR-020**: The log events in TR-13 MUST be emitted with fixed IDs, names, levels, and templates through the host's `ILogger`, and MUST NOT contain a configuration value, secret reference name, connection detail, or request route value.
- **FR-021**: Iskender MUST NOT add a logging provider, metric exporter, or trace exporter to the host.

**Contracts, sample, fixtures, and documentation**

- **FR-022**: A revision of the module dependency rules contract to version 4 MUST give `Iskender.Hosting` the `public contract` role, give the hosting sample its location, role, and edges as TR-10 decides, update the rule source, and repoint the links that contract lists.
- **FR-023**: This spec MUST publish a hosting contract in its `contracts/` directory, owning `AddIskender(...)`, `MapIskender(...)`, the runtime options, lifetimes, start-up validation, the route group's behavior, the error codes, and the events.
- **FR-024**: The hosting sample MUST be an ordinary ASP.NET Core application with its own route, middleware, service, and configuration key, MUST call `AddIskender(...)` and `MapIskender(...)`, and MUST build and run in CI with no change to Iskender source.
- **FR-025**: The embedded-host fixture in TR-11 MUST start a host in process on an in-memory server, and the tests of User Stories 1 to 3 MUST run through it. Those tests are this spec's acceptance evidence.
- **FR-026**: Documentation MUST be updated in the same PR: `docs/03` and `docs/04` link to the hosting contract and the revised rules contract, and an embedding guide covers adding the packages, choosing modules and adapters, configuration sources and environment name, start-up failure, the route prefix, and what Iskender does not change in the host.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| A host runs Iskender on a partly valid configuration | Start fails, and no service is usable without a snapshot | FR-012, FR-013, User Story 2 |
| A configuration value or secret appears in a start-up failure | Only ISK-0003's value-free rendering and codes are emitted | FR-013, FR-020, SC-005 |
| A host's own development setting relaxes Iskender's production defaults | The name is set only explicitly, and unset is production-grade | FR-002, TR-3 |
| An unrelated host setting or environment variable changes Iskender configuration | Only the explicitly named documents are read, the host's `IConfiguration` never is, and `ISKENDER__` variables only on opt-in | FR-002, TR-2 |
| Iskender changes the host's configuration, routes, error handling, or authentication | Iskender adds no global item and changes no host option | FR-014, User Story 3 |
| One request sees another's tenant, correlation ID, or context | One context per request, never shared | FR-009, User Story 4 |
| Two embedded gateways in one process share a snapshot, cache, or counter | No static state, and each container owns its services | FR-008, User Story 1 |
| An adapter is loaded although the host did not ask for it | Registration is explicit, with no scanning | FR-004 |
| Registration opens a database or Redis connection, or reads a secret, before the host starts | Registration is inert, and adapters connect on first use | FR-011, User Story 1 |
| A singleton captures a scoped service and leaks request state | Container validation fails the build | FR-007 |
| `Iskender.Domain` internals leak into the versioned public surface | No public member exposes a `Iskender.Domain` type, and the harness holds the surface | FR-017, FR-018 |
| Host middleware is used to skip mandatory authentication | Authentication is inside the group, owned by identity specs | FR-016 |
| A second `AddIskender(...)` silently overrides the first | It fails with a code | User Story 5 |

### Key Entities

- **Iskender runtime options**: The configuration sources, the active environment name, and any other runtime option the hosting contract defines.
- **Iskender builder**: What `AddIskender(...)` returns, through which Iskender-specific registrations are added.
- **Module or adapter registration**: An opt-in call that contributes a section schema and services.
- **Composition**: The set of registered modules and adapters, their sections and versions, and the configuration revision, for one container.
- **Validated snapshot and revision**: ISK-0003's snapshot, one per container.
- **Route group**: The host-chosen prefix under which Iskender's request bootstrap runs.
- **Request bootstrap**: The per-request step that creates the correlation ID and `GatewayExecutionContext`.
- **Hosting contract**: The versioned public contract `hosting`.
- **Hosting sample and embedded-host fixture**: The evidence artifacts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With packages restored and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass.
- **SC-002**: The hosting sample starts in process through the fixture and resolves 100% of the services each registered module and adapter documents, with 0 Iskender processes or containers started, on 20 consecutive runs.
- **SC-003**: Each of ISK-0003's 15 `invalid_request` configuration error codes, the ones an operator's input can cause, is shown, by at least one fixture configuration, to stop the host from starting with `hosting.configuration_rejected`.
- **SC-004**: For the sample's own route and middleware, 100% of compared responses are identical with and without Iskender, and the host's configuration has 0 keys added, removed, or changed.
- **SC-005**: With canaries planted as a configuration value, a secret reference name, a database host, and a request route value, each canary appears 0 times in exceptions, diagnostics, captured logs, and responses across the hosting suite.
- **SC-006**: The static-state scan finds 0 unlisted static mutable fields in production Iskender assemblies, container validation reports 0 errors, and 100 concurrent requests show 0 shared contexts.
- **SC-007**: The compatibility harness reports `hosting` at version 1 with its baseline, `core-contracts` raised by one with 0 undeclared breaks, and 0 changes to `providers-abstractions` and `policy-sdk`. Each of the 3 error codes in TR-12 is produced by at least one test.
- **SC-008**: The PR changes 0 tracked files under `.specify/` and 0 in `.iskender/grounding.yaml`, and adds 0 provider ingress endpoints, authentication schemes, readiness endpoints, container entry points, logging providers, or exporters.

## Assumptions

- ISK-0001 to ISK-0005 are merged and complete on `main`, and their contracts, `GatewayExecutionContext`, `ErrorCodes` at `core-contracts` 4, the compatibility harness, the configuration engine, the persistence adapter, and the coordination ports and coordinators exist as their contracts describe.
- The host is an ASP.NET Core application on .NET 10. Other host kinds are post-MVP.
- One `AddIskender(...)` per container. Several independently configured gateways in one container are post-MVP.
- Configuration is validated once per container start. Changing configuration while running belongs to ISK-0057.
- Any interaction with policy plans, plugin trust, durable conversations, financial controls, or streaming goes through those specs' authoritative documents. This spec composes none of them.

## Out of Scope

- Provider ingress, protocol endpoints, endpoint resolution, and authentication (ISK-0011 onward, ISK-0030 onward).
- Container entry points, Dockerfiles, readiness and liveness endpoints, and the migration operation (ISK-0007). Local Compose (ISK-0008).
- Secret resolution and production credential providers (ISK-0021).
- Configuration publication, activation, and reload (ISK-0057).
- Control-plane services in an embedded host, beyond the composition pattern (AV-5).
- Changing the constitution, the roadmap, ADRs, ISK-0003's precedence, ISK-0004's or ISK-0005's port signatures, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
