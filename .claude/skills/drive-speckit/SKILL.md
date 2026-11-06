---
name: "drive-speckit"
description: "Advance the current Iskender spec by exactly one Spec Kit workflow stage, resuming from durable state. Use when the user runs /drive-speckit or asks to continue the spec workflow."
compatibility: "Requires the Iskender grounding manifest and an installed Spec Kit"
user-invocable: true
disable-model-invocation: false
---

# drive-speckit

Advance one spec by **exactly one stage** per invocation, then stop and print the summary. Take no stage
argument: state decides the stage. Printed stage names are informational.

Only this skill moves a spec between stages. It never reinstalls or edits Spec Kit (`.specify/`), never
edits `.iskender/grounding.yaml`, and never records a commit hash it has not verified. The constitution is the one
file under `.specify/` that may change, and only when `/review-roadmap` applies an approved amendment.

Follow the Git cadence in section 6 without being asked. Never commit to `main`, and **never merge or close a
PR**. A release tag is pushed only at `pr-merge-checkpoint`, after a human has merged the PR.

## 1. Preflight (every invocation)

Run these checks first. Any failure produces a blocked result (section 9), and no stage runs.

1. **Repository root**: `git rev-parse --show-toplevel`, from any directory or worktree. Confirm
   `.iskender/grounding.yaml` is there. Run everything else from the root.
2. **Manifest and inventory**: `bash .github/scripts/check-grounding.sh`. Take every later path from the
   manifest: `grounding.root`, `grounding.inventory`, `speckit.constitution`, `speckit.specificationsRoot`.
   Never guess a path.
3. **Constitution**: read `speckit.constitution` if it exists. It outranks `grounding.constitutionPrompt`,
   which is only a bootstrap input. Never overwrite an existing constitution without recorded human
   approval. If neither resolves, block.
4. **Spec Kit**: confirm a `.claude/skills/speckit-<stage>/SKILL.md` exists for `specify`, `clarify`, `plan`,
   `tasks`, `analyze`, `checklist`, `implement`, and `converge`. If one is missing, block. Do not install it.
5. **Worktree**: note the current branch, HEAD, and `git status --short`.
6. **Pending approvals**: read `.iskender/state/approvals.json` and `.iskender/state/proposals/` if present.
7. **Select the spec** (section 2), then **take its lock** (section 3).

## 2. Select the spec and the stage

**Spec**:

First find the records. Run `git fetch origin --prune`, then read `.iskender/state/*.state.json` from `origin/main`, the
current checkout, and every branch with an open PR (`gh pr list --state open --json headRefName`). A branch holds a
record only if it changed that file since its merge base with `origin/main`. An unchanged copy is inherited from `main`,
so ignore it:

```bash
# $ref is origin/<branch>, or HEAD for the current checkout
git diff --quiet "$(git merge-base origin/main "$ref")" "$ref" -- "$record" || echo "$ref holds $record"
```

A spec's record on a branch outranks its record on `origin/main`. If it is on another branch, switch to that branch before doing anything
else, and block if the working tree is dirty. If two open branches hold a record for one spec, block and name them.

1. A record that is not finished comes first. Finished means that the record **on `origin/main`** has `currentStage`
   of `pr-merge-checkpoint` and `stageStatus` of `complete`. A complete record that exists only on a branch is waiting
   for a human merge. If several are unfinished, take the lowest ID.
2. Otherwise take the lowest-numbered `ISK-xxxx` in the `origin/main` inventory that has no state record, has a final
   merge commit of `null`, has its approved prompt, and has every dependency satisfied. A dependency is
   satisfied when its entry records a final merge commit that passes
   `git merge-base --is-ancestor <hash> origin/main`.

Never choose a spec from the current directory. If nothing is eligible, say so in the summary and stop.

**Stage**, in dependency order:

`bootstrap/detect` → `specify` → `clarify` → `plan` → `tasks` → `analyze` → `checklist` → `implement` →
`converge` → `roadmap-review` → `pr-merge-checkpoint`

