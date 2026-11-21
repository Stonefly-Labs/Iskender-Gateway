# Feature Specification: ISK-0010 — System-Operator Bootstrap & Security Baseline

**Feature Branch**: `feature/0010-system-operator-bootstrap-security-baseline`

**Created**: 2026-11-21

**Status**: Draft

**Input**: User description: "Create the specification **ISK-0010 — System-Operator Bootstrap & Security Baseline** for Iskender Gateway. Use the authoritative project inputs @docs/03-domain-and-technical-architecture.md @docs/05-identity-security-and-privacy.md and the approved ADRs ADR-0003, ADR-0005, ADR-0008, ADR-0009; load the active constitution from `.specify/memory/constitution.md` (or report its absence). The capability to specify is specifically: **Define secure initial operator setup, privilege checks and baseline unauthenticated endpoint restrictions**. In-scope behaviors and artifacts: **First-admin bootstrap; safe recovery path draft; TLS assumptions; health exposure; audit hooks**. Explicitly exclude: **No complete external IdP integration or project-level grants**. Prerequisites: **ISK-0004, ISK-0007, ISK-0008, ISK-0009**; do not assume unavailable implementation already exists. […] Acceptance must demonstrably include: **Uninitialized admin routes deny access; bootstrap token cannot be reused**. […] Produce the Spec Kit **specification only**, with measurable requirements and out-of-scope boundaries; do not plan, implement, modify repo code, change constitution, amend roadmap or advance stages during specify." The full prompt is the ISK-0010 entry of `docs/17-master-spec-inventory.md`.

## Overview

The constitution is present at `.specify/memory/constitution.md` and governs this spec.

Iskender's control plane today binds only a health listener and serves nothing else (container contract version 2, Listeners rule 5). Every later control-plane spec, from local accounts (ISK-0013) and external OIDC (ISK-0014) to RBAC (ISK-0015), management authentication (ISK-0020), and the management APIs (ISK-0080), needs a control plane that has a first operator, refuses everything until it has one, checks privilege on every admin route, and exposes nothing anonymous that has not been reviewed. `docs/05` requires that every control-plane change is authenticated, scoped, and audited, that the global platform operator is distinct from organization and project roles, and that mandatory checks fail closed. ISK-0006 left composing control-plane services into an embedded host to the first spec that adds them (its AV-5), and ISK-0007 left TLS on the listeners and protection of the health listener to this spec (its AV-5).

This spec gives operators and later specs:

1. **An installation state**: one operator-scope record saying whether the installation is `uninitialized` or `initialized`, in PostgreSQL, shared by every control-plane replica.
2. **A one-time bootstrap token**, issued outside the request path, stored only as a hash, short-lived, and redeemable exactly once, even under concurrent attempts.
3. **The first system operator**: redeeming the token creates the first operator principal, an internal identity separate from any identity-provider subject, grants it the system-operator role at the operator scope, and returns a one-time enrollment code that a later sign-in spec redeems, all in one commit.
4. **Privilege checks**: every admin route declares the operator action it needs, or is on a reviewed anonymous list. Checks deny by default and fail closed when the state or the store cannot be read.
5. **An uninitialized gate**: while the installation is uninitialized, or its state is unknown, every admin route except the bootstrap and recovery redemptions denies access, whatever credential the caller presents.
6. **A baseline of unauthenticated endpoints**: an inventory of every endpoint reachable without authentication on the data plane, the control plane, and the worker, enforced by a test that fails on any endpoint missing from it.
7. **TLS assumptions and health exposure**, settling ISK-0007's AV-5 for the data plane, the control plane, and the worker: optional in-process TLS on the traffic listeners, required on both planes outside development unless the operator declares TLS terminated upstream.
8. **A recovery path draft**: an explicit, audited way to regain operator access when no operator can sign in, whose final policy waits for human approval.
9. **Audit hooks**: a durable, append-only operator audit record, written in the same unit of work as each security state change.
10. **The developer path**: how a developer completes the first-operator bootstrap in the developer topology without a committed or fabricated secret.

This spec adds no local account, password, session, cookie, external OIDC login, organization or project role, project-level grant, API key, management resource API, or management UI. It adds no credential provider, so before ISK-0021 no process can connect to PostgreSQL, and every admin route fails closed.

Contract, type, command, service, and path names appear in this spec where an approved contract already fixes them or where they are the interface this spec defines, not as a choice of implementation.

## Decision Classification

### Accepted decisions (already approved; this spec applies them)

| Decision | Source |
|---|---|
| A modular monolith with explicit modules. Control and data planes are independently deployable, and dependency direction is preserved | ADR-0003, `docs/03` Logical architecture, Constitution II |
| Organization → project → resource ownership with RBAC. System-level configuration belongs to a separate operator scope and never impersonates a tenant | ADR-0005, `docs/03` Resource hierarchy, Constitution V |
| Control-plane identity is local ASP.NET Core Identity or a bring-your-own external OIDC provider. An internal principal identity is persisted separately from any IdP subject, and roles bind to it. Email-only auto-linking is forbidden | ADR-0008, `docs/05` Control-plane identity |
| Data-plane ingress uses API keys and validated JWT bearer, with key issuance and acceptance independently controllable. No unauthenticated production endpoint exists without an explicitly reviewed exceptional requirement | ADR-0009, `docs/05` Trust zones 2 |
| The global platform operator and organization and project roles are distinct. Authorization is `principal × action × resource owner/scope × policy/constraints`, never inferred from role strings alone. Admins cannot bypass guardrails except through explicitly auditable elevated operations | `docs/05` Mandatory authorization |
| Every control-plane change is authenticated, scoped, and audited. Audit events record who, what, when, scope, result, configuration revision, and reason, separately from inference content | `docs/05` Trust zones 1, Logging, audit, retention, deletion |
| Mandatory security checks default to least privilege and fail closed | Constitution V, `docs/03` Stream invariants |
| Every database access happens in a unit of work for one owner scope. Operator-scope units cannot touch tenant tables. Operator access to tenant data needs a named, audited elevated operation that a later spec defines | Persistence contract version 3, Units of work, Isolation rule 6 |
| Migrations run only through the control-plane image's `migrate` command, never at start, and no long-running process needs the migration login | Container contract version 2, Start-up rule 5, Migration command |
| The control plane binds only its health listener until a management API spec adds endpoints. Both listeners are plain HTTP, TLS is terminated in front of the containers, and the health listener is protected only by being a separate port that is not published | Container contract version 2, Listeners rules 5 and 7 (AV-5) |
| `GET /health/live` and `GET /health/ready` answer without authentication, and hold no host, port, user name, secret reference name, or configuration value | Container contract version 2, Liveness, Readiness rules 1 and 10 |
| `Iskender.Management` owns `AddIskenderControlPlane(...)` and `AddIskenderWorker(...)`, and references only `Iskender.Domain` and `Iskender.Core.Contracts`. A module adds one `CompositionModule` through `CompositionServices.AddModule`. Two roots on one service collection fail with `hosting.registration_duplicate` | Hosting contract version 3, Ownership rules 2 and 4, Registration rule 9, Registration mistakes |
| The data-plane host never reaches `Iskender.Management`, and the data-plane image holds no `Iskender.Management` assembly | Module dependency rules contract version 5, rule A6; container contract version 2, Entry points rule 4 |
| The worker composes exactly the registrations of container contract Entry points rule 2, binds only its health listener, always, and publishes nothing in the developer topology | Container contract version 2, Entry points rule 2, Listeners rule 5; local environment contract version 2, Ports rule 1 |
| The developer topology publishes only on `127.0.0.1`, never publishes a health port, publishes nothing from the control plane, and its smoke script asserts each plane's and the worker's readiness answer byte for byte. A change to an expected smoke state is a revision of the local environment contract | Local environment contract version 2, Ports, Smoke assertions, Changing this contract |
| No tracked file holds a password, token, or credential value, and passwords exist only in the generated, ignored password file | Local environment contract version 2, Passwords, Documents rule 6 |
| No credential provider ships until ISK-0021, so before then no process can connect to PostgreSQL | Persistence contract version 3, Credentials rule 2 |
| Payload logging is off by default. Telemetry uses a bounded, sensitive-safe vocabulary, and never carries a credential | Constitution V and VIII, `docs/05` Logging |
| Error codes are registered in `ErrorCodes` with a `since` version, and new codes raise `core-contracts` with compatible changes only. The `identity.unauthenticated` and `identity.auth_denied` codes already exist | Public contracts and versioning contract, Error codes, Versioning policy |
| The exact minimum local MFA and admin recovery policy, audit retention defaults, and key-rotation SLAs need approval | `docs/05` Decision status, `docs/16` Recommendations and unresolved approvals |
| No SLO, latency, or throughput figure is asserted without a reviewer-approved baseline. Every bound in this spec is a safety bound | Constitution VIII |
| One stage runs per `/drive-speckit` invocation, and `specify` produces the specification only | ADR-0025, Constitution X |

