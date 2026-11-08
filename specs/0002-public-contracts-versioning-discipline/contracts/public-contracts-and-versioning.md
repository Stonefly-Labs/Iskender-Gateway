# Contract: Public Contracts and Versioning

**Version**: 1

**Sources**: the error code registry (`src/Iskender.Core.Contracts/Errors/ErrorCodes.cs`), the resource type registry (`src/Iskender.Core.Contracts/Identifiers/ResourceTypes.cs`), the wire contract registry (`src/Iskender.Core.Contracts/Versioning/WireContracts.cs`), and the break record (`tests/Iskender.Contracts.Compatibility.Tests/break-record.json`). The tables below must match them, and rule H4 fails the run if they do not.

**Enforced by**: `tests/Iskender.Contracts.Compatibility.Tests`, under CI check "4. Test". Contract-type behavior is tested in `tests/Iskender.Core.Contracts.Tests`, under the same check.

**Review owner**: Architecture (ADR) owner, for every section. See `CONTRIBUTING.md` for who holds the role.

**Review is requested, not enforced**: `.github/CODEOWNERS` requests Architecture (ADR) owner review for the sources, the baselines, the fixtures, and this contract's harness. While rules R5 and R6 in `docs/18-branch-protection.md` stay unapplied under logged exception DEC-20261105-002, GitHub does not require that review (FR-003).

## Changing this contract

A new error category, a new rule, a new wire contract, a change to the versioning policy, or a change to the types allowed by H6 is made by a spec's revision of this contract. The revision increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `README.md` and `CONTRIBUTING.md`, and adds a row here:

| Version | Spec | Change | Reason | Review owner |
|---|---|---|---|---|
| 1 | ISK-0002 | Baseline: IDs, errors, request context, version tags, versioning policy, break catalog, harness | First public contract surface | Architecture (ADR) owner |

Adding an error code, a resource type, or a wire contract version needs no revision of this contract. It is a compatible change to a source, and rule H4 keeps the tables below in step.

Rule IDs, error codes, and resource type prefixes are never reused. A removed rule stays listed as retired, and a retired code or prefix stays registered as retired.

## Versioning policy

1. **Until ISK-0086 completes**: package versions follow ADR-0030 (`0.0.<spec number>-alpha`) and carry no compatibility commitment to outside consumers. A change in the break catalog is allowed only when this change adds a matching entry to the break record (see Declaring a break). Every other break fails the run.
2. **After ISK-0086**: the release rule ISK-0086 sets applies, and this contract is revised to point to it.
3. **Compatible changes**: adding a public type or member, adding an optional wire field, adding a new wire contract version that keeps every earlier supported version readable, adding an error code or resource type, changing an error code's safe message, and deprecating an item. Clients and tests rely on codes, never on message text (FR-042).
4. **Contract versions**: each public contract package is a separate contract with its own version, declared by `[assembly: ContractPackage("<contract>", <version>)]`:

   | Package | Contract ID | Version |
   |---|---|---|
   | `Iskender.Core.Contracts` | `core-contracts` | 1 |
   | `Iskender.Providers.Abstractions` | `providers-abstractions` | 1 |
   | `Iskender.Policy.Sdk` | `policy-sdk` | 1 |

   When a package's surface differs from its surface at the merge base with `origin/main`, or, for `core-contracts`, a registry or wire contract has a G or W break, its version must be the base version plus one. When it does not, the version must not change. A package with no baseline at the base starts at 1 (rule V2).
5. **Version tags**: every public type carries `[ContractVersion("<contract>", <since>)]`. A member added after its type carries its own tag. A new item's `since` is the package's current version, and an existing tag never changes (rules V1, V3).
6. **Deprecation**: an item is deprecated with `[Obsolete("Deprecated in <contract>@<n>. Use <replacement>.")]`, or `"... No replacement."` when there is none. This is reported as a deprecation, not a break. Removing the item later is a break (rule V4, FR-032).

## Declaring a break

Add an entry to `tests/Iskender.Contracts.Compatibility.Tests/break-record.json` in the same change as the break:

```json
{
  "contract": "core-contracts",
  "item": "method Iskender.Core.Contracts.Identifiers.ResourceId::Parse(System.String,Iskender.Core.Contracts.Identifiers.ResourceType)",
  "kind": "S2",
  "reason": "Replaced by Parse(ReadOnlySpan<char>, ResourceType); see ISK-00NN.",
  "approvedBy": "Architecture (ADR) owner",
  "version": 2,
  "date": "2026-11-20"
}
```

