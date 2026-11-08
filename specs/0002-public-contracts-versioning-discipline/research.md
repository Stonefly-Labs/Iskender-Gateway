# Research: ISK-0002 — Public Contracts & Versioning Discipline

Each entry records a decision, why it was made, and what was rejected. The spec left no `NEEDS CLARIFICATION` marker. These entries settle how the approved requirements are built, and they change none of them.

## R1. Where the registries live

**Decision**: The error code registry and the resource type registry are C# declarations in `Iskender.Core.Contracts`, one file each (`Errors/ErrorCodes.cs`, `Identifiers/ResourceTypes.cs`). `ErrorCode` and `ResourceType` have internal constructors, so no other assembly can create one, and a harness rule (H5) fails if a static field of either type is declared outside its registry file's class. The break record is a JSON file in the harness project. The published contract's tables are checked against all three by rule H4, as FR-002 allows.

**Rationale**: The contract types must consume the registries at run time (FR-024 rejects an unregistered code), so a declaration the compiler checks is the safest single source. A JSON file read at startup would add a parse failure mode to every process and still need C# names for callers. Internal constructors make the registry the only place a code or type can come from, which also means later specs that add codes (FR-026) add them here, under Architecture (ADR) owner review.

**Alternatives considered**: An embedded JSON resource with generated C# accessors, which needs a source generator or duplicated constants; a registry per module, which FR-021's single registry and unique-code rule forbid.

## R2. How the SDK surface is read

**Decision**: The harness loads each public contract assembly through its project reference and walks its public and protected surface with reflection, using `NullabilityInfoContext` for nullable annotations. It writes one canonical line per item to `baselines/<contract>.surface.txt`, sorted by ordinal comparison. An item's key holds its kind, declaring type, name, and parameter types. Its attributes hold everything else the break catalog compares: accessibility, modifiers, return type, nullability, parameter names, enum values, base type, interfaces, and the version tag.

**Rationale**: Reflection over a referenced assembly needs no new package (FR-044 and the ISK-0001 deny-list) and runs offline (FR-039, AV-4). `NullabilityInfoContext` reads the compiler's nullable metadata, which hand-decoding with `System.Reflection.Metadata` would have to reimplement. A text baseline shows each change as a one-line diff in review.

**Alternatives considered**:

- `Microsoft.CodeAnalysis.PublicApiAnalyzers`: reports removals as build diagnostics, with no break kinds, no break record, and no rule IDs, and adds an analyzer package.
- `Microsoft.DotNet.ApiCompat` package validation: needs the baseline package from a feed or a committed binary, and reports its own diagnostic IDs rather than the break catalog's.
- Roslyn compilation of fixture pairs in memory: adds `Microsoft.CodeAnalysis.CSharp` to a test project for no gain over a compiled fixtures project (R5).

## R3. What a change is compared against

**Decision**: The authority for every comparison is the state at the merge base with `origin/main`. The harness reads the baseline files, the break record, the registry snapshot, and the wire fixtures at that commit with `git show <base>:<path>`. It then makes two checks:

1. The committed baseline equals the current surface. If it does not, the run fails with H3 and prints the regenerated text, so the baseline is always up to date.
2. The difference from the base baseline to the current surface is classified with the break catalog. Each break needs a break record entry added in this change.

Break record entries already present at the base are history. Removing or editing one fails (H2). An entry added in this change must match a break in this change's difference, and must not repeat a historical entry (FR-033). The base can be named with `ISKENDER_COMPAT_BASE`. If it cannot be resolved, the run fails with H1 and says to run `git fetch origin main`.

**Rationale**: FR-036 requires that editing a baseline to hide a break fails. If the committed baseline were the authority, an edit that matched the break would leave nothing to compare. Comparing against the base makes the baseline a record of the result rather than the judge of it. Splitting new and historical entries is what lets a declared break pass on its own PR and stay valid on every later one. CI's checkout already uses `fetch-depth: 0`, so `origin/main` is present, and git runs offline once fetched.

**Alternatives considered**: An append-only baseline, which is still editable without history; a hash chain over the baseline, which an editor can recompute; comparing against the latest published package, which needs a feed and has no compatibility commitment until ISK-0086 (ADR-0030).

