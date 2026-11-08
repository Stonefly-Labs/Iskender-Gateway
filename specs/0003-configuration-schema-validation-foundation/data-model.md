# Data Model: ISK-0003 — Configuration Schema & Validation Foundation

There is no database, table, or storage model (FR-038). These are the in-memory types, where each lives, and the rules each enforces. Exact member signatures are frozen by the configuration contract and the surface baseline, not here. Decisions are in [research.md](research.md).

## Public types: `Iskender.Core.Contracts.Configuration`

Every type below is public, sealed unless stated, immutable, and tagged `[ContractVersion("core-contracts", 2)]`.

### `SectionSchema`

A module's declaration of the section it owns.

| Field | Type | Rule |
|---|---|---|
| `Namespace` | `string` | 1 to 32 characters of `a-z`, `0-9`, and `-`, starting with a letter (FR-006) |
| `OwnerModule` | `string` | 1 to 64 characters, used in diagnostics only |
| `Version` | `int` | 1 or more |
| `Fields` | `IReadOnlyList<FieldSchema>` | Unique names, no two differing only in case (research R6, R7) |

Created through a fluent builder that returns new immutable instances. Its own consistency is checked when the registry is built, not when it is created (FR-008).

### `FieldSchema`

Created with `FieldSchema.Create(name, kind)` and `With` methods (`Required`, `DevelopmentOnly`, `WithDefault`, `WithConstraints`, `WithFields`, `WithItems`), each returning a new immutable instance, rather than one factory per type, since several type names (`Uri`, `SecretReference`, `Array`) would collide with types of the same name.

| Field | Type | Rule |
|---|---|---|
| `Name` | `string` | 1 to 64 characters of `a-zA-Z0-9`, starting with `a-z` |
| `Kind` | `ConfigurationFieldKind` | See below |
| `IsRequired` | `bool` | A required field has no default. An `object` field cannot be required, since it is always present |
| `Default` | typed value, or none | Must satisfy the field's own constraints. Not allowed on `object`, `array`, or `secretRef` fields |
| `IsDevelopmentOnly` | `bool` | Not allowed on a `secretRef` field |
| `Constraints` | `FieldConstraints` | Only those that apply to `Kind` (research R7) |
| `Fields` | `IReadOnlyList<FieldSchema>` | For `object` only |
| `Items` | `FieldSchema` | For `array` only. Not itself an `array` |

### `ConfigurationFieldKind` (enum)

`Text`, `WholeNumber`, `Number`, `Boolean`, `Enumeration`, `Duration`, `Uri`, `Section`, `Array`, `SecretReference` (FR-007). `Text`, `WholeNumber`, and `Section` are the contract's `string`, `integer`, and `object` types: analyzer rule CA1720 rejects type names as member names, and ISK-0001 rule B6 allows no suppression (maintainer decision, `implement` stage, 2026-11-08).

### `FieldConstraints`

| Constraint | Applies to | Form |
|---|---|---|
| Minimum, maximum | `Integer` (`long`), `Number` (`decimal`), `Duration` (`TimeSpan`) | Inclusive. Minimum not above maximum |
| Minimum length, maximum length | `String` | Unicode scalar values. Maximum not above 65,536 |
| Allowed characters | `String` | A `CharacterClassPattern`: `CharacterClasses` flags (lower-case letters, upper-case letters, digits) and listed punctuation, with an optional first-character class |
| Members | `Enumeration` | At least one, unique, case-sensitive |
| Minimum items, maximum items | `Array` | Inclusive |
| Allowed schemes | `Uri` | At least one when set, compared without regard to case |

### `SecretReference` (sealed class, value equality)

| Field | Type | Rule |
|---|---|---|
| `Name` | `string` | 1 to 256 characters of `a-zA-Z0-9._-/`, no leading or trailing `/`, no empty segment, no `..` segment (TR-7) |

`ToString()` returns `secretRef:<name>`. Construction with a malformed name throws `GatewayException` with `config.secret.reference_malformed`, and the exception carries no value.

### `ConfigurationLayer` (enum)

`Defaults`, `Base`, `Overlay`, `Environment`. Rendered in text as `defaults`, `base`, `overlay`, `environment`.

### `ConfigurationSourceLocation` (sealed class, value equality)

| Field | Type | Rule |
|---|---|---|
| `Source` | `string` | The source name a host gave, an environment variable's name, or `(none)` |
| `Line`, `Column` | `int` | 1-based, or 0 for an environment variable or `(none)` |
| `Layer` | `ConfigurationLayer` | |

### `ConfigurationFieldError` (sealed class)

| Field | Type | Rule |
|---|---|---|
| `Code` | `ErrorCode` | One of the codes in FR-031 |
| `Path` | `string` | Dot-separated from `iskender`, array items as `[n]`, or the reserved `(document)` or `(environment)` for an error not tied to one field. Never empty |
| `Location` | `ConfigurationSourceLocation` | |
| `Problem` | `string` | From the code's template. Never holds a value (FR-020) |
| `Fix` | `string` | From the code's template. Never empty |

`ToString()` returns the line form in the contract's Text rendering section.

### `ConfigurationValidationReport` (sealed class)

| Field | Type | Rule |
|---|---|---|
| `IsValid` | `bool` | |
| `Snapshot` | `ConfigurationSnapshot?` | Set exactly when valid |
| `Revision` | `ConfigurationRevision?` | Set exactly when valid, equal to `Snapshot.Revision` |
| `Errors` | `IReadOnlyList<ConfigurationFieldError>` | Empty when valid. At most 100 when not |
| `TotalErrorCount` | `int` | 0 when valid. At least `Errors.Count` when not |

