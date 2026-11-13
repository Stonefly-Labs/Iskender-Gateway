# Contributing to Iskender Gateway

## Branches and pull requests

- All work happens on a `feature/<topic>`, `chore/<topic>`, or `bugfix/<topic>` branch off `main`.
- Never commit or push directly to `main`. Merge through a pull request with passing CI and review.
- Commit on your branch as each task is implemented, not in one batch at the end.
- Push after every commit.
- Open the pull request right after the first push.
- Keep the pull request notes current, and make sure they are complete before it merges.
- Agents follow these steps without being asked, but never merge or close a pull request.
- The repository rules, and which are currently applied, are in
  [docs/18-branch-protection.md](docs/18-branch-protection.md).

## Before you push

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

These need no cloud credentials. `dotnet restore` needs a NuGet feed or a primed package cache. The PostgreSQL and Redis integration tests
need a container runtime and pull digest-pinned images on their first run. Without one they are skipped, unless `CI` is `true`, when they fail.

## Documentation merge gate

A pull request that changes any of these paths must also change a file under `docs/`, or the root
`README.md` or `CLAUDE.md`:

- `src/Iskender.Core.Contracts/**`, `src/Iskender.Providers.Abstractions/**`, `src/Iskender.Policy.Sdk/**`
- `src/Iskender.Hosting/**`
- `src/Iskender.Gateway.Host/**`, `src/Iskender.ControlPlane.Host/**`
- `.github/workflows/**`, `.github/scripts/**`
- `.iskender/grounding.yaml`

Deleting a doc doesn't count. The master inventory, `docs/17-master-spec-inventory.md`, is its own documentation: a pull
request that changes only that file passes, but it doesn't count as the documentation change for anything else.

If no documentation needs to change, a CODEOWNER of every affected path applies the `docs-not-required`
label, and you explain why under the `## Docs-not-required justification` heading in the PR description.
The gate fails if the label is present with no justification, or if someone else applied it, even if the
pull request also changes documentation. Changes outside those paths are never blocked. To try a scenario locally, see the variables at the top of
`.github/scripts/docs-gate.sh`.

## Spec-driven workflow

Features follow the specs in [docs/17-master-spec-inventory.md](docs/17-master-spec-inventory.md), driven
by `/drive-speckit`, one stage per invocation. Roadmap changes go through `/review-roadmap`: material
changes need recorded human approval before any authoritative file changes.

Do not reinstall or edit Spec Kit (`.specify/`). The one exception is the constitution,
`.specify/memory/constitution.md`, which, like `.iskender/grounding.yaml`, changes only through that approval process.

## Review owners

The constitution names a review owner for each principle in its
[Quality Gates & Review Owners](.specify/memory/constitution.md#quality-gates--review-owners) table. This
table records who holds each role. Every role is currently held by the sole maintainer, with no delegates.

| Role | Holder |
|---|---|
| Product/Charter owner | @christopherhouse |
| Architecture (ADR) owner | @christopherhouse |
| Security owner | @christopherhouse |
| Privacy/Operations owner | @christopherhouse |
| Performance owner | @christopherhouse |
| QA | @christopherhouse |
| Spec Kit maintainer | @christopherhouse |
| Human approver for Roadmap Steward proposals | @christopherhouse |

## Secure development standards

- **Least privilege**: workflows, tokens, and services get only the permissions they use.
- **Secret scanning**: never commit credentials. CI scans the full history with Gitleaks. Verified false
  positives go in `.gitleaksignore` by exact fingerprint; rotate anything real.
- **Dependency integrity**: package versions are pinned centrally and restored in locked mode, CI actions
  are pinned to commit SHAs, and CI fails on known vulnerable packages or if it cannot run that check.
- **No untrusted plugin execution**: code from outside the repository does not run without operator
  approval.
- **Threat review**: every new public endpoint, identity flow, or serialization format gets an explicit
  threat review in its pull request.

Report vulnerabilities as described in [SECURITY.md](SECURITY.md).

## Releases and versioning

- SDK and API surfaces follow [Semantic Versioning](https://semver.org/).
- A change to a public contract must pass the compatibility harness, `tests/Iskender.Contracts.Compatibility.Tests`, or
  be declared in its break record, as the
  [public contracts and versioning contract](specs/0002-public-contracts-versioning-discipline/contracts/public-contracts-and-versioning.md)
  states.
- A release is cut by a version tag on `main`. `.github/workflows/release.yml` then builds, tests, and packs at
  that version, and publishes a GitHub release with the NuGet packages and SBOM attached. Nothing is pushed to
  a NuGet feed yet.
- The current rule is `v0.0.<spec number>-alpha`, with leading zeros dropped: ISK-0000 is `v0.0.0-alpha`, and
  ISK-0012 is `v0.0.12-alpha`. `/drive-speckit` tags the merge commit after a human merges the spec's PR: its latest post-merge PR if it has one, or
  otherwise its feature PR. This
  rule will change for later milestones, and only the skill and this section need to change with it.
- Never move or delete a release tag.
- Architecture decision identifiers are stable and are never reused, including after a decision is
  superseded.
- Spec IDs (`ISK-xxxx`) are immutable once published, and are never recycled.

## License

By contributing, you agree that your contributions are licensed under the [MIT License](LICENSE).
