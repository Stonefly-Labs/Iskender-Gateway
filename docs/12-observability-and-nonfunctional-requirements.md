# 12 — Observability and nonfunctional requirements

**Authority:** production telemetry, nonfunctional test protocol and MVP verification requirements. Policy telemetry: [10](10-first-party-policy-catalog.md); release gates: [02](02-product-requirements-and-roadmap.md).

## OTel architecture

Auto-instrument request ingress, authorization, effective policy plan, conversation lookup, admission/limits, routing candidate selection, provider attempt, streaming/response, persistence and usage settlement. Use W3C trace context when trusted; sanitize external trace headers and make baggage acceptance explicit. Instrument .NET `ActivitySource`/`Meter`-style abstractions; collector/exporter vendor is operator-selected, not hardcoded. Respect stable OpenTelemetry **GenAI semantic conventions** with schema/version pinning because conventions evolve; support migration and accurately represent provider name, requested model, resolved deployment, token usage and latency without disclosing prompt/response bodies.

## Standard signals

| Signal | Required minimum | Privacy/size control |
|---|---|---|
| Traces | root gateway request, policy steps with safe results, selected pool/deployment, each attempt, stream terminal state, usage settlement | sampled at ingress; safe refs/hashes, never raw keys/tokens/prompts |
| Metrics | request and error rates, gateway overhead latency, provider latency, stream duration, inflight requests, tokens, estimates, quota denial, spend reservation, retries, breakers, plugin time | bounded attribute vocabulary, no end-user IDs/freeform labels |
| Structured logs | startup/config validation, lifecycle, safe control changes, policy compile errors, provider failures, reconciler outcomes | body capture off; template-based; automated secret test |
| Audit log | control-plane change, access/denial, key lifecycle, plugin trust updates, effective policy promotion, privileged conversation deletion | authorized tenant access, retention/tamper controls |

Each policy can enrich spans, emit custom metrics or safe structured events under typed, allowlisted SDK capabilities. Global metric instruments must reject arbitrary namespace collisions, high-cardinality attribute names/values and unbounded user-defined labels. Policy changes are correlated by immutable config revision and handler version.

## Measurements and objectives

A performance target is a **proposed release objective**, never evidence of an achieved number. Actual thresholds are to be approved and recorded in benchmark protocol/spec before running. Target **method** is mandatory now:

- Measure p50/p95/p99 **added gateway latency** as `gateway end-to-end - controlled upstream baseline`, separately for nonstreaming, SSE time-to-first-event and per-chunk overhead; publish machine size, .NET runtime, request shape, load, sampling and environment.
- Run throughput vs concurrent-clients curves, including saturation, cancellation, long streams, high policy count, CEL compilation/evaluation, plugin execution, cache hit/miss and PostgreSQL/Redis overhead. Record allocations/GC, CPU, resident memory, socket/connection pool use and async blocking.
- Compare container and embedded host under as nearly identical conditions as possible. Record version/commit, warmup policy, load generator, run-to-run variance and confidence bounds.
- Define concrete pass/fail targets per scenario **before** release candidate validation; no artificial success claims or performance numbers in docs.

## Reliability and horizontal scale

The stateless data plane must be safe behind load balancing and support horizontal replica scaling without sticky sessions for durable conversations. Test at least two replicas for: distributed quotas/budgets/breakers, concurrent append conflicts, policy snapshot rollout, provider failover, restart during stream, plugin readiness failure and secret rotation. Define graceful shutdown: reject new requests, drain bounded active streams, cancel at deadline, reconcile leases and outstanding usage. Document PG failover, pool saturation, Redis outage, config publication outage, plugin feed outage and at-least-once metadata notification behavior. Hard security/budget controls fail closed; safe degraded modes must be explicit.

## Security, data and operations SLO framework

Track auth rejection correctness, tenant-leak negative tests, zero observed secret disclosure in default telemetry, recovery-point implications of Postgres backups, measured failover, durable state deletion evidence and configuration rollback exercises. No asserted uptime SLA without owned production service. Operators receive deploy/restore/upgrade and incident runbooks and alarms for breaker saturation, hard budget failures, incomplete settlements, WAL/storage pressure and dropped telemetry.

## Test taxonomy and MVP gates

| Class | Mandatory gates / evidence |
|---|---|
| Unit/property | deterministic policy inheritance, CEL typing, canonical state, routing algorithms, financial reservation arithmetic |
| Architecture | module dependency rules, no provider SDKs in neutral contracts, no unowned resource reads |
| Integration | Postgres and Redis via Testcontainers, transactional conflict, rolling schema compatibility, external OIDC/JWT fixtures |
| Provider conformance | deterministic mock + live gated samples for every advertised provider operation, streaming, tools, errors, structured outputs |
| Security | auth bypass, SSRF, cross-org/project access, key revocation, secrets scrubbed, plugin tamper/supply chain |
| Chaos/fault | lost connections, provider 429/500, partial SSE, control-plane outage, Redis failure, killed replica, orphan reconciliation |
| Performance | approved benchmark envelope, p50/95/99, memory/GC, 1 vs N replicas, regression budgets |
| Deployment | stock container, embedded NuGet host, Compose, ACA reference, clean installation/upgrade/rollback |
| Accessibility/UX | UI keyboard nav, operator diagnosis of failed configuration, error-to-resolution flows |
| Release evidence | published versioned matrix, SBOM/licensing, security signoff, open risks/waivers, docs consistency, actual merge commits |

Critical/high security findings and unresolved medium-or-higher convergence findings block release. A low-only exception may be accepted in workflow convergence from pass 4 onward but **does not** authorize bypassing a separately mandatory security or release gate. Every performance benchmark includes raw machine-readable results and a human-readable methodology.

## Pending target approval

Numbers for p95 gateway overhead, maximal throughput, memory limits, request body sizes, stream maximum duration, RTO/RPO and default data retention are deliberately **not fabricated**. Owners propose thresholds using baseline measurements and must obtain scope/ADR/roadmap approval if they change product commitments. The release spec prevents a ship with unspecified measured objectives.
