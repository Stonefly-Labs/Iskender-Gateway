# Contract: Configuration Schema and Validation

**Version**: 1

**Sources**: the public types in `src/Iskender.Core.Contracts/Configuration/`, the engine in `src/Iskender.Domain/Registry/`, and the error codes in `src/Iskender.Core.Contracts/Errors/ErrorCodes.cs`.

**Enforced by**: `tests/Iskender.Domain.Tests` (engine behavior, fixtures, canary, determinism) and `tests/Iskender.Core.Contracts.Tests` (public types), under CI check "4. Test". The public surface and the error codes are also held by the [public contracts and versioning contract](../../0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md), whose harness fails the run on an undeclared break.

**Review owners**: listed per section below. See `CONTRIBUTING.md` for who holds each role. `.github/CODEOWNERS` requests review for the sources and the test project. While rules R5 and R6 in `docs/18-branch-protection.md` stay unapplied under logged exception DEC-20261105-002, GitHub does not require that review.

## Changing this contract

A change to a rule, form, limit, precedence, canonical form, or event below is made by a spec's revision of this contract. The revision increments the version, publishes the contract in that spec's `contracts/` directory, repoints the links in `docs/03-domain-and-technical-architecture.md` and `docs/04-technology-stack-and-deployment.md`, and adds a row here:

| Version | Spec | Change | Reason | Review owner |
|---|---|---|---|---|
| 1 | ISK-0003 | Baseline: root and namespaces, inputs, overlays, environment, secret references, revisions, limits, errors, rendering, events | First configuration foundation | Architecture (ADR) owner |

A change to the canonical form changes every revision, so it also increments the canonical form's `form` number.

Adding a section schema is not a change to this contract. Each module's spec declares its own section.

## Root and namespaces

*Review owner: Architecture (ADR) owner.*

1. All Iskender configuration sits under the root key `iskender`. Keys outside it are never read, changed, validated, or reported.
2. Each key directly under `iskender` is a namespace, owned by exactly one module through a registered `SectionSchema`. A namespace is 1 to 32 characters of `a-z`, `0-9`, and `-`, starting with a letter.
3. A field name is 1 to 64 characters of `a-z`, `A-Z`, and `0-9`, starting with `a-z`. Sibling names are unique and may not differ only in case.
4. Validation is closed. An undeclared namespace is `config.namespace.unknown`, and an undeclared field is `config.field.unknown`. No schema can opt out.
5. A module reads its section only through the `SectionSchema` instance it registered. Any other instance fails with `config.namespace.unknown`.
6. Configuration here is system and operator configuration. It defines no tenant-owned resource and never acts for a tenant.

### Field types

| Type | Value | Constraints |
|---|---|---|
| `string` | Text | Length in Unicode scalar values, allowed-character pattern |
| `integer` | 64-bit signed integer, written `-?(0\|[1-9][0-9]*)` | Inclusive range |
| `number` | `decimal`, written in the JSON number grammar | Inclusive range |
| `boolean` | `true` or `false` | |
| `enumeration` | One of the declared members, case-sensitive | At least one member |
| `duration` | A non-negative integer and a unit, `ms`, `s`, `m`, `h`, or `d`, such as `30s` | Inclusive range |
| `uri` | An absolute URI | Allowed schemes |
| `object` | Declared fields | |
| `array` | Items of one declared type, not itself an array | Item count |
| `secretRef` | A secret reference placeholder | |

Any field may be required, may have a default unless it is an `object`, `array`, or `secretRef`, and may be marked development-only unless it is a `secretRef`. A required field has no default.

### Schema faults

Building the registry fails, before any document is read, with a `GatewayException` in the `internal` category:

| Code | When |
|---|---|
| `config.schema.duplicate_namespace` | Two registrations name one namespace, including one module registering it twice. Diagnostics name both owners |
| `config.schema.invalid` | A malformed namespace or field name; duplicate or case-colliding sibling names; a required field with a default; minimum above maximum; an empty enumeration; a default that breaks its own constraints; a constraint that does not apply to the type; a default on an `object`, `array`, or `secretRef`; a development-only `secretRef`; an array of arrays |

## Inputs

