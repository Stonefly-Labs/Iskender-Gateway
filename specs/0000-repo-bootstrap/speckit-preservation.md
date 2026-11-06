# Spec Kit Preservation Notes: ISK-0000

Spec Kit was already installed before this spec. ISK-0000 uses it as found, and does not reinstall, reinitialize, or edit it (FR-001..FR-003).

| Item | Value |
|---|---|
| Version | 1.3.0 (`.specify/init-options.json`) |
| Integration | Claude Code skills, PowerShell scripts |
| Baseline commit | `ade58b7` |
| Preservation check | `git diff --exit-code ade58b7 -- .specify` |

## Discovered commands

Installed as `.claude/skills/<name>/SKILL.md`:

`speckit-analyze`, `speckit-checklist`, `speckit-clarify`, `speckit-constitution`, `speckit-converge`, `speckit-implement`, `speckit-plan`,
`speckit-specify`, `speckit-tasks`.

`.specify/feature.json` is written by Spec Kit commands and ignored by `.specify/.gitignore`, so it is not part of the preserved tree.

## Validation results

Run on 2026-11-05 on Windows 11 and Ubuntu under WSL, against a clone of a snapshot of the uncommitted working tree on top of `ade58b7`, with no cloud credentials.

| Scenario | Check | Result |
|---|---|---|
| 1 | `dotnet restore --locked-mode`, `dotnet format --verify-no-changes`, `dotnet build -warnaserror`, `dotnet test` | Pass: 0 warnings, 19 of 19 tests |
| 1 | `git diff --exit-code ade58b7 -- .specify` | Pass: no differences |
| 1 | No `az`, `azd`, `terraform`, `bicep`, or `pulumi` in CI, scripts, or project files | Pass |
| 1 | `README.md`, `CLAUDE.md`, and the logo exist; the logo has a title and description, rendered on light and dark | Pass |
| 2 | `check-grounding.sh` on the real manifest | Pass |
| 2 | Broken copies: V1 to V7, V8 tolerated, inventory and link failures | Pass: each exits non-zero and names the rule, except V8, which warns and passes |
| 2 | Symlink escape: directory junction on Windows, file and directory symlinks on Linux | Pass: rejected as V7 |
| 3 | Prompt extraction for `ISK-0000` compared with the fenced bytes | Pass: identical, 1940 bytes |
| 3 | All 125 inventory entries yield a prompt; a removed prompt and an unknown ID yield nothing | Pass |
| 10 | `speckit.constitution` resolves to the active constitution, and the skills give it precedence over the bootstrap prompt | Pass |

Scenarios 4 to 8 are confirmed by first real use of the skills (FR-003a).

### Scenario 9 on GitHub

CI run [42159879442](https://github.com/Stonefly-Labs/Iskender-Gateway/actions/runs/42159879442) on PR 3 passed all 10 checks, including the documentation gate's API calls, SBOM generation against public NuGet, and the provenance attestation.

Not yet shown, so T104 stays open:

- **Direct push rejected:** `main` has no protection yet, so a direct push would succeed. The rule is documented as pending in `docs/18-branch-protection.md`.
- **Docs-missing PR blocked:** no real PR has hit this. Run by hand with PR 3's live labels and a simulated file list, the gate blocked as expected.