| Record | Action |
|---|---|
| None | Run `bootstrap/detect` |
| `stateVersion` newer than 1 | Block: the owner must update this skill. Read older versions without loss |
| `stageStatus` is `complete` at `pr-merge-checkpoint` | Run `pr-merge-checkpoint`, which reports the records PR |
| `stageStatus` is `complete` | Verify (below), then enter the next stage |
| `stageStatus` is `pending` or `in_progress` | Verify (below), then run or resume `currentStage` |
| `stageStatus` is `blocked` | Verify, and re-check the recorded blocker. If it still holds, repeat the blocked result. If not, resume `currentStage` |

**Verify before resuming or entering a stage**: every `artifactPaths` entry exists, `gitEvidence.commit` exists and is HEAD or an
ancestor of it, and every `sourceHashes` value still matches. Any mismatch blocks. Do not guess. A squash or rebase merge
of the spec's PR causes this mismatch, so name rule R8 and its recovery steps in `docs/18-branch-protection.md` in the
block's `Unblock`. A mismatch on a `complete` record leaves that record unchanged, `stageStatus` included, so the next
invocation verifies it again and enters the next stage, and the finished stage does not run again.

The one exception is a source changed through `/review-roadmap`. If a proposal with `appliedAt` set lists the current
hash under the same key in its `resultingHashes`, update `sourceHashes` and append
`{ source, from, to, proposalId, timestamp }` to `sourceHashHistory` in the record. For `approvedPrompt`, the proposal's
`affectedSpecIds` must also name this spec. With no such proposal, block.

Hash sources with line endings normalized, so a clone with different line endings still matches:

```bash
tr -d '\r' < "$file" | sha256sum | cut -d' ' -f1
```

`sourceHashes` holds `manifest`, `approvedPrompt`, and `constitution`, each prefixed `sha256:`.

## 3. State and lock

**State files** live in `.iskender/state/` and are committed, except `*.lock` and `*.tmp`:

- `<specId>.state.json`: `specs/0000-repo-bootstrap/contracts/workflow-state.schema.md`
- `<specId>.converge.json`: `contracts/convergence-ledger.schema.md` in the same directory
- `approvals.json` and `proposals/<proposalId>.json`: owned by `/review-roadmap`

**Every state write is atomic**. Write the whole record to a temp file, then rename it:

```bash
# After writing .iskender/state/<specId>.state.json.tmp
sync; mv -f ".iskender/state/$spec.state.json.tmp" ".iskender/state/$spec.state.json"
```

Persist after every task and every repair attempt. `runnerId` is `host:user:startedAtUtc`, where
`startedAtUtc` is when this invocation began. `gitEvidence` holds only real values from Git.

**Take the lock** before writing anything:

```bash
runner="$(hostname):$(id -un):$(date -u +%Y-%m-%dT%H:%M:%SZ)"
( set -o noclobber; printf '{"runnerId":"%s","timestamp":"%s"}\n' "$runner" "$(date -u +%Y-%m-%dT%H:%M:%SZ)" \
    > ".iskender/state/$spec.lock" ) 2>/dev/null
```

- **It fails**: read the lock. If its runner is on this host and its timestamp is more than 15 minutes old,
  the lock is stale: delete it, take it again, and add a `lockReclaimed` entry (previous runner, timestamp,
  reclaiming runner) to `partialFailureDiagnostics`. Otherwise **block and exit**, naming the owner and how
  to verify it. Never wait, retry in a loop, or steal a live lock. **Write nothing and release nothing**: the record
  and the lock belong to their owner.
- **While holding it**: rewrite the timestamp on every state write and before any long step, and split long
  steps so that no gap between refreshes approaches 15 minutes.
- **Release it**: delete the lock as the last action of every invocation, including blocked ones.

## 4. Stages

**Be on the spec's branch before the first write** (section 6). If that means switching or creating a branch and
the working tree is dirty, block rather than carry changes across branches.

On entering a stage, set `currentStage`, `stageStatus: in_progress`, `enteredAt`, and `runnerId`, and write
the record. Also set the entry's `Workflow status` value in the inventory:

| Stage | Status |
|---|---|
| `specify` | `specifying` |
| `clarify` | `clarified` |
| `plan` | `planning` |
| `tasks` | `tasking` |
| `analyze`, `checklist` | `analyzing` |
| `implement` | `implementing` |
| `converge` | `converging` |
| `roadmap-review` | `roadmap-review` |
| `pr-merge-checkpoint` | `pr-ready`, then `merged` in the records-only PR |

