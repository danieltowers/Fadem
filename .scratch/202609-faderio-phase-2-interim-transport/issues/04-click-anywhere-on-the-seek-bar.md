# 04: Click anywhere on the seek bar

Status: resolved
Blocked by: —

Decided in [D6](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What happens today

The seek bar is a stock `TrackBar` (`MainForm.cs:127-134`) with `Maximum` = length in ms;
`Seek` fires on `MouseUp`. A click on the channel pages by the default `LargeChange` (5 ms), so
the only way to move far is to drag the thumb.

## What to build

- Replace the `TrackBar` with a custom seek bar control (`SeekBar`, in `Faderio.App`):
  - A click anywhere on its travel seeks to that proportion of the attended voice's `Length`.
  - Pressing on the thumb (or anywhere) and dragging moves the thumb with the mouse; the seek
    fires **once, on release** (D6). The view's playhead updates are suppressed during the drag,
    as `_seekDragging` does now.
  - Disabled (no thumb, no click) when `View.Attended` is null or transport is stopped (D3).
  - Keyboard: ← → move 1 s and seek on key-up; nothing else.
- The control speaks `Playhead` and `Length` (glossary types) at its edge; pixels never leave
  it. It converts pixel ↔ proportion internally and asks nothing of the engine to do so — that
  is presentation, not engine arithmetic.
- Same footprint in the transport row; the time readout is unchanged.

## Where the logic lives

- **Shell.** A control. The engine's `Seek(Playhead)` is unchanged.

## Acceptance

- [x] Click at 50 % of the bar during playback → the playhead lands within 0.5 s of half the
      length and the readout follows.
- [x] Drag: the audio does not move until release.
- [x] Click while paused → seeks; ▶ resumes there.
- [x] Click at launch (stopped) → nothing happens.
- [x] During a crossfade the bar scrubs the attended (outgoing) voice, as today.

## Tests

- **unit (App):** pixel → `Playhead` mapping at 0, mid and end for a given width and length;
  disabled with no attended voice.
- **form:** a click posts exactly one `Seek`; a drag posts one on release.

## Smoke

- **Looks:** click three-quarters along the bar; the thumb jumps there and the readout shows a
  time near three-quarters of the length.

## Comments

- Built 2026-09-26. `SeekBar` (`src/Faderio.App/SeekBar.cs`), a user-painted `Control`:
  `Show(Playhead, Length?)` on every render, one `SeekRequested(Playhead)` event per click, drag or
  run of arrow keys, which `MainForm` passes straight to `engine.Seek`. `_seekDragging` is gone
  from the form: the bar ignores the view's playhead itself while it is held. Travel runs between
  a 6 px inset at each end so the thumb is whole at both ends; `PlayheadAt(x)` is the only
  pixel → playhead mapping. Disabled, with no thumb, when `Length` is null (nothing attended).
- An arrow-key run that loses focus before key-up still seeks, so the thumb never strands where
  the sound is not.
- Crossfade: the engine's `Seek` is unchanged, and
  `SeekSkipSemanticsTests.SeekDuringACrossfade_CancelsTheTransition_AndScrubsTheAttendedVoice`
  already covers it; the bar is fed the attended voice as the TrackBar was.
- Tests: `SeekBarTests` (5: mapping, click, drag, keys, disabled) and one form test (a click while
  paused seeks the attended voice to half of 8 s; at launch it does nothing). The existing playhead
  test reads `_seek.Playhead` instead of `_seek.Value`.
- UI Automation sees the bar as a pane named "Seek" (its window text; `AccessibleName` alone
  did not reach UIA). `docs/smoke.md` says so and how to find the thumb.