### Maintainer decisions taken in `specify`

| Decision | Recorded in |
|---|---|
| **What redemption yields** (Q1): the new operator principal's ID and a one-time **enrollment code**, bound to that principal, which ISK-0013 or ISK-0014 redeems to attach the first sign-in method. Until a sign-in method exists, every protected admin route denies everyone, and privilege checks are tested through a test-only principal resolver | TR-4, TR-6, TR-22, FR-011 |
| **TLS** (Q2): optional in-process TLS on the traffic listeners, from certificate and key files mounted read-only. Outside `development`, the control plane refuses to start unless its traffic listener either has in-process TLS or the operator explicitly declares that TLS is terminated upstream. The health listeners stay plain HTTP and unpublished. **This settles ISK-0007's AV-5**. Extended to the data plane in `clarify` | TR-10, FR-022 |
| **Issuing the token** (Q3): a one-shot `bootstrap` command of the control-plane image, run like `migrate`: `bootstrap issue` for a bootstrap token and `bootstrap recover` for a recovery token, each storing the token's hash and printing the token once to standard output. The developer topology runs it through a script, in a container removed when it exits | TR-2, TR-14, TR-20, FR-003, FR-035 |

### Technical recommendations (proposed by this spec; reviewed in `clarify`)

| ID | Recommendation | Why | Alternative rejected |
|---|---|---|---|
| TR-1 | **Installation state**: one operator-scope record holding the state (`uninitialized` or `initialized`), when it was initialized, and by which principal, in a new migration module `identity`, schema `isk_identity`, owned by the Identity/Authorization module (`Iskender.Domain.Identity`, with its PostgreSQL implementation in `Iskender.Infrastructure.PostgreSql.Identity`), in operator tables only. A database with no record is `uninitialized`. The state only moves forward: nothing in this spec returns an installation to `uninitialized`. The module joins the shipped modules through a revision of the persistence contract (version 4) | PostgreSQL is the source of truth, and every control-plane replica must see one state. Operator tables are the existing kind for system-level records | Process memory or a file, which replicas cannot share; a tenant table, which the operator scope must never use |
| TR-2 | **Issuing the bootstrap token** (decided in `specify`, Q3): `bootstrap issue`, a one-shot command of the control-plane executable, run like `migrate` with the serving control plane's documents and registrations. It runs no composition start-up, binds no listener, and needs no migration login. In one unit of work it supersedes any active bootstrap token, stores the new token's hash, and writes the audit record, then prints the token, and nothing else, once to standard output. Its events and diagnostics go to standard error and never hold the token. It fails, with an exit code the container contract revision adds and never reuses, when the installation is already `initialized`, and exits 5 on a `persistence.*` failure, as `migrate` does | The token reaches only the operator who ran the job, never a log collector, configuration document, or committed file, and the trust anchor is the same as for migrations | Writing the token to the serving control plane's log at first start, which ships it to log collectors; an operator-made token hashed into configuration, which makes expiry and supersession awkward |
| TR-3 | **Token form and storage**: the token holds a non-secret identifier and a secret of at least 256 bits from a cryptographically secure source. Only a hash of the secret is stored, with the token's purpose (`bootstrap`, `recovery`, or `enrollment`, TR-22), its expiry, and whether it was consumed or superseded. Verification compares hashes in constant time. Its lifetime is `operator.bootstrap.tokenLifetime`, default 1 hour, from 5 minutes to 24 hours. At most one unconsumed token of a purpose is active: issuing another supersedes the previous one. The plaintext never appears in a log, trace, metric, error, readiness answer, audit record, or database row | A high-entropy, short-lived, single-active, hashed token limits what a leaked database row or log line can reveal, and bounds the window of an intercepted token | A long-lived or reusable setup password; a token stored in plaintext |
| TR-4 | **Redemption**: an anonymous admin route, `POST` to the bootstrap path under the admin prefix, takes the token. In one unit of work, it consumes the token on the condition that it is unconsumed, unexpired, not superseded, and the installation is `uninitialized`, then creates the first operator principal, grants it the system-operator role (TR-5), records the audit entry (TR-13), and moves the installation to `initialized`, all committed together. So exactly one of any number of concurrent redemptions succeeds, and a consumed token never succeeds again. Every failed redemption, whether the token is wrong, expired, consumed, superseded, or the installation is already initialized, gets one identical answer, a `403` with one registered code, so the answer reveals nothing about why. The reason is recorded only in the audit entry and the event. A successful redemption returns, once, the new principal's ID and its enrollment code (decided in `specify`, Q1; TR-22) | Atomic, conditional consumption is what makes the token single-use across replicas and races, and uniform failures stop the answer becoming an oracle | Checking then consuming in separate statements, which lets two concurrent redemptions both succeed |
| TR-5 | **Operator principal and grant**: an internal principal with an opaque ISK-0002 ID of a new resource type, `principal`, registered with `since: 7`, separate from any identity-provider subject, and an operator grant binding it to the role `system_operator` at the operator scope. This spec defines only that role and only that scope. Organization and project roles, memberships, and grants are ISK-0015's, and a `system_operator` grant gives no access to tenant data: that still needs the audited elevated operation the persistence contract reserves | `docs/05` and ADR-0008 require an internal principal to which roles bind, and keep the global operator distinct from tenant roles | Treating the first account as the principal, which ties the operator to one sign-in method; an implicit "first user is admin" rule |
| TR-6 | **Privilege checks**: an operator authorization port evaluates `principal × action × operator scope` against the stored grants, and denies when the principal is unknown, has no grant for the action, or when the grants or the installation state cannot be read. Every admin route declares exactly one of an operator action it requires, or membership of the reviewed anonymous list (TR-8). A route that declares neither fails the control plane's start with a registered code, so no admin route can be added unprotected by omission. Authentication is a seam: a principal-resolution port that ISK-0013, ISK-0014, and ISK-0020 implement. This spec ships no production implementation of it, so in production every protected admin route answers `401` with `identity.unauthenticated` once the installation is initialized. Tests use a test-only implementation that never ships | Deny by default and fail closed are constitutional, and a declaration rule turns "forgot to protect a route" into a start failure instead of an exposure | Per-route ad hoc checks; authorization from role strings in a token |
| TR-7 | **The uninitialized gate**: before any authentication runs, every admin route except the bootstrap and recovery redemptions (decided in `clarify`) reads the installation state. While it is `uninitialized`, the route answers `403` with a registered code and does nothing else, whatever credential the caller presents. The redemptions check the state only inside their own conditional consumption (TR-4, TR-14), so on any state they fail only with the uniform failure. When the state cannot be read, every admin route, the redemptions included, answers `503` with a registered code, and fails closed. Once a replica has read `initialized`, it may keep that answer, because the state never moves back (TR-1). It never keeps an `uninitialized` or unknown answer | The approved acceptance, "uninitialized admin routes deny access", holds for every route, including routes later specs add, without each spec repeating it | Letting each route check initialization itself |
| TR-8 | **Unauthenticated endpoint baseline**: the `operator-security` contract lists every endpoint reachable without authentication, per process and listener: the health endpoints of the data plane, the control plane, and the worker on their health listeners; the bootstrap and recovery redemptions on the control plane's traffic listener; and the data plane's catch-all `404` under `MapIskender("/")`, which answers with no data until the ingress specs (ISK-0019, ISK-0029) put authentication in front of it. A test enumerates every endpoint each process maps, on each listener, and fails on any endpoint reachable without authentication that is not on the list. Adding an anonymous endpoint is a revision of that contract, with Security owner review | `docs/05` forbids unauthenticated production endpoints without an explicitly reviewed requirement, and an enforced inventory makes the review real | A written policy with no test |
| TR-9 | **Health exposure**: the health endpoints stay as ISK-0007 defined them on all three processes: unauthenticated, on the health listener only, with the same bodies. The control plane's new traffic listener serves no health endpoint: `/health/live` and `/health/ready` there reach the admin surface, which answers `404`, as the data plane's traffic listener does. The installation state never appears in readiness, so the readiness answers, and the developer topology's smoke answers, stay byte for byte unchanged. The worker's health listener is included in the AV-5 settlement of TR-10 | Readiness says whether a process can serve, not whether it has been set up, and keeping the answers unchanged keeps every probe and smoke check stable | Reporting `uninitialized` as not ready, which takes an unbootstrapped control plane out of the load balancer before anyone can bootstrap it |
| TR-10 | **TLS assumptions, settling ISK-0007's AV-5** (decided in `specify`, Q2): through a revision of the container contract's `container` section, each traffic listener takes a transport mode: `listener`, in-process TLS from a PEM certificate chain file and a PEM private key file, both mounted read-only and read once at start; `upstream`, an explicit declaration that TLS is terminated in front of the container; or `none`, plain HTTP with no declaration, which is development-only. Outside `development`, each plane refuses to start, with a registered code, exit code 2, and no listener bound, unless its traffic mode is `listener` or `upstream` (the data plane decided in `clarify`). ISK-0019 and ISK-0085 recheck the data plane's transport once credentials cross it. A certificate or key file that is missing, unreadable, malformed, expired, or mismatched fails start with exit code 2, naming the field and never the file's content. In `listener` mode, TLS 1.2 is the minimum version. Rotating a certificate needs a restart. The health listeners of all three processes stay plain HTTP, protected by being a separate port that is never published beyond the orchestrator's private network. The worker binds no traffic listener, so the rule does not apply to it. The private key's file content, path, and passphrase never appear in a log, event, error, or readiness answer | The bootstrap token and the enrollment code cross the control plane's traffic listener, so the transport must be either protected by the process or explicitly vouched for by the operator, never left implicit | Plain HTTP with only a documented assumption, which a misdeployment silently breaks; mandatory in-process TLS, whose key material would wait for ISK-0021 |
| TR-11 | **Container contract revision (version 3)**: the control plane binds its traffic listener and serves only the admin routes there, under one versioned admin prefix the revision fixes. Its new commands are `bootstrap issue` and `bootstrap recover` (TR-2, TR-14). The `container` section gains the transport mode fields of TR-10. The data plane's routes and readiness are unchanged, and it gains only the transport mode rule of TR-10. The worker composes no service this spec adds, binds no traffic listener, and its readiness and stop sequence are unchanged. Every existing exit code keeps its meaning, and any new one is added, never reused | The prompt asks that admin routes reach the control plane through a revision of that contract, and that the worker's composition be stated | A third image or executable for the admin surface |
| TR-12 | **Composition and embedding, settling ISK-0006's AV-5**: one registration in `Iskender.Management` adds the operator-security module, whose requirements name the `control-plane` module, so it composes only under `AddIskenderControlPlane(...)`. In the worker or under `AddIskender(...)` it fails start with `hosting.registration_missing`. One mapping method maps the admin routes under a prefix the host chooses. An embedding application opts in by composing the control-plane root on its own service collection, calling the registration, and mapping the routes. It cannot compose them beside `AddIskender(...)` on one collection, because two roots on one collection are refused, so an application that embeds both planes runs two hosts. This adds a revision of the hosting contract (version 4), and of the module dependency rules contract (version 6) where `Iskender.Management` needs ASP.NET Core to map routes or new namespaces need homes | Keeps the operator surface out of the data plane, as rule A6 requires, while letting an embedding host run its own control plane | Letting the data-plane root compose the admin surface, which would put the management surface in the internet-facing process |
| TR-13 | **Audit hooks**: an audit-recording port of the Telemetry/Audit module (`Iskender.Domain.Telemetry`), whose PostgreSQL implementation appends one record in the caller's own unit of work, so the record commits or rolls back with the change it describes. It writes an operator table in a new migration module `audit`, schema `isk_audit` (persistence contract version 4). Each record holds an opaque `audit` resource ID, the time, the actor (a principal ID, `anonymous`, or `operator_command`), the target principal when there is one, the action, the scope (`operator`), the result, a reason class from a fixed list, the configuration revision, and the request's correlation ID. It never holds a token, a hash, a network address, or a request body. The runtime login may insert and read audit records, and may not update or delete them. Records are written for token issued, token superseded, redemption succeeded, redemption failed against a stored token, recovery token issued, and recovery redeemed. A failed redemption whose identifier names no stored token, or whose token is malformed, writes no record. It and denied admin requests are counted by a metric and an event, not by durable records, so an anonymous flood cannot grow the audit table | `docs/05` requires that every control-plane change be audited, separately from inference content, and an audit record in the same transaction cannot be lost or invented apart from its change | Audit only in logs, which are neither durable nor tamper-resistant; a durable record per denied request, which turns the audit table into a denial-of-service target |
| TR-14 | **Recovery path draft**: a recovery token, issued by `bootstrap recover` (decided in `specify`, Q3) only when the installation is `initialized`, with the same form, lifetime, single-active rule, delivery, and audit as the bootstrap token, and failing with its own exit code when the installation is `uninitialized`. Redeeming it at its own anonymous route creates a new operator principal with the system-operator role, and returns its ID and enrollment code, as for the first operator (Q1, TR-22). It never reveals, changes, or removes another principal or grant. Its trust anchor is the same as the bootstrap's: the ability to run the control plane's command with its documents and database access. The minimum recovery policy, such as MFA reset, notification of other operators, or a second approver, needs human approval (`docs/05` Decision status) and is recorded as AV-4 | A sole operator who loses access must have a way back that is explicit, audited, and no weaker than the bootstrap | A recovery flag in configuration that silently re-opens bootstrap; resetting the installation to `uninitialized` |
| TR-15 | **Request bounds on anonymous routes**: the redemption routes accept only `application/json` bodies of at most 4 KiB, and each control-plane process limits redemption attempts to `operator.bootstrap.attemptsPerMinute` (default 10, from 1 to 600) across both routes, answering `429` beyond it. The limit is per process and is a safety bound: the token's entropy, not the limit, is what defeats guessing | Bounded payloads and per-identity rate limits are `docs/05` threat-review controls, and a per-process limit needs no coordinator | A distributed limit through ISK-0005, which a 256-bit secret does not need |
| TR-16 | **Configuration**: an ISK-0003 namespace `operator`, registered only by the operator-security module, holding `bootstrap.tokenLifetime`, `bootstrap.attemptsPerMinute`, and `enrollment.codeLifetime` (TR-22). The transport mode fields belong to the `container` section (TR-10). The data plane and the worker reject an `operator` section with `config.namespace.unknown` | Keeps operator settings out of the planes that do not use them, as ISK-0009 does for `work` | Settings on the registration call, which configuration review cannot see |
| TR-17 | **Error codes and resource type**: a small set under `operator.*` in `ErrorCodes`, with `since: 7`, raising `core-contracts` from 6 to 7 with compatible changes only, covering at least a failed redemption, an uninitialized installation, an unreadable installation state, a recovery requested before initialization, an admin route with no declared requirement, too many redemption attempts, a plane's traffic listener with no transport protection outside `development`, an unusable certificate or key file, and an invalid or consumed enrollment code. The `principal` resource type is added in the same version. The plan fixes the codes, categories, and safe messages | Routes fail only with registered codes, as every other surface does | Reusing `core.internal` |
| TR-18 | **Diagnostics**: events from 9001 under the category `Iskender.Identity`, and metrics and activities from `Iskender.Identity`: redemption attempts and outcomes, uninitialized and unauthenticated denials, privilege denials, tokens issued and superseded, and route-declaration failures at start. Fields and labels are limited to action, outcome, reason class, and route template. No token, hash, principal ID, network address, or configuration value appears in any field or label. The entry points subscribe to `Iskender.Identity` as they do to `Iskender.Work` | Operable diagnostics with a bounded vocabulary | Logging principal IDs or caller addresses |
| TR-19 | **`operator-security` contract**: a new versioned contract, version 1, in this spec's `contracts/` directory, owning the installation state, the token and its redemption, the enrollment code and port, the operator principal and grant, the privilege-check and principal-resolution ports, the uninitialized gate, the anonymous endpoint inventory, the TLS assumptions and transport modes, the recovery draft, the audit hooks, configuration, error codes, diagnostics, and the test fixture. The revisions are container version 3 (TR-11), hosting version 4 (TR-12), local environment version 3 (TR-20), persistence version 4 (TR-1, TR-13), and module dependency rules version 6 where TR-12 needs it. Each is published in this spec's `contracts/` directory, and the links in `docs/03`, `docs/04`, and `docs/05` are repointed or added | Later identity specs build on one owner of these rules | Rules spread across the existing contracts with no owner |
| TR-20 | **Developer topology (local environment contract version 3)**: the control plane's traffic port is published on `127.0.0.1` only, default host port 18090, overridable by `ISKENDER_LOCAL_CONTROL_PLANE_PORT` under the existing port rules. A developer completes the bootstrap by running a new script, `local/scripts/bootstrap.sh`, with `issue` or `recover`, which runs the control-plane image's `bootstrap` command once, in a container removed when it exits, so the token appears only in the developer's own terminal, and then redeeming it on that published port. Each plane's development overlay sets its traffic mode to `none`, which only `development` permits, so the documents state the loopback-only plain HTTP openly, and each base document, which validates alone, declares a mode that is valid outside `development`. The token is generated at run time, never written to a tracked file, never committed, and never fabricated by a script or document. No health port and nothing of the worker is published. The smoke script asserts the three readiness answers unchanged byte for byte, and adds, before ISK-0021, that the control plane's admin routes fail closed: a redemption and a protected route each answer `503` with the unreadable-state code, and the issuing script exits 5 with `persistence.unavailable` | A developer needs a route to the admin surface and a real token, and the smoke check must show the fail-closed state until credentials exist | Leaving the control plane unpublished, which makes the developer bootstrap impossible; a sample token in the documents, which would be a committed secret |
| TR-21 | **Test fixture**: an operator fixture, built on the ISK-0004 PostgreSQL fixture, that issues tokens, redeems them from many concurrent callers against two or more control-plane hosts sharing one database, resolves test principals through the test-only principal-resolution implementation, makes the state store unreachable, and scans every mapped endpoint of each process for the inventory test. Container tests run the control-plane image's new commands and traffic listener. It follows ISK-0005's container-runtime rule | It is how the approved acceptance is shown rather than asserted | A mocked store, which cannot show conditional consumption across replicas |
| TR-22 | **Enrollment code** (decided in `specify`, Q1): each successful bootstrap or recovery redemption creates, in the same commit, one enrollment code bound to the new principal, with the token form and hash-only storage of TR-3 and purpose `enrollment`. Its lifetime is `operator.enrollment.codeLifetime`, default 24 hours, from 1 hour to 7 days. It is returned once in the redemption's answer and never again. An enrollment port lets ISK-0013 or ISK-0014 consume it exactly once, atomically, for its bound principal only, to attach the first sign-in method. This spec ships no consumer, so in production no enrollment completes until one of those specs lands. An expired or lost code is replaced only through recovery (TR-14), which creates a new principal. Its consumption, when a consumer exists, writes an audit record | Keeps every sign-in credential in the spec that owns it, and gives ISK-0013's admin invite a principal to attach to | An interim operator access token, which ISK-0020 would have to take over or retire; a principal with no follow-on code, which leaves ISK-0013 without a trust anchor |