- `item` is the item's key as the violation prints it. `kind` is the rule ID. `version` is the contract version the break lands in.
- `approvedBy` must be `Architecture (ADR) owner`. The approval itself is the owner's review of the PR.
- An entry added in this change must match a break in this change. An entry already present at the merge base is history: it may not be edited or removed, and a new entry may not repeat it (rule H2).
- A declared break still fails the run if the contract version is not incremented (rule V2). A registry (G) or wire (W) break increments `core-contracts`, which publishes the registries and the wire contracts.

The current break record is empty:

| Contract | Item | Kind | Reason | Approved by | Version | Date |
|---|---|---|---|---|---|---|

## Break catalog

Each kind is reported with its rule ID. An undeclared break fails the run, and a declared one is listed as passed.

### Public .NET surface (SDK half)

The surface is every public type, and every public or protected member of a type that can be seen outside its assembly.

| ID | Breaking change |
|---|---|
| S1 | A public type is removed, renamed, or moved to another namespace |
| S2 | A public or protected member is removed or renamed, including an overload whose parameter types, count, or order changed |
| S3 | A parameter is renamed |
| S4 | A member's return, property, field, or parameter-passing type changes, or an optional parameter's default is removed or changed |
| S5 | A nullable annotation changes on a return, property, field, parameter, or generic argument |
| S6 | Accessibility narrows, a type becomes `sealed`, `abstract`, or `static`, a member becomes `abstract`, or a `virtual` member stops being overridable |
| S7 | An abstract member is added to a public abstract class, or a member without a default implementation is added to a public interface |
| S8 | An enum value is removed, renamed, or renumbered |
| S9 | A type's kind changes (class, struct, interface, enum, delegate), or a base type or implemented interface is removed |

### Wire contracts (API half)

| ID | Breaking change |
|---|---|
| W1 | A field is removed or renamed in an existing version |
| W2 | A field's type changes in an existing version |
| W3 | An optional field becomes required in an existing version |
| W4 | A fixture of a supported version is rejected, or read with a different meaning: the reader was tightened. For `resource-id`, any change to the text form |
| W5 | The current version's writer output differs from its fixture |
| W6 | A supported version is dropped, or its fixture is removed or changed |

A safe-message change is compatible. A fixture is regenerated in the same change, and neither W5 nor W6 reports it, when the only difference is the `message` value and the new value equals the registered safe message for the fixture's `code`.

W1 to W3 compare each version's `schema.json` with the one at the base. The current version's `schema.json` is also compared with what the current writer emits for that version's fixture inputs, so an edit to the codec alone is caught: a required field the writer leaves out is W1, a field it writes with another type is W2, and a field it writes that `schema.json` lacks is H3.

### Registries

| ID | Breaking change |
|---|---|
| G1 | An error code is removed |
| G2 | An error code moves to another category |
| G3 | An error code's meaning changes, or a retired code becomes active again |
| G4 | A resource type is removed, its prefix changes, or a retired type becomes active again |
| G5 | An error category is added or removed, or its retry rule changes. This also needs a revision of this contract (FR-026) |

## Other rules

| ID | Rule | Source |
|---|---|---|
| V1 | Every public type, and every member added after its type, carries a version tag naming its package's contract | FR-027 |
| V2 | A package's contract version equals its base version when its surface is unchanged, and the base version plus one when it changed. A G or W break changes `core-contracts` | FR-029, FR-030 |
| V3 | A version tag present at the merge base does not change | FR-027 |
| V4 | A deprecation states its version, and a replacement or "No replacement" | FR-032 |
| H1 | The run fails when it finds no public contract package, a public contract package with no baseline, a supported wire contract version with no fixture, or no merge base | FR-037 |
| H2 | Every new break record entry is complete, names the Architecture (ADR) owner, matches a break in this change, and does not repeat history. History is not edited or removed | FR-033, FR-036 |
| H3 | Each committed baseline equals the current surface or registry. The failure prints the regenerated text | FR-034, FR-036 |
| H4 | This contract's tables match the sources, and the rule tables match the harness's rule catalog | FR-002 |
| H5 | `ErrorCode` and `ResourceType` instances are declared only in `ErrorCodes` and `ResourceTypes` | FR-002, FR-021 |
| H6 | Only the types listed below derive from `OwnershipResolver`. The list is empty | FR-011 |
| H7 | No public member in `Iskender.Core.Contracts.Context` exposes `IServiceProvider`, a storage or stream handle, a credential or secret type, or a type from a transport, ASP.NET Core, provider SDK, or cloud namespace | FR-017, US4 |
| H8 | Every S, W, G, and V rule has a negative fixture that fails with its ID, and every compatible-change fixture passes | FR-038 |

