# Feature Specification: ISK-0003 — Configuration Schema & Validation Foundation

**Feature Branch**: `feature/0003-configuration-schema-validation-foundation`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0003 — Configuration Schema & Validation Foundation** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/04-technology-stack-and-deployment.md @docs/09-policy-language-and-execution.md and the approved ADRs ADR-0003, ADR-0005, ADR-0002, ADR-0004; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Provide typed configuration loading, schema validation and environment overlay conventions with immutable version markers**. In-scope behaviors and artifacts: **JSON/YAML input; validation errors; namespaced settings; secret reference placeholders; configuration revisions**. Explicitly exclude: **No policy CEL compiler or dynamic plan activation yet**. Prerequisites: **ISK-0001, ISK-0002**; do not assume unavailable implementation already exists. ISK-0002 already published `ConfigurationRevision`, the opaque configuration marker that `GatewayExecutionContext` carries (1 to 64 characters of `a-z`, `0-9`, `.`, and `-`), and the error code registry `ErrorCodes`, both in `Iskender.Core.Contracts`, so build configuration revisions on that type rather than a parallel one, and add any gateway error codes to that registry under the existing categories. Follow the current public contracts and versioning contract, linked from the Interfaces, isolation and portability section of @docs/03-domain-and-technical-architecture.md: `tests/Iskender.Contracts.Compatibility.Tests` fails the run on an undeclared break to either. Define interfaces, ownership and versioned contracts needed at this boundary, negative security/failure scenarios, public compatibility impact, test fixtures and operable diagnostics. Acceptance must demonstrably include: **Bad schema and unsafe values produce actionable field errors before runtime**. Respect the approved .NET modular-monolith, cloud-portable, tenant-isolated design; any interactions with CEL typed policies, plugin trust, durable conversations, financial controls or streaming must follow their authoritative documents rather than introducing a competing implementation. Separate accepted decisions, technical recommendations, assumptions needing validation and post-MVP work. Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify."

## Overview

The constitution is present at `.specify/memory/constitution.md` (version 1.1.0) and governs this spec.

Iskender has no configuration handling yet. ISK-0001 placed the Registry/Configuration module in `Iskender.Domain` under `Iskender.Domain.Registry`, and ISK-0002 published `ConfigurationRevision`, the opaque marker a request carries, and the `ErrorCodes` registry, both in `Iskender.Core.Contracts`. Every later module that reads settings (persistence, coordination, hosting, identity, providers, routing) needs the same things: one way to declare the settings it owns, one way to load them from JSON or YAML with environment-specific overlays, and one way to report what is wrong before the gateway starts serving.

This spec defines that foundation:

1. **Namespaced settings**: each module declares the configuration section it owns, with a typed, versioned schema. No two modules own the same section, and a section nobody declared is an error.
2. **Loading and overlays**: JSON and YAML documents read into one canonical form, and a fixed precedence of base document, environment overlay, and environment variables merges into one result.
3. **Validation errors**: every schema violation and every unsafe value is reported as a field error with a stable code, a field path, a source location, and a fix, all in one pass and before anything runs.
4. **Secret reference placeholders**: a secret is never written into configuration. A field that needs one takes a reference placeholder, which this spec checks for form and never resolves.
5. **Configuration revisions**: a configuration that validates becomes an immutable snapshot named by a deterministic `ConfigurationRevision` derived from its content.

ISK-0006 calls this from `AddIskender(...)`, ISK-0007 calls it from the container entry points and their readiness checks, ISK-0021 resolves the secret references, ISK-0054 builds policy configuration on the same schema mechanism, and ISK-0057 publishes and activates snapshots. This spec does none of those things itself.

