# 04: Remove rows and clear the queue

Status: ready-for-agent
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

- [ ] Removing three selected rows, one of them playing, is one engine call. Playback continues
      on the row that took the playing row's place, from 0:00.
- [ ] Paused stays paused after removing the playing row.
- [ ] Removing the outgoing or the incoming row mid-transition cuts that voice; the other plays
      on at full volume.
- [ ] Removing the last playing row finishes playback, with *Stopped* on audible time.
- [ ] Clear asks for confirmation beyond one row, then empties the queue and stops.

## Tests

- **fake:** each bullet above, including both mid-transition cases, plus `Clear`.
- **form:** Delete key, menu items, and confirmation.

## Smoke

- **Looks:** multi-select and Delete removes the rows; Clear shows its confirmation.
- **Sounds:** delete the playing row; the next one starts cleanly from its beginning.