Types allowed by H6:

| Type | Spec |
|---|---|

H7's denied namespaces are `Microsoft.AspNetCore`, `Microsoft.Extensions.DependencyInjection`, `System.Net`, `System.Data`, `System.IO`, `Azure`, `Amazon`, `Google`, `OpenAI`, and `Anthropic`. Its denied types are `System.IServiceProvider` and any type whose name ends in `Credential`, `Secret`, or `Connection`.

## Wire contracts

| Contract | Current version | Supported versions |
|---|---|---|
| `error-envelope` | 1 | 1 |
| `resource-id` | 1 | 1 |

### `error-envelope` version 1

A UTF-8 JSON object, without indentation or a trailing newline, written in this field order. Field names are snake_case. A reader ignores unknown fields.

| Field | Type | Required | Content |
|---|---|---|---|
| `schema_version` | integer | Yes | `1` |
| `code` | string | Yes | A registered error code |
| `category` | string | Yes | The code's category |
| `message` | string | Yes | The code's registered safe message |
| `correlation_id` | string | Yes | The gateway request ID |
| `retry` | object | No | Present only when retry is advised. `after_seconds` (integer, optional): the retry delay in whole seconds, rounded up |
| `provider` | object | No | Present only for a provider-originated error. `name` (string, 1 to 64 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `-`), `request_id` (string, optional), `error_code` (string, optional) |
| `redaction_status` | string | Yes | `none` or `withheld`. Always `withheld` for the `not_found` category, so that a missing resource and another tenant's resource give the same envelope |

The envelope never carries credentials, secret values, prompt or completion content, raw upstream bodies, stack traces, internal hostnames, or another tenant's identifiers or names. How it maps to an HTTP status, an OpenAI-compatible error, or a stream terminal event belongs to ISK-0029, ISK-0037, and the other ingress specs.

### `resource-id` version 1

```text
id     = prefix "_" body
prefix = 2*8 ( %x61-7A )                     ; a registered resource type prefix, lower case
body   = ( %x30-37 ) 25 crockford             ; 26 characters, 128 bits, two leading pad bits zero
crockford = %x30-39 / "a" / "b" / "c" / "d" / "e" / "f" / "g" / "h" / "j" / "k" / "m" / "n"
          / "p" / "q" / "r" / "s" / "t" / "v" / "w" / "x" / "y" / "z"
```

At most 35 characters. The text has no in-band version marker. Its version is the one the `WireContracts` registry records, and any change to the text form is a break (W4). The body comes from a cryptographically secure random source and encodes no owner scope, natural name, timestamp, sequence, or host. Parsing needs the expected type, and fails with one of the `core.resource_id.*` codes below.

## Error categories

| Category | Meaning | Retry advice permitted |
|---|---|---|
| `invalid_request` | The request or a value in it is malformed or fails validation | No |
| `unauthenticated` | No valid credential was presented | No |
| `permission_denied` | The caller is authenticated and the action is not allowed on a resource it may see | No |
| `not_found` | The resource does not exist or the caller may not see it | No |
| `conflict` | The request conflicts with current state, such as a stale revision | No |
| `capability_unavailable` | The requested feature is not supported for the resolved endpoint, alias, or deployment | No |
| `limit_exceeded` | A rate, token, concurrency, or quota limit applies | Yes, when the limit states when it resets |
| `budget_blocked` | A hard budget blocks the request | No |
| `upstream_unavailable` | No healthy backend could take the request before any output | Yes, before any output only |
| `upstream_failed` | A backend failed or returned an error | Only before any output, and only when the mapped failure is retriable |
| `deadline_exceeded` | The request's deadline passed | No |
| `cancelled` | The caller or the gateway cancelled the request | No |
| `internal` | An unexpected gateway fault | No |

After streamed output has been visible, no error carries retry advice, whatever its category.

Rule H4 checks each row's category ID and retry rule against `ErrorCategory`, reading the "Retry advice permitted" column with this fixed map: `No` is `Never`, `Yes, when the limit states when it resets` is `WhenResetKnown`, `Yes, before any output only` is `BeforeOutputOnly`, and `Only before any output, and only when the mapped failure is retriable` is `BeforeOutputWhenRetriable`. The Meaning column is documentation and is not checked.

## Error codes

Codes are lower case and dot-namespaced by owning module. Each belongs to one category.