Created only through `Success(ConfigurationSnapshot)` and `Failure(IEnumerable<ConfigurationFieldError>, int totalErrorCount)`. A failure with no errors, or a success with a null snapshot, throws (FR-022). `ToString()` returns the full text rendering (FR-024).

### `ConfigurationSnapshot` (abstract class, protected constructor)

| Member | Type | Rule |
|---|---|---|
| `Revision` | `ConfigurationRevision` | `cfg-` and 52 base32 characters (research R9) |
| `EnvironmentName` | `string?` | As given. `null` when none was set |
| `Namespaces` | `IReadOnlyList<SectionVersion>` | Each registered namespace and its schema version, in ordinal order |
| `Layers` | `ConfigurationLayers` | Base source name or none, overlay source name or none, count of environment variables applied |
| `GetSection(SectionSchema)` | `ConfigurationSection` | Only for the registered instance. Any other throws `config.section.invalid_access` (research R11) |

No member changes a value or returns a writable collection (FR-009, FR-029). Only `Iskender.Domain.Registry` derives from it in this repository, which a test checks (research R10).

### `ConfigurationSection` (abstract class, protected constructor)

| Member | Rule |
|---|---|
| `Namespace`, `SchemaVersion` | The section's identity |
| `Path` | Its path from `iskender`, such as `iskender.persistence` or `iskender.persistence.pools[0]` |
| Typed getters | `GetString`, `GetInt64`, `GetDecimal`, `GetBoolean`, `GetDuration`, `GetUri`, `GetSecretReference`, `GetSection`, `GetSections`, and array forms returning `IReadOnlyList<T>`. Absent values give the default, or `null` when there is none. `GetSection` for a nested object never returns `null`, since every object is materialized. An undeclared name, or a getter of the wrong type, throws `config.section.invalid_access` |

### Supporting value types

`SectionVersion` (namespace and version) and `ConfigurationLayers` (base source, overlay source, environment variable count): sealed, immutable, value equality.

## Engine types: `Iskender.Domain.Registry`

Public where a host or test must call them, internal otherwise. `Iskender.Domain` is embeddable and unversioned, so these are not a supported API.

| Type | Visibility | Role |
|---|---|---|
| `ConfigurationSchemaRegistry` | public | Built once from `SectionSchema` instances. Throws on `config.schema.duplicate_namespace` or `config.schema.invalid` (FR-008, research R14) |
| `ConfigurationDocumentSource` | public | A named document with its `ConfigurationDocumentFormat` (`Json` or `Yaml`), created from bytes, a string, a stream, or a file path. The stream and file forms read at most the size limit plus one byte |
| `ConfigurationEnvironmentSource` | public | A read-only map of variable names to text, created from a dictionary or from the process |
| `ConfigurationLoadRequest` | public | The registry, an optional base document, an optional overlay document, an optional environment source, and an optional environment name |
| `ConfigurationValidator` | public | `Validate(ConfigurationLoadRequest, ILogger?)` returns a `ConfigurationValidationReport`. No other public route produces a snapshot (FR-022, FR-023) |
| `ConfigurationLimits` | internal | The TR-12 limits, as constants |
| `CanonicalNode` and kinds | internal | The tree in research R3, with each node's `ConfigurationSourceLocation` |
| `JsonDocumentReader`, `YamlDocumentReader` | internal | Bounded readers (research R1, R2, R4) |
| `EnvironmentVariableReader` | internal | Research R6 |
| `OverlayMerger` | internal | Research R5 |
| `SchemaValidator` | internal | Every FR-021 check, collecting errors (FR-018) |
| `ErrorTextTemplates`, `NameSuggester` | internal | Research R13 |
| `CanonicalWriter`, `Base32Lower` | internal | Research R9 |
| `ValidatedSnapshot`, `ValidatedSection` | internal sealed | The only subclasses of the public abstract views |
| `ConfigurationLog` | internal | The three `LoggerMessage.Define` events (research R12) |

## Flow and states

```text
SectionSchema* ──► ConfigurationSchemaRegistry.Create ──► throws config.schema.* (developer fault)
                                │
                                ▼
ConfigurationLoadRequest ──► check environment name; read base, overlay (bounded; document errors) ──► any error? ─► Failure report
                         ──► read environment variables (prefix, mapping)
                                │
                                ▼
                    check each layer for unknown names (names the layer)
                                │
                                ▼
                    merge: base ◄ overlay ◄ environment; then defaults
                                │
                                ▼
                    validate merged tree against schemas (collect all errors)
                                │
              ┌─────────────────┴─────────────────┐
         errors > 0                           errors = 0
              │                                   │
     Failure(first 100, total)        canonical form ──► SHA-256 ──► cfg-<base32>
     log 3002 × n, 3003                ValidatedSnapshot ──► Success(snapshot)
                                       log 3001
```

A document error, such as malformed input, an unsupported construct, or a size limit, stops that document's reading. Other documents are still read and checked for document errors, so one run reports every document's first fault, and field validation does not run.

A report is a final value. A snapshot never changes after it is created, and a new configuration gives a new snapshot (FR-029). There is no reload state (FR-038).

## Relationships

- A `SectionSchema` is owned by exactly one module and registered once. A `ConfigurationSchemaRegistry` holds every schema of a process.
- A `ConfigurationSnapshot` holds one `ConfigurationSection` per registered namespace, reachable only through that namespace's registered `SectionSchema` instance.
- A `ConfigurationRevision` (ISK-0002) names exactly one canonical content. Two snapshots with equal revisions hold equal values.
- A `SecretReference` names a secret held elsewhere. ISK-0021 resolves it, and nothing here does.
