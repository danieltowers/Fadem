# 04: Remove rows and clear the queue

Status: resolved
Blocked by: —

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md) (Remove
and clear). Carries v2 0003's intent. Closes audit finding 4: `Remove` and `Clear` have no
test at any seam.

## What to build

- **`Remove(rows)`** takes the selected rows. Today's `Remove(row)` becomes its one-row case.
  The gestures are the Delete key and the context menu's **Remove**.
- **Removing the playing row** continues on the row that takes its place, from 0:00, as a hard
  cut. Paused stays paused. Removing the last row finishes playback.
- **During a transition,** a removed voice is cut and the other plays on at full volume.
- **Clear queue** is in the context menu. It asks for confirmation when the queue has more than
  one row.
- The queue list allows multi-select.

## Where the logic lives

- **Engine:** `Remove(rows)` and its playback consequences, in one call.
- **Shell:** selection, the key and menu, and the confirmation dialog.

## Acceptance

- [x] Removing three selected rows, one of them playing, is one engine call. Playback continues
      on the row that took the playing row's place, from 0:00.
- [x] Paused stays paused after removing the playing row.
- [x] Removing the outgoing or the incoming row mid-transition cuts that voice; the other plays
      on at full volume.
- [x] Removing the last playing row finishes playback, with *Stopped* on audible time.
- [x] Clear asks for confirmation beyond one row, then empties the queue and stops.

## Tests

- **fake:** each bullet above, including both mid-transition cases, plus `Clear`.
- **form:** Delete key, menu items, and confirmation.

## Smoke

- **Looks:** multi-select and Delete removes the rows; Clear shows its confirmation.
- **Sounds:** delete the playing row; the next one starts cleanly from its beginning.

## Comments

- Built 2026-09-24. `Remove(IEnumerable<RowId>)` replaces `Remove(RowId)`; a single row is
  `Remove(new[] { row })`. Rows already gone are one notice, and the rest are still removed.
- **Which row takes the place:** the first surviving row after the removed playing row, in
  visible order. When none is left after it, repeat-all wraps to the first surviving row, as ⏭
  would. With repeat off, playback finishes. This wrap is an interpretation of "as a skip
  lands" and needs the user's confirmation. Under repeat-one the next row plays, as ⏭ does.
- **Mid-transition:**
  - Removing the outgoing row reuses `AbandonInFlightTransition`, so the incoming voice snaps to
    full volume.
  - Removing the incoming row uses `ReturnToOutgoing`, which is extracted from `Seek`. The
    outgoing voice's fade-out is cancelled and its boundary is scheduled again against its new
    next row.
  - Removing both is a hard cut to the row after them.
- **Removing a row that is not playing** reschedules the pending boundary, so a removed next row
  takes its transition with it.
- **Paused with device latency:** a paused device emits nothing, so the audible handover would
  wait for Play, and the view would sit on the removed row. `Remove` completes the handover at
  once while paused. On resume, up to one device buffer of the removed row can still sound.
- **Shell:** the list is multi-select. **Delete** and the context menu's **Remove** send the
  selection in one call. **Clear queue** asks through `ConfirmClear` when the queue has more
  than one row; Cancel is the default button.
- **Tests:** `RemoveAndClearTests` (14) and five form tests. Four mutations were each caught:
  no reschedule, no fade-out cancel on return, no repeat-all wrap, and the previous row as the
  replacement.
- **Smoke found** that Clear, and removing the last playing row, reach
  [issue 19](19-stop-can-freeze-the-window.md)'s deadlock. Stacks are in its comments. It also
  found [issue 20](20-adding-a-large-folder-stalls-the-window.md), which predates this change.