Project, package, and type names appear in this spec because they are its subject, not a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| Registry/Configuration owns validation and versioning of configuration, and must not directly mutate activated data-plane snapshots | `docs/03` Module boundaries |
| Registry/Configuration lives in `Iskender.Domain`, namespace `Iskender.Domain.Registry`. `Iskender.Domain` may reference only what ISK-0001's rules allow | ISK-0001 module dependency rules contract, Module map |
| Data planes use immutable validated snapshots with a version marker. Runtime configuration updates are validate → compile → publish → atomic activate, preserving version correlation | `docs/03` Persistence, `docs/04` Deployment invariants |
| Configuration revisions are immutable, and a request is pinned to one for its lifetime. Policy changes are correlated by immutable config revision | `docs/03` pipeline step 3, `docs/12` |
| JSON and YAML declarative definitions and fluent C# configuration compile to one canonical validated model | `docs/09` Approved architecture, Constitution VI |
| CEL is used only for `when` applicability, and its compiler belongs to the policy specs. No external code runs because it appears in configuration | `docs/09`, Constitution VI |
| Embedded hosting must not take global ownership of routes, logging, or configuration | `docs/04` Topology 3, ADR-0004 |
| Never persist complete API keys, bearer tokens, or cloud access credentials in general configuration JSON. Use managed secret references or a replaceable secret store | `docs/05` Cryptography and secret management, ISK-0021 non-goals |
| Credentials never reach telemetry, client configuration, or plugin configuration | Constitution V, `docs/05` |
| Dev-only defaults cannot appear in production manifests | `docs/04` Deployment invariants |
| Readiness is separate from liveness, and missing mandatory configuration makes a process not ready, with safe diagnostic reason codes | `docs/04` Deployment invariants |
| Structured logs cover startup and configuration validation, are template-based, capture no bodies, and pass an automated secret test | `docs/12` Structured logs |
| Operators must be able to diagnose a failed configuration through an error-to-resolution flow | `docs/12` Accessibility/UX |
| `ConfigurationRevision` is the configuration marker `GatewayExecutionContext` carries: 1 to 64 characters of `a-z`, `0-9`, `.`, and `-`. Configuration revisions build on it, not on a parallel type | ISK-0002 contract, ISK-0003 approved prompt |
| New gateway error codes go in `ErrorCodes` under the existing categories. Adding a code is a compatible change and needs no revision of the ISK-0002 contract | ISK-0002 contract, Changing this contract |
| A change to `Iskender.Core.Contracts`' public surface increments `core-contracts` by one, every new public type carries a `[ContractVersion]` tag, and an undeclared break fails the harness | ISK-0002 contract, Versioning policy, rules V1 and V2 |
| Cloud dependencies sit behind interfaces with a local path. No cloud SDK in domain or contract projects | Constitution II, `docs/04`, ISK-0001 rules |
| System-level configuration has an operator scope that never impersonates a tenant | `docs/03` Resource hierarchy, ADR-0005 |
| Libraries are candidates, not mandates, and are chosen through specs and plans after license review | `docs/04` Questions reserved, Constitution IX |
| No numeric performance figure is asserted without a reviewer-approved baseline | Constitution VIII |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | All of Iskender's configuration lives under one root section, `iskender`. Directly beneath it, each key is a **namespace** owned by exactly one module through a registered section schema, such as `iskender.persistence`. Keys outside `iskender` belong to the host and are never read, changed, or reported on | One root lets an embedding host keep its own configuration untouched, as ADR-0004 requires, and one owner per namespace makes every setting traceable to a module | Flat top-level keys, which collide with host settings; letting modules read any key, which hides ownership |
| TR-2 | Validation is **closed**: an unknown namespace, or an unknown field inside a declared section, is an error, not a warning | A misspelled key that is silently ignored is a setting that silently never applies, which is the most common configuration failure and can disable a security setting | Ignoring unknown keys; warning only |
| TR-3 | YAML is read as a restricted, JSON-equivalent subset: one document per file, no anchors or aliases, no custom or language tags, no merge keys, no duplicate keys, and only plain JSON types. A scalar's type comes from its schema, so `no`, `on`, `0755`, or `1e3` is never coerced to another type silently | Aliases enable expansion attacks, tags can instantiate types, and implicit typing changes meaning (the boolean `no` problem). A subset keeps JSON and YAML equivalent, which `docs/09` requires | Full YAML 1.1 or 1.2, which gives two formats different meanings |
| TR-4 | Overlay precedence, lowest to highest: built-in schema defaults, the base document, the environment overlay document for the active environment, then environment variables. Objects merge by key, and a scalar or array at a higher layer replaces the lower value whole. A higher layer may not introduce a field the schema does not declare | One fixed order is predictable across container and embedded hosts. Whole-array replacement avoids index-merge surprises | Merging arrays by index; letting command-line arguments or arbitrary sources join the order, which ISK-0006 and ISK-0007 can add through their own sources only |
| TR-5 | Environment variables use the prefix `ISKENDER__`, with `__` separating path segments, mapped case-insensitively to declared field names. Only variables with that prefix are read | A single prefix stops unrelated process variables from reaching configuration, and `__` works on every platform | Reading every variable; using `:` as a separator, which Linux shells reject |
| TR-6 | The active environment name is required to be explicit: 1 to 32 characters of `a-z`, `0-9`, and `-`. Only the name `development` permits values a schema marks development-only. Every other name, and a missing name, is treated as production-grade | Failing safe means a forgotten setting never enables dev-only behavior in production, as `docs/04` requires | Defaulting to `development`; letting each deployment define which names count as production |
| TR-7 | A secret reference placeholder is a JSON or YAML object with exactly one member, `secretRef`, whose value is a reference name: 1 to 256 characters of `a-z`, `A-Z`, `0-9`, `.`, `_`, `-`, and `/`, not starting or ending with `/`, with no empty segments and no `..` segment. A schema field declared as a secret accepts only a placeholder, and a placeholder anywhere else is an error | An object form cannot be confused with an ordinary string, and a bounded character set stops path traversal and injection into a secret provider's lookup | An in-string syntax such as `${secret:name}`, which a real value can contain by accident; URI schemes, which pre-empt ISK-0021's provider model |
| TR-8 | The configuration revision is derived from a cryptographic hash (SHA-256) of the canonical form of the validated, merged configuration, written as `cfg-` followed by the hash in lowercase base32 without padding (56 characters in all). The canonical form has sorted keys, normalized numbers and strings, and placeholders as their reference names, so secret values never feed the hash | A content-derived revision is the same on every replica and every host for the same content, needs no coordinator, and changes exactly when the content changes | A counter or timestamp, which differs between replicas; a random value, which breaks "same content, same revision" |
| TR-9 | A value is never echoed in an error, a log, or a diagnostic, whatever field it came from. An error states the constraint the value broke, never the value | A secret pasted into the wrong field is the case that matters most, and a validator cannot always know which fields hold one | Echoing values for non-secret fields only |
| TR-10 | The public declaration and report types go in `Iskender.Core.Contracts`, namespace `Iskender.Core.Contracts.Configuration`, so that `Iskender.Domain`, provider adapters, and later the policy SDK and plugins can declare sections. The loader, overlay, validator, and revision computation go in `Iskender.Domain.Registry`, behind those contracts | Section declaration is used across packages, and `Iskender.Core.Contracts` is where ISK-0001 put shared contracts. The engine is Registry/Configuration's own work and stays an embeddable, unversioned implementation | Putting everything in `Iskender.Domain`, which provider adapters and plugins cannot reach; a new project, which needs a revision of the ISK-0001 contract |
| TR-11 | No new wire contract. The validation report is a typed .NET object with a fixed, documented text rendering. A serialized report for the management API is left to the management specs | A new wire contract needs a revision of the ISK-0002 contract. No consumer needs a serialized report yet | Defining a `config-validation-report` wire contract now |
| TR-12 | Default limits per document: 1 MiB of input, nesting depth 32, 10,000 nodes, 64 KiB per string value. At most 100 field errors are reported per validation, followed by a truncation notice giving the total count. A schema may set tighter limits on its own fields | Fixed bounds stop a hostile or broken file from exhausting memory or flooding logs. These are safety bounds, not performance claims | Unbounded input |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | ISK-0003 may define what a `ConfigurationRevision` names for a validated configuration snapshot, as its approved prompt requires. The current XML comment on `ConfigurationRevision` says "ISK-0057 decides what it names". This spec reads that as ISK-0057 owning publication, signing, and activation of revisions, and changes the comment to say so. The approved prompt outranks a code comment, but the reading should be confirmed in `clarify` | `clarify`, ISK-0057 |
| AV-2 | ISK-0021 can resolve the secret reference placeholder this spec defines through `ICredentialResolver`, without changing its form. This spec defines only the form and the typed value, and no resolution | ISK-0021, ISK-0022 |
| AV-3 | ISK-0054 can express policy configuration schemas on the section schema mechanism defined here, or extend it additively | ISK-0054 |
| AV-4 | ISK-0006 can bind the `iskender` root from the host's configuration without taking over the host's configuration, and ISK-0007 can map its configuration roots onto the base and overlay documents | ISK-0006, ISK-0007 |
| AV-5 | A YAML reader exists under an MIT-compatible license that can enforce TR-3's subset and report line and column, and it may be referenced from `Iskender.Domain` under ISK-0001's rules. The plan names the package and records the license review | This spec's plan |
| AV-6 | The overlay precedence in TR-4 is enough for the container and embedded topologies in `docs/04`, with no further layer | ISK-0006, ISK-0007, ISK-0008 |

