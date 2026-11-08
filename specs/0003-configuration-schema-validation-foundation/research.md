# Research: ISK-0003 — Configuration Schema & Validation Foundation

Each entry records a decision, why it was made, and what was rejected. The spec left no `NEEDS CLARIFICATION` marker, and `clarify` settled TR-2, TR-3, TR-6, TR-10, and AV-1. These entries settle how the approved requirements are built, and they change none of them.

## R1. The YAML reader (AV-5)

**Decision**: `YamlDotNet` 18.1.0, MIT, with no package dependencies, used only through its low-level event parser (`YamlDotNet.Core.Parser`). Iskender builds its own canonical tree from the event stream. The deserializer, the representation model, and every type-resolving feature of the package are never called.

**Rationale**: The event parser reports every construct TR-3 bans as an event or a property of one, with its start mark (line and column), before anything is expanded:

| Construct | How it shows in the event stream | Result |
|---|---|---|
| Anchor | A non-empty `Anchor` on a scalar, sequence, or mapping start | `config.document.unsupported_construct` |
| Alias | An `AnchorAlias` event | `config.document.unsupported_construct` on the first one. The parser never expands an alias, so nothing is expanded at all |
| Tag | A non-empty `Tag` that is not implicit, on any node | `config.document.unsupported_construct` |
| Merge key | A mapping key that is the plain scalar `<<` | `config.document.unsupported_construct` |
| Several documents | A second `DocumentStart` | `config.document.unsupported_construct` |
| Duplicate key | Detected by the tree builder, which keeps the keys of each open mapping | `config.document.duplicate_key` |

Writing a YAML subset parser by hand would mean owning YAML's scanner rules (indentation, flow and block styles, folding, escapes) for no gain. The package's license, MIT, is compatible with Iskender's, it has no dependencies, and no ISK-0001 deny-list category names it, so `Iskender.Domain` may take it under rule A19 with no contract revision.

**Known limit**: YAML version directives (`%YAML`) and tag directives (`%TAG`) are rejected as unsupported constructs too, since a tag directive only matters with a tag.

**Alternatives considered**: A hand-written subset parser; `SharpYaml`, which is a fork with less maintenance; converting YAML to JSON with an external tool, which needs a process and loses locations.

## R2. Reading JSON with locations

**Decision**: `System.Text.Json.Utf8JsonReader` over the bounded byte buffer, with comments and trailing commas disallowed (strict RFC 8259) and `MaxDepth` set to the depth limit plus one, so that the reader stops on its own as well as through Iskender's count. Lines and columns are computed from each token's `TokenStartIndex` against a table of line-start offsets built in one pass over the buffer. A column counts Unicode scalar values from the start of the line, starting at 1. A duplicate key is found by keeping the keys of each open object, as for YAML.

**Rationale**: `Utf8JsonReader` is in the base class library, never builds a whole document, and reports the byte offset of every token. `JsonDocument` would hold the whole tree and drops duplicate keys silently. The line table costs one pass over at most 1 MiB.

**Alternatives considered**: `JsonDocument` or `JsonNode`, which keep the last of two duplicate keys and give no locations; Newtonsoft.Json, which is a new package for something the base library does.

## R3. One canonical tree, typed by schema

