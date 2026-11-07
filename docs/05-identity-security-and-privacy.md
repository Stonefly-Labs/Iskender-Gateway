# 05 — Identity, security, and privacy

**Authority:** security and identity invariants. **Controls in policy catalog:** [10](10-first-party-policy-catalog.md). **Scopes and resource ownership:** [03](03-domain-and-technical-architecture.md).

## Trust zones

1. **Control plane:** human/operator sessions and service automation manage configuration. Every change is authenticated, scoped and audited.
2. **Data plane ingress:** workload clients call logical endpoints using approved per-scope **API keys** or **OAuth2/OIDC JWT bearer tokens**; trust rules must be separately configurable by organization/project/resource. No unauthenticated production endpoint unless an explicitly reviewed exceptional requirement exists; core platform isolation remains mandatory.
3. **Provider egress:** Iskender resolves an approved upstream credential profile/identity on behalf of the authorized deployment; never forwards an ingress key as an upstream secret by accident.
4. **Extension code:** trusted in-process third-party assemblies have process-level privileges; review/sign/lock/approve before loading. See [11](11-plugin-extensibility-and-sdk.md).
5. **Management persistence:** sensitive data and authorization metadata are isolated and access-controlled in PostgreSQL, Redis and secrets sources.

## Control-plane identity

**MVP approved options:** ASP.NET Core Identity for **local accounts**, or **bring-your-own external OIDC identity provider** (including enterprise Entra or other standards-compliant provider). Local account security requires password hashing, resets, MFA strategy/rollout decisions, anti-brute-force limits, secure session cookies, CSRF protection for browser changes, security stamp/session invalidation and audit. External OIDC requires exact issuer/audience validation, nonce/state/PKCE for interactive clients, JWKS rotation and explicit subject linking; never auto-link users on email-only matching. Persist an internal principal identity separate from IdP subject and bind organization memberships/roles to it. Embedded hosting may delegate auth integration while still satisfying Iskender RBAC.

**Not MVP:** operating a built-in OIDC authorization server via OpenIddict; may be evaluated as a future module. External OIDC is an identity *source*, not a replacement for Iskender authorization.

## Mandatory authorization

The global platform operator and organization/project roles are distinct. Suggested default role families: operator, organization admin, project admin, deployer/policy editor, auditor/read-only, inference consumer. **Exact permissions are a proposed implementation detail** finalized under identity specs. Authorization uses `principal × action × resource owner/scope × policy/constraints`; permissions must not be inferred solely from JWT role strings or a displayed organization selector. All mutation/query paths including streams, SSE resume, audit, plugin state, credential profiles and conversation history must enforce the same boundary. Admins cannot bypass immutable system guardrails except through explicitly auditable elevated operations.

Negative tests must cover ID enumeration; foreign-key cross-tenant injection; access after role/key removal; admin UI and API differences; stale config; background worker leakage; provider response ownership; metadata and usage queries; batch inputs and direct object references.

## Data-plane API keys

- Issue only for authenticated, authorized organization/project operators, with **one-time plaintext display** and keyed/hash-at-rest verification. Key prefix/ID is not a secret; secret part has high entropy.
- Store owner project, scopes, permitted aliases/endpoints, expiry, status, rotation metadata, provenance and last-used timestamps. Enforce boundaries even when key scopes are broad.
- Support creation, listing metadata, revocation, overlap rotation and expiry. Cache revocation with a defined maximum staleness and force invalidation/re-read where security demands it.
- **Independent controls:** disable future API-key issuance; disable API-key acceptance/validation; optionally revoke existing keys. Disabling issuance alone must not silently change acceptance; disabling acceptance must reject previously issued keys.
- Bearer-only deployments remain fully supported. Multiple trusted issuers/audiences are explicitly configured, not accepted opportunistically.

## Data-plane OAuth/JWT bearer

Validate signature (alg allowlist; reject `none`), key, issuer, audience, temporal claims with bounded skew, token type/scope and tenant/project binding. Reject ambiguous issuers and cross-tenant subjects. Support issuer-specific subject-to-principal/project mapping with auditable configuration and negative tests. Policy engine may require extra claims or business authorization; cannot negate mandatory bearer validity or resource ownership. Network token introspection and token exchange are post-MVP unless a selected MVP requirement explicitly relies on them.

