<p align="center">
  <img src="assets/iskender-logo.svg" alt="Iskender Gateway logo" width="128" height="128">
</p>

# Iskender Gateway

An MIT-licensed, AI-first gateway for enterprise platform teams, built on .NET 10 and ASP.NET Core. It can run as a container or be
embedded through an SDK.

**[Read the documentation index](docs/README.md)** for the product charter, architecture, decisions, and roadmap.

> **Status:** early bootstrap. The repository holds a compilable project skeleton and the development workflow. No gateway, provider,
> policy, or database functionality is implemented yet.

## Mission

Iskender gives platform teams one governed entry point to many AI providers, with routing, policy, identity, and observability under
their control. It is fully open source: nothing needed for the MVP's value is held back from the open-source distribution. See the
[product charter](docs/01-product-charter-and-vision.md).

## Architecture

Iskender is a modular monolith. Its boundaries are enforced by separate projects and by tests, which leaves room to deploy the data plane
and control plane independently later.

| Project | Role |
|---|---|
| `Iskender.Core.Contracts` | Versioned contract surface, with no internal dependencies |
| `Iskender.Domain` | Domain modules |
| `Iskender.Infrastructure` | Infrastructure |
| `Iskender.Providers.Abstractions` | Provider abstractions, with no adapters yet |
| `Iskender.Policy.Sdk` | Policy SDK surface, with no engine yet |
| `Iskender.Management` | Management surface |
| `Iskender.Gateway.Host` | Composition host, which no project may reference |

`tests/Iskender.Architecture.Tests` fails the test run if a reference breaks the
[approved dependency direction](specs/0000-repo-bootstrap/contracts/module-dependency-rules.md). The full design is in the
[architecture document](docs/03-domain-and-technical-architecture.md).

## Build and test

You need the .NET SDK version pinned in `global.json`. You don't need cloud credentials.

```bash
dotnet restore
dotnet build -warnaserror
dotnet test
```

`dotnet restore` needs a NuGet feed or a primed package cache. Build and test need no network after that.

## Upgrade and rollback

The skeleton has no runtime, schema, or stored data yet, so both are source-level steps. A later spec that adds any of those must add its own upgrade and rollback steps here.

**To upgrade**, pull `main` or check out a [release tag](https://github.com/Stonefly-Labs/Iskender-Gateway/releases), install the SDK that `global.json` pins, then run the build and test commands above. Check the release notes for breaking changes to the public contract and SDK packages.

**To roll back**, go back to the previous release tag. To undo a change on `main`, open a pull request with `git revert <merge commit>`. Never force-push `main`, and never move or delete a release tag. If the change also touched `.iskender/state/`, the revert restores those records too, so run `/drive-speckit` afterwards to check them against the tree.

## Working with Spec Kit

Features are built one spec at a time, from the [master spec inventory](docs/17-master-spec-inventory.md), using the installed
[GitHub Spec Kit](https://github.com/github/spec-kit) through Claude Code.

| Command | What it does |
|---|---|
| `/drive-speckit` | Advances the next spec by one stage, and resumes from saved state. Run it again for the next stage |
| `/review-roadmap` | Checks a converged spec against the roadmap and proposes exact diffs. Material changes wait for human approval |
| `/speckit-*` | The individual Spec Kit stages, which `/drive-speckit` runs for you |

Each spec's approved starting prompt comes verbatim from the inventory. Don't reinstall or edit Spec Kit.

## Future scope

A policy language and runtime, a first-party policy catalog, and a plugin SDK are planned but not built. See
[policy execution](docs/09-policy-language-and-execution.md), the [policy catalog](docs/10-first-party-policy-catalog.md), and
[plugin extensibility](docs/11-plugin-extensibility-and-sdk.md).

## Contributing

Work on a `feature/`, `chore/`, or `bugfix/` branch off `main`, and open a pull request. CI must pass, and behavior changes need matching
documentation. See [CONTRIBUTING.md](CONTRIBUTING.md). Report vulnerabilities as described in [SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE)