**Decision**: Both readers produce the same tree: objects (ordered as read, with unique keys), arrays, and scalars. A scalar keeps its text and its **lexical kind**: `string` (a JSON string, or a quoted YAML scalar), `number` and `boolean` (JSON tokens), `null`, or `plain` (an unquoted YAML scalar, or an environment variable's text). Every node carries its source name, line, column, and layer (FR-013). The validator, not the reader, gives a scalar its type, from its schema field:

| Field type | Accepts |
|---|---|
| `string`, `enumeration` | A `string` scalar, or a `plain` scalar taken as its exact text, so an unquoted `no` stays `no` |
| `integer` | A `number` or `plain` scalar matching `-?(0\|[1-9][0-9]*)`. `0755`, `1e3`, and `1.0` are type mismatches, and a value that does not fit a 64-bit signed integer is out of range |
| `number` | A `number` or `plain` scalar matching the JSON number grammar. A value outside `decimal`'s range, or with more significant digits than `decimal` holds exactly, is out of range, never rounded |
| `boolean` | A `boolean` scalar, or a `plain` scalar that is exactly `true` or `false`. `yes`, `on`, and `True` are type mismatches |
| `duration` | A `string` or `plain` scalar in the duration form (R7) |
| `uri` | A `string` or `plain` scalar that is an absolute URI with an allowed scheme |
| `object`, `array` | An object or an array node |
| `secretRef` | An object with exactly one member, `secretRef` (R8) |

A `null` scalar, a plain `null`, `~`, or an empty plain value means "absent" (spec edge case). A JSON `string` given to an `integer` field, and a JSON `number` given to a `string` field, are type mismatches, since JSON states its types. So a YAML document and a JSON document with the same content give the same snapshot, while YAML never changes a value's type behind the author's back.

**Rationale**: Typing by schema is what makes TR-3's "no silent coercion" hold, and a lexical kind is what lets JSON's explicit types and YAML's plain scalars share one validator.

**Alternatives considered**: Typing scalars in the YAML reader with the YAML 1.2 core schema, which reintroduces the `no` and `0755` problems for string fields; converting YAML to JSON text first, which loses locations.

## R4. Bounded reading

**Decision**: The loader reads a source into one buffer of at most the size limit (1 MiB) plus one byte, and stops with `config.document.too_large` as soon as that extra byte arrives, without reading further. It then checks, in order: a UTF-8 byte order mark, valid UTF-8 (`System.Text.Unicode.Utf8.IsValid`), and control characters other than tab, line feed, and carriage return, each failing with `config.document.malformed`. Escapes can produce a control character that the raw bytes do not hold, such as JSON `"\u0000"` or YAML `"\x00"`, so the tree builder applies the same control-character check to every key and string after decoding (spec edge case: "a string with ... control characters"). The tree builder counts nodes and depth as it goes and stops at the first one past its limit. String length is counted in UTF-8 bytes, so the 64 KiB limit is a byte limit.

**Rationale**: Reading at most the limit plus one byte is how FR-011's "stop as soon as a limit is passed" holds for size, and counting as nodes arrive is how it holds for depth and nodes. The byte-order-mark, UTF-8, and control-character checks run once over a buffer that is already bounded.

**Alternatives considered**: Streaming the parsers over the source stream directly, which bounds memory the same way but makes the UTF-8 checks harder to place before parsing.

**Risk for SC-004**: The alias bomb is rejected at its first alias, so the work is bounded by the bytes before it. Peak allocation includes the parser's fixed buffers, about the same for any small file. The test measures allocation with `GC.GetAllocatedBytesForCurrentThread()` after one warm-up read of a different file. The bound is a fixed 1 MiB ceiling, TR-12's input limit, plus a scaling check: a bomb expanding 1,000 times further must allocate within 64 KiB of the first, which a large fixed buffer cannot satisfy if expansion happens. (This replaced a 10-times-file-size bound, which the parser's fixed cost would have failed for any honest small bomb; maintainer approval, 2026-11-08.) If either check fails, the implement stage reports that as a blocker. It does not change the fixture to pass.

## R5. Overlay merge

**Decision**: The merge works on canonical trees, lowest layer first: schema defaults, the base document, the overlay document, then environment variables (TR-4). Objects merge by key. A scalar or array at a higher layer replaces the lower node whole, and the merged node keeps the higher layer's location. A `null` at any layer is dropped before merging, so it never removes a lower value (FR-014). Each layer is checked against the schema for unknown names before merging, so `config.field.unknown` names the layer that introduced the field (FR-017). Defaults are applied to the merged tree last, as the lowest layer, so a default never hides a missing parent object.

The host supplies at most one base document and at most one overlay document, chosen for the active environment. Either may be missing. The snapshot records which were used and how many environment variables applied (FR-029).

**Rationale**: One fixed order, applied the same way by every host, is what User Story 4 and AV-6 need. Whole replacement of arrays avoids merging lists by index, which surprises operators.

**Alternatives considered**: Several base or overlay documents, which no host needs yet (AV-6); a delete marker, which `null`-as-absent forbids.

## R6. Environment variables

**Decision**: The host passes the variables as a read-only map, and the loader offers a helper that reads the process's variables. Only names starting with `ISKENDER__` are read, compared case-insensitively. The rest of the name is split on `__` and each segment is matched to a declared name under `iskender` without regard to case. Every case-insensitive comparison here folds only ASCII letters, by ordinal comparison, so the result is the same under every culture: under `tr-TR`, a culture-aware comparison would not match `iskender__` to `ISKENDER__`. A first segment naming no registered namespace is `config.namespace.unknown`, a later segment naming no declared field is `config.field.unknown` (as for documents, FR-021), and a name with nothing after the prefix or with an empty segment is `config.field.unknown`. A variable may set only a scalar field. One that names an object or array field, or a path through an array, is `config.field.type_mismatch`. A secret field is set through a final `__SECRETREF` segment, mirroring the document form, so `ISKENDER__PROVIDERS__APIKEY__SECRETREF=openai/primary` sets a placeholder, and `ISKENDER__PROVIDERS__APIKEY=...` is `config.secret.plaintext`. A `__SECRETREF` segment on a field that is not a secret sets a placeholder there, which is `config.field.type_mismatch`, as in a document. The field name `secretRef` is reserved in every schema (R7), so the segment never names a declared field. The text is a `plain` scalar, typed by its field as in R3. Two variables that map to one path are `config.overlay.conflict`, naming both variables and neither value, located at the variable whose name sorts first by ordinal comparison. A variable's location is its name, with line and column 0, and its layer is `environment`. A variable's text faces the same checks as a document string, as FR-015's "the same errors as a document value" requires: a control character other than tab, line feed, or carriage return is `config.document.malformed`, and text past 65,536 UTF-8 bytes is `config.document.too_large`.

Because names match without regard to case, a section schema whose sibling fields differ only in case is `config.schema.invalid`, so the mapping is never ambiguous.

**Rationale**: The prefix keeps unrelated variables out (TR-5). Requiring the `__SECRETREF` segment keeps a pasted key in a secret field's variable from being taken as a reference name, which the bounded reference character set alone would allow for many API key formats.

**Alternatives considered**: Treating a secret field's variable text as a reference name, which accepts `sk-...` style keys as references; JSON text in a variable for objects and arrays, which no host needs yet.

## R7. Field declarations and constraints

**Decision**: Field names are 1 to 64 characters of `a-z`, `A-Z`, and `0-9`, starting with a lower-case letter. Constraints:

- **Integer and number ranges**: inclusive minimum and maximum.
- **String length**: minimum and maximum, counted in Unicode scalar values.
- **Character-class pattern**: an allowed set built from classes (lower-case letters, upper-case letters, digits) plus listed punctuation characters, with an optional separate class for the first character. There is no regular expression, so no input can make matching slow.
- **Enumeration members**: exact, case-sensitive strings.
- **Array length**: minimum and maximum item counts.
- **URI schemes**: an allowed list, matched without regard to case, and only absolute URIs.
- **Duration form**: a non-negative integer written `0|[1-9][0-9]*` followed by `ms`, `s`, `m`, `h`, or `d`, such as `30s`, held as a `TimeSpan` and canonicalized as whole milliseconds. Any other form, including a leading zero, is `config.field.invalid_format`, and a value too large for `TimeSpan` is `config.field.out_of_range`. Ranges on durations are minimum and maximum `TimeSpan` values.

A schema is checked when the registry is built (FR-008). Besides the contradictions FR-008 lists, `config.schema.invalid` covers a malformed namespace or field name, duplicate field names, sibling names that differ only in case (R6), a constraint that does not apply to the field's type, an array whose item type is itself an array, a string maximum length above 65,536, a field that is both required and development-only (no value could satisfy it outside `development`), and a field named `secretRef` in any case, which is reserved so that a placeholder can never be read as an ordinary object (TR-7).

**Rationale**: A character-class pattern covers every case the known module settings need (names, hosts, identifiers) without a regular expression engine running on operator input. The duration form is short, unambiguous, and the same in YAML, JSON, and a variable.

**Alternatives considered**: Regular expressions, which risk slow matching on hostile input; ISO 8601 durations (`PT30S`), which are harder for operators to read; `TimeSpan` text (`00:00:30`), which YAML 1.1 readers turn into base-60 numbers.

## R8. Secret reference placeholders

**Decision**: A placeholder is an object with exactly one member, `secretRef`, whose value is a reference name of 1 to 256 characters of `a-z`, `A-Z`, `0-9`, `.`, `_`, `-`, and `/`, with no leading or trailing `/`, no empty segment, and no `..` segment (TR-7). In the snapshot it is a `SecretReference`, a sealed value type in `Iskender.Core.Contracts.Configuration` carrying only the name, whose `ToString()` returns `secretRef:<name>`. Nothing in this spec resolves it (FR-026).

An object with a `secretRef` member and any other member, or a `secretRef` value that is not a string, is `config.secret.reference_malformed`. A placeholder in a field not declared as a secret is `config.field.type_mismatch`, and a plain value in a secret field is `config.secret.plaintext`.

**Rationale**: The reference name is not a secret, so its text form may name it, and it is the only thing a later resolver (ISK-0021, AV-2) needs.

**Alternatives considered**: A `SecretReference` that carries a provider or a URI scheme, which would pre-empt ISK-0021's provider model.

## R9. Canonical form and the revision

**Decision**: The canonical form is UTF-8 JSON with no insignificant whitespace, written by `Utf8JsonWriter` with a fixed encoder:

```json
{"form":1,"iskender":{...},"schemas":{"<namespace>":<version>,...}}
```

- Object keys are sorted by ordinal comparison of their UTF-16 code units at every level.
- Integers are written as their shortest decimal form. Numbers are written as the `decimal` value with trailing fractional zeros removed and no exponent, so `1.50`, `1.5`, and `15e-1` are the same.
- Booleans are `true` or `false`. A duration is its whole number of milliseconds, as an integer. A URI is its original text, after validation. Strings are their exact code points, with JSON escaping fixed by the encoder. No Unicode normalization is applied, since the snapshot hands modules the same text that was hashed.
- A placeholder is `{"secretRef":"<name>"}`.
- Defaults that apply are written as if a layer had supplied them (FR-028). Absent optional fields with no default are omitted.
- The `schemas` member lists each registered namespace's schema version, so a schema change that keeps the same values still changes the revision. The `form` member lets a future change to these rules change every revision on purpose.
- The environment name, the layer split, and source names are not part of the canonical form, so the same content gives the same revision on every host (FR-028, SC-005).

The revision is `cfg-` followed by the SHA-256 of those bytes in lower-case RFC 4648 base32 with no padding: 52 characters, 56 in all, which fits `ConfigurationRevision`'s 64-character, `a-z0-9.-` form. The encoder is a small internal type in `Iskender.Domain.Registry`, tested with RFC 4648's test vectors.

**Rationale**: A fixed writer over a sorted tree is deterministic across platforms. Including schema versions is what keeps a revision from naming two different meanings. RFC 4648's lower-case alphabet (`a-z`, `2-7`) fits the revision's character set. The Crockford codec in `Iskender.Core.Contracts` is internal, and making it public would add a contract surface for one caller.

**Alternatives considered**: RFC 8785 (JCS) exactly, which formats numbers as IEEE doubles and so rounds large integers; hexadecimal, which needs 68 characters with the prefix and does not fit; hashing the source documents, which differs with formatting and layer split.

## R10. Where each type lives (TR-10)

**Decision**:

- **`Iskender.Core.Contracts.Configuration`** (public, `core-contracts` version 2): the section and field declarations, `SecretReference`, the layer and field-type enumerations, `ConfigurationSourceLocation`, `ConfigurationFieldError`, `ConfigurationValidationReport`, and the read-only `ConfigurationSnapshot` and `ConfigurationSection` views.
- **`Iskender.Domain.Registry`** (embeddable, unversioned): `ConfigurationSchemaRegistry`, the sources, the readers, the merge, the validator, the canonical writer, the revision encoder, the snapshot implementation, and the diagnostic events.

`ConfigurationSnapshot` and `ConfigurationSection` are public abstract classes with protected constructors and no public way to create one. The validator in `Iskender.Domain.Registry` returns internal sealed subclasses. `ConfigurationValidationReport` is sealed and created only through `Success(snapshot)` or `Failure(errors, totalCount)`, so a success always holds a snapshot and no errors, and a failure always holds at least one error and no snapshot (FR-022).

**Rationale**: Provider adapters, the policy SDK, and plugins cannot reach `Iskender.Domain`, so the types they use to declare and read a section must be in `Iskender.Core.Contracts`. The snapshot must be built by the validator in `Iskender.Domain`, which cannot use an internal constructor in `Iskender.Core.Contracts` (rule A16 forbids `InternalsVisibleTo` to a production project). A protected constructor is the route ISK-0002 took for `OwnershipResolver` (its research R7).

**Known limit**: Code in the same process can derive its own `ConfigurationSnapshot` and hand it to a module. That needs in-process code, which is already trusted, as the constitution says assembly loading is not a sandbox. A test in `Iskender.Domain.Tests` fails if any production assembly in the repository derives from either abstract class outside `Iskender.Domain.Registry`. It scans this repository only. The contract states both.

**Compatibility note**: Adding an abstract member to either class later would break a deriving type, so later members are added as virtual members with a default. The harness classifies member additions, and the contract says so.

**Alternatives considered**: Sealed snapshot types with public constructors, which let any caller pair arbitrary values with any revision; interfaces, which cannot gain a member without a break (ISK-0002 research R8); putting the snapshot in `Iskender.Domain`, which provider adapters cannot reach.

## R11. A module reads only its own section

**Decision**: `ConfigurationSnapshot.GetSection(SectionSchema schema)` returns the section only for the exact schema instance that was registered, compared by reference. A copy with the same namespace, or another module's namespace, fails with `GatewayException` and `config.namespace.unknown`. `ConfigurationSection` has typed getters by declared field name: `GetString`, `GetInt64`, `GetDecimal`, `GetBoolean`, `GetDuration`, `GetUri`, `GetSecretReference`, `GetSection` for a nested object, `GetSections` for an array of objects, and `GetStrings`, `GetInt64s`, and the like for arrays of scalars, which return `IReadOnlyList<T>` over immutable arrays. Each getter returns the default when a value is absent, or `null` for an optional field with no default. A name the schema does not declare throws with `config.field.unknown`, and a getter that does not match the declared type throws with `config.field.type_mismatch`. Neither exception carries a value.

**Rationale**: Holding the schema instance is the capability: the module that declared a section keeps it, and no other module can name its way in (FR-009, User Story 3). Getters typed by declaration make "typed" hold without a binding library or reflection.

**Alternatives considered**: Binding to options classes with `Microsoft.Extensions.Configuration.Binder`, which uses reflection, coerces types, and brings in the host configuration model that ADR-0004 says the embedded host must not take over; lookup by namespace string, which any module can call.

## R12. Diagnostics

**Decision**: `Iskender.Domain` takes `Microsoft.Extensions.Logging.Abstractions` 10.0.14, MIT. Events are declared with `LoggerMessage.Define`, which has fixed templates and event IDs, rather than the `[LoggerMessage]` source generator, which emits types the A18 namespace check would also have to accept:

| Event ID | Name | Level | Fields |
|---|---|---|---|
| 3001 | `ConfigurationValidated` | Information | revision, environment, namespaces with schema versions, base source, overlay source, environment variable count |
| 3002 | `ConfigurationFieldError` | Warning | code, path, source, line, column, layer |
| 3003 | `ConfigurationRejected` | Error | error count, reported count, environment |

Namespace lists are written as one bounded string of `name@version` pairs. Field values are built from the schema, the path, and the code alone, never from configuration values (FR-020, FR-037). No logger provider, metric, or exporter is added. A null logger is used when the host gives none.

**Rationale**: `ILogger` is the standard abstraction a .NET host supplies, so embedded and container hosts route these events their own way (ADR-0004). Fixed templates keep events structured and searchable, as `docs/12` requires.

**Alternatives considered**: `EventSource`, which hosts route less readily; the source generator, as above; writing to `Console`, which an embedding host does not control.

## R13. Error messages and suggestions

**Decision**: Every problem and fix text comes from a template per code, filled with the field path, the schema's constraint (range, length, members, schemes), and the layer, never with the value (FR-020). For `config.field.unknown`, the closest declared sibling is found with optimal string alignment distance (Levenshtein plus adjacent transposition), ignoring case. A name within distance 2 is named, ties going to the ordinal-first name. Otherwise the fix lists up to 10 declared siblings in ordinal order (FR-019).

A missing required field has no node of its own, so its location is the nearest ancestor present in any layer. If none is present, the location is source `(none)`, line 0, column 0, and layer `defaults`. Errors are ordered by layer (none, base, overlay, environment), then line, column, path, and code, so the order is total and environment variable errors, which all have line and column 0, come last, ordered by path (FR-018).

The text rendering of each error follows User Story 5, scenario 3, and the report ends with `<n> configuration error(s); configuration rejected.` or `Configuration valid: revision <revision>.` (FR-024). When more than 100 errors exist, the first 100 are kept, followed by `... <total - 100> more error(s) not shown.` before the final line.

**Rationale**: Templates are the only way to guarantee no value reaches a message, since the validator cannot always know which field holds a secret (TR-9). Transposition counts as one edit because it is the most common typing slip.

## R14. Registry build failures

**Decision**: `ConfigurationSchemaRegistry.Create(IEnumerable<SectionSchema>)` checks every schema and throws one `GatewayException` for the first fault in namespace order: `config.schema.duplicate_namespace`, with both owning modules in its `ErrorDiagnostics`, or `config.schema.invalid`, with the namespace, field, and broken rule in its diagnostics. Both are `internal` category, and both happen before any document is read (FR-008).

**Rationale**: A schema fault is a developer's bug in a module, found on the first test run. A single exception matches how `GatewayException` is used for other developer faults in ISK-0002, and diagnostics keep the module names out of the client-visible message.

**Alternatives considered**: Returning a report of schema faults, which mixes developer faults with operator errors in one type.

## R15. New error codes and the contract version

**Decision**: The 17 codes in FR-031 are added to `ErrorCodes` with a `since: 2` variant of the existing `Active` helper. Each new static field carries `[ContractVersion("core-contracts", 2)]`, as does each new public type in `Iskender.Core.Contracts.Configuration`. The assembly attribute becomes `[assembly: ContractPackage("core-contracts", 2)]`, the surface and registry baselines are regenerated, and the error codes table and versioning policy table of the ISK-0002 contract are updated in the same PR (FR-032, FR-033). The XML comment on `ConfigurationRevision` changes to: "An opaque marker naming the configuration a request runs under. ISK-0003 derives it for a validated configuration snapshot. ISK-0057 owns publication and activation." Its surface does not change (FR-030).

**Rationale**: This is the route the ISK-0002 contract gives for compatible additions: increment once, tag new items, declare no break.

## R16. Tests and where they live

**Decision**: A new test project, `tests/Iskender.Domain.Tests`, holds the configuration tests and fixtures, with xUnit v3, a committed lock file (rule B2), a solution entry, and a CODEOWNERS entry. Fixtures are files under `tests/Iskender.Domain.Tests/Configuration/Fixtures/`, kept LF by `.gitattributes`. Each negative fixture has a sidecar `<name>.expect.json` naming its expected code, path, line, and column (FR-035). The canary test plants one fixed string, `ISKENDER-CANARY-7f3a`, in every value of every negative fixture and checks reports, renderings, exception messages, and a capturing `ILogger` (SC-003). Contract-type tests (declarations, `SecretReference`, the report's invariants) go in `tests/Iskender.Core.Contracts.Tests`.

`SkeletonContainsNoGatewayBusinessCode` in `tests/Iskender.Core.Contracts.Tests/SolutionBuildsTests.cs` lists the source files allowed outside the contract packages. It gains `src/Iskender.Domain/Registry/` as an allowed prefix in the commit that adds the first source file there, as ISK-0002 did for `Iskender.Core.Contracts` (its research R15).

**Rationale**: The engine is `Iskender.Domain` code, so its tests need a project that may reference `Iskender.Domain`, and none exists. One project per production assembly under test matches the existing layout.

## R17. The license scan

**Decision**: `.github/scripts/check-license.sh` currently fails when any production project takes a package, saying "Extend this scan to cover package licenses". This PR extends it: for each package in each production project's `packages.lock.json`, it reads the `<license type="expression">` of the restored package's `.nuspec` in the NuGet global packages folder and passes only `MIT`, `Apache-2.0`, `BSD-2-Clause`, or `BSD-3-Clause`. A package with no expression, another expression, or no restored `.nuspec` fails with an owner action. It stays offline and read-only. The packages this PR adds are `YamlDotNet` (MIT), `Microsoft.Extensions.Logging.Abstractions` (MIT), and its dependency `Microsoft.Extensions.DependencyInjection.Abstractions` (MIT).

**Rationale**: The script names this extension as its own owner action, and Constitution I's gate is a license scan, which must keep passing once a package arrives. The listed licenses are permissive and compatible with MIT distribution. Changing `.github/` falls under its CODEOWNERS entry, so the Product/Charter owner reviews the allow-list.

**Alternatives considered**: Hard-coding the three package names, which the next package would break; a networked license service, which the offline rule forbids.

## R18. Documentation

**Decision**: `docs/03` (Persistence, publication and distributed coordination) and `docs/04` (Deployment invariants) each gain one sentence linking the configuration contract, in the style of the ISK-0001 and ISK-0002 links (FR-002). The contract carries the review owner for each section. This satisfies the documentation gate for the behavior-bearing paths this PR changes (`src/Iskender.Core.Contracts/`).

**Rationale**: FR-002 names both places, and `CONTRIBUTING.md` requires a documentation change with a behavior-bearing change.