Change only that value in the entry. **Commit and push these entry writes before running the stage**, so that it
starts from a clean tree and its own changes can be checked alone. On leaving a stage, commit and push its outputs
(section 6), then set
`stageStatus: complete`, add the outputs to `artifactPaths`, set `gitEvidence` to that commit and the branch,
write the record, and commit and push the record. Spec Kit stages run through the Skill tool as
`speckit-<stage>`, and every Spec Kit script they run needs `SPECIFY_FEATURE_DIRECTORY` set to the spec's directory, taken
from `artifactPaths`. Shell variables do not persist, so set it on each command. `.specify/feature.json` is ignored by Git
and may be missing or point at another spec, so never rely on it.

### `bootstrap/detect`

Runs only when the spec has no record. Create or switch to the spec's branch (section 6), create the record,
starting at the first stage whose output is missing, commit and push it, then stop. Do not repeat work that already
exists, and do not invent evidence for it.

| First match in the spec's directory | Start at |
|---|---|
| No `spec.md` | `specify` |
| `spec.md` has no `## Clarifications` section | `clarify` |
| No `plan.md` | `plan` |
| No `tasks.md` | `tasks` |
| No `checklists/` beyond `requirements.md` | `analyze` |
| A checklist has an unchecked item | `checklist` |
| `tasks.md` has an unchecked task | `implement` |
| Otherwise | `converge` |

Record `stageStatus: pending`, the source hashes, the existing artifacts, and the current commit.

### `specify`

1. Extract the approved prompt. This prints the fenced body, byte-for-byte, and nothing else:

   ```bash
   awk -v id="$ISK_ID" '
     inside && /^```\r?$/ { exit }
     inside { print; next }
     /^## / { line = $0; sub(/\r$/, "", line); entry = (line == "## " id || index(line, "## " id " ") == 1); labelled = 0; next }
     entry && /^\*\*Starting `\/speckit\.specify` prompt/ { labelled = 1; next }
     entry && labelled && /^```text\r?$/ { inside = 1; next }
   ' "$INVENTORY"
   ```

2. **Empty output blocks** the stage. Never write, regenerate, or substitute a prompt, or derive one from the
   title. The owner fixes the inventory through `/review-roadmap`.
3. Run `speckit-specify`, passing the extracted text unchanged, with `SPECIFY_FEATURE_DIRECTORY` set to
   `<specificationsRoot>/<NNNN>-<slug>` so that the directory number matches the spec ID.

### `clarify`, `plan`, `tasks`, `analyze`

Run the matching Spec Kit skill for the spec. If it needs the user's answer, ask, and stay in the stage until
the answer arrives. If `analyze` finds a problem that needs a human decision, block on it.

### `checklist`

1. Run `speckit-checklist` once per risk domain that the spec warrants.
2. Perform the review yourself against the spec and design artifacts, and mark each satisfied item `[x]`.
3. Fix gaps you can settle from the approved docs in the spec's own artifacts, then re-review.
4. Raise only the items that need human input. They **block** the stage. Never tick an item to get past it.

This stage reviews the quality of the requirements, not the implementation, and changes no code.

### `implement`

Run `speckit-implement` for all remaining tasks, skipping tasks already marked complete or at or before
`lastConfirmedTask`. Keep going until they are finished, a real blocker occurs, or the user must answer. Do not
return after each task. After each task, tick it in `tasks.md`, update `lastConfirmedTask`, write the record, and
commit and push (section 6). Apply section 5 to failures. When finished, stop: **never start `converge` from here**.

### `converge`

1. If the ledger has `passComplete: false`, an earlier run of this pass was interrupted: keep `passNumber` and redo
   the pass. Otherwise increment `passNumber` and set `passComplete: false`. Write the ledger first, so that a crash
   cannot lose the pass, and a resume cannot add one.
2. Run `speckit-converge`. Afterwards, `git status --short` must show changes only in the spec's `tasks.md` and
   `.iskender/state/`. Anything else is a violation: block.
3. Record each finding, keeping the same `CONV-<specId>-NNN` ID across passes for the same issue. Map severity
   `CRITICAL/HIGH/MEDIUM/LOW` to `Critical/High/Medium/Low`. Mark earlier findings that no longer appear as
   `remediated`.
