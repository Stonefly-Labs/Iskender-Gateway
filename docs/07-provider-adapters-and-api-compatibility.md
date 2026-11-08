# 07 — Provider adapters and API compatibility

**Authority:** provider/ingress compatibility contract. Request lifecycle: [03](03-domain-and-technical-architecture.md); state: [06](06-conversation-state-architecture.md); routing: [08](08-routing-resilience-and-governance.md).

## Mandatory provider categories

1. **OpenAI** native APIs, with normalized OpenAI-compatible ingress.
2. **Azure OpenAI / Azure AI Foundry** inference deployments and their distinct endpoint/version/auth requirements; don't assume all Azure Foundry endpoints speak the same schema.
3. **Anthropic** Messages capability adapted with honest role/tool/stream translation.
4. **Google Gemini** with declared limitations on tool semantics, schemas, modality, streaming and auth.
5. **Generic OpenAI-compatible** vendor/local endpoints with per-deployment conformance profile; do not assume all compatible implementations fully implement Responses or Conversations.

Concrete deployment metadata includes provider kind, upstream endpoint, API version, region/account, model or deployment ID, credential profile, declared+probed capabilities, timeouts, weights/priorities, prices and circuit/health policy. Backend health and identity are **per deployment** even when multiple deployments live behind one alias.

## Ingress contracts

Required MVP OpenAI-style APIs: **Chat Completions, Responses, Conversations, Embeddings**; support stream transport (SSE where defined), tool/function calls and structured outputs **only for provider/model combinations demonstrated in the matrix**. Public compatibility is versioned and pinned to explicit tested semantics, not an unqualified statement of full vendor API emulation. Provider-native pass-through can be exposed via an explicit route with distinct capability/auth expectations.

## Adapter contract (conceptual)

- `DescribeCapabilities(deployment)` returns feature flags **and typed limits**: supported ingress operations, tools, structured schema subset, streaming event map, context length, token accounting support, provider-side state, model identity, modality and auth.
- `ValidateAndTranslate(request, canonicalContext, options)` rejects unsupported options *before* dispatch where possible; reports loss of semantic fidelity explicitly.
- `InvokeAsync(...)` or stream equivalent emits typed normalized events with stable correlation, usage and completion/failure status; preserves cancellation/backpressure.
- `MapError(providerError)` yields stable gateway error category, safe message, retriable flag, provider request ID and redaction status.
- `EstimateTokens(...)` declares estimator accuracy; reconcile against final provider usage when available.

These are illustrative contracts; final public SDK names go through spec/ADR review. Adapter logic never mutates tenant ownership, reads raw unrelated secrets, or enforces organization RBAC itself.

## Feature matrix: baseline obligations, not assertions of native parity

| Feature | Ingress MVP | Required adapter behavior | Compatibility risk / failure behavior |
|---|---|---|---|
| Chat Completions | Yes | Map roles, turns, usage, finish reasons | Reject incompatible role/tool settings |
| Responses | Yes | Native or gateway translation with visible gaps | Stateful IDs may require Mode C |
| Conversations | Yes | Gateway-owned Mode C API where appropriate | Not every provider exposes native conversation API |
| Embeddings | Yes | Inputs, dimensions if supported, usage | Unsupported dimensions fail explicitly |
| SSE streaming | Yes where provider permits | Order, deltas, terminal/error event mapping | No mid-stream retry replay |
| Tool calls + results | Yes where provider permits | Stable IDs, args, ordering, completion | Reject unsupported multi-call/parallel semantics |
| JSON/structured output | Yes where provider permits | Validate schema subset, provider strictness | Never claim strict schema if only prompt-enforced |
| Provider-managed state | Conditional | Preserve provider-native references securely | No universal cross-provider transfer |
| Gateway-managed continuation | Yes with portable content | Mode C request reconstruction | Incompatible hidden reasoning/modality rejected or omitted with disclosure |

At release publish a **versioned, evidence-backed per-provider/per-model matrix** with `Supported`, `Supported with documented translation`, `Unavailable`, or `Untested`, and test IDs. `Untested` is not equivalent to supported. Capability flags must be consistent between routing admission and published matrix.

## Provider conformance scenarios

For each provider category, cover basic success, malformed input, auth rejection/refresh, missing capability, model/region mismatch, explicit content limit, retryable and permanent error, deadline, cancellation, SSE delta and terminal event, tokenizer/usage mismatch, tool-call argument streaming, tool-result continuation, JSON schema acceptance, rate/429 with Retry-After, error redaction, and one cross-provider Mode C continuation fixture. Live-provider smoke tests are separately keyed and must not expose CI credentials; offline deterministic adapters are used for reliable regression.

## Streaming, errors and retries

Incremental conversion must preserve provider event order, handle UTF-8 fragmentation, bounded buffering, slow-client backpressure, early close and provider timeouts. Provider-native errors may arrive after a 200 SSE response begins: emit protocol-compliant terminal failure, mark usage and conversation outcome, and do not replace upstream with another provider after visible output. Separate safe pre-header retries/failover in [08](08-routing-resilience-and-governance.md). Error envelope contains stable code, category, retry hints only when safe, correlation ID and sanitized provider metadata; never reveal original credentials or private prompts.

The transport-neutral envelope is [`error-envelope` version 1](../specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md#error-envelope-version-1), with the error categories and codes, in the public contracts and versioning contract that ISK-0002 first published. Mapping it to an HTTP status, an OpenAI-compatible error, or a stream terminal event belongs to the ingress specs, which may add codes but not categories without a revision of that contract.

## Provider capabilities and cost

Routing must filter candidate deployments based on the *full* requested capability set before applying weights or latency. Price/usage metadata has effective dates, model/version/region basis and provenance; unavailable final provider usage requires a defensible estimate plus unknown/estimated flag. No assumption vendor pricing or behavior is eternally stable.

## Post-MVP

Richer multimodal APIs, extra cloud/local providers, native reasoning-state bridge improvements, provider federation, batching and model lifecycle integrations are represented in [17](17-master-spec-inventory.md). MVP does not fabricate them.
