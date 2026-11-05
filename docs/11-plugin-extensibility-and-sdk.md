# 11 — Plugin extensibility and SDK

**Authority:** approved external custom policy authoring, distribution, integrity, loading and operational safety. Engine contracts: [09](09-policy-language-and-execution.md); first-party descriptors: [10](10-first-party-policy-catalog.md).

## MVP developer workflow

```text
dotnet new iskender-policy -n Acme.Gateway.Policies
      ↓ create typed C# policy handler + options + DI registration
 dotnet test / dotnet pack
      ↓ pack-time auto-generated manifest + typed config schemas + per-file hashes
 iskender plugins inspect <artifact>
 iskender plugins validate <artifact>
 iskender plugins verify <artifact> [--trust ...]
      ↓ operator reviews artifact and emits approved lockfile
 iskender plugins lock <approved-artifacts> --output iskender.plugins.lock.yaml
      ↓ readonly mounted bundle OR approved NuGet download at startup
 gateway verifies lockfile/digests/dependencies/compatibility before loading
      ↓ runtime policy settings may change via atomic plan activation
 binary updates → rolling restart or redeployment
```

Commands above describe required behaviors; exact flags/output schema are determined by CLI specs and tested before public release. Manual manifests remain supported but must satisfy precisely the same checks.

## SDK contracts

Provide versioned NuGet SDK: typed `IPolicyHandler<TSettings>`-style interface, descriptor metadata (kind, version, legal stage/scope, schema), deterministic execution context/effects, dependency injection registration, test host/fakes, cancellation, safe diagnostic API, telemetry capability contracts and explicit runtime capability declarations. Sample plugin demonstrates CEL applicability outside handler, stable kind namespace and options validation. Built-in handlers should use same SDK where sensible; no privilege escalation by consuming first-party assemblies.

## Artifact/manifest security model

A **plugin artifact** is a closed set of files under a content-addressed install root, with a verifiable manifest. Minimum manifest: schema version; publisher and owned namespace; package ID/version; assembly entry points/handler kinds+versions; SDK/runtime compatibility; target framework/RID/platform support; dependency identities/versions; generated JSON schemas and hashes; **cryptographic file digests for every loadable/payload file**; optional signature/attestation evidence; permissions/capabilities and declared configuration surface. Canonical serialization and hashing rules must avoid ambiguity, path traversal, symlinks, ZIP-slip, duplicate file names and ambiguous case handling. Signatures enhance publisher authenticity; **hashes + operator approval lockfile are mandatory even without signatures**. Hashing a manifest itself requires an external signed reference or lockfile hash anchor rather than self-reference.

**Operator approval lockfile:** atomically generated from inspected artifacts after review; pins allowed exact package ID/version/artifact digest, manifest/schema hashes, transitive dependency versions and files, approved publisher/namespace and origin, allowed handlers, optional required signatures and compatible gateway versions. Lockfile is treated as privileged configuration: protected review, digest provenance and strict validation. Do not dynamically approve a downloaded artifact merely because it is reachable from a feed.

## Delivery modes

1. **Read-only bundle mount (first-class):** operator mounts verified plugin bundle, manifest and approved lockfile into a constrained runtime path. Loader considers only entries declared by lockfile; no blind directory sweep. A mounted unknown DLL is ignored/rejected, never loaded.
2. **Startup NuGet download (first-class):** resolve only lockfile-pinned ID/version/source/digest, download into bounded staging, verify manifest, all files, dependency closure, signatures if required and compatibility, then atomically promote; fail readiness for missing/tampered **required** plugin. No unpinned `latest` resolution at startup.
3. **Custom container image (optional):** a deployment alternative, not the only way to add handlers; embedded hosting can register approved SDK handlers subject to equivalent trust policy.

No runtime download-to-execute in request handlers. A missing optional plugin may disable its optional plan only through explicit validated policy and safe diagnostics; if a mandatory plan references it, fail readiness/activation closed.

## Loading and lifecycle

Use explicit approved descriptor registry, controlled assembly resolution with isolated dependency load contexts **only for compatibility, not security**; startup validation prevents conflicting framework/runtime dependencies. .NET **AssemblyLoadContext is not a sandbox**: in-process plugin code can access process memory, network and filesystem with host identity. Only run trusted operator-approved binaries; document code review and artifact provenance expectations. Required capabilities and DI services are allowlisted; plugin handler timeouts/cancellation protect availability best-effort but cannot forcibly make malicious in-process code safe. Provider/plugin exceptions become sanitized request errors. Managed unload is opportunistic and not a security boundary.

Configuration/settings and CEL changes may compile and activate dynamically against **already loaded policy binary versions**; new binaries, dependency graph or binary replacement require restart/rolling redeploy. Versioned plan snapshots pin handler identity until in-flight requests drain. Plan cannot reference unloaded or incompatible handler.

## Compatibility and test obligations

Version SDK ABI/API, policy descriptor and manifest schemas independently; define compatibility ranges with no accidental major-version guess. Template includes unit tests, test-host integration, schema snapshots, packaging validation and sample config. CLI verifies offline (without executing assemblies during mere inspection), provides machine-readable JSON diagnostics with failed path/hash, incompatible runtime, missing dependency, namespace collision or signature failure; **never** treat uploaded DLL inspection as trust. CI uses tampering/path-traversal/collision, dependency confusion, version skew, feed outage, read-only filesystem and missing lockfile tests.

## MVP vs later

**MVP mandatory:** public SDK, `dotnet new iskender-policy`, pack-time generation, CLI inspect/validate/verify/lock, approval lockfiles, immutable integrity pinning, read-only mounts, startup NuGet retrieval, trusted in-process loading, restart requirement for binaries. **Post-MVP:** out-of-process/isolated untrusted execution, marketplace registry, advanced signature policy/transparency integrations, live binary swap/hot reload, distributed plugin deployment orchestration. No claim that out-of-process execution is already designed/approved.

## Implementation outcomes

A developer creates one handler, runs pack successfully without writing hashes by hand, and an operator loads it on stock Iskender container from an approved read-only mount and separately from pinned NuGet startup fetch. A changed byte fails verify/readiness; a missing transitive dependency fails; disallowed namespace fails; a valid dynamic settings update activates without restart; an upgraded binary does not appear until a controlled restart. All exercises generate actionable diagnostics without loading untrusted code.