### Assumptions needing validation

| ID | Assumption | Validated by |
|---|---|---|
| AV-1 | `Iskender.Management` can map the admin routes, through an ASP.NET Core framework reference or a mapping seam, without reaching an infrastructure assembly, so rule A3 holds | This spec's plan, Architecture (ADR) owner |
| AV-2 | One conditional statement, or one unit at the isolation the plan chooses, makes concurrent redemptions of one token serialize so that exactly one commits, without an advisory lock or a coordinator | This spec's plan; the concurrency test of SC-002 |
| AV-3 | ISK-0013, ISK-0014, and ISK-0020 can implement the principal-resolution port, and ISK-0013 or ISK-0014 the enrollment port, attaching sign-in to the operator principal this spec creates without changing the installation state or the gate | Those specs |
| AV-8 | ASP.NET Core can serve in-process TLS from read-only mounted PEM files in the chiseled, non-root, read-only-root runtime image, with no writable certificate store | This spec's plan; the container tests |
| AV-4 | The recovery draft of TR-14 is an acceptable interim recovery policy. The minimum admin recovery and MFA policy needs human approval before the public MVP | Security owner, human approver; ISK-0013 and ISK-0085 |
| AV-5 | Once ISK-0021 provides credential providers, the developer topology's bootstrap works end to end with no change of service or document layout, and its expected smoke state changes through a revision of the local environment contract | ISK-0021 |
| AV-6 | ISK-0026 can extend the audit records of TR-13 to tenant scopes, retention, and tamper-evident ordering without changing the records this spec writes | ISK-0026 |
| AV-7 | The operator fixture, the endpoint inventory test, and the container tests run in the existing CI checks, so CI stays at 10 checks | This spec's plan |

