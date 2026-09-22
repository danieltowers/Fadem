# faderio v3

## Agent skills

### Issue tracker

Issues and specs live as local markdown files under `.scratch/<feature-slug>/` in this repo. See `docs/agents/issue-tracker.md`.

### Triage labels

Default vocabulary: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`, recorded as a `Status:` line in each issue file. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: one `CONTEXT.md` and `docs/adr/` at the repo root. See `docs/agents/domain.md`.

## Verification

- Run `dotnet test` before every commit and **state the result in the commit message**
  (count passed, count failed, or "no test project yet"). Not a hook, by decision: the suite
  renders real audio and a slow hook gets bypassed.
- A session that touches the shell runs the **Looks** half of `docs/smoke.md` before its
  commit and reports item by item. The **Sounds** half is the user's; never claim it.
- A research investigation that measures the engine leaves a **permanent test** behind, not a
  throwaway script. If it rendered through the production chain, it is already an integration
  test — commit it as one.
