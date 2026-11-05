# 08 — Routing, resilience, and governance

**Authority:** selection, distributed limits, usage and financial invariants. Domain model: [03](03-domain-and-technical-architecture.md); policy application: [09](09-policy-language-and-execution.md).

## Backend pools and logical routing

A consumer selects a **logical endpoint/model alias**; its attached backend pool may contain multiple individually configured deployment resources, including same-model endpoints with different provider accounts/regions. Each deployment owns identity, credential reference, capabilities, health signal, circuit state, timeout, cost profile and routing weight/priority. Endpoint and pool configuration must reject incompatible deployments when a requested capability has no eligible route.

| Algorithm | MVP semantics | Verification |
|---|---|---|
| Round robin | Fair selection among healthy eligible deployments, with defined replica coordination strategy | Deterministic sequence/fairness bounds |
| Weighted | Relative selection proportional to configured positive weights; excludes unhealthy | Distribution and zero-weight tests |
| Priority | Highest-priority eligible healthy tier, fail down when unavailable | Fault injection and tier return |
| Latency-aware | Select by bounded measured latency signal with sample/age gates and deterministic fallback | Noise/low-sample and oscillation tests |

Routing first filters capabilities, residency/security restrictions, spend/limit eligibility, and health, then chooses a backend. Avoid starvation/oscillation via cooldown/hysteresis. Routing policies can constrain eligible deployment set; they cannot authorize a deployment otherwise denied by platform security.

## Health and circuit breakers

Combine active probes only where provider supports safe cheap probes with passive observations. Circuit state is **per backend deployment**, with optional policy-defined grouping; sample window, minimum sample size, open/half-open timers and half-open probes are explicit. Multi-replica breakers require a distributed coordinator or a declared weaker mode that cannot be presented as distributed breaker guarantees. Distinguish auth/config errors from transient provider/server faults so one customer's invalid input doesn't trip shared health. Expose sanitized breaker transition events and reason diagnostics.

## Retry, timeout and failover

Use per-attempt deadlines bounded by an end-to-end caller deadline, exponential backoff/jitter, retry budgets and classification of transient errors. **Only retry when** upstream execution/idempotency and ingress visibility make it safe. A timeout with unknown upstream execution can have side effects (including metered token spend); do not blindly retry tools or non-idempotent operations. **After first downstream visible byte/event, automatic cross-provider failover is prohibited** absent an explicit protocol recovery contract. Track attempts, backend selected, reasons, cumulative latency and provider requests without duplicating usage ledger entries.

## Distributed request and token rate enforcement

Define atomic scoped counters (organization/project/key/endpoint/model/deployment as permitted) with windows/algorithm and fail mode. Request-rate checks may use leased/atomic Redis script primitives or another verified coordinator; local in-memory limit is only a single-instance feature. Token throttling estimates pre-call token usage, reserves an allowance, then reconciles using measured/estimated completion. Maintain separate input/output/total dimensions when supported; hold explicit accounting for interrupted streams. Counter keys must not encode raw secrets.

## Quotas and concurrency

- Quotas cover requests/tokens over calendar or rolling periods, with timezone/calendar definition and reset semantics.
- Concurrency uses atomic leases with fencing/expiry and robust release on disconnection/crash; long streams hold leases until terminal cleanup.
- Overlapping policies combine by declared precedence (typically most restrictive hard bound); lower scopes cannot weaken mandatory org-level limits.
- Return appropriate status and retry metadata without revealing other tenant volumes.

## Budgets, spend and usage

Treat **estimated reservation → actual/estimated settlement → immutable usage ledger** as distinct steps. A budget has currency, accounting window, scope, hard/soft action, effective prices, reservation accuracy/uncertainty and notification thresholds. Admission considers outstanding reservations to avoid overrun under concurrent replicas; final settlement releases unused reserve or records overspend variance. A provider may return delayed, approximate or absent token usage; ledger distinguishes `reported`, `estimated`, `corrected` and `unknown`. Policies cannot promise invoice-exact billing. Reconciliation jobs identify stranded reservations, stale leases, duplicate provider attempts and exchange/price metadata revisions. Authorized operators can inspect lineage and variance.

Budget failure behavior is **explicit**: hard budget check must fail closed when coordination cannot establish remaining balance; soft alerts may degrade with safe signal. Rate/limit storage failures cannot silently become unlimited in multi-replica deployment.

## Coordination and consistency

PostgreSQL is authoritative for durable governance configuration and usage ledger; Redis or a proven equivalent can own hot atomic counters, leases, breaker observations and short-lived admission reservations, synchronized by event/version design. Document whether each feature provides strict/leased/best-effort semantics. Replica failures require expiry/fencing and reconciliation. No architecture relies on node affinity to enforce tenant budgets. Concurrency/load tests must exercise race conditions across ≥2 gateway replicas and target the same scoped counters.

## Operational controls and diagnostics

Every rejected decision has safe machine-readable reason (`auth_denied`, `model_disallowed`, `quota_exceeded`, `budget_blocked`, `no_healthy_backend`, etc.) with effective scope and correlation ID where disclosure is permitted. Provide preview/simulation of effective policy/routing *configuration* in MVP; rich counterfactual simulations are post-MVP. Telemetry records request count, tokens, estimate provenance, reservations, retries, circuit state, backend selection and error classes with cardinality limitations [12](12-observability-and-nonfunctional-requirements.md).

## Acceptance exercises

Run saturation with different organizations/projects, rotate keys while requests are active, fail coordinator during admission and settlement, kill one replica during an SSE request, trigger each routing policy, exhaust budget with concurrent requests, retry before/after first response byte, force breaker half-open competition, change costs mid-window, and verify no cross-scope usage leakage. Require explainable ledger reconciliation; never mark ambiguous upstream charges as confirmed zero.