### Post-MVP work (named so that it is not designed here)

Reloading configuration while running, or watching files for changes. Remote configuration sources and providers. Encrypting configuration documents. Signed configuration documents (ISK-0057 owns signing revisions). A JSON Schema export of section schemas for editors. A serialized validation report and a validate operation in the management API. A standalone command-line validator.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A bad configuration is rejected before anything runs, with errors that say how to fix it (Priority: P1)

An operator writes a configuration file with a misspelled key, a value of the wrong type, a number out of range, and a missing required field. Before the gateway serves anything, they get every problem at once, each naming the file, line and column, the field path, what was expected, and how to fix it.

**Why this priority**: This is the acceptance evidence. A configuration error found at runtime is an outage, and an error that does not say where it is wastes operator time.

**Independent Test**: Validate a fixture with several known defects and check that the result is a failure, that it holds one field error per defect with the expected code, path, location, and fix text, and that no configuration snapshot exists.

**Acceptance Scenarios**:

1. **Given** a document with an unknown field `iskender.persistence.conectionTimeout`, **When** it is validated, **Then** the result fails with `config.field.unknown`, path `iskender.persistence.conectionTimeout`, the file, line, and column, and a fix naming the closest declared field.
2. **Given** a field declared as an integer from 1 to 300 that holds `0`, **When** it is validated, **Then** the result fails with `config.field.out_of_range`, stating the range, and without the value `0` appearing in the error.
3. **Given** a document with five independent defects, **When** it is validated, **Then** all five errors are reported in one result, ordered by source location.
4. **Given** any validation failure, **When** a caller asks for the configuration, **Then** no snapshot and no revision are available. A partly valid configuration is never returned.
5. **Given** a valid document, **When** it is validated, **Then** the result succeeds with a snapshot and a revision, and no errors.

---

### User Story 2 - Unsafe values are caught, and secrets never sit in configuration (Priority: P1)

A security reviewer needs to know that a plaintext secret, a development-only value in production, or a hostile YAML file cannot get past validation, and that no error, log, or diagnostic ever shows a value.

