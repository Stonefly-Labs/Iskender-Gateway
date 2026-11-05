# 10 — First-party policy catalog

**Authority:** proposed, comprehensive **MVP** built-in handler catalog and stable identifier namespace. Types/lifecycle defined in [09](09-policy-language-and-execution.md); plugin equivalence in [11](11-plugin-extensibility-and-sdk.md).

## Naming and security semantics

Built-in kinds use `iskender.<domain>.<semantic-action>`, lower-case kebab-cased semantic segments and explicit schema versions. Never duplicate exact Azure API Management XML policy identifiers (`validate-jwt`, `rate-limit`, `set-header`, `trace`, `emit-metric`). Third-party policies must use separately owned namespaces; plain-language UI labels are independent of identifiers. An ID is a contract and cannot be reassigned to a different meaning. Decommission through aliases/migrations, not breaking reuse.

**Classifications:** MVP security-enhancing, governance, financial, routing, resilience, transformation, context, telemetry. Platform identity validation, tenant isolation, baseline RBAC, secret protection, and plugin trust enforcement exist **outside** this optional catalog and cannot be disabled. Auth predicates enhance an already-validated identity. No redundant separate JWT-only claims policy: `iskender.access.jwt-requirements` covers JWT claims, while `iskender.access.authorize-action` is credential-independent.

Scope key **O/P/R** = organization/project/resource; **P/R** = project/resource. Inheritance: **M** mandatory/monotone restrictive; **A** additive; **B** bounded overrides. Final runtime ordering and handler contracts remain in [09](09-policy-language-and-execution.md).

## MVP handler definitions

### `iskender.access.jwt-requirements` — JWT requirements

- **Purpose:** Require an already-valid bearer credential with configured issuer/audience/scope/typed claim constraints; does not replace platform JWT validation.
- **Typed configuration model (fields):** `issuers[], audiences[], requiredScopes[], claimPredicates[], allOrAny, errorCode`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `authentication,authorization`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: parent constraints intersect; child may tighten only.
- **Security and operational constraints:** Never accept unsigned tokens; claim values redacted; platform validity cannot be overridden.
- **Classification:** MVP security-enhancing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.access.authorize-action` — Contextual authorization

- **Purpose:** Authorize an operation independent of whether caller used API key or JWT.
- **Typed configuration model (fields):** `actions[], allowedRoles[], subjectTags[], resourceTags[], CEL-condition`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `authorization`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: mandatory grants cannot be widened downstream; additive deny only.
- **Security and operational constraints:** Must not grant ownership of foreign resources; missing identity denies.
- **Classification:** MVP security-enhancing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.access.identity-constraint` — Identity restrictions

- **Purpose:** Constrain ingress identity to approved principal types, issuer sets, key classes and trusted client attributes.
- **Typed configuration model (fields):** `acceptedAuthTypes[], subjectClasses[], issuerRefs[], denyIfMissing`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `authorization`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: intersection of accepted identities.
- **Security and operational constraints:** Do not infer trust from spoofable headers or IP alone.
- **Classification:** MVP security-enhancing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.access.network-boundary` — Client network constraint

- **Purpose:** Constrain request origin using trusted proxy metadata and allow/deny CIDR lists.
- **Typed configuration model (fields):** `trustedProxyRefs[], allowCidrs[], denyCidrs[], mismatchAction`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: restrictive additive composition.
- **Security and operational constraints:** Never trust unverified X-Forwarded-For; avoid accidental admin lockout.
- **Classification:** MVP security-enhancing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.traffic.request-throttle` — Request rate throttle

- **Purpose:** Bound admitted request count in configured distributed window.
- **Typed configuration model (fields):** `scopeKey, algorithm, window, maxRequests, burst, failMode`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: tighter bound wins; optional overrides explicitly bounded.
- **Security and operational constraints:** Enforcement requires proven shared atomic coordinator under replicas.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.traffic.token-throttle` — Token throughput throttle

- **Purpose:** Limit estimated and reconciled input/output token flow.
- **Typed configuration model (fields):** `inputTpm, outputTpm, totalTpm, window, estimator, reserveFraction`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: tighter bound wins.
- **Security and operational constraints:** Pre-reserve; reconcile/cancel; token estimates marked as such.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.traffic.volume-quota` — Request/token quota

- **Purpose:** Enforce cumulative usage ceiling per accounting window.
- **Typed configuration model (fields):** `quotaKind, ceiling, windowType, tz, scopeKey, hardOrSoft`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: restrictive additive; hard parents cannot be disabled.
- **Security and operational constraints:** Atomic reservation and period rollover; deny on unavailable hard-check.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.traffic.parallelism-cap` — Concurrent invocation cap

- **Purpose:** Limit concurrent active requests or streams with leases.
- **Typed configuration model (fields):** `limit, scopeKey, leaseTtl, heartbeat, waitMode`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: smallest hard cap applies.
- **Security and operational constraints:** Lease fencing and recovery; long streams count until terminal.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.governance.spend-cap` — Spend budget guardrail

