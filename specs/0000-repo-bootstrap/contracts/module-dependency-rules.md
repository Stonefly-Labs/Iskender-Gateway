# Contract: Module Dependency Direction

**Applies to**: the `src/` skeleton; enforced by `tests/Iskender.Architecture.Tests` and CI check 5.
Traces: FR-005/FR-006, Principle II.

## Projects (all compile empty — no business logic, FR-047)

| Project | Namespace root | Role |
|---|---|---|
| `Iskender.Core.Contracts` | `Iskender.Core.Contracts` | Versioned contract surface; **no internal deps**. |
| `Iskender.Domain` | `Iskender.Domain` | Domain/modules. |
| `Iskender.Infrastructure` | `Iskender.Infrastructure` | Infrastructure. |
| `Iskender.Providers.Abstractions` | `Iskender.Providers.Abstractions` | Provider abstractions (no adapters). |
| `Iskender.Policy.Sdk` | `Iskender.Policy.Sdk` | Policy SDK surface (no engine). |
| `Iskender.Management` | `Iskender.Management` | Management surface (Next.js UI = future scope). |
| `Iskender.Gateway.Host` | `Iskender.Gateway.Host` | Composition host; **referenced by none**. |

## Approved direction (allowed references →)

```text
Core.Contracts            → (nothing internal)
Providers.Abstractions    → Core.Contracts
Policy.Sdk                → Core.Contracts
Domain                    → Core.Contracts
Infrastructure            → Domain, Core.Contracts
Management                → Core.Contracts
Gateway.Host              → (composes all of the above)
```

## Enforced rules (project-graph and NetArchTest assertions)

Each rule is asserted against the `ProjectReference` graph parsed from every `.csproj`, because the
compiler drops unused references from an empty assembly and type-level checks alone would pass without
checking anything. `NetArchTest` assertions cover the same rules at type level as real types arrive. A
test proves that a deliberate violation fails.

| # | Rule |
|---|---|
| A1 | `Core.Contracts` has **no** dependency on any other `Iskender.*` production project. |
| A2 | **No** project depends on `Iskender.Gateway.Host`. |
| A3 | `Domain`, `Providers.Abstractions`, `Policy.Sdk`, `Management` do not depend on `Infrastructure`. |
| A4 | No cyclic dependencies among `src/` projects. |

Violations fail the test (and CI). Exact edges may be refined during implementation but the
direction above is the approved baseline (Principle II gate).
