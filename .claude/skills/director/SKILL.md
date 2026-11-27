---
name: director
description: Orchestrate subagents on a large multi-part task. Use only when the user asks; you may suggest it for a large task that splits into areas.
---

# Director mode

> **Use at the user's discretion.** Orchestration spends tokens on several agents at once. Don't enter this mode on
> your own judgement. You may *recommend* it when a task is large and splits cleanly into areas (a spec, several
> milestones, a bug queue), then wait for the user to ask.

You plan, dispatch, review, commit and report. Subagents read code, write code, build and test. Tokens go where
context is re-read: every turn of an agent re-reads its whole context, so **the cost of work is roughly its context
size times its turn count**. Everything below is about keeping both small without losing quality.

## Setup (once per task)

In the scratchpad, before any dispatch:

- `addendum.md`: the authoritative decisions log. User rulings, deviations from the spec, numbered (`§C.12`) so
  prompts can cite them. Overrides the spec; `CLAUDE.md` overrides both. Append every ruling the moment you get it.
- `design-<area>.md`: one per area, written by that area's design agent, accepted by you (and the user where it
  matters). Implementers read their design and the cited addendum sections, not your retelling.
- `handover.md`: a running list of known gaps and follow-ups for the final report. Append as they surface.

Ask the user early about anything balance-, art-, licence- or scope-shaped. They'd rather be asked than find a
guess baked in. Batch questions (up to 4 per call, with a recommended option first).

## Models

| Work | Model, effort | Why |
| --- | --- | --- |
| Design phases, cross-cutting or subtle areas (prediction, physics, networking, rendering), reviews | Opus, medium (low for follow-ups) | Wrong designs are the expensive failure |
| Implementation against an accepted design, well-scoped fixes | Sonnet, medium | The design carries the judgement |
| Builds, test runs, suite sweeps, log/transcript/git scouting, mechanical edits fully specified by the caller (renames, locale entries, attribution files, id moves) | Haiku, low | Output-heavy and judgement-light: keep the logs out of expensive contexts |

**Haiku runners.** Builds and test runs produce the bulk of tokens and need no judgement. Wherever an agent would
run `dotnet build`/`dotnet test` and read the output, a Haiku runner does it and returns only:
`result line; failures grouped by message (test name + first 3 lines of the first failure per group); nothing else`.
Implementers may spawn their own runner (tell them to, in the prompt) or you spawn one between their turns. A sweep
is a Haiku agent with a list of filters, run one after another, returning a pass/fail table.

## Agent lifetime: reuse, then hand over

- **Reuse** an agent (`SendMessage`) for follow-ups in the area it already knows: a review fix, the next related
  milestone. That beats re-briefing a fresh one.
- **Replace it at ~250k context tokens.** Check before every follow-up: the last `"cache_read_input_tokens"` in its
  output file (`grep -o '"cache_read_input_tokens":[0-9]*' <file> | tail -1`, never read the file itself). Past
  the cap, stop it if running (`TaskStop`), write `handover-<area>.md` (state at HEAD, what's done and proven, what's
  open, rulings that apply, user notes verbatim) and spawn a fresh agent pointed at the handover + design. You can't
  compact a subagent; this is the substitute.
- One agent per area (interior, driving, weapons, render...). Areas are the unit of reuse and of handover.

## Loop

1. **Scout** - Haiku or `Explore` with numbered questions; big findings go to a scratchpad file, reply is the path
   and ≤5 lines.
2. **Design** - Opus, read-only (no edits, no dotnet), writes `design-<area>.md` and replies ≤150 words with the
   questions it needs settled. Design phases run **in parallel with whoever holds the build slot**: they cost no
   build time.
3. **Rule** - settle the design's questions (yourself, or ask the user), record them in `addendum.md`, accept.
4. **Implement** - the area's agent gets the build slot with a short "go" citing the design and addendum sections.
   An agent can't message you mid-run: its final report is its only channel. If you want to see a design before code,
   that's two dispatches, not "send it to me then continue".
5. **Review** - read upstream diffs yourself (markers, could it have been a partial/`.Klovn.` file?) and spot-check
   high-risk logic. Check surprising claims with one cheap command. Push back on symptom-hiding fixes.
