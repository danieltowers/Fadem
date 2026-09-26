# 06: Nudge a point by a tenth of a second

Status: ready-for-agent
Blocked by: —

Decided in [D8](../../202609-faderio-interim-improvements/decisions.md).

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

- [ ] Fade-out point 12.000, `+` → 12.100; Shift-`+` → 13.100; `−` → 13.000.
- [ ] From end ticked, showing 5.000 from end, `+` → 6.000 from end (earlier in the track).
- [ ] At 0.000, `−` stays 0.000. At `Length`, `+` stays at `Length`.
- [ ] After a nudge the panel shows unsaved edits and Preview plays the nudged point.

## Tests

- **unit (Core):** `Nudge` from-start, from-end, both clamps, sub-millisecond precision kept.
- **form:** each button posts one `Nudge` with the right delta; disabled when the box is
  invalid.

## Smoke

- **Looks:** open the editor, press `+` beside the fade-out point; the number rises by 0.1 and
  Save becomes enabled.

## Comments
