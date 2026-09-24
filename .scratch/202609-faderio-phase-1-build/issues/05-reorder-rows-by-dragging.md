# 05: Reorder rows by dragging

Status: resolved
Blocked by: 04

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md)
(Reorder). This is new: v2 never had it.

## What to build

- **`Move(rows, before)`**, driven by dragging the selected rows within the queue.
- Rows keep their identity, so moving the playing row does not interrupt it.
- If a move changes which row comes next, the pending boundary is rescheduled, as long as its
  transition has not started. A transition in flight plays out.
- Under shuffle, a move changes the shuffled (visible) order.

## Where the logic lives

- **Engine:** `Move` and the reschedule.
- **Shell:** drag feedback and the drop position, passed as a `RowId`.

## Acceptance

- [ ] Dragging two rows moves them together, in order, with one engine call.
- [ ] Moving the playing row keeps it playing, uninterrupted.
- [ ] Moving a different row into the next position before the fade starts makes that row's
      adjacency fire.
- [ ] The same move after the fade has started changes nothing audible.

## Tests

- **fake:** each bullet above, and a move under shuffle.
- **form:** a drag-drop produces the right `Move`.

## Smoke

- **Looks:** the drop indicator shows where rows will land.
- **Sounds:** drag a different track into the next position mid-song; it is the one that plays
  next.

## Comments

- Built 2026-09-24. `Move(IEnumerable<RowId> rows, RowId? before)`: a null `before` is the end.
  The rows land in their queue order, not the order they were picked.
- **Dropping on one of the dragged rows:** they land before the next row that is not moving,
  so dropping rows on themselves changes nothing.
- **Stale rows:** rows already gone are one notice and the rest still move, as `Remove` does. The
  check is shared as `EntriesOf`. A `before` that is gone moves nothing and is one notice.
- **Shuffle:** under shuffle a move changes only the shuffled order, so turning shuffle off
  returns to the original order without the move. Unshuffled, a move is also the original order,
  so a later shuffle on and off keeps it. The first half is an interpretation of "Under shuffle,
  a move changes the shuffled (visible) order" and needs the user's confirmation.
- **Found in review, fixed for every reschedule:** a last row's authored end fade is written on
  the voice, not scheduled, and rescheduling left it there.
  - A move (or add, remove or repeat change) that gave the last row a next row with a hard cut
    still faded it out early. The fade is now cleared when its boundary is cancelled.
  - An end fade that has already started counts as in flight and plays out.
- **Shell:**
  - `ItemDrag` drags the selected rows' ids in a private format. `DragOver` shows a drop line: a
    thin panel, since a `ListView` draws no insertion mark in details view.
  - The drop position is the row under the cursor by height alone: its upper half is before
    it, its lower half after it, and below the last row is the end. It is passed as the
    `RowId` to land before.
  - A row keeps its selection across a rebuild.
  - A drag held near the top or bottom edge scrolls on a timer, since `DragOver` fires only on
    mouse movement. This is not in the issue: without it a row could not be dragged past the
    visible part of a long queue.
- **Tests:** `MoveRowsTests` (11) and three form tests.
  - Seven Core tests failed first: the move itself, the reschedule, the original order, the end
    fade, dropping on itself, and both stale-row cases.
  - Four are guards: the playing row, a move with the transition in flight, under shuffle, and
    an end fade in flight.
  - All three form tests failed first.
- **Smoke:** a real mouse drag of Tone 1 onto Tone 4's upper half showed the line between
  Tone 3 and Tone 4, and dropped Tone 1 there. The timer scroll was not checked with a real
  mouse, because the user was working in another window. See the commit for the rest.