4. Never lower a severity without a reason and the Spec Kit maintainer as `changedBy`. Each severity change
   appends `{ pass, from, to, changedBy, reason, timestamp }` to `severityHistory`.
5. Decide the outcome, then set `passComplete: true` and write the ledger:

| Findings | Outcome | Next stage |
|---|---|---|
| None, on any pass | Success | `roadmap-review` |
| Any, on passes 1–3 | All become `scheduled`, including Low | `implement`, in a separate invocation |
| Any Medium or above, on any pass | Not converged | `implement`, or a block if its budget is spent |
| Low only, on pass 4 or later | Ask the owner to accept each one, recording `{ owner, rationale, evidence, timestamp }`. Success if all are accepted and no release gate prohibits it | `roadmap-review`, otherwise `implement` |

When returning to `implement`, set `currentStage: implement` and `stageStatus: pending`. A finding that survives
a remediation pass counts as a failed repair under section 5, with the finding ID as its `taskId`. Never fix
code here, and never start `roadmap-review` from here.

### `roadmap-review`

Eligible only after successful convergence. Run `/review-roadmap`. If a material proposal is awaiting a decision,
block, naming the proposal's owner and its `affectedSpecIds`. It completes when no proposal is pending and every approved one has
been applied.

### `pr-merge-checkpoint`

This terminal stage may take several invocations. Each one does the first step that applies, then stops. "The PR"
below is the spec's latest `chore/<NNNN>-post-merge` PR if it has one (section 6), otherwise its feature PR, so the tag
and the recorded merge commit include any post-merge fixes.

If the PR has already merged when a `complete` record at `roadmap-review` enters this stage, write nothing on entry: its
branch is finished, with no open PR to carry a commit. Go straight to step 5, and the records PR carries the stage change.

1. **Records PR exists** (`gh pr list --head chore/<NNNN>-record-merge --state all`): if it is open, report its URL and
   its checks, and stop. **Never merge or close it, and do not create the branch again.** If it closed unmerged, block,
   and ask the owner what to do.
2. **PR open, notes not final**: confirm everything is committed and pushed, then update the PR's notes so they are
   complete and describe the final state of the branch. Stay `in_progress`, and report that it awaits a human merge.
3. **PR open, notes final, not merged**: report its URL and its checks, and that it must use a merge commit (rule R8 in
   `docs/18-branch-protection.md`), and stop. **Never merge or close it.**
4. **PR closed unmerged**: block, and ask the owner what to do.
5. **PR merged**:
   - Get the real hash with `gh pr view <n> --json mergeCommit`, `git fetch origin`, then confirm it with
     `git merge-base --is-ancestor <hash> origin/main`. If this fails, block. Never fabricate a hash.
   - Verify that the `ci` check passed, including the documentation merge gate, and that required reviews are
     present. A review rule that `docs/18-branch-protection.md` lists as pending is reported as pending, not as
     passed.
   - Tag the verified merge commit, which starts the Release workflow. The spec number is the patch segment, with
     leading zeros dropped, so ISK-0012 is `v0.0.12-alpha`:

     ```bash
     tag="v0.0.$((10#$NNNN))-alpha"
     git tag -a "$tag" "$hash" -m "ISK-$NNNN" && git push origin "$tag"
     ```

     If the tag already exists on that commit, skip this step. If it exists on another commit, block. **Never move or
     delete a tag.** Report the release workflow's result. If it fails, block, and name the failed run.
   - Create `chore/<NNNN>-record-merge` from `origin/main`. Change **only** these records: in the inventory entry,
     `Workflow status` becomes `merged` and `Final merge commit` becomes the short hash; in the state record,
     `currentStage` becomes `pr-merge-checkpoint`, `gitEvidence` becomes the full hash, and `stageStatus` becomes
     `complete`. No code changes.
   - Open that PR. **The agent never merges or closes it.** The spec is finished once a human merges it.

## 5. Repair budgets

On any failure, compute the **problem fingerprint**, the same across attempts and restarts:

`specId/taskId/stage + normalized error class + dependency context + log hash`

Classify each attempt mechanically. Before the next attempt, append it to `perProblem[fingerprint].attempts` as
`{ command, expectedObservation, result, classification, timestamp }`, so the comparison and the blocker's `Attempted`
field survive a restart:

- **Meaningful**: its command or action differs from the last attempt in that list, **and** it has a recorded expected
  observation.
