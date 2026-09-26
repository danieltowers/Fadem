# 04: Click anywhere on the seek bar

Status: ready-for-agent
Blocked by: —

Decided in [D6](../../202609-faderio-interim-improvements/decisions.md).

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

- [ ] Click at 50 % of the bar during playback → the playhead lands within 0.5 s of half the
      length and the readout follows.
- [ ] Drag: the audio does not move until release.
- [ ] Click while paused → seeks; ▶ resumes there.
- [ ] Click at launch (stopped) → nothing happens.
- [ ] During a crossfade the bar scrubs the attended (outgoing) voice, as today.

## Tests

- **unit (App):** pixel → `Playhead` mapping at 0, mid and end for a given width and length;
  disabled with no attended voice.
- **form:** a click posts exactly one `Seek`; a drag posts one on release.

## Smoke

- **Looks:** click three-quarters along the bar; the thumb jumps there and the readout shows a
  time near three-quarters of the length.

## Comments
