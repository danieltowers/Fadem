# faderio v3

## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/<feature-slug>/` in this repo. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`, recorded as a `Status:` line in each issue file. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Where logic belongs

Three mechanical tests, checkable by reading a diff. A change that fails one belongs in the
engine, not the shell. Decided in wayfinder ticket 04.

1. **One user intent is one engine call.** If the shell sequences two engine members to do one
   thing the user asked for, the intent is missing from the engine.
2. **The shell formats engine values; it never computes from them.** If a number needs
   arithmetic, the engine returns the result.
3. **The shell holds no engine state between calls.** It reads, renders, forgets.

Frames never cross the shell seam: externally `Playhead`, `Point`, `Length` and `TimeSpan`;
`Frames` appears only on `IAudioOutput`.

## Verification

- Run `dotnet test` before every commit and **state the result in the commit message**
  (count passed, count failed, or "no test project yet"). Not a hook, by decision: the suite
  renders real audio and a slow hook gets bypassed.
- Core's and the shared shell's tests run twice, on **net8.0** and on **net48 x86** (the MusicBee
  plugin's runtime, ADR 0012). The commit message states the net8.0 and net48 x86 counts
  separately. A net48 test that cannot run there is excluded in the test project with its
  reason, never skipped.
- A session that touches the shell runs the **Looks** half of `docs/smoke.md` before its
  commit and reports item by item. The **Sounds** half is the user's; never claim it.
- A research investigation that measures the engine leaves a **permanent test** behind, not a
  throwaway script. If it rendered through the production chain, it is already an integration
  test — commit it as one.