- **Purpose:** Reserve estimated expense, deny above hard budget, reconcile final usage.
- **Typed configuration model (fields):** `currency, maxSpendMinorUnits, period, priceProfile, enforcement, softAlerts`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: mandatory budgets additive and non-removable.
- **Security and operational constraints:** No false invoice certainty; missing coordination denies hard budget.
- **Classification:** MVP financial; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.governance.usage-attribution` — Usage attribution

- **Purpose:** Attach approved cost center, application or environment tags to metering ledger.
- **Typed configuration model (fields):** `costCenterRef, allocationTags, defaultTags, validationRules`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** A: inherit defaults; child can add tags under keys policy.
- **Security and operational constraints:** No unbounded personally identifying tag cardinality.
- **Classification:** MVP financial; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.governance.model-boundary` — Model allow/deny

- **Purpose:** Restrict requested logical aliases or model capability classes.
- **Typed configuration model (fields):** `allowAliases[], denyAliases[], allowCapabilities[], denyCapabilities[]`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: allowed set intersects; denies union.
- **Security and operational constraints:** Cannot bypass model capabilities or tenant ownership.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.governance.provider-boundary` — Provider/deployment allow/deny

- **Purpose:** Restrict candidate provider types, accounts, regions and concrete deployments.
- **Typed configuration model (fields):** `allowProviders[], denyProviders[], allowedDeploymentRefs[], regionRules`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `route-selection`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: candidate-set intersection.
- **Security and operational constraints:** Prevent disallowed residency and cross-project credential references.
- **Classification:** MVP governance; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.routing.pool-target` — Backend pool selection

- **Purpose:** Resolve eligible backend pool and constrain routing strategies.
- **Typed configuration model (fields):** `poolRefs[], fallbackPoolRefs[], requiredCapabilities[], selectorRef`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `route-selection`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** B: child override only within parent-allowed pools.
- **Security and operational constraints:** Cannot select unowned or disallowed deployments.
- **Classification:** MVP routing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.routing.selection-mode` — Routing strategy

- **Purpose:** Choose round-robin, weighted, priority or latency-aware policy over eligible set.
- **Typed configuration model (fields):** `mode, weights, tier, latencyWindow, hysteresis, fallback`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `route-selection`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** B: override permitted only where parent bounds allow.
- **Security and operational constraints:** Cannot bypass health/circuit/capability eligibility.
- **Classification:** MVP routing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.routing.deployment-pin` — Deployment pinning

- **Purpose:** Pin request class to an explicitly allowed deployment for testing or compliance.
- **Typed configuration model (fields):** `deploymentRef, matchRules, failIfUnavailable`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `route-selection`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** B: bounded to allowed candidate set.
- **Security and operational constraints:** Pinned deployment still requires auth, health/circuit and governance checks.
- **Classification:** MVP routing; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.resilience.retry-envelope` — Retry envelope

- **Purpose:** Configure safe retry budget, backoff and transient classifications.
- **Typed configuration model (fields):** `maxAttempts, maxElapsedMs, retryableClasses[], jitter, backoff`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `upstream-request`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: smallest maxima; child cannot increase hard budget.
- **Security and operational constraints:** No retries after visible streaming bytes; idempotency required.
- **Classification:** MVP resilience; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.resilience.deadline-bound` — Deadline limits

- **Purpose:** Bound total and per-attempt inference time.
- **Typed configuration model (fields):** `overallTimeoutMs, perAttemptMs, connectTimeoutMs, capClientDeadline`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,upstream-request`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: strictest timeout applies.
- **Security and operational constraints:** Propagate cancellation; settlement independent of response status.
- **Classification:** MVP resilience; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.resilience.failover-rule` — Backend failover

- **Purpose:** Define allowed fallback deployments/tier conditions.
- **Typed configuration model (fields):** `fallbackPoolRefs[], allowedErrors[], maxSwitches, idempotentOnly`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `route-selection,upstream-request`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** B: only parent-allowed target pools.
- **Security and operational constraints:** No stream replay or forbidden region/provider transitions.
- **Classification:** MVP resilience; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.transform.request-headers` — Upstream request header transform

- **Purpose:** Set/remove approved non-security upstream headers with typed templates.
- **Typed configuration model (fields):** `setMap, removeKeys[], allowedVariables[], maxLength`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `upstream-request`. **Supported scopes:** `P/R`.
- **Inheritance semantics:** A: append order with duplicate collision diagnostics.
- **Security and operational constraints:** Deny modification of Authorization, Host, hop-by-hop and trust headers.
- **Classification:** MVP transformation; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.transform.response-headers` — Downstream response header transform

