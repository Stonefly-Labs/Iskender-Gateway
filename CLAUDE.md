# CLAUDE.md

## Project

Iskender Gateway is an MIT-licensed, AI-first .NET 10 / ASP.NET Core gateway. It is currently a compilable skeleton with no gateway, provider,
policy, or database code. Don't add any outside an approved spec.

## Document authority

When sources conflict, the higher one wins. Escalate the conflict rather than reinterpreting approved scope.

1. Human-approved product decisions, and later approved ADRs and roadmap changes
2. The active constitution, `.specify/memory/constitution.md`
3. Topic documents under `docs/`. `docs/17-master-spec-inventory.md` is authoritative for spec order, prompts, and status
4. The current spec, plan, and tasks, plus actual code and tests
5. Bootstrap prompts and examples, such as `docs/15-constitution-prompt.md`. These are inputs, not authority

Start reading at `docs/README.md`.

## Finding files

`.iskender/grounding.yaml`, at the repository root, is the single registry of grounding paths. Find the root with
`git rev-parse --show-toplevel`, then resolve paths through the manifest. Never guess a path or add a second registry. If the manifest fails
validation, stop.

## Branches

- `main` is protected. Never commit or push to it directly.
- Work on `feature/<topic>`, `chore/<topic>`, or `bugfix/<topic>`, off `main`.
- Commit on the current branch as each task is implemented, and push after every commit. You don't need to be asked.
- Open the PR right after the first push. Keep its notes current, and complete before it merges.
- Agents **never merge or close PRs**. That is always the maintainer's call.
- Rules, and which are currently applied, are in `docs/18-branch-protection.md`.

## Spec Kit

Spec Kit is installed already, as `.claude/skills/speckit-*`. **Never reinstall, reinitialize, or edit it.** `.specify/` stays byte-for-byte
unchanged, except for the ignored `.specify/feature.json` that its own commands write, and the constitution
(`.specify/memory/constitution.md`), which changes only through an amendment with recorded human approval.

| Command | Use |
|---|---|
| `/drive-speckit` | Advances a spec by exactly one stage, resuming from `.iskender/state/`. Takes no arguments |
| `/review-roadmap` | Produces diff-only roadmap proposals. Material changes wait for recorded human approval |
| `/speckit-*` | Individual stages, normally run through `/drive-speckit` |

ISK-0000 creates `/drive-speckit`, the Roadmap Steward agent, and `/review-roadmap`. **They don't exist before ISK-0000 is implemented.** On a
checkout without them, use the installed `/speckit-*` commands manually.

The workflow skills are plain skills. Don't build a CLI or library behind them, or write unit tests for them. Check them by using them.

## Review gates

- **CI**: the 10 checks in `.github/workflows/ci.yml` must pass.
- **Documentation gate**: changes to behavior-bearing paths need a documentation change in the same PR. See `CONTRIBUTING.md`.
- **Review owners**: the constitution names an owner per principle, and `CONTRIBUTING.md` lists who holds each role.
- **Material roadmap changes** need recorded human approval before any authoritative file changes. Never approve on a human's behalf.

## Before pushing

These run offline once packages are restored, and need no cloud credentials:

```bash
dotnet format --verify-no-changes
dotnet build -warnaserror
dotnet test
bash .github/scripts/check-grounding.sh
bash .github/scripts/check-license.sh
```

## Privacy and integrity

- Never commit secrets, tokens, customer data, or personal email addresses.
- Never fabricate progress, test results, or commit hashes. Record only real Git evidence, and leave a merge commit `null` until the PR has
  actually merged.
- Never report a repository rule as applied unless it has been verified.
- Never skip acceptance checks, disable tests, or change approved requirements to get past a failure. Report the blocker.
- Don't edit `.iskender/grounding.yaml`, the constitution, or an approved prompt without recorded human approval.