## R4. When a contract version changes

**Decision**: Each public contract package declares its contract ID and current version with an assembly attribute, such as `[assembly: ContractPackage("core-contracts", 1)]`. When the surface at the base differs from the current surface, the current version must be exactly the base version plus one. When it does not differ, the version must equal the base version. A package with no baseline at the base starts at 1. Every type and every member added in this change carries `[ContractVersion("<contract>", <current version>)]`, and a tag present at the base may not change.

**Rationale**: FR-027 needs the version that introduced each item, and FR-029 needs each package versioned on its own. Tying the increment to "the surface changed since `main`" is a rule the harness can check without knowing what has been published. Several commits in one PR still make one increment, because the base is `main`.

**Alternatives considered**: Incrementing per breaking change only, which leaves additions untraceable to a version; using the package version, which ADR-0030 ties to spec numbers.

## R5. How each break kind gets a negative fixture

**Decision**: A new test project, `tests/Iskender.Contracts.Compatibility.Fixtures`, holds compiled before-and-after pairs, one namespace pair per SDK break kind: `Kinds.S1.Before` and `Kinds.S1.After`, and so on. The harness reads each namespace as if it were a package, strips the prefix, and runs the same classifier. Wire and registry break kinds use before-and-after files under the harness's `fixtures/breaks/`. A meta-test, like ISK-0001's `[ProvesRule]` check, fails if a catalog rule ID has no negative fixture, or a fixture's rule ID is not in the catalog (FR-038, SC-002).

**Rationale**: Real compiled metadata proves the extractor and the classifier together, with no compiler package. The pattern already works in `tests/Iskender.Architecture.Fixtures`.

**Alternatives considered**: Hand-written surface text pairs, which prove the classifier but not that the extractor sees nullability, parameter names, or enum values.

## R6. Resource ID shape

**Decision**: `ResourceId` is a sealed class. Its text is `<prefix>_<body>`: a registered prefix of 2 to 8 lowercase ASCII letters, an underscore, and 26 characters of lowercase Crockford base32 (`0123456789abcdefghjkmnpqrstvwxyz`) encoding 16 bytes from `RandomNumberGenerator.Fill`. The maximum length is 35. 26 characters carry 130 bits, so the first body character must be `0` to `7`, and any other is non-canonical. Parsing takes the expected `ResourceType` and checks, in order: length, separator, prefix registered, prefix not retired, prefix equals the expected type, body alphabet, and leading pad bits. The first failure names its registered code (data model). Equality is ordinal on the text.

**Rationale**: TR-1 and the clarification fix the alphabet and casing. A sealed class has no default value, so an uninitialized ID cannot pass as valid, which a struct would allow. Rejecting `i`, `l`, `o`, `u`, upper case, and set pad bits gives each ID exactly one text form, so ordinal equality is correct. `RandomNumberGenerator.Fill` throws `CryptographicException` on failure, which generation maps to `core.random_source_failed` with no fallback (FR-012).

**Alternatives considered**: A `readonly struct`, whose `default` is an invalid ID that compiles; one type per resource type, which the spec's single ID type rules out; accepting upper case and folding it, which the clarification made non-canonical.

## R7. Constructing a trusted scoped reference

**Decision**: `ScopedResourceReference` has no public constructor. The only way to create one is `OwnershipResolver.Attest(ResourceId, OwnerScope)`, a `protected static` method on a public abstract class. Harness rule H6 lists the types allowed to derive from `OwnershipResolver` across every production assembly. The list starts empty. ISK-0011 and ISK-0012 add their resolver through a revision of this spec's contract.

**Rationale**: FR-011 leaves the resolution path to ISK-0011 and ISK-0012, which live in `Iskender.Domain`, so an internal constructor cannot reach them, and rule A16 forbids `InternalsVisibleTo` to a production project. A protected factory keeps construction off every caller-facing route, and H6 makes deriving from the resolver an explicit, reviewed act. An `OwnerScope` itself may be built from values, because it is inert until a resolver attests it.