**Why this priority**: Unsafe values are named in the acceptance evidence, and a secret leaked through configuration or its diagnostics breaks Constitution V.

**Independent Test**: Validate fixtures holding a plaintext value in a secret field, a placeholder in a non-secret field, a malformed placeholder, a development-only value under `production`, and each banned YAML construct. Check each is rejected with its code, and that a planted canary value appears in none of the errors, renderings, or captured log output.

**Acceptance Scenarios**:

1. **Given** a field declared as a secret that holds a plain string, **When** it is validated, **Then** it fails with `config.secret.plaintext`, and the fix says to use a `secretRef` placeholder.
2. **Given** a placeholder whose reference name contains `..` or is longer than 256 characters, **When** it is validated, **Then** it fails with `config.secret.reference_malformed`.
3. **Given** a placeholder in a field not declared as a secret, **When** it is validated, **Then** it fails with `config.field.type_mismatch`.
4. **Given** a value a schema marks development-only, and an active environment of `production` or no environment at all, **When** it is validated, **Then** it fails with `config.value.unsafe_for_environment`.
5. **Given** a YAML document using an alias, a custom tag, a merge key, a duplicate key, or several documents, **When** it is read, **Then** it fails with `config.document.unsupported_construct` or `config.document.duplicate_key`, before any expansion takes place.
6. **Given** a document larger than its limit or nested deeper than its limit, **When** it is read, **Then** it fails with `config.document.too_large` without being read whole into memory.
7. **Given** a canary string planted in every field of every negative fixture, **When** all of them are validated, **Then** the canary appears in no error, no text rendering, and no captured log output.

---

### User Story 3 - Each module owns its own namespaced settings (Priority: P1)

A module author declares the section their module owns, with a schema and version, and gets a typed, read-only view of only that section. A second module claiming the same namespace is caught before anything runs.

**Why this priority**: Every later module depends on this to read its settings, and ownership keeps settings traceable in a modular monolith.

**Independent Test**: Register two test sections, validate a document holding both, and check that each module's typed view holds only its own section. Register a duplicate namespace and check that registration fails.

**Acceptance Scenarios**:

1. **Given** sections registered for namespaces `alpha` and `beta`, **When** a valid document is validated, **Then** each typed view holds only its own section, and neither can read the other's.
2. **Given** two registrations for the same namespace, **When** the registry is built, **Then** it fails with `config.schema.duplicate_namespace`, naming both registrants.
3. **Given** a document with a namespace that no module registered, **When** it is validated, **Then** it fails with `config.namespace.unknown`.
4. **Given** keys outside the `iskender` root, **When** a document is validated, **Then** they are ignored and never reported, read, or changed.
5. **Given** a typed view of a validated snapshot, **When** code tries to change a value, **Then** it cannot: the view is read-only.

---

### User Story 4 - The same configuration overlays the same way on every host (Priority: P2)

An operator keeps a base file, a `production` overlay, and a few environment variables for container-specific values. The merge gives the same result in the container and in an embedding application, and the same content always gives the same revision.

**Why this priority**: Overlays are how one configuration serves several environments. Revisions only work as markers if they are deterministic.

**Independent Test**: Merge a fixture base, overlay, and environment variable set, and compare the result with an expected merged fixture. Validate equivalent JSON and YAML documents, and reordered documents, and check the revisions are equal.

**Acceptance Scenarios**:

1. **Given** a base value, an overlay value, and an environment variable for the same field, **When** they are merged, **Then** the environment variable wins, then the overlay, then the base, then the schema default.
2. **Given** an array in the base and a different array in the overlay, **When** they are merged, **Then** the overlay's array replaces the base's whole.
3. **Given** an overlay or an environment variable naming an undeclared field, **When** it is merged, **Then** it fails with `config.field.unknown`, and the error names the layer it came from.
4. **Given** a JSON document and a YAML document with the same content, or the same document with keys reordered, **When** both are validated, **Then** they give identical snapshots and identical revisions.
5. **Given** a change to any value, **When** it is validated, **Then** the revision changes.
6. **Given** an environment variable without the `ISKENDER__` prefix, **When** configuration is loaded, **Then** it is ignored.

---

### User Story 5 - Operators can see which configuration is in force (Priority: P3)

An operator investigating an incident needs to see which revision is loaded, which layers contributed to it, and why validation failed, without seeing any values.

**Why this priority**: Diagnostics make the other stories operable, and `docs/12` requires an error-to-resolution flow.

**Independent Test**: Validate a valid and an invalid fixture with log capture, and check the structured events, their fields, and the text rendering against the documented forms.

**Acceptance Scenarios**:

1. **Given** a successful validation, **When** it completes, **Then** one structured event records the revision, the environment, the registered namespaces and their schema versions, and the layers used (file names and the count of environment variables), and no values.
2. **Given** a failed validation, **When** it completes, **Then** one structured event per field error records its code, path, location, and layer, followed by one summary event with the total count, and no values.
3. **Given** a validation report, **When** it is rendered as text, **Then** each line follows the documented form `<code> at <path> (<source>:<line>:<column>, <layer>): <problem>. Fix: <fix>`.

### Edge Cases