*Review owner: Security owner, for the YAML subset and limits; Architecture (ADR) owner otherwise.*

1. A host or test supplies sources: at most one **base** document, at most one **overlay** document for the active environment, and at most one set of environment variables. Each document has a source name and a declared format, `json` or `yaml`.
2. **JSON** is RFC 8259: no comments, no trailing commas.
3. **YAML** is a JSON-equivalent subset. These are rejected with `config.document.unsupported_construct`, at the first one, before any expansion: anchors, aliases, tags of any kind, merge keys (`<<`), `%YAML` and `%TAG` directives, and more than one document.
4. A key repeated in one object is `config.document.duplicate_key`, in both formats.
5. A document that is not well-formed, is not valid UTF-8, starts with a byte order mark, or holds a control character other than tab, line feed, or carriage return is `config.document.malformed`, at the location where reading stopped.
6. A scalar is typed by its schema field, never by YAML's implicit typing:

   | Field type | A JSON value must be | A YAML or environment value may be |
   |---|---|---|
   | `string`, `enumeration`, `duration`, `uri` | A string | Quoted or unquoted. Unquoted text is taken as written, so `no` stays `no` |
   | `integer`, `number` | A number | Unquoted, in the number form above. A quoted value is a type mismatch |
   | `boolean` | `true` or `false` | Unquoted `true` or `false` only. `yes`, `on`, and `True` are type mismatches |

7. `null`, and in YAML an unquoted `~` or empty value, means "absent".
8. A document error stops that document. Every supplied document is still read for its own document error, and field validation does not run.

### Limits

Per document. A schema may set tighter limits on its own fields, never looser ones.

| Limit | Value | Error |
|---|---|---|
| Input size | 1 MiB (1,048,576 bytes) | `config.document.too_large`. Reading stops at the byte past the limit |
| Nesting depth | 32 | `config.document.too_large` |
| Nodes | 10,000 | `config.document.too_large` |
| String value | 64 KiB of UTF-8 (65,536 bytes) | `config.document.too_large` |
| Field errors reported | 100, then a truncation line with the remaining count | |

These are safety bounds, not performance figures (Constitution VIII).

The reader applies the string limit, in UTF-8 bytes, before any schema check. A field's maximum length counts Unicode scalar values and may be up to 65,536, so a string within that length whose UTF-8 form passes 65,536 bytes is rejected first, as `config.document.too_large`.

## Overlays and environment variables

*Review owner: Architecture (ADR) owner.*

### Precedence

Lowest to highest: **defaults** (schema defaults), **base**, **overlay**, **environment**.

1. Objects merge by key.
2. A scalar or array at a higher layer replaces the lower value whole.
3. `null` at any layer means absent and never removes a lower value.
4. A higher layer may not introduce an undeclared field. The error is `config.field.unknown` and names that layer.
5. A missing overlay is valid, and the snapshot records that none was used.

### Environment variables

1. Only names starting with `ISKENDER__`, compared without regard to case, are read. All other variables are ignored.
2. The rest of the name, split on `__`, maps without regard to case to a declared path under `iskender`. For example, `ISKENDER__PERSISTENCE__CONNECTIONTIMEOUT` maps to `iskender.persistence.connectionTimeout`.
3. A variable sets a scalar field only. A path to an `object` or `array` field, or through an array, is `config.field.type_mismatch`.
4. A `secretRef` field is set with a final `__SECRETREF` segment, whose text is the reference name: `ISKENDER__PROVIDERS__APIKEY__SECRETREF=openai/primary`. A variable naming the field itself is `config.secret.plaintext`.
5. Two variables mapping to one path are `config.overlay.conflict`. The error names both variables and neither value.
6. An undeclared path is `config.field.unknown`, with layer `environment`.
7. A variable's location is its name, with line 0 and column 0.

### Active environment

1. The host supplies the active environment name, or none.
2. A name, when given, is 1 to 32 characters of `a-z`, `0-9`, and `-`. Anything else is `config.environment.invalid`.
3. Only `development` permits development-only values. Any other name, and no name, is production-grade. No name is not itself an error.
4. A development-only field holding anything other than its default, outside `development`, is `config.value.unsafe_for_environment`.