**ISK-0006's AV-5 is settled by this spec** (TR-12): control-plane services compose into an embedded host through the same pattern, under the control-plane root on the host's own service collection, and never beside the data-plane root on one collection.

**ISK-0007's AV-5 is settled by this spec** (TR-9, TR-10), for the data plane, the control plane, and the worker.

### Post-MVP work (named so that it is not designed here)

A built-in OIDC authorization server. Hardware-backed or two-person recovery. Automatic certificate management, certificate reload without restart, and mutual TLS. A setup wizard in the management UI, which waits for ISK-0081. Distributed rate limits on anonymous routes.

## Clarifications

### Session 2026-11-21 (specify)

- Q: No sign-in method exists until ISK-0013, ISK-0014, and ISK-0020. What does the first operator receive when the bootstrap token is redeemed? → A: The new principal's ID and a one-time enrollment code bound to it, which ISK-0013 or ISK-0014 redeems to attach the first sign-in method. Until then every protected admin route denies everyone, and privilege checks are tested through a test-only principal resolver (TR-4, TR-6, TR-22, FR-011, FR-011a).
- Q: Are plain-HTTP listeners, with TLS terminated in front of the containers, enough now that the control plane accepts a bootstrap token (ISK-0007's AV-5)? → A: No longer by assumption alone. Traffic listeners gain optional in-process TLS from read-only mounted certificate files. Outside `development`, the control plane refuses to start unless its traffic listener has in-process TLS or the operator explicitly declares TLS terminated upstream. Health listeners stay plain HTTP and unpublished (TR-10, FR-022 to FR-022b). **Extended to the data plane in `clarify`.**
- Q: How is the bootstrap token issued and delivered? → A: A one-shot `bootstrap issue` command of the control-plane image, run like `migrate`, which stores the token's hash and prints the token once to standard output, with `bootstrap recover` for recovery. The developer topology runs it through `local/scripts/bootstrap.sh` in a container removed when it exits (TR-2, TR-14, TR-20, FR-003, FR-035).

### Session 2026-11-21 (clarify)

- Q: When someone fails to redeem a token, which failures should write a permanent audit record? → A: Only a failure whose identifier names a stored token: a wrong secret, or an expired, consumed, superseded, or wrong-purpose token. A failure with an unknown identifier or a malformed token writes no durable record, and is counted only by the metric and the event, so anonymous guessing cannot grow the audit table (TR-13, FR-028).
- Q: Outside `development`, should the data plane also refuse to start without declared transport protection, as the control plane will? → A: Yes, both planes. Outside `development`, the data plane, like the control plane, starts only with traffic mode `listener` or `upstream`, and otherwise exits 2 with the same registered code and no listener bound. Its routes and readiness answers are unchanged, and its development overlay sets `none` (TR-10, TR-11, TR-20, FR-022a, FR-022b, FR-031, FR-035). This extends the Q2 decision taken in `specify`.
- Q: On an uninitialized installation, should the recovery redemption route give the uninitialized answer, or the uniform redemption failure? → A: The uniform failure. Both redemption routes, bootstrap and recovery, are exempt from the uninitialized gate, and each checks the installation state only inside its own conditional consumption, so every failed redemption has one shape and a token at the wrong purpose's route always gets the uniform failure. An unreadable state still answers `503` on both (TR-7, FR-018, FR-019, FR-024).
- Correction, no choice left: 50 concurrent redemptions across two hosts exceed the default limit of 10 attempts per process per minute, so some would answer `429` rather than the uniform failure. The concurrency tests set `operator.bootstrap.attemptsPerMinute` within its range so that every attempt reaches the store, and the limit has its own test (US2.3, SC-002, FR-043).

## User Scenarios & Testing *(mandatory)*

The "users" are operators, who install and run the control plane, developers, who run the developer topology, and the authors of later identity specs, who build sign-in and authorization on this baseline.

### User Story 1 - Uninitialized admin routes deny access (Priority: P1)

An operator deploys a fresh control plane. Until the first operator is bootstrapped, no admin route does anything for anyone, whatever they present.

**Why this priority**: It is half of the approved acceptance evidence, and every later admin route inherits it.

**Independent Test**: Start a control-plane host against an empty migrated database, call every mapped admin route except the bootstrap and recovery redemptions, with no credential, a malformed credential, and a credential the test principal resolver accepts, and assert that each answers `403` with the uninitialized code and changes nothing.

**Acceptance Scenarios**:

1. **Given** an uninitialized installation, **When** any admin route other than the bootstrap and recovery redemptions is called, with or without any credential, **Then** it answers `403` with the uninitialized code, and no state, grant, or audit record changes.
2. **Given** a control plane whose database is unreachable, **When** any admin route is called, **Then** it answers `503` with the unreadable-state code and does nothing else.
3. **Given** a route added to the admin surface without a declared requirement, **When** the control plane starts, **Then** the start fails with the route-declaration code, and no listener serves it.
4. **Given** an uninitialized installation, **When** a test adds a new protected admin route through the supported mapping, **Then** that route also answers `403` with the uninitialized code, with no code of its own for initialization.

---

### User Story 2 - The first operator is bootstrapped once, and the token cannot be reused (Priority: P1)

An operator obtains a bootstrap token, redeems it, and becomes the first system operator. The same token, or any other, never bootstraps again.

**Why this priority**: It is the other half of the approved acceptance evidence.

**Independent Test**: Issue a token, redeem it, and assert that the installation is `initialized` with one operator principal and one grant. Redeem it again, redeem it from 50 concurrent callers across two hosts on a fresh database, and redeem expired and superseded tokens, and assert the outcomes below.

**Acceptance Scenarios**:

1. **Given** an uninitialized installation and a valid token, **When** it is redeemed, **Then** the installation is `initialized`, exactly one operator principal with the `system_operator` grant exists, one enrollment code bound to it exists and is returned once, and one audit record of the success exists, all committed together.
2. **Given** a token that has been redeemed, **When** it is redeemed again, **Then** the answer is the uniform `403` failure, and nothing changes beyond a failure audit record.
3. **Given** one valid token and 50 concurrent redemptions across two control-plane hosts sharing one database, with `operator.bootstrap.attemptsPerMinute` set so that no attempt is limited, **When** they complete, **Then** exactly one succeeds, 49 get the uniform failure, and exactly one operator principal exists.
4. **Given** an expired token, a superseded token, or a wrong secret with a valid identifier, **When** each is redeemed, **Then** each gets the same status, code, and body shape as every other failure, and the audit record names the reason class.
5. **Given** an initialized installation, **When** a new bootstrap token is requested, **Then** none is issued, and the request fails with its registered code.
6. **Given** any test run, **When** every captured log, trace, metric, error body, readiness answer, audit record, and database row is searched for each token's secret, **Then** none is found.

---

### User Story 3 - Privilege checks deny by default and fail closed (Priority: P1)

After bootstrap, an admin route runs only for a principal holding the grant its action needs.

**Why this priority**: "Privilege checks" are in the approved capability, and fail-closed authorization is constitutional.

**Independent Test**: On an initialized installation, call a protected admin route with no credential, with a test principal holding no grant, with the operator principal, and with the grant store unreachable, and assert each outcome.

**Acceptance Scenarios**:

1. **Given** an initialized installation and no credential, **When** a protected admin route is called, **Then** it answers `401` with `identity.unauthenticated`.
2. **Given** a resolved principal with no grant for the route's action, **When** it calls the route, **Then** it answers `403` with `identity.auth_denied`.
3. **Given** the operator principal, **When** it calls a route whose action `system_operator` holds, **Then** the route runs.
4. **Given** a resolved principal and an unreadable grant store, **When** it calls a protected route, **Then** it answers `503` and the route does not run.
5. **Given** a production composition with no principal-resolution implementation, **When** any protected admin route is called on an initialized installation, **Then** it answers `401`.

---

### User Story 4 - Nothing is anonymous unless reviewed (Priority: P2)

A security reviewer can see every endpoint reachable without authentication on every process, and a test fails if one appears that is not on the list.

**Why this priority**: "Baseline unauthenticated endpoint restrictions" and "health exposure" are in the approved scope.

**Independent Test**: Run the inventory test against the data plane, the control plane, and the worker, then add an unlisted anonymous endpoint in a test build and assert the test fails naming it.

**Acceptance Scenarios**:

1. **Given** the three processes as shipped, **When** the inventory test enumerates their endpoints on each listener, **Then** every endpoint reachable without authentication is on the `operator-security` contract's list, and the test passes.
2. **Given** an anonymous endpoint that is not on the list, **When** the inventory test runs, **Then** it fails, naming the process, listener, method, and route.
3. **Given** the control plane's traffic listener, **When** `/health/live` or `/health/ready` is requested there, **Then** it answers `404`, and the health endpoints answer only on the health listener.
4. **Given** the data plane, the control plane, and the worker, **When** each readiness endpoint is read, **Then** its body is byte for byte what container contract version 2 records for the same state, and holds no installation state.
5. **Given** a data plane or control plane outside `development` with traffic mode `none`, or with an unreadable, malformed, expired, or mismatched certificate, **When** it starts, **Then** it exits 2 with its registered code, binds no listener, and its diagnostics name the field and never the file's path or content.
6. **Given** a control plane with traffic mode `listener`, **When** a client connects with TLS 1.2 or later, **Then** the admin routes answer; **When** a client sends plain HTTP to that listener, **Then** no admin route runs.

---

### User Story 5 - Security changes leave an audit trail (Priority: P2)

An operator can see who bootstrapped the installation, when, and every failed or superseded attempt, and the records cannot be edited by the running service.

**Why this priority**: Audit hooks are in the approved scope, and `docs/05` requires every control-plane change to be audited.

**Independent Test**: Run issue, supersede, failed redemption, successful redemption, recovery issue, and recovery redemption, and assert one audit record each, with the fields of TR-13, committed with its change. Roll back a redemption's unit and assert no record remains. As the runtime login, try to update and delete a record and assert both are refused.

**Acceptance Scenarios**:

1. **Given** a successful redemption, **When** its unit commits, **Then** exactly one audit record exists for it, with the actor `anonymous`, the new principal as its target, the action, the result, and the configuration revision.
2. **Given** a redemption whose unit rolls back, **When** the audit table is read, **Then** it holds no success record for that redemption.
3. **Given** the runtime login, **When** it tries to update or delete an audit record, **Then** the database refuses it.
4. **Given** 1,000 anonymous denied requests, **When** they complete, **Then** the audit table grows by 0 records, and the denial metric counts them.

---

### User Story 6 - A locked-out installation can be recovered (Priority: P2)

No operator can sign in. An operator with access to run the control plane's commands issues a recovery token and redeems it to create a new operator, and the recovery is recorded.

**Why this priority**: A safe recovery path draft is in the approved scope, and a single-operator installation must not be lost.

**Independent Test**: On an initialized installation, issue a recovery token, redeem it, and assert a new operator principal and grant exist, the original principal and grant are unchanged, and two audit records exist. Assert that a recovery token cannot be issued on an uninitialized installation, cannot be reused, and that a bootstrap token cannot be redeemed as a recovery token or the reverse.

**Acceptance Scenarios**:

1. **Given** an initialized installation, **When** a recovery token is issued and redeemed, **Then** a new operator principal with `system_operator` exists, every other principal and grant is unchanged, and the issue and redemption are both audited.
2. **Given** an uninitialized installation, **When** a recovery token is requested, **Then** none is issued, and the request fails with its registered code.
3. **Given** a redeemed recovery token, **When** it is redeemed again, **Then** it gets the uniform failure.
4. **Given** a bootstrap token, **When** it is presented to the recovery route, or a recovery token to the bootstrap route, **Then** each gets the uniform failure.

---

### User Story 7 - A developer bootstraps locally, and an embedder composes the control plane (Priority: P3)

A developer starts the developer topology, reaches the control plane's admin surface on loopback, and bootstraps with a token generated in their own terminal. An application author composes the operator surface into their own control-plane host.

**Why this priority**: The prompt asks for both, but neither works end to end before ISK-0021 in the developer topology.

**Independent Test**: Run ISK-0008's acceptance cycle with the revised topology and assert the smoke checks of TR-20. Separately, compose the operator surface into an `EmbeddedHost` with the control-plane root and run User Stories 1 to 3 against it, and assert that composing it under `AddIskender(...)` or `AddIskenderWorker(...)` fails start with `hosting.registration_missing`.

**Acceptance Scenarios**:

1. **Given** a fresh clone, **When** the start script runs, **Then** the control plane's traffic port answers on `127.0.0.1` at its published port, and nothing else new is published.
2. **Given** the running topology before ISK-0021, **When** smoke runs, **Then** the three readiness answers match byte for byte, the admin routes answer `503` with the unreadable-state code, and the token-issuing script exits 5.
3. **Given** the tracked files, **When** they are searched, **Then** no bootstrap or recovery token, and no fixed value that could act as one, is found.
4. **Given** an embedded host with the control-plane root and the operator-security registration, **When** User Stories 1 to 3 run against it, **Then** they pass as against the container.

### Edge Cases

- A token is issued while another is active: the earlier one is superseded at once, and redeeming it gets the uniform failure.
- The token expires while a redemption is in flight: the conditional consumption compares against the database's time in the same statement, so a token is either consumed before its expiry or not at all.
- A redemption's commit outcome is unknown: nothing retries it automatically (persistence contract, Failures). Either the installation is initialized, and a new redemption gets the uniform failure, or it is not, and the operator issues a new token.
- Two control-plane replicas, one of which has cached `initialized`: the other reads it from the database; neither ever caches `uninitialized`.
- A request arrives at a replica during its start, before composition finishes: the gate cannot read the state and answers `503`.
- The database is migrated below this spec's modules while a control plane runs: the state cannot be read, and every admin route fails closed.
- A `migrate` run: it issues no token, serves no route, and its migration set includes the new modules.
- An `operator` section in a data-plane or worker document: rejected with `config.namespace.unknown`, exit 2.
- A redemption body over 4 KiB, of the wrong content type, or malformed: rejected before the token is read, with no audit record, counted by the denial metric.
- Redemption attempts beyond the per-process limit: `429`, with no database read.
- A principal-resolution implementation that throws: the route fails closed with `503`, never runs, and the event names the failure's type only.
- A certificate that expires while the control plane runs: it keeps serving with it until restart, and the runbook says to rotate before expiry; the next start with the expired file fails with exit code 2.
- An `upstream` declaration with no TLS actually in front: the process cannot detect it, and the runbook names it as the operator's responsibility.
- An enrollment code that expires unused: no sign-in can be attached to that principal, and recovery creates a new one.
- An admin route mapped twice, or under the data plane's traffic listener: refused at start.

## Requirements *(mandatory)*

### Functional Requirements

**Installation state**

- **FR-001**: The installation state MUST be one operator-scope record in the `identity` module's schema `isk_identity`, in operator tables only, read and written only in operator-scope units, under persistence contract version 4. A database with no record MUST be treated as `uninitialized`.
- **FR-002**: The state MUST move only from `uninitialized` to `initialized`, and only through a successful bootstrap redemption (FR-008). Nothing in this spec MAY return an installation to `uninitialized`.

**Tokens**

- **FR-003**: A bootstrap token MUST be issued only while the installation is `uninitialized`, and a recovery token only while it is `initialized`, through the control-plane executable's `bootstrap issue` and `bootstrap recover` commands respectively, each failing otherwise with its registered `operator.*` code and its own exit code. Each command MUST print the token, and nothing else, once to standard output, run no composition start-up, bind no listener, and need no migration login.
- **FR-004**: A token's secret MUST have at least 256 bits from a cryptographically secure source. Only its hash MAY be stored, with its non-secret identifier, purpose, expiry, and consumed and superseded markers. Verification MUST compare in constant time.
- **FR-005**: A token MUST expire after `operator.bootstrap.tokenLifetime` (default 1 hour, from 5 minutes to 24 hours), measured by the database's clock.
- **FR-006**: Issuing a token MUST supersede any unconsumed token of the same purpose in the same commit, so at most one is active per purpose.
- **FR-007**: A token's plaintext MUST never appear in a log, trace, metric, error, readiness answer, audit record, configuration document, or database row, and MUST be shown to the operator once, only on the issuing command's standard output.

**Redemption**

- **FR-008**: A bootstrap redemption MUST, in one unit of work, consume the token only if it is unconsumed, unexpired, not superseded, of purpose `bootstrap`, and the installation is `uninitialized`, and then create the operator principal, its `system_operator` grant, the success audit record, and the `initialized` state, committing all together or nothing.
- **FR-009**: Of any number of concurrent redemptions of one token, across any number of control-plane processes sharing one database, exactly one MUST succeed. A consumed token MUST never succeed again.
- **FR-010**: Every failed redemption, for any reason, MUST answer with the same status, code, and body shape, and the reason MUST appear only in the audit record and the event as a reason class.
- **FR-011**: A successful redemption MUST return, once, the new principal's ID and one enrollment code bound to it, created in the same commit, and nothing else.
- **FR-011a**: An enrollment code MUST have the token form and hash-only storage of FR-004, purpose `enrollment`, and a lifetime of `operator.enrollment.codeLifetime` (default 24 hours, from 1 hour to 7 days). An enrollment port MUST let a consumer consume it exactly once, atomically, only for its bound principal, writing an audit record. This spec MUST ship no production consumer, and its plaintext MUST never appear where FR-007 forbids a token's.
- **FR-012**: The redemption routes MUST accept only `application/json` bodies of at most 4 KiB, and MUST answer `429` beyond `operator.bootstrap.attemptsPerMinute` attempts per process per minute (default 10, from 1 to 600), with no database read.

**Principal, grant, and privilege checks**

- **FR-013**: The operator principal MUST have an opaque ID of the `principal` resource type, registered with `since: 7`, and MUST be stored separately from any identity-provider subject or sign-in method.
- **FR-014**: This spec MUST define exactly one role, `system_operator`, granted only at the operator scope. A `system_operator` grant MUST NOT give access to any tenant row.
- **FR-015**: An operator authorization port MUST decide each admin request from the resolved principal, the route's declared action, and the stored grants, and MUST deny when the principal is unresolved, unknown, or lacks the grant, and fail closed with `503` when the grants cannot be read.
- **FR-016**: Every admin route MUST declare either the operator action it requires or its entry on the anonymous inventory (FR-020). A route declaring neither MUST fail the control plane's start with its registered code, with event and exit code as the container contract revision records.
- **FR-017**: Authentication MUST reach admin routes only through a principal-resolution port. This spec MUST ship no production implementation of it, and with none composed every protected admin route MUST answer `401` with `identity.unauthenticated` on an initialized installation.

**Uninitialized gate**

- **FR-018**: Before any authentication runs, every admin route except the bootstrap and recovery redemptions MUST answer `403` with the uninitialized code while the installation is `uninitialized`, whatever the request carries, and MUST change nothing.
- **FR-019**: When the installation state cannot be read, every admin route, the redemptions included, MUST answer `503` with the unreadable-state code and change nothing. A process MAY keep an `initialized` answer once read, and MUST NOT keep an `uninitialized` or unknown answer.

**Unauthenticated endpoints and health**

- **FR-020**: The `operator-security` contract MUST list every endpoint reachable without authentication, per process and listener, and an automated test MUST fail on any reachable anonymous endpoint of the data plane, the control plane, or the worker that is not listed, naming its process, listener, method, and route.
- **FR-021**: The health endpoints MUST stay unauthenticated, on the health listener only, on all three processes, with their bodies as container contract version 2 records them. The control plane's traffic listener MUST serve no health endpoint, and the readiness answers MUST NOT include the installation state.
- **FR-022**: Container contract version 3 MUST give each traffic listener a transport mode, `listener` (in-process TLS from read-only mounted PEM certificate chain and private key files, read once at start, TLS 1.2 minimum), `upstream` (an explicit declaration that TLS is terminated in front), or `none` (plain HTTP, development-only).
- **FR-022a**: Outside `development`, the data plane and the control plane MUST each refuse to start, with the registered code, exit code 2, and no listener bound, unless its traffic mode is `listener` or `upstream`. A missing, unreadable, malformed, expired, or mismatched certificate or key file MUST fail start with exit code 2, naming the field and never the file's path, content, or passphrase.
- **FR-022b**: The data plane's routes and readiness answers MUST be unchanged by FR-022a. The health listeners of the data plane, the control plane, and the worker MUST stay plain HTTP, and the contracts MUST state that they are never published beyond the orchestrator's private network. The `operator-security` and container contracts MUST record ISK-0007's AV-5 as settled.

**Recovery**

- **FR-023**: A recovery redemption MUST, in one unit of work, consume a `recovery` token under the same conditions as FR-008 except that the installation is `initialized`, and create a new operator principal with the `system_operator` grant and the audit record. It MUST NOT read, change, or remove any other principal, grant, or sign-in method.
- **FR-024**: A token of one purpose MUST fail with the uniform failure at the other purpose's route, whatever the installation state, because neither redemption route is behind the uninitialized gate (FR-018).
- **FR-025**: The `operator-security` contract MUST mark the recovery path as a draft whose minimum policy awaits human approval (AV-4), and `docs/05`'s Decision status MUST still list the admin recovery policy as needing approval.

**Audit hooks**

- **FR-026**: An audit-recording port MUST append each record in the caller's own unit of work, in the `audit` module's schema `isk_audit`, operator tables only, under persistence contract version 4, so a record exists if and only if its change committed.
- **FR-027**: Each record MUST hold an opaque `audit` resource ID, the time, the actor, the target principal when there is one, the action, the scope, the result, a reason class from the contract's fixed list, the configuration revision, and the correlation ID, and MUST hold no token, hash, network address, request body, or configuration value.
- **FR-028**: Records MUST be written for token issued, token superseded, bootstrap redemption succeeded, recovery token issued, and recovery redemption succeeded, and for a failed bootstrap or recovery redemption whose identifier names a stored token (wrong secret, expired, consumed, superseded, or wrong purpose). A failed redemption whose identifier names no stored token, or whose token is malformed, and denied admin requests MUST NOT write durable records, and are counted only by the metric and the event.
- **FR-029**: The runtime login MUST be able to insert and read audit records and MUST NOT be able to update or delete them.

**Composition, embedding, and planes**

- **FR-030**: One registration method in `Iskender.Management` MUST add the operator-security module, requiring the `control-plane` module, so that it fails start with `hosting.registration_missing` under `AddIskender(...)` or `AddIskenderWorker(...)`, and one mapping method MUST map the admin routes under a host-chosen prefix. Both MUST be embeddable and documented in `docs/04`'s embedding guide.
- **FR-031**: Container contract version 3 MUST make the control plane bind its traffic listener and serve only the admin routes on it, add the `bootstrap issue` and `bootstrap recover` commands with new exit codes for a state that refuses them, keep every existing exit code's meaning, and state that the worker composes no service this spec adds and binds no traffic listener. The data plane's routes and readiness MUST be unchanged, and it MUST gain only the transport mode rule of FR-022a.
- **FR-032**: The `migrate` command's migration set MUST include the `identity` and `audit` modules, and a `migrate` run MUST issue no token and serve no route.
- **FR-033**: The registration MUST register the ISK-0003 namespace `operator` with `bootstrap.tokenLifetime`, `bootstrap.attemptsPerMinute`, and `enrollment.codeLifetime`. The data plane and the worker MUST reject an `operator` section with `config.namespace.unknown`.

**Developer topology**

- **FR-034**: Local environment contract version 3 MUST publish the control plane's traffic port on `127.0.0.1` only, default host port 18090, overridable by `ISKENDER_LOCAL_CONTROL_PLANE_PORT`, and MUST publish no health port and nothing of the worker.
- **FR-035**: The developer topology MUST provide `local/scripts/bootstrap.sh`, with `issue` and `recover`, which runs the control-plane image's `bootstrap` command once in a container removed when it exits, and shows the token only in the developer's terminal. Each plane's development overlay MUST set its traffic mode to `none`, and each base document MUST still validate alone and declare a mode valid outside `development`. No tracked file MAY hold a token, an enrollment code, or a fixed value usable as one.
- **FR-036**: The smoke script MUST assert the data-plane, control-plane, and worker readiness answers byte for byte as version 2 records them, and, before ISK-0021, that a redemption and a protected admin route each answer `503` with the unreadable-state code and that the token script exits 5.

**Diagnostics**

- **FR-037**: Events MUST be written under the category `Iskender.Identity` with IDs from 9001, and metrics and activities from `Iskender.Identity`, for redemption attempts and outcomes, gate denials, authentication and privilege denials, tokens issued and superseded, and route-declaration failures. Fields and labels MUST be limited to action, outcome, reason class, and route template.
- **FR-038**: No log, trace, metric, error, audit record, or readiness answer MAY hold a token, a hash, a principal ID, a network address, or a configuration value. The container entry points MUST subscribe to `Iskender.Identity`.

**Contracts, compatibility, and documentation**

- **FR-039**: This spec MUST publish the `operator-security` contract, version 1, and the revisions container version 3, hosting version 4, local environment version 3, persistence version 4, and, where TR-12 needs it, module dependency rules version 6, in its `contracts/` directory, each recording its change in its table, and repoint the links in `docs/03`, `docs/04`, and `docs/05`.
- **FR-040**: The `operator.*` codes and the `principal` resource type MUST be added with `since: 7`, raising `core-contracts` from 6 to 7 with compatible changes only, and the compatibility harness MUST pass. No public member of `Iskender.Hosting` MAY change.
- **FR-041**: `docs/04` MUST gain an operator bootstrap runbook: issuing and redeeming the token, recovery, the TLS assumptions, what to publish and what never to publish, the audit records, the diagnostics, and the known limits. `docs/05` MUST link the `operator-security` contract from its control-plane identity and authorization sections.
- **FR-042**: Every port and adapter type this spec adds MUST name ISK-0010 as its owning spec in its XML comment.

**Test fixture**

- **FR-043**: The operator fixture MUST run redemptions from at least 50 concurrent callers across at least two control-plane hosts sharing one database, with the attempt limit set so that no attempt is limited, test the limit separately, resolve test principals through a test-only implementation that no production project references, make the state store unreachable, and enumerate every endpoint of each process for FR-020. Tests that need a container runtime MUST follow ISK-0005's rule.

### Negative Security and Failure Scenarios

| Threat or failure | Required behavior | Covered by |
|---|---|---|
| An attacker reaches a fresh control plane first | Every admin route but the two redemptions denies, and each redemption needs its token | FR-018, User Story 1 |
| A forged or stolen credential on an uninitialized installation | Denied by the gate before authentication runs | FR-018, User Story 1 |
| Reuse of a redeemed token | Uniform failure, nothing changes | FR-009, User Story 2 |
| Two operators, or an operator and an attacker, redeem at once | Exactly one succeeds | FR-009, User Story 2 |
| Guessing the token | 256-bit secret, per-process attempt limit | FR-004, FR-012 |
| Learning why a redemption failed | One uniform answer | FR-010, User Story 2 |
| A token in logs, telemetry, errors, or the database | Never written; hash only | FR-004, FR-007, User Story 2 |
| A stale or leaked older token | Superseded by the newer one, expired after its lifetime | FR-005, FR-006 |
| A route added without protection | Start fails | FR-016, User Story 1 |
| An unreviewed anonymous endpoint | Inventory test fails | FR-020, User Story 4 |
| The grant store or state store is unreachable | Fail closed with `503` | FR-015, FR-019, User Story 3 |
| Authorization inferred from a role string | Grants are read from the store for an internal principal | FR-013, FR-015 |
| The operator role reads tenant data | Not granted; the elevated operation stays reserved | FR-014 |
| The admin surface in the data plane or the worker | Refused at start | FR-030, User Story 7 |
| Health endpoints exposed on the admin listener | Not served there | FR-021, User Story 4 |
| Installation state leaked through readiness | Not included | FR-021, User Story 4 |
| Recovery used to take over another operator | Creates a new principal only, audited | FR-023, User Story 6 |
| Token-purpose confusion | Uniform failure | FR-024, User Story 6 |
| An audit record lost, invented, or edited | Same unit as the change; no update or delete for the runtime login | FR-026, FR-029, User Story 5 |
| An anonymous flood fills the audit table | Denials, and redemptions naming no stored token, are not durably recorded | FR-028, User Story 5 |
| A committed or fabricated developer token | Generated at run time, never tracked | FR-035, User Story 7 |
| The control plane or health port reachable from another machine in the developer topology | Loopback only; no health port | FR-034 |
| Plain HTTP interception of the token or enrollment code, or of later data-plane credentials | Outside `development`, each plane starts only with in-process TLS or an explicit upstream declaration; short lifetime and single use bound the rest | FR-005, FR-009, FR-022a |
| A bad or expired certificate silently downgrades to plain HTTP | Start fails, exit code 2 | FR-022a |
| The private key leaks through diagnostics | Never written; only the field is named | FR-022a |
| An enrollment code used for another principal, or twice | Consumed once, for its bound principal only | FR-011a |

### Key Entities

- **Installation state**: the operator-scope record of whether the installation is `uninitialized` or `initialized`, when, and by which principal.
- **Operator token**: a one-time, short-lived credential of purpose `bootstrap` or `recovery`, stored as a hash with its identifier, expiry, and consumed and superseded markers.
- **Enrollment code**: a one-time, hashed code bound to one operator principal, which a later sign-in spec consumes to attach the first sign-in method.
- **Traffic transport mode**: `listener`, `upstream`, or development-only `none`, per traffic listener.
- **Operator principal**: an internal identity with an opaque `principal` ID, separate from any sign-in method or identity-provider subject.
- **Operator grant**: the binding of a principal to the `system_operator` role at the operator scope.
- **Admin route declaration**: a route's required operator action, or its entry on the anonymous inventory.
- **Anonymous endpoint inventory**: the reviewed list of endpoints reachable without authentication, per process and listener.
- **Audit record**: an append-only operator-scope record of one security state change.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On an uninitialized installation, 100% of calls to every mapped admin route other than the two redemptions, with no credential, a malformed credential, and an accepted test credential, are denied, and 0 rows change.
- **SC-002**: Across at least 20 runs of 50 concurrent redemptions of one token against two control-plane hosts on a fresh database, with the attempt limit set so that no attempt is limited, exactly 1 succeeds in each run, and exactly 1 operator principal exists after each.
- **SC-003**: A redeemed, expired, superseded, wrong-secret, or wrong-purpose token succeeds 0 times, and every failure answer is identical in status, code, and body shape apart from the correlation ID.
- **SC-004**: The captured logs, traces, metrics, error bodies, readiness answers, audit records, and database rows of the whole test run hold 0 token secrets and 0 token hashes outside the token table, and the captured logs, traces, and metrics hold 0 principal IDs.
- **SC-005**: The inventory test passes for the three shipped processes, and fails for a build with one added unlisted anonymous endpoint, naming it.
- **SC-006**: Every protected admin route answers `401` without a credential, `403` for a principal without the grant, and `503` with an unreadable store, in 100% of the privilege tests.
- **SC-007**: Every security state change in the tests has exactly one audit record committed with it, a rolled-back change has 0, the runtime login's updates and deletes of audit records are refused 100% of the time, and 1,000 denied anonymous requests add 0 records.
- **SC-008**: The developer topology's five-cycle acceptance run passes with the three readiness answers byte for byte unchanged and the fail-closed admin checks of FR-036, and a search of the tracked files finds 0 tokens.
- **SC-009**: Outside `development`, a data plane or control plane with traffic mode `none`, or with a missing, malformed, expired, or mismatched certificate, fails start with exit code 2 in 100% of the tests and binds no listener; with `listener`, a TLS 1.2 or later client completes the bootstrap, and a plain-HTTP request to that listener is refused.
- **SC-010**: The compatibility harness, the architecture tests, and the 10 CI checks pass, with `core-contracts` at version 7 and no change to the `hosting` contract package.

## Assumptions

- No sign-in method exists in this spec. The first operator gains a way to sign in through ISK-0013 or ISK-0014, and service automation through ISK-0020 (AV-3).
- Before ISK-0021, no process can connect to PostgreSQL, so in every container the admin routes fail closed and the token commands exit 5. Tests run against the ISK-0004 fixture, which provides its own credentials in test projects only.
- The token's entropy, not the rate limit, defeats guessing, so a per-process limit is enough.
- The recovery draft's trust anchor is the ability to run the control plane's command with its documents and database access, which is already enough to run migrations.
- The audit records of this spec are operator-scope only. Tenant audit, retention, and tamper-evident ordering are ISK-0026's (AV-6).

## Out of Scope

- Local accounts, passwords, password resets, lockout, MFA, sessions, cookies, and CSRF (ISK-0013, ISK-0020).
- External OIDC login and principal linking (ISK-0014). A built-in OIDC server.
- Organization and project roles, memberships, project-level grants, and the role matrix (ISK-0015).
- Data-plane API keys and JWT bearer validation (ISK-0016 to ISK-0019).
- Management resource APIs and the management UI (ISK-0080, ISK-0081).
- Credential providers and secret storage (ISK-0021).
- Tenant audit, retention, and deletion (ISK-0026).
- The audited elevated operator operation over tenant data.
- Any change to CEL typed policies, plugin trust, durable conversations, financial controls, or streaming.
- Any new image, executable, or Dockerfile.