- An empty document, or a document whose `iskender` root is empty: valid if every required field has a schema default, and otherwise each missing required field is reported.
- A document that is not valid JSON or YAML: one `config.document.malformed` error with the location where reading stopped, and no further field errors.
- A field present with an explicit `null`: treated as absent, so a required field without a default is reported as missing. A higher layer cannot remove a value by setting `null`.
- A JSON number too large for its declared type, or a fractional number for an integer: `config.field.out_of_range` or `config.field.type_mismatch`, never truncated or rounded.
- A duplicate key in JSON: rejected with `config.document.duplicate_key`, the same as YAML. Duplicate keys are not "last one wins".
- A string with a byte order mark, invalid UTF-8, or control characters other than tab, line feed, and carriage return: the document fails with `config.document.malformed`.
- Two environment variables mapping to the same field with different casing: `config.overlay.conflict`, naming both variables but not their values.
- An overlay document for an environment that has no overlay file: valid, and the snapshot records that no overlay was used.
- An environment name that breaks TR-6's form: `config.environment.invalid`.
- The same section schema registered with two different versions by the same module: `config.schema.duplicate_namespace`.
- A section schema that is itself invalid, such as a required field with a default, a range with minimum above maximum, or a development-only marking on a secret field: registration fails with `config.schema.invalid`, which is a developer fault in the `internal` category.
- More than 100 field errors: the first 100 by source location are reported, then a truncation notice with the total.
- Two validations of the same content on two replicas: identical revisions.
- A field holding a string that looks like a credential (for example a long high-entropy token) outside a secret field: not detected by this spec, which relies on schema-declared secret fields. Heuristic detection is post-MVP work for the telemetry and privacy specs.
- A secret value rotated in the secret store while the placeholder is unchanged: the revision does not change, because it hashes the reference, not the value.

## Requirements *(mandatory)*

### Functional Requirements

**Contract publication and ownership**

- **FR-001**: This spec MUST publish a configuration contract at `contracts/configuration.md` in this directory, labelled version 1. It states the root and namespace rules, the YAML subset, the overlay precedence and environment variable mapping, the environment name rules, the secret reference placeholder form, the revision form and canonicalization, the limits, the error codes, the text rendering of a report, the diagnostic events, and the review owner for each section.
- **FR-002**: `docs/03-domain-and-technical-architecture.md` (Persistence, publication and distributed coordination) and `docs/04-technology-stack-and-deployment.md` (Deployment invariants) MUST each link to that contract in the same PR, in the style of the existing ISK-0001 and ISK-0002 links.
- **FR-003**: Public declaration and report types MUST live in `Iskender.Core.Contracts` under `Iskender.Core.Contracts.Configuration`. The loader, overlay, validator, and revision computation MUST live in `Iskender.Domain` under `Iskender.Domain.Registry`. Both MUST use only the references ISK-0001's module dependency rules allow. If the implementation needs a new project, role, or edge, the plan MUST make it through a revision of that contract.
- **FR-004**: The PR MUST add no ASP.NET Core, dependency injection, file system, or transport type to the public surface of `Iskender.Core.Contracts.Configuration`. Reading files is the loader's job in `Iskender.Domain`, from sources a host supplies.

**Namespaced settings**

- **FR-005**: All Iskender configuration MUST sit under the root key `iskender`. Keys outside it MUST NOT be read, changed, validated, or reported.
- **FR-006**: A module MUST declare each section it owns as a section schema that names a namespace (1 to 32 characters of `a-z`, `0-9`, and `-`, starting with a letter), the owning module, a schema version (a positive integer), and its fields.
- **FR-007**: A section schema MUST be able to declare, for each field: its type (string, integer, number, boolean, enumeration, duration, URI, object of declared fields, array of a declared type, or secret reference), whether it is required, a default, and constraints: integer and number ranges, string length, a character-class pattern, enumeration members, array length, and URI schemes allowed. A field MAY be marked development-only.
- **FR-008**: Building the schema registry MUST fail with `config.schema.duplicate_namespace` when two registrations name one namespace, and with `config.schema.invalid` when a schema contradicts itself (a required field with a default, minimum above maximum, an empty enumeration, a default that breaks its own constraints, or a development-only secret field). Both are `internal`-category developer faults reported before any document is read.
- **FR-009**: A validated snapshot MUST give each module a typed, read-only view of only its own section. No public member of a view MAY return a writable collection or allow a value to change.

**JSON and YAML input**

- **FR-010**: The loader MUST accept JSON (RFC 8259) and the YAML subset in TR-3, chosen by the source's declared format, and MUST read both into one canonical tree, so that equivalent documents give identical snapshots.
- **FR-011**: The loader MUST reject, with the code in brackets: input that is not well-formed, not UTF-8, carries a byte order mark, or holds control characters other than tab, line feed, and carriage return [`config.document.malformed`]; duplicate keys in either format [`config.document.duplicate_key`]; YAML anchors, aliases, tags, merge keys, or more than one document [`config.document.unsupported_construct`]; and input past TR-12's size, depth, node, or string limits [`config.document.too_large`]. It MUST stop reading as soon as a limit is passed.
- **FR-012**: A scalar MUST be typed by its schema field, never by YAML's implicit typing. A string field given an unquoted `no` gets the string `no`. A boolean field accepts only `true` and `false`.
- **FR-013**: Every node in the canonical tree MUST carry its source name, line, column, and layer, so that each error can point to where the value came from.

