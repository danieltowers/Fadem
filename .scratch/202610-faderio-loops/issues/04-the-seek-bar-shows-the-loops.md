# 04: The seek bar shows the loops

Status: ready-for-agent
Blocked by: 02

Shell, both hosts (`SeekBar` in `Faderio.Shell`). See the [spec](../spec.md), D16 and the
*Seek bar while looping* drawing.

## What to build

- Each loop in `VoiceView.Loops` is drawn as a span on the bar: two bracket marks at its start
  and end positions and a slightly heavier track line between them, in the fade marks' family
  of colours but distinct from a wedge. A parked loop (count 1) draws fainter.
- The sounding loop (`VoiceView.Pass` not null) shows its text under the span: "pass 2/4", or
  "∞" for open-ended. No other loop shows text. The text sits inside the bar's existing height;
  if the span is too narrow for it, it is drawn beside the span, never clipped.
- The playhead jumps back as the view says it does; the bar does nothing to smooth it.
- Clicking or scrubbing inside a span seeks there as today (the engine applies D10); nothing on
  the bar is draggable for loops in this slice.
- The bar maps positions to pixels and draws text the view gives it. It computes nothing and
  keeps nothing between paints.

## Acceptance

- [ ] A headless paint into a bitmap with two loops in the view draws two spans at the right
      columns, and "pass 2/4" under the sounding one only.
- [ ] A view with `Loops` empty paints as today (pixel-equal to the pre-change bar on the same
      view).
- [ ] An open-ended loop shows "∞".
- [ ] The MusicBee panel's narrower bar still draws the text unclipped.
- [ ] Looks half of `docs/smoke.md` run and reported item by item, both hosts; the new Looks
      items from the spec's *Smoke* added.
- [ ] net8.0 and net48 x86 counts stated in the commit.