## Secret reference placeholders

*Review owner: Security owner.*

1. A placeholder is an object with exactly one member, `secretRef`, whose value is a string:

   ```yaml
   apiKey:
     secretRef: openai/primary
   ```

2. The reference name is 1 to 256 characters of `a-z`, `A-Z`, `0-9`, `.`, `_`, `-`, and `/`. It does not start or end with `/`, and has no empty segment and no `..` segment.
3. A `secretRef` field accepts only a placeholder. A plain value is `config.secret.plaintext`.
4. A placeholder in any other field is `config.field.type_mismatch`.
5. A malformed name, another member beside `secretRef`, or a non-string `secretRef` value is `config.secret.reference_malformed`.
6. In a snapshot, the value is a `SecretReference` carrying only the name. Its text form is `secretRef:<name>`.
7. Nothing in this contract resolves a reference or contacts a secret store. ISK-0021 and the credential specs own resolution.
8. A secret's value is never in configuration, so a revision hashes the reference name, and rotating the secret does not change the revision.

## Revisions and snapshots

*Review owner: Architecture (ADR) owner.*

### Canonical form

UTF-8 JSON with no insignificant whitespace:

```json
{"form":1,"iskender":{...},"schemas":{"<namespace>":<schema version>}}
```

1. Object keys at every level are sorted by ordinal comparison of UTF-16 code units, so `form`, `iskender`, `schemas` appear in that order.
2. The `iskender` member is the merged, validated tree, with every default that applies written in. An absent optional field with no default is omitted.
3. Integers are written in shortest decimal form. Numbers are written as their `decimal` value with no exponent and no trailing fractional zeros (`1.50` and `15e-1` are both `1.5`). Booleans are `true` or `false`.
4. A duration is its whole number of milliseconds, as an integer. A URI is its validated text. A string is its exact code points. No Unicode normalization is applied.
5. A placeholder is `{"secretRef":"<name>"}`.
6. The environment name, source names, and the split of values between layers are not part of the form.

### Revision

The revision is `cfg-` followed by the SHA-256 of the canonical form, in lower-case RFC 4648 base32 (`a-z`, `2-7`) with no padding: 56 characters in all. It is a `ConfigurationRevision` (ISK-0002), and no parallel type exists.

1. Identical canonical content gives an identical revision, whatever the format, key order, whitespace, comments, layer split, or host.
2. Any change to a value in the merged configuration, or to a registered schema version, changes the revision.
3. ISK-0003 derives the revision of a validated snapshot. ISK-0057 owns publication, signing, and activation.

### Snapshot

1. A validation that reports any error returns no snapshot and no revision. There is no public way to obtain a partly validated configuration.
2. A snapshot records its revision, the active environment, each namespace with its schema version, and the layers used: the base and overlay source names, or none, and the count of environment variables applied.
3. A snapshot and its sections never change after creation, and no member returns a writable collection.
4. `ConfigurationSnapshot` and `ConfigurationSection` are abstract, with protected constructors. In this repository, only `Iskender.Domain.Registry` derives from them, which a test checks.

## Error codes

*Review owner: Architecture (ADR) owner.*

These codes are in the `ErrorCodes` registry and in the error codes table of the public contracts and versioning contract, `since` 2.