**Environment overlays**

- **FR-014**: The merge MUST apply TR-4's precedence: schema defaults, then the base document, then the overlay document for the active environment if a source supplies one, then environment variables. Objects MUST merge by key. A scalar or array at a higher layer MUST replace the lower value whole. `null` at any layer MUST mean "absent" and MUST NOT remove a lower value.
- **FR-015**: Environment variables MUST be read only when their name starts with `ISKENDER__`. The rest of the name, split on `__`, MUST map case-insensitively to a declared path under `iskender`. Two variables mapping to one path MUST fail with `config.overlay.conflict`. A variable naming an undeclared path MUST fail with `config.field.unknown`. A variable's text MUST be converted using its field's declared type, with the same errors as a document value.
- **FR-016**: The active environment name MUST match TR-6's form, and otherwise fail with `config.environment.invalid`. A field marked development-only that holds a value other than its default MUST fail with `config.value.unsafe_for_environment` unless the active environment is `development`. A missing environment name MUST be treated as production-grade.
- **FR-017**: A higher layer MUST NOT introduce a field the schema does not declare, and the error MUST name the layer.

**Validation errors**

- **FR-018**: Validation MUST run every check on the merged tree and collect every field error in one pass, up to TR-12's limit of 100, followed by a truncation notice with the total count. Errors MUST be ordered by source, line, and column, with errors from environment variables last, ordered by variable name.
- **FR-019**: Each field error MUST carry: a registered error code, the field path as dot-separated segments from `iskender` (array items as `[n]`), the source name, line, and column, the layer, a problem statement naming the broken constraint, and a fix. For `config.field.unknown`, the fix MUST name the closest declared field when one is within an edit distance of 2, and otherwise list the declared fields of the parent, up to 10.
- **FR-020**: No error, text rendering, log event, exception message, or diagnostic MAY contain a configuration value, from any field (TR-9). Messages MUST be built from the schema, the path, and the code only.
- **FR-021**: Validation MUST check, with the code in brackets: missing required fields [`config.field.required`]; undeclared namespaces [`config.namespace.unknown`]; undeclared fields [`config.field.unknown`]; wrong types, including a placeholder in a non-secret field [`config.field.type_mismatch`]; values outside ranges, lengths, or array bounds, and numbers that do not fit their type [`config.field.out_of_range`]; values breaking a pattern, enumeration, duration, or URI form, or using a URI scheme not allowed [`config.field.invalid_format`]; a plain value in a secret field [`config.secret.plaintext`]; a malformed placeholder [`config.secret.reference_malformed`]; and development-only values outside `development` [`config.value.unsafe_for_environment`].
- **FR-022**: A validation that reports any error MUST return no snapshot and no revision. There MUST be no public way to obtain a partly validated configuration.
- **FR-023**: Validation MUST be callable from a library, with in-memory or host-supplied sources, without starting a host, opening a network connection, or reading a secret, so that it runs in tests, in CI, and in a host before it serves anything.
- **FR-024**: A validation report MUST have the text rendering in User Story 5, scenario 3: one line per error and a final line `<n> configuration error(s); configuration rejected.` or `Configuration valid: revision <revision>.`

**Secret reference placeholders**

- **FR-025**: A field declared as a secret reference MUST accept only TR-7's placeholder, and MUST hold, in the snapshot, a typed reference value carrying only the reference name. That type's text form MUST name the reference and never imply a resolved value.
- **FR-026**: This spec MUST NOT resolve a reference, contact a secret store, or define `ICredentialResolver`. Resolution belongs to ISK-0021 and the credential specs.

**Configuration revisions and immutable snapshots**

- **FR-027**: A successful validation MUST produce an immutable snapshot of the merged configuration and a revision of type `ConfigurationRevision`, computed as TR-8 states. A parallel revision type MUST NOT be added.
- **FR-028**: The revision MUST be identical for identical canonical content, whatever the format, key order, whitespace, comments, layer split, or host, and MUST differ when any value in the merged configuration differs. Schema defaults that apply MUST be part of the canonical content.
- **FR-029**: The snapshot MUST record its revision, the active environment, each namespace with its schema version, and the layers used. It MUST NOT be changeable after creation, and a new configuration MUST produce a new snapshot.
- **FR-030**: The XML comment on `ConfigurationRevision` MUST be updated to say that ISK-0003 derives the revision of a validated configuration snapshot, and that ISK-0057 owns publication and activation (AV-1). Its public surface MUST NOT change.

**Error codes**

