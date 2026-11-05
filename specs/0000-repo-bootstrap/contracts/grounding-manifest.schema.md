# Contract: Grounding Manifest Schema & Validation

**Applies to**: `.iskender/grounding.yaml` (read-only; preserved). Consumer: `Iskender.Workflow`
manifest validator. Traces: FR-012..FR-015, SC-003.

## Shape (schemaVersion 1)

```yaml
schemaVersion: 1                      # int; MUST equal a supported major (currently 1)
grounding:
  root: docs                          # relative to repo root
  inventory: 17-master-spec-inventory.md
  constitutionPrompt: 15-constitution-prompt.md
  bootstrapBrief: 14-spec-0000-bootstrap-brief.md
  decisionRegister: 16-architecture-decision-register.md
speckit:
  constitution: .specify/memory/constitution.md    # relative to repo root
  specificationsRoot: specs                         # relative to repo root
workflow:
  initialSpec: '0000'
```

## Validation rules (fail-closed)

| # | Rule | Failure output |
|---|---|---|
| V1 | File parses as YAML | parser error + file path + owner action |
| V2 | `schemaVersion` is an integer equal to a supported major (`1`) | offending value + supported majors + owner action |
| V3 | All required keys present (the keys shown above) | missing key path + owner action |
| V4 | `grounding.root` resolves to an existing dir **under** repo root | offending value |
| V5 | Each `grounding.*` doc path resolves under `grounding.root` and the target file exists | offending key + path |
| V6 | Each `speckit.*` path resolves under repo root | offending key + path |
| V7 | No path uses `..` traversal, a disallowed absolute path, or a symlink whose resolved real path escapes the repo root (checked after full symlink expansion) | offending value flagged "unsafe" |
| V8 | Unknown keys are tolerated | warning only (not a failure) |

## Guarantees

- On **any** V1–V7 failure the validator returns a non-zero result naming the exact offending
  key/value and the owner instruction; **no workflow stage proceeds** (SC-003: 100% blocked).
- The validator never guesses a missing path or invents a second registry (FR-015).
- Validation is pure/read-only — it never writes to the manifest or the Spec Kit tree.

## CLI surface

`Iskender.Workflow.Cli validate-manifest [--manifest <path>]` → exit 0 (valid) or non-zero (first
failure, with the message above). Also callable as a library: `ManifestValidator.Validate(path)`
returning a typed result `{ IsValid, OffendingKey, OffendingValue, Reason, OwnerAction }`.
