# 06: Skip, previous and the end of the queue

Status: resolved
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

## Comments

- Built 2026-09-24. The adjacency ⏭ would reach is a view field, `PlayerView.NextAdjacency`
  (`Adjacency?`). It comes from the row the listener is on, so mid-transition it is the one in
  flight, and it moves on at the audible handover. It is published when stopped too, from the
  current row. Issue 08 can read it as it is. Issue 15 needs a per-row query for right-clicked
  rows: the rule is `PlaybackQueue.IndexSkippedTo(index, count, repeat)`, which is static so the
  snapshot can share it. `Presenter.AdjacencyAt` still computes `rowIndex + 1`, which is issue
  15's to remove.
- **The queue's rules:** `IndexAfter` is the natural end (repeat-one repeats). `IndexSkippedTo`
  is ⏭ (repeat-one moves on). `IndexBefore` is ⏮ (repeat-all wraps at the head).
  `PreviousIndex` is gone.
- **⏮ now works from the attended voice's row.** Before, mid-transition it worked from the
  incoming row, so with X A B and A → B in flight, ⏮ restarted A instead of going to X.
- **Stopped follows the playhead.** When the last voice ends, it stays current and in the view
  until the device has emitted its last frame (`_finishAt`, checked in `ObserveClock`). Only then
  is the device stopped and *Stopped* published. As a result:
  - *Stopped* trails the last audible frame by up to one render call, because nothing checks
    the clock sooner.
  - During that tail, Play does nothing and ⏮ restarts the track (the source is gone, so this is
    a fresh start, not a seek). ⏭ stops, or wraps under repeat-all. Seek does nothing.
  - Switching repeat on or appending a row during the tail does not revive it: the decision was
    made when the voice ended. This is a window of one device buffer.
- **Found while testing ⏭ while paused:** with device latency, a paused hard cut left the view
  on the old row until Play, because the audible handover waits for frames a paused device never
  emits. `Remove` already worked around this. It is now shared as `LandWhilePaused` and used by
  ⏭, ⏮ and `Remove`.
- **Deleted:** `SkipBehaviour` (setting, enum, test), `FindForOutgoing` (interface, store, test),
  the end-of-sequence branch of `ScheduleBoundary`, `BoundarySchedule.EndOfSequence`,
  `EndOfQueue_AuthoredFadeOutPlaysOut_ThenPlaybackStops`, and issue 05's two end-fade move tests,
  whose premise is gone.
- **Not changed:** an unreadable next track at the end still finishes at once, cutting the
  previous track's tail (`SkipToNextAfter`). That is issue 10's.
- **Tests:** `SkipPreviousAndEndOfQueueTests` (20), plus queue tests for the three rules.
  - Run against HEAD, eight failed: ⏭ under repeat-one, ⏭ while paused with latency, the end
    with no fade, *Stopped* following the playhead, Play during the tail, ⏮ during the tail, ⏮
    wrapping under repeat-all, and ⏮ mid-transition.
  - Seven pinned rules that already held.
  - The five `NextAdjacency` tests are new API.