| Code | Category | Status | Since | Meaning | Safe message |
|---|---|---|---|---|---|
| `core.resource_id.malformed` | `invalid_request` | active | 1 | The text is empty, too long, or not `<prefix>_<body>` | The identifier is not in a valid format. |
| `core.resource_id.unknown_type` | `invalid_request` | active | 1 | The prefix is not registered | The identifier is not of a known resource type. |
| `core.resource_id.retired_type` | `invalid_request` | active | 1 | The prefix is retired | The identifier is of a resource type that is no longer accepted. |
| `core.resource_id.wrong_type` | `invalid_request` | active | 1 | The ID is a valid ID of another type | The identifier is not of the expected resource type. |
| `core.resource_id.non_canonical` | `invalid_request` | active | 1 | The body has upper case, a character outside the alphabet, or set pad bits | The identifier is not in canonical form. |
| `core.resource.not_found` | `not_found` | active | 1 | Missing, or not visible to the caller | The resource was not found. |
| `core.context.already_resolved` | `internal` | active | 1 | A set-once context value was resolved twice | An internal error occurred. |
| `core.context.not_resolved` | `internal` | active | 1 | A context value was read before it was resolved | An internal error occurred. |
| `core.error.unregistered_code` | `internal` | active | 1 | An error was built with a code not in the registry | An internal error occurred. |
| `core.error.retired_code` | `internal` | active | 1 | An error was built with a retired code | An internal error occurred. |
| `core.error.retry_not_permitted` | `internal` | active | 1 | Retry advice was given where the category or visibility forbids it | An internal error occurred. |
| `core.random_source_failed` | `internal` | active | 1 | The secure random source failed | An internal error occurred. |
| `core.value.invalid` | `invalid_request` | active | 1 | A bounded value, such as a revision or provider ID, is outside its limits | A value in the request is outside its allowed limits. |
| `core.deadline_exceeded` | `deadline_exceeded` | active | 1 | The request's deadline passed | The request did not complete before its deadline. |
| `core.cancelled` | `cancelled` | active | 1 | The request was cancelled | The request was cancelled. |
| `core.internal` | `internal` | active | 1 | An unexpected gateway fault | An internal error occurred. |
| `identity.unauthenticated` | `unauthenticated` | active | 1 | No valid credential was presented | No valid credential was presented. |
| `identity.auth_denied` | `permission_denied` | active | 1 | The caller may not perform the action (`docs/08` reason) | The caller is not allowed to perform this action. |
| `policy.model_disallowed` | `permission_denied` | active | 1 | Policy does not allow the requested model (`docs/08` reason) | The requested model is not allowed. |
| `governance.quota_exceeded` | `limit_exceeded` | active | 1 | A quota was reached (`docs/08` reason) | A usage limit was reached. |
| `governance.budget_blocked` | `budget_blocked` | active | 1 | A hard budget blocks the request (`docs/08` reason) | A budget limit blocks this request. |
| `routing.no_healthy_backend` | `upstream_unavailable` | active | 1 | No healthy backend could take the request (`docs/08` reason) | No backend is currently available to handle the request. |

The last five carry the `docs/08` reasons as their final segment.

## Resource types

| Name | Prefix | Status | Since |
|---|---|---|---|
| organization | `org` | active | 1 |
| project | `prj` | active | 1 |
| endpoint | `ep` | active | 1 |
| model alias | `alias` | active | 1 |
| backend pool | `pool` | active | 1 |
| deployment | `dep` | active | 1 |
| credential profile | `cred` | active | 1 |
| API key | `key` | active | 1 |
| policy definition | `poldef` | active | 1 |
| policy assignment | `polasgn` | active | 1 |
| budget | `budget` | active | 1 |
| conversation | `conv` | active | 1 |
| audit entry | `audit` | active | 1 |

The prefix `req` is reserved for the gateway request correlation ID and is never registered as a resource type.

## Request context

`GatewayExecutionContext` is created by the host for one request and never shared. It exposes the resolved owner scope, the resolved endpoint reference, the configuration revision, the deadline and cancellation signal, and the correlation context. The scope, endpoint, and revision are each set once. The members later specs own are reached through sealed views, which those specs extend by adding members:

| View | Owner |
|---|---|
| `PrincipalView` | Identity specs (ISK-0011 onward) |
| `RoutingAttemptView` | Routing specs |
| `BudgetReservationView` | Governance specs |
| `PolicyExecutionView` | Policy specs (ISK-0052 onward) |

