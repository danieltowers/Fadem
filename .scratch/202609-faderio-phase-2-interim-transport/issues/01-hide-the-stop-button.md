# 01: Hide the stop button

Status: resolved
Blocked by: —

Decided in [D3](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What to build

- The ⏹ button is removed from the transport row (`MainForm.cs`, the 9-column
  `TableLayoutPanel`). The remaining controls close the gap; nothing else in the row moves.
- `engine.Stop()` is **not** touched, not deprecated, not tested less. It is still what the
  engine does at launch, after the queue ends and when a library is switched (next spec).
- `PlayerView.Transport == Transport.Stopped` still exists and still occurs.

## Where the logic lives

- **Shell only.** One control removed, one grid column fewer.

## Acceptance

- [x] No ⏹ anywhere in the window.
- [x] `Stop()` and its tests are unchanged in the diff.
- [x] The Looks half of `docs/smoke.md` no longer names ⏹; its note that "UI Automation
      matches ⏹ and ⏸ as the same name" is updated to say only ⏸ exists.

## Tests

- **form:** the transport row's button names no longer include the stop glyph.

## Smoke

- **Looks:** the transport row reads ⏮ ▶ ⏭ ⤳ 🔀 🔁, seek bar, time.

## Comments

- Built 2026-09-26. `_stop` is gone from `MainForm`: field, construction, click handler and its
  grid cell; the transport table is 8 columns (6 auto-sized buttons, the seek bar, the time).
  `PlaybackEngine.Stop()` and its tests are untouched. Its only production caller is now
  `Renderer`; `Clear` and removing the last row stop through the same internals
  (`ResetForRestart` + `StopOutput`), not through `Stop()`.
- Form test `TheTransportRow_HasNoStopButton` asserts the row's buttons, in order, are
  ⏮ ▶ ⏭ ⤳ 🔀 🔁. It failed before the change for the right reason.
- `docs/smoke.md`: the UI Automation note no longer names ⏹; the Delete / Clear queue freeze item
  and the clip-light Sounds item no longer press it; a Looks item for the row's order is added.