- **FR-031**: These codes MUST be added to `ErrorCodes` with status `active` and `since` equal to the new `core-contracts` version, each with a safe message that holds no value:

  | Code | Category | Meaning |
  |---|---|---|
  | `config.document.malformed` | `invalid_request` | The document is not well-formed JSON or YAML, or is not clean UTF-8 |
  | `config.document.too_large` | `invalid_request` | The document passes a size, depth, node, or string limit |
  | `config.document.duplicate_key` | `invalid_request` | A key appears twice in one object |
  | `config.document.unsupported_construct` | `invalid_request` | The YAML uses an anchor, alias, tag, merge key, or several documents |
  | `config.namespace.unknown` | `invalid_request` | A key under `iskender` names no registered section |
  | `config.field.unknown` | `invalid_request` | A field is not declared by its section schema |
  | `config.field.required` | `invalid_request` | A required field without a default is missing |
  | `config.field.type_mismatch` | `invalid_request` | A value is not of the declared type |
  | `config.field.out_of_range` | `invalid_request` | A value is outside its range, length, or count, or does not fit its type |
  | `config.field.invalid_format` | `invalid_request` | A value breaks its pattern, enumeration, duration, or URI rule |
  | `config.secret.plaintext` | `invalid_request` | A secret field holds a plain value instead of a placeholder |
  | `config.secret.reference_malformed` | `invalid_request` | A placeholder's form or reference name is invalid |
  | `config.value.unsafe_for_environment` | `invalid_request` | A development-only value is set outside `development` |
  | `config.environment.invalid` | `invalid_request` | The active environment name is malformed |
  | `config.overlay.conflict` | `invalid_request` | Two environment variables map to one field |
  | `config.schema.duplicate_namespace` | `internal` | Two section schemas claim one namespace |
  | `config.schema.invalid` | `internal` | A section schema contradicts itself |

- **FR-032**: The error codes table in the ISK-0002 contract MUST be updated in the same PR so that rule H4 passes. No new error category MAY be added.

**Public compatibility**

- **FR-033**: Adding the configuration types and codes changes `core-contracts`' surface and registry, so its version MUST go from 1 to 2, every new public type and member MUST carry `[ContractVersion("core-contracts", 2)]`, the surface and registry baselines MUST be regenerated, and the versioning policy table in the ISK-0002 contract MUST show version 2. The PR MUST declare no break, and the harness MUST pass with no undeclared break.
- **FR-034**: `Iskender.Providers.Abstractions` and `Iskender.Policy.Sdk` MUST NOT change, so their versions stay at 1.

**Test fixtures and diagnostics**

- **FR-035**: The PR MUST add committed fixtures: at least one valid JSON document, the same content in YAML and reordered, base, overlay, and environment variable sets with their expected merged result, and at least one negative fixture per error code in FR-031 and per edge case above. Each negative fixture MUST state the code, path, and location it expects.
- **FR-036**: Tests MUST cover every acceptance scenario, every error code, every edge case, the canary check in User Story 2, scenario 7, and revision determinism across format, key order, and layer split.
- **FR-037**: Validation MUST emit the structured events in User Story 5 through the standard .NET logging abstraction a host supplies, with fixed message templates, event IDs, and bounded fields (code, path, source name, line, column, layer, revision, environment, namespace, schema version, counts). The PR MUST add no logging provider, no metric, and no exporter.

**Boundaries**

- **FR-038**: The PR MUST NOT add a CEL compiler, a policy schema, a policy plan, publication, activation, or reload of any kind, a secret store or resolver, `AddIskender(...)` or other host wiring, a database table, or a network call.
- **FR-039**: The PR MUST NOT change `.specify/`, `.iskender/grounding.yaml`, the constitution, ADRs, the roadmap, or the specs and contracts of ISK-0000 and ISK-0001. The only changes to ISK-0002's contract are the table updates in FR-032 and FR-033.

### Negative Security and Failure Scenarios

| Scenario | Expected outcome | Covered by |
|---|---|---|
| A plaintext API key is written into a secret field | Rejected with `config.secret.plaintext`, and the key appears in no output | FR-020, FR-021, SC-003 |
| A plaintext secret is pasted into an ordinary field by mistake, and that field then fails validation | The error states the constraint, never the value | FR-020, SC-003 |
| A YAML alias bomb expands to exhaust memory | Rejected on the first alias, before expansion | FR-011, SC-004 |
| A YAML tag tries to instantiate a type | Rejected as an unsupported construct | FR-011 |
| A very large or deeply nested file exhausts memory or the stack | Reading stops at the limit | FR-011, TR-12 |
| A misspelled security setting is silently ignored and never applies | Rejected as an unknown field, with the closest name | TR-2, FR-019, FR-021 |
| Duplicate keys hide a value from review | Rejected in both formats | FR-011 |
| YAML implicit typing turns `no` into `false` or `0755` into a number | Scalars are typed by schema | FR-012 |
| A development-only setting reaches production because the environment name was forgotten | A missing name is production-grade, so the value is rejected | FR-016 |
| An unrelated process variable, or a host's own setting, changes Iskender configuration | Only `ISKENDER__` variables and the `iskender` root are read | FR-005, FR-015 |
| A secret reference name traverses paths or injects into a secret store lookup | Bounded character set, no `..`, no empty segments | TR-7, FR-021 |
| Two modules claim one namespace and one reads the other's settings | Registration fails, and views are per section | FR-008, FR-009 |
| A partly valid configuration is used because some sections passed | No snapshot exists after any error | FR-022 |
| Configuration is changed after validation | Snapshots and views are immutable | FR-009, FR-029 |
| Two replicas report different revisions for the same configuration | The revision is derived only from canonical content | FR-028, SC-005 |
| A secret value is inferred from a revision | The revision hashes reference names, never secret values | TR-8 |
| A log line shows a configuration value | Templates hold codes, paths, and locations only, and the canary test checks it | FR-020, FR-037, SC-003 |
| Static configuration impersonates a tenant | This configuration is operator scope and defines no tenant-owned resource | Assumptions |

