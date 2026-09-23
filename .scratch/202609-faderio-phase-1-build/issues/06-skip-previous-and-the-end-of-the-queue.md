# 06: Skip, previous and the end of the queue

Status: ready-for-agent
Blocked by: —

Decided in [What do skip, repeat and end-of-queue do?](../../202609-faderio-phase-1-restage/issues/02-what-do-skip-repeat-and-end-of-queue-do.md).
Carries v2 0008's intent. Closes audit findings 5–8.

## What to build

Bring `Next`, `Previous` and the end of the queue into line with the ticket. It holds the
full rules for ⏭, ⏮, repeat and the end of the queue. In summary:

- **`SkipBehaviour`** and its let-finish test go.
- **⏭ under repeat-one** moves to the next row, and repeat-one stays on.
- **⏮ under repeat-all** wraps from the head of the queue to the last row.
- **The end-of-queue fade is cut:** `FindForOutgoing`, the `ScheduleBoundary` branch that
  uses it, and `EndOfQueue_AuthoredFadeOutPlaysOut_ThenPlaybackStops` all go.
- ***Stopped* is published when the device has emitted the last frame**, not when the last
  voice is rendered.
- **The engine exposes "the adjacency ⏭ would reach"** (as a view field or a query). It serves
  Transition now and the editor's this → next target (issues 08 and 15), so the shell never
  computes a next row.

## Where the logic lives

- **Engine:** all of it. The shell's buttons stay one call each.

## Acceptance

- [ ] Each rule in the ticket's Skip and Previous sections has a fake-seam test.
- [ ] ⏭ under repeat-one lands on the next row, which then loops.
- [ ] ⏮ at the head under repeat-all goes to the last row.
- [ ] With A→B authored and only A queued, A plays to its `Length` and stops, with no fade.
- [ ] The final buffer is not cut, and *Stopped* follows the playhead.
- [ ] The next-adjacency target is published: the next row, the wrap under repeat-all, and
      none on the last row with repeat off.

## Tests

- **fake:** all of the above. The existing skip and repeat tests are kept where they still
  hold, and the let-finish test and `FindForOutgoing` test are deleted.

## Smoke

- **Sounds:** the last track's tail plays to its very end before playback stops (replaces the
  current "stops cleanly" item).