**Known limit**: Reflection, or a deriving type that H6 has been told to allow, can still attest any scope. So can an assembly from outside the repository, such as a trusted in-process plugin or an embedding application, since H6 scans only this repository. Review covers the in-repository cases, and the contract's threat model says so.

**Confirmed**: the maintainer confirmed this design, with its known limits, on 2026-11-08 during the checklist stage (security checklist CHK006).

**Alternatives considered**: An internal constructor with `InternalsVisibleTo`, which A16 forbids; a process-wide issuer created once at startup, which any code could create first.

## R8. Request context: set once, request local

**Decision**: `GatewayExecutionContext` is a sealed class created by the host for one request with its correlation context, deadline, cancellation token, and a `TimeProvider`. The owner scope, endpoint reference, and configuration revision are set-once slots. Each `Resolve*` method stores its value with `Interlocked.CompareExchange` and throws a `GatewayException` with `core.context.already_resolved` on a second call. Reading an unresolved slot throws with `core.context.not_resolved`. There is no static or `AsyncLocal` state. Views for later specs are sealed, empty classes (`PrincipalView`, `RoutingAttemptView`, `BudgetReservationView`, `PolicyExecutionView`), each created with the context. `ExtensionContextView` is a read-only projection of the resolved values, with no `Resolve*` method.

**Rationale**: The pipeline resolves the scope after it has generated the correlation ID, so the values cannot all be constructor arguments, yet FR-014 makes each immutable once resolved. Sealed classes are the extension mechanism because adding a member to a sealed class is compatible, while adding one to an interface is a break (S7). The views carry no service provider, storage handle, or secret, so FR-017 holds by construction, and rule H7 checks that the context's surface references none of them.

**Alternatives considered**: A mutable property bag, which loses typing and set-once behavior; interfaces for the views, which later specs could not extend without a break; an `AsyncLocal` ambient context, which can leak across requests that share a flow.

## R9. Errors: closed categories, registered codes, fixed messages

**Decision**: `ErrorCategory` is a sealed class with 13 static instances and a private constructor, not an enum, so no value outside the set can be cast into existence. Each `ErrorCode` has an ID, a category, a meaning, a safe message, a status, and its introducing version. The client-visible message of a `GatewayError` is always its code's registered safe message, and a caller cannot supply free text for it. `GatewayError` holds internal detail in a separate `ErrorDiagnostics` object that the envelope codec never reads and whose `ToString()` returns `[withheld]`. Construction takes an `OutputVisibility` value and rejects retry advice that FR-020 or FR-025 forbids, with `core.error.retry_not_permitted`.

**Rationale**: A message from the registry cannot carry a secret, a prompt, or a tenant name, so FR-023 holds for the message by construction, and TR-3's identical answers (SC-006) follow from sharing one code. Detail held apart is still available to logs after the telemetry filter, which the observability specs own.

**Alternatives considered**: An enum of categories, which accepts `(ErrorCategory)99`; a free-text safe message with a sanitizer, which can miss a pattern.

## R10. The `docs/08` reason codes

**Decision**: FR-021 needs codes dot-namespaced by owning module, and needs the `docs/08` reasons registered. They are registered with the `docs/08` text as the last segment, under the module `docs/03` gives that decision: `identity.auth_denied` (`permission_denied`), `policy.model_disallowed` (`permission_denied`), `governance.quota_exceeded` (`limit_exceeded`), `governance.budget_blocked` (`budget_blocked`), and `routing.no_healthy_backend` (`upstream_unavailable`).

**Rationale**: This is the only reading that meets both rules. The `docs/08` text survives intact as the code's last segment, so the reason stays recognizable, and the category follows the meaning in FR-020. `auth_denied` sits under `permission_denied` because a rejected decision follows authentication. A missing credential has its own code, `identity.unauthenticated`.

**Alternatives considered**: Registering the bare `docs/08` names, which breaks FR-021's namespacing; a separate reason field beside the code, which adds a second identifier with the same meaning.

**Confirmed**: the maintainer confirmed these module names and categories, including `auth_denied` as an authorization denial under `permission_denied`, on 2026-11-08 during the checklist stage (security checklist CHK014).

## R11. Wire format of the error envelope