Extensions receive `ExtensionContextView`, which is read-only and exposes no secret, credential, storage handle, service locator, or state shared across requests.

The gateway always generates the request correlation ID, `req_` and 26 base32 characters, and it is authoritative. A caller-supplied correlation value is kept only if it is 1 to 128 characters of `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`, and is otherwise dropped. It is never used as a key, a log template, or an authorization input.

## Harness

| Half | Compares | Inputs |
|---|---|---|
| SDK | Each public contract package's public surface with its baseline | `baselines/<contract>.surface.txt` |
| API | Each wire contract version with its fixtures, and the registries with their baseline | `fixtures/wire/`, `baselines/registries.json` |

Each comparison is made against the files as they were at the merge base with `origin/main`, read with `git show`. `ISKENDER_COMPAT_BASE` names another base. The committed baselines must also equal the current surface and registries, so a baseline is regenerated in the same change. To regenerate one, run the harness and copy the text it prints under H3.

The public contract packages are the projects with the `public contract` role in the rule source of the current module dependency rules contract.

## Violation format

Every failure follows this shape, and all failures appear in one run, ordered by rule ID in this contract's order, then contract, then item:

```text
<rule ID>: <contract>@<version> <item>: <what changed>. Compared with <baseline or fixture> at <base>. Fix: restore compatibility, or declare the break in tests/Iskender.Contracts.Compatibility.Tests/break-record.json. See specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#break-catalog (version 1).
```

For example:

```text
S2: core-contracts@1 method Iskender.Core.Contracts.Identifiers.ResourceId::Parse(System.String,Iskender.Core.Contracts.Identifiers.ResourceType): member removed. Compared with baselines/core-contracts.surface.txt at 3f2a9c1. Fix: restore compatibility, or declare the break in tests/Iskender.Contracts.Compatibility.Tests/break-record.json. See specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#break-catalog (version 1).
```

For a rule that is not a break, the fix names what to change instead, and the link points at the rule's section. A passing run still lists every compatible change, declared break, and deprecation, one per line, after the violations:

```text
compatible: <contract> <item>: <what changed>
declared: <rule ID> <contract> <item>: <reason>
deprecated: V4 <contract> <item>: <deprecation message>
```

A compatible change is a surface addition, an added optional wire field or a newly supported wire version, an added or newly retired error code or resource type, and a safe-message change (FR-034).

## Threat model and known limits

### What the rules defend against

An accidental or careless change to a public contract, such as removing a member, renaming a field, or reusing a code, fails with a named rule before merge. A change written to get past the rules is caught by review, not by the rules, as in the ISK-0001 contract's threat model under DEC-20261108-001.

### Known limits

- **Review is requested, not enforced**: see the header. H2 checks the declared role, not that the owner reviewed.
- **The base must be fetched**: the harness needs `origin/main` locally. Without it the run fails with H1, which is safe but blocks a run on a clone that never fetched.
- **Reflection reads what loads**: the SDK half reads the assemblies the harness references. A public type added only through a source generator is seen, because it is compiled in. Behavior changes that keep the same surface, such as a method that starts throwing, are not breaks to the harness, and are covered by behavior tests and review.
- **`OwnershipResolver` is a protected route**: reflection, or a type wrongly added to H6's list, can attest any scope. H6 scans only this repository's production projects, so an assembly from outside the repository can also derive from `OwnershipResolver`. Such assemblies include an in-process plugin, which `docs/11` treats as trusted operator-approved code, and an application that embeds the gateway.
- **Internal detail is held apart, not yet filtered**: `ErrorDiagnostics` is never serialized to a client, and its `ToString()` returns `[withheld]`, so formatting it into a log shows no content. Filtering its content before it is logged uses the telemetry sensitive-data rules, which belong to the spec that logs it, Telemetry/Audit (ISK-0071), per FR-023.
- **Hosts are outside H5 and H6**: rule A2 of the module dependency rules contract forbids any project from reaching `Iskender.Gateway.Host` or `Iskender.ControlPlane.Host`, so H5 and H6 scan the other production projects under `src/`. This holds while the hosts contain only `Program.cs`, which `SkeletonContainsNoGatewayBusinessCode` asserts.
- **Meaning changes are compared as text**: G3 treats any edit of a code's meaning as a break, including a wording fix, which is declared like any other break.
- **Embeddable packages are outside the harness**: `Iskender.Hosting` and `Iskender.Domain` are not supported public APIs. A spec that makes one public adds it through a revision of the module dependency rules contract (AV-5).
