# Contract: Mandatory Summary & Blocker Output

**Applies to**: every `/drive-speckit` invocation and every blocked result. Traces: FR-034/FR-035,
SC-008, Principle X.

## Summary (always)

```text
drive-speckit (<stage>)
Spec: ISK-<id> — <title from inventory>
Completed this invocation: <work done + evidence>
Status: <OK | BLOCKED (…)>
Next: <next stage/action>
Run /drive-speckit again to <continue/resume> when appropriate.
```

Required fields: spec ID + title, executed stage, work done, current status, next stage/action,
re-invocation instruction (FR-034). SC-008: 100% of invocations emit this.

## Blocker (every blocked result)

A blocked result MUST additionally include **all** of:

| Field | Requirement |
|---|---|
| Blocked stage/spec/task | names what is blocked |
| Precise reason | not a vague "blocked" |
| Evidence/error | log hash, test ID, or error text |
| Tried hypotheses/commands + outcomes | the repair attempts and their results |
| Exact unblock steps | concrete owner actions + verification command |
| Responsible actor | the role/owner who can unblock |
| Resume point | the stage/task that will resume afterward |

If the root cause is unknown it MUST be **labeled unknown** with diagnostic commands + evidence
collection steps — never a bare "blocked" (FR-035). SC-008: 100% of blocked results include every
field above.

## Example (format only — not an assertion of progress)

```text
drive-speckit (implement)
Spec: ISK-0000 — Repository Bootstrap & Agentic Development Framework
Completed this invocation: T012 (host project builds), T013 (arch test green)
Status: BLOCKED (task T014); reason: NetArchTest package unresolved offline
Evidence: restore log sha256:…; error NU1101
Attempted: (A) clear nuget cache → no change; (B) pin version in Directory.Packages.props → still unresolved; counts: perProblem=2/3, run=2/10
Unblock: add package to offline feed or approve network restore; verify `dotnet restore`; owner: Architecture (ADR) owner
Resumes: implement task T014 after restore succeeds.
Run /drive-speckit again after unblock.
```