**Decision**: Version 1 is a JSON object written by a hand-written `Utf8JsonWriter` codec, in this field order: `schema_version`, `code`, `category`, `message`, `correlation_id`, `retry` (only when advice is given), `provider` (only for provider-originated errors), `redaction_status`. The reader ignores unknown fields, so a later optional field is compatible (FR-031). Output is UTF-8, without indentation, with no trailing newline. Fixtures are compared byte for byte, which `.gitattributes`' `eol=lf` keeps stable on every platform.

**Rationale**: A fixed writer gives exact output that a fixture can pin (FR-035, SC-007). `Utf8JsonWriter` and `JsonDocument` are in the base class library, so no package is needed and no transport type enters `Iskender.Core.Contracts`. The format is transport-neutral. ISK-0029 and ISK-0037 decide how it maps to HTTP and OpenAI shapes (FR-026).

**Alternatives considered**: `JsonSerializer` with attributes, whose output order and escaping follow serializer options that can change; the source generator, which adds generated files for one small type.

## R12. Correlation values from callers

**Decision**: The gateway request ID is `req_` and 26 lowercase Crockford base32 characters from the same secure random source as resource IDs, with its own type (`RequestCorrelationId`), so it cannot pass as a resource ID. A caller value is kept only if it is 1 to 128 characters from `A-Z`, `a-z`, `0-9`, `.`, `_`, `:`, and `-`. Anything else is dropped whole, and the context records that it was dropped. The trace correlation is an `ActivityTraceId` and `ActivitySpanId` pair.

**Rationale**: The edge case allows stripping, truncation, or dropping. Dropping is the only one that never stores a value the caller did not send, and it makes no partial value available for a log template or a collision (FR-016).

**Alternatives considered**: Stripping disallowed characters, which can turn two distinct inputs into one value; truncating, which keeps a crafted prefix.

## R13. Where the harness runs

**Decision**: The harness is the test project `tests/Iskender.Contracts.Compatibility.Tests`, so CI check "4. Test" runs it with the rest of the solution. Contract-type behavior tests (IDs, errors, context) go in the existing `tests/Iskender.Core.Contracts.Tests`. Neither needs a package that is not already pinned.

**Rationale**: FR-039 needs an existing check and no new required one. ISK-0001 rules treat any project under `tests/` as a test project, so no rule source change is needed, though each new project needs a lock file (B2) and a solution entry.

**Alternatives considered**: Putting the harness in `Iskender.Architecture.Tests` under check 5, which mixes a second contract and rule family into ISK-0001's rule runner and its C1 document check.

## R14. Documentation gate

**Decision**: `docs-gate.sh` counts only `docs/**`, `README.md`, and `CLAUDE.md` as documentation, so neither the contract under `specs/` nor `CONTRIBUTING.md` satisfies it for the `src/Iskender.Core.Contracts/**` change. The PR therefore links the contract from `CONTRIBUTING.md`, as FR-043 asks, and also from `README.md`, beside its existing link to the ISK-0001 contract.

**Rationale**: FR-043 limits wording changes to topic documents under `docs/`. `README.md` is not one, and a link to a contract it already lists the predecessor of is non-material. This passes the gate without the `docs-not-required` label and without a `/review-roadmap` change.

**Alternatives considered**: The `docs-not-required` label, which would be untrue, since the contract is documentation; editing a `docs/` topic document, which FR-043 routes through `/review-roadmap`.

## R15. The ISK-0001 test that lists source files

**Decision**: `SkeletonContainsNoGatewayBusinessCode` in `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs` asserts that the only `.cs` files under `src/` are the two `Program.cs` files. It changes to allow files under `src/Iskender.Core.Contracts/` and still asserts the two host files, and that neither maps an endpoint.

**Rationale**: This spec is the first to add source under `src/`. The test's purpose, which is that no gateway behavior exists, still holds, since contract types carry no transport or provider behavior (FR-044). Keeping the host assertions as they are leaves it as strict for everything else.

## R16. Spec deviations found

None. The plan needs no new project under `src/`, no new edge, and no new package, so no revision of the ISK-0001 contract is needed (FR-004).