## Upstream inference credential profiles

Deployment references the credential profile; profile supports static provider API keys and federated/workload identity profiles **where provider protocols support them**, with strict type and audience validation. MVP target integration contracts include Azure managed identity, Google workload identity/service account, OpenAI workload identity federation when supported by the actual upstream API, and OpenAI/Azure/Anthropic/Gemini keys. AWS IAM-style credentials/signing are forward-looking for additional providers; avoid promising IAM is universal. Credentials are rotated through secret-provider hooks, are never copied into traces, tool inputs, static UI payloads or plugin configuration; support account/region separation by deployment.

## Cryptography and secret management

Use managed secret references or a replaceable local encrypted secret store for self-hosting; encryption-at-rest and TLS-in-transit mandatory for sensitive data. Define key custody/rotation/restoration, encryption envelope/provider adapter and secret redaction. Never persist complete API keys, bearer tokens or cloud access credentials in general configuration JSON. Scope encryption keys appropriately and require restricted operator actions to expose/rotate secrets. Audit access by service identity, not just mutation events.

## Logging, audit, retention, deletion

**Payload logging OFF by default** for requests, model outputs, prompts, tools, embeddings and conversation items. Operational logs expose safe metadata only, with hard truncation and identifier cardinality controls. Separate audit events (who/what/when/scope/result/config revision/reason) from inference content, and ensure tamper-evident ordering/retention strategy. Support retention policy for conversations and usage with deletion lifecycle, provider-data caveats and propagation/erasure limitations explicitly disclosed. Delete or anonymize secondary caches, retries and derived accounting references under a documented data policy. Avoid presenting partial deletion as complete removal when a provider keeps independent state.

## Mandatory vs policy-enhanced enforcement

| Mandatory platform invariant (cannot opt out) | Optional/configurable additional policy |
|---|---|
| Owner-isolated resource access, valid credentials, secure secrets | Extra claim predicates, model/provider allowlists |
| Operation authorization for management, history, usage | Project-specific scope/role restrictions |
| Non-bypassable org guardrails and verified plan | Stricter quotas, budgets, request restrictions |
| No default sensitive payload capture | Explicit, narrowly authorized safe structured logging |
| Trusted plugin verification and publisher approval | Additional organization plugin restrictions |

## Threat review and acceptance

Threat model: compromised API key, JWT issuer confusion, SSRF through configurable backend/redirects, malicious/untrusted plugin, secret exfiltration through telemetry, cross-tenant data access, denial of service, distributed race on limits, stream truncation and stale policy config. Enforce outbound destination allowlists/HTTPS and block local/metadata-service SSRF targets where applicable; bounded payloads and timeouts; per-identity rate limits; deterministic failure modes. Review auth, tenancy, plugin trust and budget mechanisms with explicit negative integration tests before MVP.

**Build-time architecture rules.** The dependency and build-convention rules of ISK-0001 defend against an accidental or careless edit, and against a deliberate one by a contributor whose change goes through CODEOWNERS review. They do not defend against a compromised SDK or NuGet feed, a malicious `Exec` or `MSBuild` task, or a change a reviewer approves knowing what it does. Five layers overlap: a scan of project XML, a lexing scan of source files, a check of the built assemblies' metadata, the compiler and analyzers, and CODEOWNERS on the rule source and root build files. The layers, the classes of route each one closes, and every known limit are in [Threat model and known limits](../specs/0001-solution-dependency-boundaries-build-conventions/contracts/module-dependency-rules.md#threat-model-and-known-limits).

## Decision status

**Approved:** local Identity + external OIDC, keys + bearer ingress with separately configurable key issuance/validation, scoped ownership, upstream credential profiles and cloud workload identities, default protected data, trusted-plugin model. **Recommended:** default role catalog and storage encryption implementation. **Needs approval:** exact minimum local MFA/admin recovery policy, auditing retention defaults and key-rotation SLAs. See [16](16-architecture-decision-register.md).