| Code | Category | Meaning | Safe message |
|---|---|---|---|
| `config.document.malformed` | `invalid_request` | The document is not well-formed JSON or YAML, or is not clean UTF-8 | The configuration document could not be read. |
| `config.document.too_large` | `invalid_request` | The document passes a size, depth, node, or string limit | The configuration document is larger than allowed. |
| `config.document.duplicate_key` | `invalid_request` | A key appears twice in one object | The configuration document repeats a key. |
| `config.document.unsupported_construct` | `invalid_request` | The YAML uses an anchor, alias, tag, merge key, or several documents | The configuration document uses an unsupported construct. |
| `config.namespace.unknown` | `invalid_request` | A key under `iskender` names no registered section | The configuration names an unknown section. |
| `config.field.unknown` | `invalid_request` | A field is not declared by its section schema | The configuration names an unknown field. |
| `config.field.required` | `invalid_request` | A required field without a default is missing | A required configuration field is missing. |
| `config.field.type_mismatch` | `invalid_request` | A value is not of the declared type | A configuration value has the wrong type. |
| `config.field.out_of_range` | `invalid_request` | A value is outside its range, length, or count, or does not fit its type | A configuration value is outside its allowed range. |
| `config.field.invalid_format` | `invalid_request` | A value breaks its pattern, enumeration, duration, or URI rule | A configuration value is not in a valid format. |
| `config.secret.plaintext` | `invalid_request` | A secret field holds a plain value instead of a placeholder | A secret configuration field must use a secret reference. |
| `config.secret.reference_malformed` | `invalid_request` | A placeholder's form or reference name is invalid | A secret reference is not in a valid format. |
| `config.value.unsafe_for_environment` | `invalid_request` | A development-only value is set outside `development` | A configuration value is not allowed in this environment. |
| `config.environment.invalid` | `invalid_request` | The active environment name is malformed | The environment name is not in a valid format. |
| `config.overlay.conflict` | `invalid_request` | Two environment variables map to one field | Two configuration sources set the same field. |
| `config.schema.duplicate_namespace` | `internal` | Two section schemas claim one namespace | An internal error occurred. |
| `config.schema.invalid` | `internal` | A section schema contradicts itself | An internal error occurred. |

A section getter called with an undeclared name throws `config.field.unknown`, and one called with the wrong type throws `config.field.type_mismatch`. Both are developer faults reported with the field name in diagnostics.

## Field errors

*Review owner: Security owner, for rule 3; Architecture (ADR) owner otherwise.*

1. Every check runs on the merged tree, and every field error is collected in one pass.
2. Each field error carries a code from the table above, a path, a location (source, line, column, layer), a problem, and a fix.
   - The path is dot-separated from `iskender`, with array items as `[n]`, such as `iskender.persistence.pools[1].host`.
   - A missing required field takes the location of its nearest present ancestor. With none, the location is `(none)`, line 0, column 0, layer `defaults`.
   - The problem names the broken constraint, such as "must be an integer from 1 to 300".
   - For `config.field.unknown`, the fix names the closest declared sibling within an edit distance of 2, counting an adjacent transposition as one edit and ignoring case. Otherwise it lists up to 10 declared siblings.
3. **No error, rendering, log event, exception message, or diagnostic contains a configuration value, from any field.** Text is built from the code, the path, the schema, and the layer only.
4. Errors are ordered by layer (`defaults`, `base`, `overlay`, `environment`), then line, column, path, and code. So environment variable errors come last, ordered by variable name.
5. At most 100 errors are reported. The total is always given.

## Text rendering

*Review owner: Privacy/Operations owner.*

One line per error:

```text
<code> at <path> (<source>:<line>:<column>, <layer>): <problem>. Fix: <fix>
```

For example:

```text
config.field.unknown at iskender.persistence.conectionTimeout (gateway.yaml:12:5, base): the field is not declared by section persistence. Fix: did you mean connectionTimeout?
config.field.out_of_range at iskender.persistence.connectionTimeout (gateway.production.yaml:4:24, overlay): must be an integer from 1 to 300. Fix: set a value from 1 to 300.
```

When more than 100 errors exist, a line `... <n> more error(s) not shown.` follows the 100th. The last line is one of:

```text
<n> configuration error(s); configuration rejected.
Configuration valid: revision <revision>.
```

The rendering is a fixed text form for people. No wire contract is defined for the report. A serialized report belongs to the management specs.

## Diagnostic events

*Review owner: Privacy/Operations owner.*

Written through the `ILogger` a host supplies. No logging provider, metric, or exporter is added. Templates and event IDs are fixed.

| Event ID | Name | Level | Template |
|---|---|---|---|
| 3001 | `ConfigurationValidated` | Information | `Configuration validated: revision {Revision}, environment {Environment}, sections {Sections}, base {BaseSource}, overlay {OverlaySource}, environment variables {EnvironmentVariableCount}.` |
| 3002 | `ConfigurationFieldError` | Warning | `Configuration error {Code} at {Path} ({Source}:{Line}:{Column}, {Layer}).` |
| 3003 | `ConfigurationRejected` | Error | `Configuration rejected: {ErrorCount} error(s), {ReportedCount} reported, environment {Environment}.` |