### Key Entities

- **Configuration source**: A named document, with its format (JSON or YAML) and layer (base or overlay), or the set of `ISKENDER__` environment variables, supplied by a host or a test.
- **Section schema**: A module's declaration of the namespace it owns, its schema version, and its fields with types, defaults, and constraints.
- **Schema registry**: All registered section schemas, with one owner per namespace.
- **Canonical tree**: The format-independent reading of a document, with each node's source location and layer.
- **Field error**: Code, path, source location, layer, problem, and fix, with no value.
- **Validation report**: Success with a snapshot and revision, or failure with field errors and a total count, and its text rendering.
- **Configuration snapshot**: The immutable, validated, merged configuration, with its revision, environment, namespaces and schema versions, and layers.
- **Section view**: A module's typed, read-only view of its own section in a snapshot.
- **Secret reference**: The typed value of a placeholder, carrying only a reference name.
- **Configuration revision**: The existing `ConfigurationRevision`, holding the content-derived `cfg-` value of a snapshot.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From a fresh clone with no build output and no cloud credentials, the format check, build, and tests pass with zero warnings, all 10 CI checks pass, and validation tests run with the network disconnected once packages are restored.
- **SC-002**: 100% of the error codes in FR-031 have at least one negative fixture, and each fixture fails with exactly its expected code, path, and line. 100% of the valid fixtures pass.
- **SC-003**: Across every negative fixture, with a canary string planted in every value, the canary appears 0 times in reports, text renderings, exception messages, and captured log output.
- **SC-004**: A YAML fixture whose aliases would expand to more than 1,000,000 nodes is rejected, and the test's measured peak allocation stays within 10 times the fixture's file size.
- **SC-005**: For each of at least 5 fixture pairs that differ only in format, key order, whitespace, comments, or how values are split between layers, the revisions are equal. For each of at least 20 single-value changes, the revision differs.
- **SC-006**: A reader of any field error can find the file, line, field, and fix without opening the code: 100% of field errors in the fixtures have a non-empty path, a location, and a fix, and every `config.field.unknown` fixture with a near-miss name names the intended field.
- **SC-007**: The compatibility harness reports `core-contracts` at version 2 with only compatible changes, 0 undeclared breaks, and 0 changes to the other two packages.
- **SC-008**: The PR changes 0 tracked files under `.specify/`, 0 in `.iskender/grounding.yaml`, and 0 under the ISK-0000 and ISK-0001 spec directories. It adds 0 host wiring, 0 database tables, 0 network calls, 0 CEL or policy plan code, and 0 secret resolution.

## Assumptions

- ISK-0001 and ISK-0002 are merged and complete on `main`. `ConfigurationRevision`, `ErrorCodes`, and the compatibility harness exist as ISK-0002's contract describes.
- "Before runtime" means before the gateway serves requests. This spec provides a validation that returns errors and no snapshot. Making a host refuse to start, or report not-ready, is the job of ISK-0006 and ISK-0007.
- The configuration this spec governs is system and operator configuration, such as connection settings, limits, and module options. Tenant-owned resources (organizations, projects, endpoints, deployments, policies) are managed through the registry and control plane (ISK-0011, ISK-0012) and are not defined in these documents.
- "Unsafe values" means plaintext secrets, malformed secret references, development-only values outside `development`, values outside schema bounds, and hostile document constructs. Detecting secrets by content heuristics is not included.
- Module sections themselves (persistence, coordination, hosting, and so on) are declared by their owning specs. This spec ships only test schemas and the mechanism.
- Policy configuration documents, their schemas, and their CEL expressions belong to ISK-0053 and ISK-0054. If a section holds CEL text, this spec treats it as an opaque string.
- No latency, throughput, or other performance figure is asserted, as Constitution VIII requires. TR-12's limits are safety bounds.

## Out of Scope

- A CEL compiler, policy schemas and policy configuration (ISK-0053, ISK-0054), plan compilation, publication, signing, distribution, activation, and rollback (ISK-0057).
- Reloading configuration while running, file watching, and remote configuration sources.
- Resolving secret references, secret stores, encryption, and `ICredentialResolver` (ISK-0021, ISK-0022).
- `AddIskender(...)` and binding to host configuration (ISK-0006). Container configuration roots, startup refusal, and readiness (ISK-0007). Local Compose files (ISK-0008).
- The sections of individual modules, which their own specs declare.
- A serialized validation report, a management API validate operation, a command-line validator, and a JSON Schema export.
- Changing the constitution, the roadmap, ADRs, or `.iskender/grounding.yaml`. Any roadmap consequence goes through `/review-roadmap`.
