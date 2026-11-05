# 09 — Policy language and execution architecture

**Authority:** policy model/ordering/inheritance/runtime. Built-ins: [10](10-first-party-policy-catalog.md). Plugin SDK: [11](11-plugin-extensibility-and-sdk.md).

## Approved architecture

- Versioned, **strongly typed .NET policy definitions** and runtime handlers.
- **CEL (Common Expression Language)** exclusively for conditional *applicability* (`when`); typed **C# handlers** perform effects. No custom procedural DSL, Rego swap or arbitrary source-code execution from configuration.
- JSON/YAML declarative definitions plus fluent C# builder configuration for embedded hosts; both compile to one canonical validated intermediate model.
- Deterministic lifecycle stages, hierarchical org/project/resource attachments, explicit inheritance, overrides and immutable mandatory guardrails.
- Configuration changes are **validated and compiled into immutable execution plans**, distributed and **atomically activated** per replica at request boundaries. GitOps and management API ultimately converge on the same activation path.

## Policy shape and type safety

Illustrative serialized policy assignment (schema and exact stage names settled in corresponding specs):

```yaml
id: "org-prod-cap"
scope: { organization: "org-123" }
stage: admission
order: 200
kind: iskender.governance.spend-cap
version: 1
when: "request.model == 'primary-chat'"
config:
  currency: USD
  maxSpendMinorUnits: 250000
  period: calendar_month
  enforcement: hard
inheritance: mandatory
```

**This is an illustrative design, not an approval of numeric policy defaults or final serialized schema.** Stable `kind` identifiers are namespaced; first-party identifiers use `iskender.<domain>.<semantic-action>`, partner plugins use a separately owned namespace (e.g., `vendor.example.<domain>.<action>`). Do **not** reuse exact Azure APIM policy keywords such as `validate-jwt`, `rate-limit`, `set-header`, `trace`, or `emit-metric` as Iskender identifiers. UI labels remain human-readable and need not equal kinds.

## Lifecycle stages and ordering

Proposed typed stage sequence (exact names may change only by recorded contract migration): `ingress` → `authentication` → `authorization` → `admission` → `route-selection` → `upstream-request` → `upstream-response` → `egress` → `completion` / `failure`. Mandatory platform authentication/ownership checks execute regardless of optional policy assignments; a plugin cannot reposition them. Stage contracts declare whether streaming mutation is legal and what data is accessible. Default order resolves by stage, ancestor depth/mandatory classification, declared integer order and stable assignment ID; ties do **not** rely on database iteration order. A policy's capabilities (e.g. credential access, response modification, telemetry writing) must be explicit.

## Inheritance, overrides and guardrails

The compiler traverses system baseline → organization → project → resource scope. Every assignment declares whether it is mandatory, additive, replaceable, bounded or disabled at lower scopes; unsupported override conflicts fail validation. Mandatory parent guardrails are never shadowed; most restrictive hard limit applies where compatible, while incompatible policy configurations produce an actionable compile error. Revisions and effective policy provenance are observable through a safe inspection API. Embedded fluent configuration cannot bypass platform mandatory checks.

## CEL contract

Evaluate CEL over a **bounded, versioned, typed, read-only** context: caller metadata/claims (redacted view), organization/project scope, endpoint, model alias, request modality/estimated token counts, deployment metadata, secure time, admitted usage snapshot and prior safe policy outputs. CEL cannot access arbitrary .NET reflection, filesystem, network, secret values or request body by default. Schema-check expressions at compile time, precompile/cache ASTs, restrict cost/recursion/time and fail closed for mandatory security predicates. `when` evaluation errors must be diagnosable by safe expression ID/error category, not converted to silent `false` for mandatory controls. CEL **only decides whether the typed handler is invoked**; no effectful CEL functions.

## Runtime interface and plans

`IPolicyHandler<TConfig>`/`PolicyDescriptor` are illustrative: descriptors declare stable kind/version, config schema/serializer, legal stages/scopes, inheritance strategy, required capabilities, lifetime, streaming support and deterministic mutation rules. Handlers receive an immutable capability-filtered context, their typed config and cancellation; return structured effects (deny, select constraints, headers, annotations, usage hints, route constraints, telemetry event) instead of manipulating arbitrary host objects where possible.

Compile performs schema validation, CEL type checking, permission/scope validation, dependency resolution, policy-graph ordering, capability checking, inheritance merge and plan hashing. A successful plan is immutable and versioned. Activation atomically swaps the current plan pointer; requests already running retain their pinned revision through completion. Reject invalid updates without disturbing last-known-good, except when mandatory security state requires readiness fail-closed. Management exposes preview/diff/why a policy applies and sanitized execution diagnostics.

## Security and performance

No external code is run merely because it appears in YAML; custom C# handler must be separately packaged, locked and approved [11](11-plugin-extensibility-and-sdk.md). Place hard bounded data/time budgets on CEL and handlers, enforce body/header size limits and redact PII/secrets. In-process plugins are **trusted** and cannot be sandboxed by AssemblyLoadContext. Streaming response mutation may not buffer unbounded outputs; protected headers and auth context cannot be overwritten. Precompile, pool and benchmark plans; avoid dynamic reflection on request hot path.

## Built-in and custom parity

Built-ins register descriptors/handlers through the same contracts as custom policies wherever feasible; mandatory security logic remains non-bypassable platform code. A first-party policy is a product-supported implementation of a contract, not an APIM XML syntax clone. Catalog [10](10-first-party-policy-catalog.md) sets stable public IDs, stage/legal scope and expected effects. Plugin and core version compatibility are validated before activation.

## Acceptance criteria

Deterministic ordering/property tests; equivalent JSON/YAML/fluent evaluation; invalid CEL compile error with line/field; simulated policy conflict, mandatory guardrail override denial, atomic swap under concurrent streaming load, missing-plugin rejection, policy timeout and secret exclusion; generated effective-plan diff; latency/allocation regression budget; edge-case replayable diagnostics. Validate forward-compatible schema changes and explicit incompatibility errors.
