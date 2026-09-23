# 05: Reorder rows by dragging

Status: ready-for-agent
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