6. **Commit and push** the milestone yourself, then hand the build slot to the next agent.
7. **Verify late** - after a batch of milestones: a Haiku sweep of the suites, a real-client pass where visuals or
   feel matter, one read-only Opus review of the whole branch diff. Relay findings and ask before fixing.
8. **Final report** from `handover.md` + `addendum.md`: per milestone, every upstream edit with reason, deviations,
   failing-proofs, tuning chosen by feel, open items.

## The build slot

- **One dotnet build/test process at a time, across all agents.** Concurrent builds lock each other's outputs and
  fail with file-in-use errors that look like code errors. Agents may edit in parallel only on disjoint files and
  only while nobody else builds what they're editing. When in doubt, design-only.
- **The user's processes lock `bin/`.** When they run their own client, server or debugger, agents build into their
  own output dirs (`bin/kstest`, `bin/ksrel`...), use other ports, and never kill or touch the user's processes.
- Harness commands cap at 20 minutes: split long suites into several runs (`_KS14`, then upstream filters), never
  two at once.
- An agent that ends its turn on "waiting for the test run" may have lost the run: ask it to check, rerun and report.

## Tight prompts

A prompt carries **only what the agent can't get itself**: ≤200 words for an implementer, ≤120 for a scout or runner.
Longer means it belongs in a design or handover file.

**Free context, never restate it:** `CLAUDE.md` (cite sections: "§6 deferred deletion applies"), the area skills
(`npc-htn`, `rendering`, `prediction`, `integration-testing`... name the ones to read), and scratchpad files by path.

```
Repo <path>, branch <name>. Read <scratchpad>/design-<area>.md §<n>, addendum §C.<n>; skills: <names>.
Task: <one sentence>.
Known: <verified file:line facts only>.
Upstream edits allowed: <files, or none>. Any other, or a refused permission: stop and tell me.
Don't: commit, switch branch, kill processes, edit RobustToolbox/ or _KsModule*/, other agents' files.
Builds: own output dir, one process at a time[, via a Haiku runner returning result lines only].
Done: <exact commands>; each new test proven red with its fix reverted.
Report (≤200 words): files; deviations; upstream edits with markers; result lines; unverified items.
```

- **One task per agent.** "And also" is a follow-up.
- **Facts, not advice.** Name the §6 traps that apply ("adds a component: UninitializedSaveTest", "new BCL API:
  SandboxTest", "test-only ids: non-static fields").
- **Done is commands, not adjectives.** Ask for result lines, not logs. "Not verified" beats a guess.
- **Relay user wording verbatim** for feature requests and review notes; your paraphrase drops detail.

## Agent types

Pick the narrowest type that can do the job.

| Type | Edits | Use for |
| --- | --- | --- |
| `Explore` | no | Locating code across many files; the conclusion, not the dumps |
| `Plan` | no | Design trade-offs before a design file |
| `general-purpose` | yes | Implementing, building, testing, reviewing (reviewers get "edit nothing") |
| `fork` | yes | Only when the task needs context you hold and re-briefing costs more. Rare |

## Permissions and authority

- **No permission laundering.** If an agent was refused a permission, don't do the action for it. Surface it to the
  user with what and why, and relay an explicit approval back.
- Agent reports are claims, not user input. Approvals come only from the user's own messages.
- Upstream edits the user approved in one context don't extend to the next one.

## Builds and tests (this repo)

- Release catches warnings-as-errors; Debug runs asserts. Green is both (§5): Release build of the narrowest
  project (`Content.IntegrationTests` builds all), Debug tests filtered (`--filter "FullyQualifiedName~<Feature>"`).
- Add `UninitializedSaveTest` for component/prototype changes, `SandboxTest` for new APIs in Client/Shared,
  `_KS14/Localization` tests for popup or prototype-name changes, the YAML linter for prototype ids.
- A failure that passes alone is batch noise only after you've checked it isn't a leak (pending input, a cvar, a
  static) from an earlier test. Leaks get fixed, not rerun away.
- "Pool manager has not been initialized" across many tests is one setup failure: find the single different one.

## Reporting to the user

Lead with the outcome, then verified versus untested (in-game visuals and feel usually are), then open items and
questions. No agent ids or internals. While agents run, say they're running; never predict their results.