1. A valid configuration writes one 3001 event.
2. A rejected configuration writes one 3002 event per reported error, then one 3003 event.
3. `{Sections}` is `name@version` pairs joined by `,`. `{Environment}` is the name, or `(none)`. `{BaseSource}` and `{OverlaySource}` are source names, or `(none)`.
4. No event holds a configuration value, and the canary test checks every event.

## Embedding

*Review owner: Architecture (ADR) owner.*

1. Validation is a library call with in-memory or host-supplied sources. It starts no host, opens no network connection, and reads no secret.
2. It reads no configuration of the host's and never changes it. Keys outside `iskender` are untouched.
3. Refusing to start, or reporting not ready, on a rejected configuration is the host's job (ISK-0006, ISK-0007).

## Threat model and known limits

### What the rules defend against

An operator's mistake (a misspelled key, a wrong type, an out-of-range value, a forgotten environment name, a secret written into a file) and a hostile or broken document (an alias bomb, a type-instantiating tag, a huge or deeply nested file, duplicate keys hiding a value). Each is rejected before anything runs, with an error that names where it is and holds no value.

### Known limits

- **Secrets are found by declaration only**: A secret pasted into a field not declared as a secret is not detected. If that field then fails validation, no value is echoed. Detecting secrets by content is post-MVP work for the telemetry and privacy specs.
- **In-process code can make a snapshot**: `ConfigurationSnapshot` and `ConfigurationSection` have protected constructors, so code in the same process can derive one. In-process code is already trusted, since assembly loading is not a sandbox. A test checks that only `Iskender.Domain.Registry` derives from them in this repository. Code from outside the repository, such as a plugin or an embedding application, is not scanned.
- **Later members are virtual**: A new abstract member on either class would break a deriving type, so members added later are virtual with a default.
- **Allocation is bounded, not zero**: The YAML parser keeps fixed buffers, so a very small hostile document can allocate more than its own size before it is rejected. SC-004 bounds this at 10 times the fixture's size.
- **Environment variables are visible to the process**: A placeholder's reference name set through a variable is visible to anything that can read the process's environment. A reference name is not a secret.

## Negative security and failure scenarios

| Scenario | Outcome | Section |
|---|---|---|
| A plaintext API key in a secret field | `config.secret.plaintext`, and the key appears in no output | Secret reference placeholders, Field errors rule 3 |
| A plaintext key set through a variable for a secret field | `config.secret.plaintext`, since only `__SECRETREF` sets a placeholder | Environment variables rule 4 |
| A YAML alias bomb | `config.document.unsupported_construct` at the first alias, before expansion | Inputs rule 3 |
| A YAML tag that instantiates a type | `config.document.unsupported_construct` | Inputs rule 3 |
| A huge or deeply nested document | `config.document.too_large`, reading stops at the limit | Limits |
| A misspelled security setting | `config.field.unknown`, naming the intended field | Root and namespaces rule 4, Field errors rule 2 |
| Duplicate keys hiding a value | `config.document.duplicate_key` | Inputs rule 4 |
| `no` read as `false`, `0755` read as octal | Typed by schema | Inputs rule 6 |
| A development-only value in production, or with no environment name | `config.value.unsafe_for_environment` | Active environment |
| An unrelated variable or a host setting changing Iskender configuration | Ignored | Root and namespaces rule 1, Environment variables rule 1 |
| A reference name that traverses paths | `config.secret.reference_malformed` | Secret reference placeholders rule 2 |
| One module reading another's section | Fails without the registered schema instance | Root and namespaces rule 5 |
| A partly valid configuration used | No snapshot after any error | Snapshot rule 1 |
| Configuration changed after validation | Snapshots never change | Snapshot rule 3 |
| Replicas disagreeing on a revision | Derived from canonical content only | Revision rule 1 |
| A secret inferred from a revision | Only reference names are hashed | Secret reference placeholders rule 8 |
| A log line showing a value | Fixed templates, checked by the canary test | Diagnostic events rule 4 |
