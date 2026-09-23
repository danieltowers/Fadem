# Stage the build issues

Type: task
Status: open
Blocked by: 01, 02, 03, 04, 05, 06, 07, 08, 09, 10, 12

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
