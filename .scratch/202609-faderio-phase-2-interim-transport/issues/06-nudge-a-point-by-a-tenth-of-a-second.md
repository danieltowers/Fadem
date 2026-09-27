# 06: Nudge a point by a tenth of a second

Status: resolved
Blocked by: —

Decided in [D8](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What to build

- `−` and `+` buttons beside the **Fade-out point** and **Fade-in point** boxes in
  `TransitionEditorPanel`. **Not** beside the durations.
- A click moves the displayed number by 0.1 s; Shift-click by 1 s. "Displayed number" means:
  with **From end** ticked on the fade-out point, `+` increases the seconds-from-end, which is
  earlier in the track. The buttons do what the box shows.
- The result is clamped to the track: never below 0, never beyond `Length` (from either end).
- The nudged value lands in the box as text and marks the draft dirty, exactly as typing would;
  Save / Revert / Preview behave as they do for a typed value. No automatic preview.
- If the box does not currently parse (red refusal label), the buttons are disabled.

## Where the logic lives

- **Engine:** `Point Nudge(Track track, Point point, TimeSpan delta)` — adds `delta` in the
  point's own frame of reference (from-start or from-end) and clamps against the track's
  `Length`. The engine owns `Length`, so the clamp is its (placement test 2). If `Point` already
  carries from-end, the member needs no flag; if the presenter converts, pass what
  `FromOtherEnd` takes.
- **Shell:** four buttons; parse the box, call `Nudge`, format the result. The Presenter's
  existing parse/format is reused; it grows a modifier-aware step (0.1 s / 1 s) only.

## Acceptance

- [x] Fade-out point 12.000, `+` → 12.100; Shift-`+` → 13.100; `−` → 13.000.
- [x] From end ticked, showing 5.000 from end, `+` → 6.000 from end (earlier in the track).
- [x] At 0.000, `−` stays 0.000. At `Length`, `+` stays at `Length`.
- [x] After a nudge the panel shows unsaved edits and Preview plays the nudged point.

## Tests

- **unit (Core):** `Nudge` from-start, from-end, both clamps, sub-millisecond precision kept.
- **form:** each button posts one `Nudge` with the right delta; disabled when the box is
  invalid.

## Smoke

- **Looks:** open the editor, press `+` beside the fade-out point; the number rises by 0.1 and
  Save becomes enabled.

## Comments

- Built 2026-09-27. **Engine:** `Point Nudge(Track, Point, TimeSpan delta)` needs no from-end
  flag: a point from the start and a point from the end both run 0 → length, so adding the delta
  to the number shown and clamping to `[0, Length]` is right in either frame. Exact to the tick.
- **Panel:** `NudgeButtons` puts − and + inside each point box's own cell (a three-column strip),
  so no row moves; the point boxes are 54 px narrower than the duration boxes. A nudge goes
  through `PointOf`, so a captured point is nudged exactly and lands back in `_fadeOut`/`_fadeIn`
  as a capture does. Shift is read from `Control.ModifierKeys` at the click; `NudgePoint(fadeOut,
  direction, coarse)` is the seam the tests use for Shift. Enabled while an adjacency is open and
  the box holds a point (`ShowNudges`, on every text change and on load).
- Tests: `NudgeTests` (3, engine: steps, both clamps, tick precision) and four form tests (typed
  point with clamps and Shift, from-end direction, disabled states, a captured point kept exact).
- The full suite once failed `AddTimeBudgetTests.AFlacWithEightMegabytesOfCoverArt_AddsWithin150Milliseconds`
  (216 ms) under the parallel run; it passed 3/3 alone and on the next full run. Nothing here
  touches adding; it is a wall-clock budget that is sensitive to load.