- **Purpose:** Attach or remove permitted downstream response headers.
- **Typed configuration model (fields):** `setMap, removeKeys[], maxLength, allowList`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `upstream-response,egress`. **Supported scopes:** `P/R`.
- **Inheritance semantics:** A: append with deterministic conflict resolution.
- **Security and operational constraints:** Cannot overwrite security, content-length, trace or streaming correctness headers.
- **Classification:** MVP transformation; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.transform.request-shape` — Inference request shaping

- **Purpose:** Normalize permitted request parameters, provider-neutral defaults and hard bounds.
- **Typed configuration model (fields):** `paramDefaults, paramCaps, allowedFields[], unknownFieldAction`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,upstream-request`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** B: narrow or apply parent caps.
- **Security and operational constraints:** Never silently alter system/developer message or unsupported schema semantics.
- **Classification:** MVP transformation; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.transform.response-shape` — Inference response shaping

- **Purpose:** Add safe labels and declared response normalization where protocol permits.
- **Typed configuration model (fields):** `allowedProjection, metadataTags, transformMode`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `upstream-response,egress`. **Supported scopes:** `P/R`.
- **Inheritance semantics:** B: only legal response types and stable schema.
- **Security and operational constraints:** Do not break SSE ordering, tool IDs or output schema contracts.
- **Classification:** MVP transformation; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.context.attach-attributes` — Policy context enrichment

- **Purpose:** Add validated typed contextual fields for later CEL/handler use.
- **Typed configuration model (fields):** `attributes, sourceRefs, redactKeys[], allowedNamespaces[]`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `ingress,admission`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** A: immutable namespace merge, conflicts rejected.
- **Security and operational constraints:** No secrets/large body injection into CEL or telemetry.
- **Classification:** MVP context; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.context.require-attribute` — Required request metadata

- **Purpose:** Reject requests lacking validated application/business metadata.
- **Typed configuration model (fields):** `attributeSchemas, missingAction, allowedSources[]`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: required set union.
- **Security and operational constraints:** Untrusted client metadata may not become authorization evidence.
- **Classification:** MVP context; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.telemetry.span-enrich` — Span enrichment

- **Purpose:** Append bounded safe attributes to OTel activity/span.
- **Typed configuration model (fields):** `attributeMap, allowedKeys[], maxLengths, dropHighCardinality`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `ingress,route-selection,completion`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** A: allowed additive fields.
- **Security and operational constraints:** No prompts, credentials, raw user IDs or unbounded attributes.
- **Classification:** MVP telemetry; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.telemetry.metric-record` — Custom metric recording

- **Purpose:** Emit bounded counter/histogram measurements from policy events.
- **Typed configuration model (fields):** `instrumentName, metricType, valueExpr, attributeAllowlist, unit`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** A: additive with registration allowlist.
- **Security and operational constraints:** Cardinality budget, valid instrument names, avoid cost duplication.
- **Classification:** MVP telemetry; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.telemetry.structured-event` — Structured safe logging

- **Purpose:** Emit audited structured event with safe allowlisted fields.
- **Typed configuration model (fields):** `eventName, severity, fieldMap, sensitiveFilter, sampling`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `admission,route-selection,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** A: additive; org privacy minima immutable.
- **Security and operational constraints:** Payload logging remains off by default; redact secrets and claims.
- **Classification:** MVP telemetry; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

### `iskender.telemetry.audit-signal` — Governance audit emission

- **Purpose:** Emit mandatory or optional security/governance decision events.
- **Typed configuration model (fields):** `eventType, reasons[], retentionClass, relevantConfigIds`. Schema/version validation rejects unknown unsafe fields; precise wire names are finalized by the implementing spec.
- **Legal stages:** `authorization,admission,completion,failure`. **Supported scopes:** `O/P/R`.
- **Inheritance semantics:** M: mandatory parent signals persist.
- **Security and operational constraints:** Append audit once with durable IDs; no prompt body.
- **Classification:** MVP telemetry; **delivery:** first-party MVP handler through the same typed descriptor and execution model used by approved external plugins.

## Handler acceptance requirements

Each descriptor must supply a configuration JSON schema, sample declarative and fluent equivalent, positive/negative validation cases, stage/scoping and inherited override tests, deterministic execution-order test, safe diagnostics, redaction tests, failure semantics and latency/allocation benchmark. Group inheritance or capability constraints that cannot be compiled correctly must block plan activation. Store a catalog registry snapshot for version compatibility and migration tests.

## Classification and scope decision

This catalog reflects a **proposed MVP decomposition** of the approved built-in capability set, not evidence that every identifier or field spelling was separately approved. Stable IDs should be frozen via the first catalog implementation spec/ADR before external release; material changes to catalog scope must be approved via [13](13-development-standards-and-agent-workflows.md). Distributed coordination, financial ledger and retry correctness are implemented at platform-service level [08](08-routing-resilience-and-governance.md), not duplicated in handlers.
