# Stage the build issues

Type: task
Status: resolved
Blocked by: 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 12, 13

## Question

Nothing left to decide; this ticket reaches the destination. Write
`.scratch/202609-faderio-phase-1-build/spec.md` and its numbered issues from the decisions on
this map, per `docs/agents/issue-tracker.md`.

- **Vertical slices.** Each issue carries its engine change (if any), shell, tests at the right
  ADR 0002 seam, and the Looks and Sounds items it adds to `docs/smoke.md`.
- **Order.** Daily driver first, as the map's Notes say: queue management, robust files,
  settings, then preview, visualiser and ReplayGain. Wire `Blocked by:` where dependencies bind.
- **Placement.** Every issue passes the three tests in `CLAUDE.md`; say in each where its logic
  lives.
- **Sourcing.** Each issue links the ticket that decided it and the v2 issue whose intent it
  carries. It restates nothing a ticket or ADR already holds.
- **Status.** Every issue is `ready-for-agent` or `ready-for-human`, never `needs-triage`.

Also graduate or rule out whatever remains in this map's **Not yet specified**; the map is not
done while fog is left.

## Answer

Staged by the agent on 2026-09-23. The destination is reached:
[`spec.md`](../../202609-faderio-phase-1-build/spec.md) and 18 numbered issues in
`.scratch/202609-faderio-phase-1-build/issues/`, all `ready-for-agent`. Every link was checked
and resolves.

**The order,** daily driver first:

- **01–03, adding:** in the background, folders and drop, playlists.
- **04–05, editing the queue:** remove and clear, reorder.
- **06–08, transport:** skip, previous and end of queue; shuffle and repeat controls;
  Transition now.
- **09–11, failures:** notices, unreadable tracks, device loss and the fallback chain.
- **12–13, settings:** the file and dialog; ReplayGain live.
- **14–17, authoring:** editor completions, entry points and unsaved edits, preview, the clip
  light.
- **18:** trimming tagged MP3s, staged last.

The spec's table holds the `Blocked by:` edges.

**How each issue is written.** Each one:

- names the ticket that decided it and the v2 issue whose intent it carries;
- says where its logic lives;
- lists acceptance checks, the tests at their ADR 0002 seam, and the Looks and Sounds items it
  adds to `docs/smoke.md`.

The rules every issue inherits (placement, audible time, total commands, rows not tracks,
verification) are stated once, in the spec. **Where an issue and its ticket disagree, the
ticket wins.**

**The fog is cleared.** **Unmeasured audio paths** graduated into
[issue 11](../../202609-faderio-phase-1-build/issues/11-device-loss-and-the-output-fallback-chain.md)
as Sounds items: headphone unplug and replug, forced fallback, and a whole real album on
exclusive with a track over 20 minutes long. Automated tests of `NAudioAudioOutput` are ruled
out of scope: ADR 0002 leaves the device to the human seam.

**Left open for build sessions, by design:**

- The exact seam for simulating a mid-stream decode error (issue 10).
- Whether NAudio's shared-mode `WasapiOut` resamples on a rate mismatch (issue 11).
- Whether notice merging needs engine knowledge (issue 09).

Each issue names its question and the rule for settling it.