- **No-progress**: anything else, such as re-running the same command unchanged.

| Counter | Changes | Stop at |
|---|---|---|
| `perProblem[fingerprint].meaningfulFailures` | +1 on a failed meaningful attempt | **3** |
| `runFailedRepairs` | +1 on any failed attempt | **10** |
| `consecutiveNoProgress` | +1 on a no-progress attempt, back to 0 on a meaningful one | **2** |

A **run** starts when the record enters a stage and ends when it moves to another stage. Resuming a stage, or
re-invoking after a block, continues the same run. `runFailedRepairs` returns to 0 only when a run ends. Per-problem
counts and `consecutiveNoProgress` carry across runs.

Read counters from the record. A restart never resets them. Any other reset requires a `resetJustifications` entry from the
Spec Kit maintainer with `{ authorizer, timestamp, newHypothesis, externallyChangedCause }`. Never write one on your
own authority.

At a limit, **stop** with a blocked result that says who may authorize escalation. Never skip acceptance, disable
tests, or alter approved requirements to make progress.

## 6. Git cadence

The spec's branch is `feature/<NNNN>-<slug>`, created from `origin/main` with the slug taken from the inventory title.
Other work uses a `chore/` or `bugfix/` branch.

**If the feature PR has already merged** (`gh pr list --head feature/<spec directory name> --state merged`), its
branch is finished. Do any remaining stage work on `chore/<NNNN>-post-merge`, branched from `origin/main`, with its
own PR.

**If that PR has merged too, and stage work remains**, restart the same branch from `origin/main`, with a new PR. Block
if the working tree is dirty or `git log origin/main..<branch>` lists a commit, so that no work is left behind. Otherwise:

```bash
git switch -C "chore/$NNNN-post-merge" origin/main
```

The old tip is an ancestor of `origin/main`, because rule R8 keeps merge commits, so the next push is a fast-forward.
Only the branch's latest PR is recorded and tagged. An earlier merged one gets neither. This does not apply at
`pr-merge-checkpoint`, where a merged latest PR is the one that stage records.

Then:

1. **Commit as work is done**: after each task in `implement`, and at the end of every other stage. Don't batch.
   Stage explicit paths, and never commit a `*.lock` or `*.tmp` file. End each message with
   `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
2. **Push after every commit.** A rejected push is a blocked result. Never force-push.
3. **Open the PR right after the first push**, against `main`, unless the branch's latest PR is open. Find it with
   `gh pr list --head <branch> --state all --json number,state --jq 'max_by(.number)'`. No PR or a merged one means open
   a new PR. A closed, unmerged one is a blocked result: ask the owner what to do. End its notes with
   `🤖 Generated with [Claude Code](https://claude.com/claude-code)`.
4. **Keep the PR notes current** as stages finish. They must be complete before the PR merges.

## 7. Authoritative files

Material changes go through `/review-roadmap`. Direct edits are limited to the spec's own in-progress `spec.md` and
`plan.md` made by its Spec Kit stages before its PR merges, and the inventory status and merge-commit values above,
when they record real evidence and alter no material category.

## 8. Summary (every invocation)

```text
drive-speckit (<stage>)
Spec: ISK-<id> — <title from inventory>
Completed this invocation: <work done + evidence>
Status: <OK | BLOCKED (…)>
Next: <next stage/action>
Run /drive-speckit again to <continue/resume>.
```

Omit the last line only when no re-invocation applies, such as when no spec is eligible.

## 9. Blocked results

If this invocation holds the lock, set `stageStatus: blocked`, record `partialFailureDiagnostics`, and release the
lock. A `complete` record stays unchanged (section 2), so only release the lock. If it does not hold the lock, because preflight failed first or another runner owns it, write nothing. Either
way, add **all** of these to the summary:

```text
Blocked: <stage / spec / task>
Reason: <precise cause; if unknown, write "Root cause unknown" and add the diagnostic commands to run>
Evidence: <error text, log hash, or test ID>
Attempted: <hypotheses and commands, with outcomes and budget counts>
Unblock: <exact owner action and verification command>
Owner: <role from the constitution's Quality Gates & Review Owners table>
Resumes: <stage/task that continues afterward>
```

Role holders are listed in `CONTRIBUTING.md`. A bare "blocked" is never acceptable.
