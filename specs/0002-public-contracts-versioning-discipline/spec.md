# Feature Specification: ISK-0002 — Public Contracts & Versioning Discipline

**Feature Branch**: `feature/0002-public-contracts-versioning-discipline`

**Created**: 2026-11-07

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0002 — Public Contracts & Versioning Discipline** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/07-provider-adapters-and-api-compatibility.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Design stable common request context, resource identifiers, errors, contracts and versioning policy for transport-neutral gateway modules**. In-scope behaviors and artifacts: **Opaque scoped IDs; error categories; contract version tags; API and SDK compatibility test harness**. Explicitly exclude: **No complete provider mapping or protocol-specific business implementation**. Prerequisites: **ISK-0001**; do not assume unavailable implementation already exists. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Version contract fixtures and compatibility checks fail on unsupported breaking changes**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

ISK-0001 left three public contract projects, `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, and `Iskender.Policy.Sdk`, packable but empty. Its contract (`specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md`, version 2) gives this spec three jobs:

1. Freeze the shape of `GatewayExecutionContext` and its capability-bounded views, whose home is `Iskender.Core.Contracts`.
2. Provide the IDs and errors that more than one module uses. There is no shared namespace in `Iskender.Domain`, so these go in `Iskender.Core.Contracts`.
3. Own the compatibility rules for public contract packages.

This spec defines those transport-neutral contracts and the discipline that keeps them stable: version tags on every public contract, a written versioning policy, and a compatibility harness that fails when a public contract breaks in a way the policy does not allow. Every later spec that adds a public type, an error code, or a resource type builds on it. ISK-0011 and ISK-0012 use its IDs and owner scopes, ISK-0029 and ISK-0037 map its errors onto HTTP and OpenAI shapes, and ISK-0030 and ISK-0052 add their surfaces under its harness.

Project, package, and type names appear in this spec because they are its subject, not a choice of implementation.

## Clarifications

### Session 2026-11-07

- Q: Until ISK-0086 sets the release rule, how should the compatibility harness treat a breaking change to a public contract? (TR-4) → A: A break passes only when declared in the break record with a reason and an approving owner. An undeclared break always fails.
- Q: Which character set and casing should the random body of a resource ID use? (TR-1) → A: Lowercase Crockford base32. 128 random bits take 26 characters.
- Q: How should field names in the serialized error envelope be spelled? (FR-022, FR-028) → A: snake_case, such as `correlation_id` and `redaction_status`.
- Q: Which owner role must approve a declared break before the harness accepts it? (FR-033) → A: The Architecture (ADR) owner, always.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Resource IDs are opaque. Authorization takes the owner scope from persistent metadata, never from a caller-supplied organization value. A reference is valid only when ownership and role allow it, not merely because the target exists | `docs/03` Resource hierarchy, ADR-0005, Constitution V |
| Organization → project → resource ownership is non-bypassable, and system-level configuration has a separate operator scope that never impersonates a tenant | `docs/03`, ADR-0005 |
| A request-local `GatewayExecutionContext` carries caller principal, tenant and project, endpoint, configuration revision, deadline, correlation IDs, routing attempt ledger, budget reservations, and policy execution context. It is not shared across requests, and extensions see capability-bounded views without unrestricted storage or secret access | `docs/03` Module boundaries |
| `GatewayExecutionContext` and its views live in `Iskender.Core.Contracts`, and ISK-0002 owns their signatures. Shared IDs and errors live in `Iskender.Core.Contracts` | ISK-0001 contract v2, Port homes |
| Capabilities, error classes, idempotency, and cancellation are explicit in interfaces. Management APIs, OpenAI-compatible ingress, and the public policy SDK are versioned separately | `docs/03` Interfaces |
| Provider errors map to a stable gateway error category, a safe message, a retriable flag, the provider request ID, and redaction status. The client error envelope carries a stable code, a category, retry hints only when safe, a correlation ID, and sanitized provider metadata, and never credentials or private prompts | `docs/07` Adapter contract, Streaming |
| Rejected decisions carry a safe machine-readable reason, such as `auth_denied`, `model_disallowed`, `quota_exceeded`, `budget_blocked`, or `no_healthy_backend` | `docs/08` |
| Public compatibility is versioned and pinned to tested semantics. Public APIs, SDKs, and on-disk formats are versioned with explicit breaking-change discipline | `docs/07` Ingress contracts, `docs/01` |
| No provider SDK types, cloud-specific classes, ASP.NET Core types, or private storage models in public common contracts | `docs/03`, `docs/04`, ISK-0001 rule set for `Iskender.Core.Contracts` |
| Merged specs publish only `-alpha` prereleases, versioned `v0.0.<spec number>-alpha`, with no compatibility commitment until ISK-0086 sets the public version rule. The ISK-0002 policy must allow for that | ADR-0030 |
| The policy SDK's API, descriptor schema, and manifest schema are versioned independently, with compatibility ranges and no accidental major-version guess | `docs/11` |
| An identifier is a contract and is never reassigned to a different meaning; retire it instead | `docs/10` (policy kinds), applied here to error codes and resource type prefixes |
| .NET 10 is the runtime, and native interop must not rewrite domain or safety contracts | ADR-0002 |
| Container and embedded hosting share the same pipeline contracts | ADR-0004, ADR-0003 |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | (Alphabet confirmed in clarify.) A resource ID's text form is a registered resource-type prefix, a separator, and at least 128 bits from a cryptographically secure random source, encoded in lowercase Crockford base32, with a fixed maximum length. It encodes no owner scope, timestamp, sequence, or host | The prefix makes an ID passed as the wrong type fail at parse time. Pure randomness stops enumeration and guessing (`docs/05`), and leaves the owner to come from persistent metadata | Time-ordered IDs, which disclose creation time and narrow guessing; bare GUIDs, which cannot tell types apart in logs or at parse time; sequential IDs |
| TR-2 | The initial error categories are those in FR-020. Each code belongs to exactly one category. Codes are lower-case, dot-namespaced, and never reused | One small closed set lets ingress, telemetry, and policy agree on meaning, and `docs/08` reason codes fit beneath it | Free-form codes per module; reusing HTTP status codes as categories, which ties the contract to one transport |
| TR-3 | A resource that does not exist and one the caller may not see produce the same category, code, and safe message | Distinct answers disclose that another tenant's resource exists (`docs/05` ID enumeration) | Returning a separate "forbidden" answer for a resource in another tenant |
| TR-4 | (Confirmed in clarify.) Until ISK-0086, a breaking change to a public contract is allowed only when it is declared in the break record with a reason and an approving owner. An undeclared break always fails. After ISK-0086, the release rule it sets replaces the declaration rule | ADR-0030 gives alpha builds no compatibility commitment, yet acceptance requires that unsupported breaks fail. Declared breaks keep the record honest without freezing a pre-MVP surface | Allowing every break until ISK-0086, which leaves the acceptance evidence untestable; requiring a major version per break during alpha, which ADR-0030's `v0.0.N` rule cannot express |
| TR-5 | The harness has two halves: an SDK half that compares each public contract package's public .NET surface with a committed baseline, and an API half that checks committed wire fixtures for every versioned wire contract against the current code | A .NET surface and a serialized shape break in different ways, and either can break without the other | A surface snapshot alone, which misses a renamed JSON field; fixtures alone, which miss a removed method |
| TR-6 | Each wire contract carries its own schema version tag, such as `error-envelope` version 1, independent of package versions. Each public .NET type carries a tag naming the contract it belongs to and the version that introduced it | Package versions follow spec numbers until ISK-0086 (ADR-0030), so they cannot say which contract changed. Per-contract tags let the management API, ingress, and policy SDK version separately, as `docs/03` and `docs/11` require | Package version as the only marker |
| TR-7 | The gateway always generates its own request correlation ID, and that ID is authoritative. A caller-supplied correlation value is kept only as a separate, length-bounded, character-restricted field and is never used as a key, a log template, or an authorization input | Caller values can be forged, oversized, or crafted for log injection | Adopting the caller's ID when present |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | The members of `GatewayExecutionContext` owned by later specs (routing attempt ledger, budget reservations, policy execution context, principal details) can be added without breaking the shape this spec freezes, because this spec places them behind views that later specs extend additively | ISK-0015, ISK-0052, routing and governance specs |
| AV-2 | The error categories in FR-020 cover the rejection reasons later specs need, and new needs arrive as codes, not categories | ISK-0029, ISK-0030, ISK-0037, governance specs |
| AV-3 | One ID format suits every resource type in `docs/03`, including conversations and audit entries, in both PostgreSQL storage and URLs | ISK-0004, ISK-0011, ISK-0012, conversation specs |
| AV-4 | A public-surface comparison and wire fixtures can run offline in CI with no network and no package feed, once packages are restored | This spec's implementation |
| AV-5 | `Iskender.Hosting`'s `AddIskender(...)` surface becomes a supported public API when ISK-0006 defines it, and joins this harness through a contract revision then | ISK-0006 |

### Post-MVP work (named so that it is not designed here)

The public release and support rule after the MVP (ISK-0086 sets it), deprecation windows and long-term support lines, compatibility between gateway versions running side by side during a rolling upgrade beyond what this spec's wire fixtures prove, cross-language SDKs generated from the wire contracts, and a stable marshaling boundary for native hot paths (ADR-0002).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A breaking change is caught before merge (Priority: P1)

A contributor changes a public contract, for example by removing a member, renaming an error code, or renaming a field in the error envelope. The compatibility checks fail in their local run and in CI, name the contract, the item, and the kind of break, and say how to either restore compatibility or declare the break.

**Why this priority**: This is the named acceptance evidence. Every later spec adds public surface quickly, and without an automatic check a break reaches a published prerelease unnoticed.

**Independent Test**: Apply each kind of break in the break catalog (FR-031) to a fixture, run the harness, and confirm it fails with the expected rule ID. Apply a purely additive change and confirm it passes.

**Acceptance Scenarios**:

1. **Given** a public type or member removed from a public contract package, **When** the harness runs, **Then** it fails and names the package, the item, the change kind, and the baseline it was compared with.
2. **Given** a field renamed, removed, or retyped in a versioned wire contract without a new schema version, **When** the harness runs, **Then** it fails and names the contract, its version, and the fixture that no longer matches.
3. **Given** an error code removed, moved to another category, or reused for another meaning, **When** the harness runs, **Then** it fails and names the code.
4. **Given** a public member added, or a new optional wire field added, **When** the harness runs, **Then** it passes and reports the addition as compatible.
5. **Given** a break declared in the break record with a reason and an approving owner, before ISK-0086, **When** the harness runs, **Then** it passes and lists the declared break in its output.
6. **Given** several breaks in one change, **When** the harness runs, **Then** all of them are reported in one run, in a stable order.

---

### User Story 2 - IDs cannot be guessed, confused, or used to cross a tenant (Priority: P1)

A module author creates and parses resource IDs through one contract. An attacker who knows one ID cannot derive another, cannot learn its owner from it, and cannot get an endpoint ID accepted where a deployment ID is expected.

**Why this priority**: Every later resource spec (ISK-0011, ISK-0012, and the registry and conversation specs) stores and accepts these IDs, and the cross-tenant tests in ISK-0028 depend on them being opaque.

**Independent Test**: Generate a large sample of IDs and check uniqueness, the absence of scope or time information, and the absence of order. Parse IDs of the wrong type, malformed IDs, and oversized input, and confirm each is rejected with the expected error code.

**Acceptance Scenarios**:

1. **Given** a generated ID, **When** its text is examined, **Then** it contains no organization, project, operator scope, timestamp, sequence number, or host identifier.
2. **Given** an ID of one resource type, **When** it is parsed as another type, **Then** parsing fails with a stable invalid-request code and no partial value.
3. **Given** malformed, empty, oversized, or non-canonical input, **When** it is parsed, **Then** it is rejected, and the error does not echo the raw input beyond a bounded, sanitized excerpt.
4. **Given** a scoped reference built from an ID and an owner scope, **When** the owner scope is taken from anything other than the trusted resolution path (such as a caller-supplied header value), **Then** the contract gives no way to construct a trusted scoped reference from it.
5. **Given** an operator-scope owner, **When** it is compared with any organization or project scope, **Then** it never compares equal and cannot be converted into one.

---

### User Story 3 - Errors are stable, categorized, and safe to show (Priority: P1)

An ingress author, a provider adapter author, or a policy author returns a gateway error from one contract. Clients see a stable code and category, a correlation ID, and retry advice only when retrying is safe. Nothing in the client-visible part reveals credentials, prompts, raw provider bodies, or another tenant's resources.

**Why this priority**: `docs/07` and `docs/08` require stable, safe error envelopes, and ISK-0029 names stable error IDs as its acceptance evidence. Without one shared contract each module would define its own.

**Independent Test**: Build an error of every category from a fixture that plants a secret, a prompt fragment, a foreign tenant name, and a raw provider body in its internal detail, and confirm none appears in the client-visible part or its serialized fixture.

**Acceptance Scenarios**:

1. **Given** any gateway error, **When** it is serialized for a client, **Then** it contains the schema version, code, category, safe message, and correlation ID, and contains retry advice only when the error's category and context permit it.
2. **Given** an error whose internal detail holds a credential, prompt text, or raw upstream body, **When** its client-visible form is produced, **Then** none of them appears, and the redaction status says that detail was withheld.
3. **Given** a failure after streamed output has been visible to the client, **When** the error is produced, **Then** it carries no retry or failover advice.
4. **Given** a resource that is missing and one that belongs to another tenant, **When** each is requested, **Then** both produce the same code, category, and safe message.
5. **Given** a code that is not in the error code registry, **When** an error is built with it, **Then** construction fails in tests rather than producing an unregistered code.

---

### User Story 4 - Modules share one request context without sharing state (Priority: P2)

A module author reads the caller's resolved scope, endpoint, configuration revision, deadline, and correlation IDs from one request context, and hands extensions a view that shows only what they are allowed to see.

**Why this priority**: The context's home and owner are already fixed by ISK-0001. ISK-0006, ISK-0029, ISK-0052, and the routing and governance specs need its shape before they can build on it, but it carries no behavior on its own.

**Independent Test**: Create two contexts for two requests and confirm that neither can observe or change the other. Inspect each view's surface and confirm it exposes no secret values, storage access, or mutable shared state.

**Acceptance Scenarios**:

1. **Given** a context for a request, **When** the owner scope, configuration revision, or correlation ID has been resolved, **Then** it cannot be changed for the rest of that request.
2. **Given** two concurrent requests, **When** each reads and writes its own request-local state, **Then** neither sees the other's values.
3. **Given** an extension-facing view, **When** its public surface is inspected, **Then** it exposes no secret values, credential material, storage handles, or service locator.
4. **Given** the request's deadline or cancellation, **When** a module reads it from the context, **Then** it gets the same deadline and cancellation signal as every other module in that request.
5. **Given** the context's public surface, **When** the dependency rules run, **Then** it references no transport, ASP.NET Core, provider SDK, cloud, or storage type.

---

### User Story 5 - Contract owners can see what version everything is (Priority: P3)

A maintainer or an external SDK consumer can find, for each public contract, its current version, the version that introduced each item, the declared breaks, and the policy that governs changes, in one published place.

**Why this priority**: It makes the policy usable by humans, but the harness in User Story 1 enforces it either way.

**Independent Test**: Remove the version tag from a public type in a fixture and confirm the harness fails. Compare the published contract document with the machine-readable registries and confirm they match.

**Acceptance Scenarios**:

1. **Given** a public type in a public contract package without a contract version tag, **When** the harness runs, **Then** it fails and names the type.
2. **Given** the published versioning contract, **When** it is compared with the error code registry, the resource type registry, and the break record, **Then** they match, or the run fails.
3. **Given** a wire contract whose version was increased, **When** the harness runs, **Then** fixtures for every earlier supported version still parse, and the new version has its own fixture.

---

### Edge Cases

- The harness finds no public contract packages, no baseline, or no fixtures: it fails rather than passing with nothing checked.
- A baseline is edited to match a break without a break record entry: the harness fails, because a baseline change that removes or alters an item requires a matching declared break.
- A break record entry names an item that did not break, or a break that has already shipped in an earlier record: the harness fails and names the stale entry.
- A public contract package is added, renamed, or loses its role: the harness fails until it has a baseline or its removal is declared.
- An enum value is reordered or renumbered without being removed: treated as a break, because serialized and compiled consumers depend on the values.
- A member becomes obsolete: allowed, and recorded as a deprecation, not a break. Removing it later is a break.
- A nullable member becomes non-nullable, or a parameter gains a required position: treated as a break.
- A resource type prefix is retired: it stays registered as retired and is never reassigned. An ID with a retired prefix fails to parse with a stable code.
- Two resource types or two error codes are given the same identifier: the registries reject the duplicate.
- An ID is produced when the secure random source is unavailable: generation fails with an internal error, and never falls back to a weaker source.
- A caller-supplied correlation value contains control characters, line breaks, or is very long: it is stripped or truncated to the bounded form, or dropped, and the gateway's own ID still applies.
- A deadline has already passed when the context is created: the context reports it as expired and the request does not proceed to an upstream call.
- An error is built for a category that never permits retry advice, with retry advice supplied: construction rejects it.
- A provider returns its own error code that resembles a gateway code: it stays in sanitized provider metadata and is never promoted to the gateway code field.

## Requirements *(mandatory)*

### Functional Requirements

**Contract publication and ownership**

- **FR-001**: This spec MUST publish a versioning contract at `contracts/public-contracts-and-versioning.md` in this directory, labelled version 1. It states the versioning policy, the break catalog, the declaration rule, the wire contracts and their versions, and the review owner for each.
- **FR-002**: The error code registry, the resource type registry, and the break record MUST each have exactly one machine-readable source, which the harness and the contract types consume. The published contract MUST either be generated from those sources or be checked against them by a test.
- **FR-003**: Those sources, the committed baselines, and the committed fixtures MUST be covered by `.github/CODEOWNERS`, requesting Architecture (ADR) owner review. While rules R5 and R6 in `docs/18-branch-protection.md` stay unapplied under logged exception DEC-20261105-002, these entries request that review and GitHub does not enforce it, and the contract MUST say so.
- **FR-004**: The contracts this spec defines MUST live in `Iskender.Core.Contracts` and use only the references ISK-0001 contract version 2 allows for it. If the implementation needs a new project, role, or edge, the plan MUST make it through a revision of that contract, as its own change process requires.

**Opaque scoped resource IDs**

- **FR-005**: The contract MUST provide one resource ID type whose text form follows TR-1: a registered resource-type prefix, a separator, and at least 128 bits from a cryptographically secure random source, encoded in lowercase Crockford base32 (26 characters for 128 bits), with a fixed maximum length. Upper-case or mixed-case input is non-canonical under FR-007.
- **FR-006**: An ID's text MUST NOT encode owner scope, natural name, timestamp, sequence, host, or any value derived from them.
- **FR-007**: Parsing MUST accept only the canonical form of a registered, non-retired prefix with a valid body, and MUST be told the expected resource type. An ID of another type, a retired prefix, a non-canonical form, or input over the maximum length MUST fail with a stable invalid-request code and produce no value.
- **FR-008**: The resource type registry MUST list each resource type's prefix, its status (`active` or `retired`), and the contract version that introduced it. Prefixes MUST be unique and never reassigned.
- **FR-009**: The initial registry MUST hold the resource types `docs/03` names that later MVP specs are known to create: organization, project, endpoint, model alias, backend pool, deployment, credential profile, API key, policy definition, policy assignment, budget, conversation, and audit entry. Later specs add types to it, and adding a type is a compatible change.
- **FR-010**: The contract MUST provide an owner scope type with three kinds: organization, project (which always names its organization), and operator. An operator scope MUST NOT compare equal to, convert into, or be accepted in place of an organization or project scope.
- **FR-011**: The contract MUST provide a scoped resource reference that pairs an ID with its owner scope. Code outside the trusted resolution path MUST NOT be able to construct one from caller-supplied scope values: the public surface offers no constructor or factory that takes an unverified scope. The resolution path itself is owned by ISK-0011 and ISK-0012.
- **FR-012**: Generation MUST fail with an internal error if the secure random source fails, and MUST NOT fall back to a weaker source.
- **FR-013**: Provider-native identifiers, such as a provider's response or conversation ID, MUST NOT be representable as resource IDs. The contract MUST keep them in a separate type that carries no ownership meaning.

**Common request context**

- **FR-014**: The contract MUST define `GatewayExecutionContext` as a request-local context exposing: the resolved owner scope, the resolved endpoint reference, the pinned configuration revision marker, the deadline and cancellation signal, and the correlation context (FR-016). Each of these MUST be immutable once resolved for the request.
- **FR-015**: The members that later specs own (caller principal details, routing attempt ledger, budget reservations, policy execution context) MUST be reachable only through views whose shapes those specs define and extend additively. This spec MUST define the view mechanism and an empty view for each, and MUST NOT define their contents.
- **FR-016**: The correlation context MUST hold the gateway-generated request correlation ID, which is authoritative, the trace correlation used by OpenTelemetry, and an optional caller-supplied correlation value. The caller value MUST be length-bounded and character-restricted, MUST NOT replace the gateway ID, and MUST NOT be used as a key, a log template, or an authorization input.
- **FR-017**: Extension-facing views MUST NOT expose secret values, credential material, storage handles, service locators, or mutable state shared across requests.
- **FR-018**: A context MUST NOT be shared between requests, and nothing on its public surface MAY allow one request's state to reach another's.
- **FR-019**: A context created with a deadline already passed MUST report itself as expired.

**Error categories and errors**

- **FR-020**: The contract MUST define this closed set of error categories, each with a stable identifier and a documented meaning:

  | Category | Meaning | Retry advice permitted |
  |---|---|---|
  | `invalid_request` | The request or a value in it is malformed or fails validation | No |
  | `unauthenticated` | No valid credential was presented | No |
  | `permission_denied` | The caller is authenticated and the action is not allowed on a resource it may see | No |
  | `not_found` | The resource does not exist or the caller may not see it (TR-3) | No |
  | `conflict` | The request conflicts with current state, such as a stale revision | No |
  | `capability_unavailable` | The requested feature is not supported for the resolved endpoint, alias, or deployment | No |
  | `limit_exceeded` | A rate, token, concurrency, or quota limit applies | Yes, when the limit states when it resets |
  | `budget_blocked` | A hard budget blocks the request | No |
  | `upstream_unavailable` | No healthy backend could take the request before any output | Yes, before any output only |
  | `upstream_failed` | A backend failed or returned an error | Only before any output, and only when the mapped failure is retriable |
  | `deadline_exceeded` | The request's deadline passed | No |
  | `cancelled` | The caller or the gateway cancelled the request | No |
  | `internal` | An unexpected gateway fault | No |

- **FR-021**: Every error code MUST belong to exactly one category and be listed in the error code registry with its category, meaning, status (`active` or `retired`), and introducing contract version. Codes MUST be lower-case and dot-namespaced by owning module, and MUST NOT be reused or moved to another category. The `docs/08` reason codes MUST be registered under the matching categories.
- **FR-022**: A gateway error MUST carry: its code, its category, a safe message, the correlation ID, retry advice only where FR-020 permits it, sanitized provider metadata when it came from a provider (including the provider request ID), and a redaction status saying whether detail was withheld.
- **FR-023**: Internal diagnostic detail MUST be held separately from the client-visible part, and MUST be filtered by the same sensitive-data rules as telemetry before it is logged. The client-visible part MUST NOT contain credentials, secret values, prompt or completion content, raw upstream bodies, stack traces, internal hostnames, or another tenant's identifiers or names.
- **FR-024**: Building an error with an unregistered code, with a code from a different category, or with retry advice its category forbids MUST fail.
- **FR-025**: An error raised after streamed output has been visible MUST carry no retry or failover advice, whatever its category.
- **FR-026**: The error contract MUST be transport-neutral. Mapping a category or code to an HTTP status, an OpenAI-compatible error shape, or a stream terminal event belongs to ISK-0029, ISK-0037, and the other ingress specs. Those specs MAY add codes, and MUST NOT add categories without a revision of this spec's contract.

**Contract version tags**

- **FR-027**: Every public type in a public contract package MUST carry a version tag naming the contract it belongs to and the contract version that introduced it. Members added later MUST carry the version that introduced them.
- **FR-028**: Every versioned wire contract MUST carry its own schema version in its serialized form. This spec's wire contracts are the error envelope and the resource ID text form, each at version 1. The error envelope's field names are snake_case.
- **FR-029**: The SDK surfaces of `Iskender.Core.Contracts`, `Iskender.Providers.Abstractions`, and `Iskender.Policy.Sdk` MUST each be versioned as a separate contract, so that ISK-0030, ISK-0052, and the management API can change independently, as `docs/03` and `docs/11` require.

**Versioning policy**

- **FR-030**: The contract MUST state the versioning policy:
  - Until ISK-0086 completes, package versions follow ADR-0030 (`0.0.<spec number>-alpha`) and carry no compatibility commitment. A break is allowed only when declared in the break record (FR-033).
  - After ISK-0086, the release rule that ISK-0086 sets applies, and this spec's contract MUST be revised to point to it.
  - Additive changes, deprecations, and new wire contract versions that keep earlier versions readable are compatible.
- **FR-031**: The contract MUST include a break catalog listing every change kind the harness treats as breaking. It MUST include at least: removing or renaming a public type, member, or parameter; changing a member's type, signature, nullability, or accessibility in a narrowing way; adding a required member to a public interface or abstract type; removing, renaming, or renumbering an enum value; removing a wire field, renaming it, changing its type, or making an optional field required; tightening what a parser accepts for an existing version; changing the ID text form; and removing, reusing, or re-categorizing an error code or resource type prefix.
- **FR-032**: Marking an item obsolete MUST be recorded as a deprecation with the version and a replacement where one exists. It is not a break, and removing the item later is.

**Compatibility harness**

- **FR-033**: The break record MUST list each declared break with the contract, the item, the change kind, the reason, the approving owner role, the contract version it lands in, and the date. The approving owner role MUST be the Architecture (ADR) owner. An entry naming any other role, an entry that matches no actual break, or one that repeats a break already recorded MUST fail the harness.
- **FR-034**: The SDK half of the harness MUST compare each public contract package's public surface with its committed baseline and report every difference as compatible, a deprecation, a declared break, or an undeclared break. Any undeclared break MUST fail the run.
- **FR-035**: The API half of the harness MUST keep committed fixtures for every supported version of every wire contract, and MUST fail when the current code cannot read an earlier supported version's fixture, or writes the current version differently from its fixture.
- **FR-036**: A baseline or fixture change that removes or alters an item MUST be matched by a break record entry, or the run MUST fail.
- **FR-037**: The harness MUST fail when it finds no public contract package, no baseline for a public contract package, or no fixture for a wire contract version.
- **FR-038**: Every change kind in the break catalog MUST have a negative fixture that the harness rejects with that kind's rule ID, and a compatible-change fixture MUST pass.
- **FR-039**: The harness MUST run as part of the existing test run, offline once packages are restored, with no cloud credentials, and under an existing CI check, so that no new required check is needed.

**Diagnostics**

- **FR-040**: Every harness failure MUST report a rule ID, the contract and its version, the item, the change kind, the baseline or fixture compared, and the contract section to consult, and MUST say how to fix it: restore compatibility, or declare the break.
- **FR-041**: The harness MUST report all failures in one run, in a stable order, and MUST list declared breaks and deprecations even when it passes.
- **FR-042**: Parse and construction failures in the contract types MUST use registered error codes, so that tests and diagnostics can assert on codes rather than message text.

**Documentation and scope control**

- **FR-043**: Because `Iskender.Core.Contracts` is a behavior-bearing path, the PR MUST include the documentation change the documentation gate requires: the published contract in this directory, and a link to it from `CONTRIBUTING.md` where contributors look for change rules. Any wording change to a topic document under `docs/` beyond adding that link goes through `/review-roadmap`.
- **FR-044**: The PR MUST add no provider mapping, no protocol-specific error shape or HTTP status mapping, no ingress, no database table or storage model, and no provider, database, cloud, or coordination package.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| An attacker increments, decrements, or otherwise derives a neighbor of a known ID | No relationship exists to exploit, since IDs are random | FR-005, FR-006, SC-004 |
| An attacker reads the owner from an ID | Nothing in the ID reveals it | FR-006 |
| An endpoint ID is passed where a deployment ID is expected | Parse fails with a stable code | FR-007 |
| A caller-supplied organization value is used to build a trusted scoped reference | Not possible through the public surface | FR-011 |
| An operator scope is used as a tenant scope | Never equal, never converted | FR-010 |
| A provider's conversation ID is treated as a gateway resource ID | Not representable | FR-013 |
| A secret, prompt, raw body, or foreign tenant name reaches a client error | Withheld, and the redaction status says so | FR-022, FR-023, SC-005 |
| A probe tells a missing resource from another tenant's resource | Both answers are identical | TR-3, FR-020 |
| A client retries or fails over after visible streamed output, because the error advised it | No retry advice is ever given after visible output | FR-025 |
| A forged or oversized correlation value is used for log injection or to collide with another request | Bounded and sanitized, never authoritative | FR-016 |
| One request reads another's context | Not possible | FR-018 |
| An extension reads a secret through a context view | No view exposes one | FR-017 |
| The secure random source fails | Generation fails, with no weaker fallback | FR-012 |
| A contributor edits a baseline to hide a break | The run fails without a matching break record entry | FR-036 |
| A contributor deletes fixtures or baselines so that nothing is checked | The run fails | FR-037 |
| A retired error code or prefix is reused for a new meaning | The registry and the harness reject it | FR-008, FR-021, FR-031 |

### Key Entities

- **Resource ID**: The opaque, type-prefixed identifier of a gateway resource. Carries no owner, time, or sequence information.
- **Resource type registry entry**: A resource type's prefix, status, and introducing version.
- **Owner scope**: An organization, a project within an organization, or the operator scope.
- **Scoped resource reference**: A resource ID paired with the owner scope that the trusted resolution path established.
- **Provider-native identifier**: An identifier issued by a provider, kept apart from resource IDs and carrying no ownership meaning.
- **Gateway execution context**: The request-local holder of the resolved scope, endpoint, configuration revision, deadline, cancellation, and correlation, with views for members owned by later specs.
- **Correlation context**: The gateway request ID, the trace correlation, and the optional bounded caller value.
- **Gateway error**: Code, category, safe message, correlation ID, permitted retry advice, sanitized provider metadata, redaction status, and separately held internal detail.
- **Error category** and **error code registry entry**: The closed category set, and each code's category, meaning, status, and introducing version.
- **Contract version tag**: The contract and version an item belongs to and was introduced in.
- **Public surface baseline**: The committed record of a public contract package's public surface at its last accepted version.
- **Wire fixture**: A committed serialized example of one version of a wire contract.
- **Break record entry**: A declared break, with its reason, approving owner role, version, and date.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone with no build output and no cloud credentials, the format check, build, and tests pass with zero warnings, and all 10 CI checks pass. The compatibility harness runs with the network disconnected once packages are restored.
- **SC-002**: 100% of the change kinds in the break catalog have a negative fixture, and each is rejected with its expected rule ID. 100% of the compatible-change fixtures pass.
- **SC-003**: A reader of any harness failure can identify the contract, the item, the change kind, and the fix, without opening the harness code, and every failure in a change appears in one run.
- **SC-004**: Across 1,000,000 generated IDs of one type there are 0 duplicates, 0 IDs whose text contains the planted owner scope values or the generation time, and consecutively generated pairs are in ascending text order between 49% and 51% of the time, as random IDs would be. 100% of IDs of every other registered type are rejected when parsed as that type.
- **SC-005**: For each of the 13 error categories, an error built with a planted credential, prompt fragment, raw upstream body, and foreign tenant name in its internal detail shows 0 of them in its client-visible form or serialized fixture.
- **SC-006**: A missing resource and another tenant's resource produce byte-identical client-visible errors, apart from the correlation ID.
- **SC-007**: For every wire contract, the fixture of each supported version is read correctly by the current code, and the current version's output matches its fixture exactly.
- **SC-008**: 100% of public types in the three public contract packages carry a contract version tag, and removing one fails the harness.
- **SC-009**: The PR changes 0 files under `.specify/` that Git tracks, 0 in `.iskender/grounding.yaml`, and 0 under `specs/0000-repo-bootstrap/` or `specs/0001-solution-dependency-boundaries-build-conventions/`. It adds 0 provider, database, cloud, or coordination packages, 0 database tables, and 0 transport types in `Iskender.Core.Contracts`.

## Assumptions

- ISK-0001 is merged and its record is complete on `main`. The repository as it stands is the starting point, with three empty public contract projects.
- "Unsupported breaking change" in the acceptance evidence means a change in the break catalog that is not declared in the break record, until ISK-0086 sets the release rule.
- The `-alpha` prereleases ADR-0030 publishes carry no compatibility commitment to outside consumers. The harness still runs during alpha, so that every break is visible, intentional, and recorded.
- `Iskender.Providers.Abstractions` and `Iskender.Policy.Sdk` start with empty baselines. ISK-0030 and ISK-0052 add their surfaces, and those additions are compatible changes.
- Embeddable packages, including `Iskender.Hosting` and `Iskender.Domain`, are not supported public APIs, as ISK-0001 states, and are outside the harness until a spec makes them public (AV-5).
- The authorization check that turns a caller and an ID into a trusted scoped reference, and natural-name uniqueness within an owner scope, belong to ISK-0011, ISK-0012, and ISK-0015. This spec provides only the types they use.
- The typed, read-only CEL context in `docs/09` is defined by the policy specs. This spec's views are the mechanism they build on, not that context.
- Budget and quota decisions, routing attempts, durable conversation storage, plugin manifests, and streaming events are owned by their own specs and documents. This spec adds only the error categories and context views they use, and no competing behavior.
- No latency, throughput, or other performance figure is asserted, as Constitution VIII requires.

## Out of Scope

- Provider mappings of any kind, including `MapError` implementations and provider capability declarations (ISK-0030 and the adapter specs).
- Protocol-specific error shapes, HTTP status mapping, stream terminal event mapping, and ingress validation (ISK-0029, ISK-0037, and the other ingress specs).
- The contents of the principal, routing ledger, budget reservation, and policy execution views, and the `ICredentialResolver` signature (their owning specs).
- Resource storage, ownership resolution, authorization, and natural-name rules (ISK-0004, ISK-0011, ISK-0012, ISK-0015).
- Configuration schema and validation errors for configuration files (ISK-0003), which may register their own codes.
- The `AddIskender(...)` public surface (ISK-0006) and the policy SDK's descriptor and manifest schemas (ISK-0052 and the plugin specs).
- The public release and version rule after the MVP (ISK-0086).
- Changing the constitution, the roadmap, ADRs, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
