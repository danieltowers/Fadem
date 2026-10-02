# 02: Reordering the plugin's queue by dragging rows

Status: resolved
Blocked by: none

Phase 6, MusicBee improvements round 1. Raised 2026-10-02: rows in the plugin's queue cannot be
dragged to a new place, as they can in standalone faderio.

## The cause

Standalone faderio's queue moves dragged rows (build issue 05): a line shows where they will
land, a drag held at the top or bottom edge scrolls the list, and the drop is one
`Engine.Move(rows, before)`. All of it lives in `MainForm`. Phase 5 kept it standalone (issue 10:
"no column layout, no drag"; issue 11: the `QueueList` carve-out not done), so the plugin's
view has none of it.

## What to build

Selected rows in the plugin's queue are dragged to a new place exactly as in standalone
faderio, docked or floating.

## Implementation decisions

- **Shared, not copied.** The row drag moves out of `MainForm` into `Faderio.Shell`, attached
  to a queue `ListView` by both hosts: the drag of the selected rows (its private data format),
  the drop line, the edge scroll on a timer, and the drop index (before the row under the
  cursor's upper half, after it over its lower half, the end below the rows). Its drop is one
  `Engine.Move(rows, before)`, the row at the drop index or null for the end. Where the rows
  land and what that does to playback stay the engine's.
- **The file drop stays each host's**, through the same shared piece: rows Move, files Copy,
  anything else None. The files still go to `Engine.Add` at the end (issue 03 changes that).
- **Rows never leave their list.** The rows' format is faderio's own, and is only read by the
  queue that started the drag. A row dragged out over MusicBee does nothing there.
- **Placement.** The shell turns a cursor position into a row; one drop is one engine call.

## Testing decisions

- The shared piece in `Faderio.Shell.Tests`, on both targets: the drop index from a point
  (upper half, lower half, below the rows, above the top row when scrolled), and a drop of rows
  making one `Move` with the right `before`.
- The App's existing drag tests stay as they are and pass unchanged: the carve-out moves code,
  not behaviour.
- Seam 2, the plugin: rows dragged onto a later row's upper half move there in their order and
  stay selected; the playlist file holds the new order; playback is not interrupted.

The MusicBee **Looks** half gains an item, the user's (a real mouse): rows dragged in the
view's queue show the line and land there, docked and floating.

## Acceptance criteria

- [x] Rows in the plugin's queue can be dragged to a new place, with a line showing where.
- [x] Standalone faderio's row drag is the same code, its tests unchanged.
- [x] One drop is one `Engine.Move`.

## Comments

- **Resolved 2026-10-02.** Built as specified above.
  - **Shared:** `QueueDrag` in `Faderio.Shell` holds what was `MainForm`'s: the drag of the
    selected rows, the drop line, the edge scroll on a timer, the drop index, rows Move and
    files Copy. Its drop of rows is one `Engine.Move`. The rows' data names the list they came
    from, and another list refuses them. `MainForm` keeps `_dropLine` and `StartDrag` as
    pass-throughs, so the App's tests are unchanged; `PluginView` replaces issue 01's handlers
    with it, files still going to `Engine.Add` at the end.
  - **Tests:** `QueueDragTests` on both targets (the drop index, a row drop as one Move, files
    to the host and text refused); `PluginTests.Drop` gains a row dragged onto an earlier row's
    upper half (moved, saved, still selected, playback going on), the line shown, and rows from
    another view refused. The App's drag tests pass unchanged.
  - **Smoke** on the portable MusicBee, this build installed: ✅ loads with no box, the strip
    "Ready."; ✅ (the user) rows dragged in the view's queue land on the line, dropping from
    MusicBee still works.
