# 06 — Conversation state architecture

**Authority:** MVP three-mode state machine, durable canonical representation and failure semantics. APIs/providers: [07](07-provider-adapters-and-api-compatibility.md). Isolation and retention: [05](05-identity-security-and-privacy.md).

## Explicit mandatory modes

| Mode | State of truth | Behavior and MVP contract |
|---|---|---|
| A — Stateless passthrough | Calling client | Process each request independently; no history ownership; provider-native pass-through as declared |
| B — Provider-managed | Provider | Forward/manage provider-side conversation/response identifier and lifecycle with explicit provider scope and persistence rules; do not falsely claim portability |
| C — Gateway-managed durable | **Iskender/PostgreSQL** | Gateway-issued conversation and response IDs; persist typed events; reconstruct provider requests; continue across supported providers with documented fidelity limits |

Mode selection is explicit per endpoint/request policy; no accidental migration, double ownership or ID collision. The gateway may use cache/Redis for hot ephemeral state **but Redis is not the only durable source** for Mode C. The data-plane replicas remain stateless with respect to authoritative history.

## Canonical typed model

Store `Conversation` (opaque ID, organization/project owner, mode, timestamps, revision, status, retention metadata) and ordered `ConversationItem` (stable ID, type, role/actor, message parts/content refs, tool-call correlation, timestamps, visibility, status, provenance/provider metadata). Minimum typed item families: system/developer/user/assistant messages; text/structured parts; tool call requests; tool results; error/interrupt markers; usage/accounting refs. Distinguish provider-generated opaque items from portable canonical components. Preserve provenance and unsupported extension fields without pretending they can be replayed to every other provider.

Use immutable append or explicit revisioned updates with monotonic sequence/version. Implement optimistic concurrency and compare-and-set append: concurrent branches on a single conversation cannot silently interleave or overwrite; return conflict or support serialized queue according to defined endpoint semantics. Conversation/result ownership is always checked from authoritative tenant metadata, not from ID format.

## Responses and Conversations API compatibility

OpenAI-compatible `/v1/responses` creation/continuation/retrieval and `/v1/conversations` creation/retrieval/item management (only supported subset recorded in compatibility matrix) are part of MVP. Map `previous_response_id`, gateway-owned conversation IDs and provider-native IDs deliberately. Streaming event types/order, refusal/error shapes, tool call IDs and finish reasons must be preserved or mapped transparently. No unsupported fake feature replies; use well-defined 400/422/501-style capability errors as appropriate. Stable generated IDs are scoped and non-enumerable.

## Write and stream transaction protocol

1. Authenticate/authorize, validate the owning conversation and expected version; validate limits and conversation mode.
2. Create an in-progress response/turn with a deterministic idempotency correlation and an initial reservation within a transaction; do **not** publish completed history yet.
3. Translate canonical context to target adapter subject to capability/size/window; persist outbound attempt metadata and tool correlations separately from committed user-visible messages.
4. Forward stream with bounded backpressure. Track canonical decoded event deltas and durable checkpoints only if compatible with the chosen recovery contract; ensure a client sees an event at most as specified by that contract.
5. On successful terminal provider completion, transactionally append canonical result, tool events, usage finalization and response status; advance conversation revision.
6. On client cancellation, process crash, upstream failure or ambiguous terminal event, mark turn interrupted/failed, reconcile reservations safely, and never mislabel partial output as a successful committed assistant response.
7. On restart, detect orphaned in-progress turns by lease/timeout, settle or mark ambiguous, and provide stable diagnostics/retry semantics. No unsupported replay of already streamed output.

Guarantees require tests for double submit, lost ack, duplicate provider event, concurrent tool completion, cancellation and replica termination. Idempotency keys should be scoped and expire in a documented interval. **Exactly-once external provider execution cannot be promised** without provider idempotency support; locally committed items must still avoid duplicates.

## Cross-provider continuation

Construct a target-provider request from **portable canonical content**, not from the previous provider's opaque response object. Resolve differences in system roles, image/tool types, tool results, max context size, structured schema, reasoning tokens, safety boundaries, and ID semantics using capability declarations. The continuation function may reject or mark limitations: hidden reasoning, encrypted thought blobs, unsupported tools/modalities and provider-private conversation IDs are not generally portable. Minimal context-window awareness (size estimation, explicit reject or deterministic user-approved truncation) belongs in MVP; intelligent summarization/compaction does not.

For tools: each generated call has canonical ID and provider ID mapping; results may attach only to valid outstanding requests and preserve strict ordering where required. Retain tool inputs/outputs according to privacy policy, with sufficient information for legitimate continuation and audit without blanket operational logging.

## Persistence and governance

PostgreSQL stores authoritative items and response revisions. Partition/index by organization/project/conversation/time as supported by real query patterns, with constrained row-level/semantic authorization. Encryption and deletion semantics in [05](05-identity-security-and-privacy.md); metering and token-cost reservations in [08](08-routing-resilience-and-governance.md). Redis may cache recent item windows and hold stream leases, using versioned invalidation; never let cache eviction erase history. Deletion removes future continuation capability; provider-side records may require separate deletion and are not claimed as under Iskender's control.

## Conformance matrix and invariants

| Behavior | A Stateless | B Provider | C Gateway |
|---|---|---|---|
| Durable Iskender history | No | Metadata/mapping only | **Yes** |
| Continue after gateway restart | Client supplies history | Subject to provider lifecycle | **Yes** |
| Cross-provider continuation | Client-mediated | Not guaranteed | **Basic portable content** |
| Tool-call/result history owned by Iskender | No | Not authoritative | **Yes** |
| Tenant-scoped retrieval/deletion | Request scope | Gateway metadata & provider caveats | **Full owned data** |
| Streaming interruption recovery | Per provider/protocol | Per provider | Safe failed/partial status, not invented resume |

**Post-MVP only:** semantic/AI summarization, compaction policy engine, branching trees, sophisticated context optimization, semantic search and conversation analytics. These are not prerequisites for the approved Mode C MVP.
