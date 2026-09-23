# 17: The clip light

Status: ready-for-agent
Blocked by: 16

Decided in [Does the visualiser come back?](../../202609-faderio-phase-1-restage/issues/03-does-the-visualiser-come-back.md). The
preview case is from [What is preview?](../../202609-faderio-phase-1-restage/issues/04-what-is-preview.md). **Clip** is in
`CONTEXT.md`. Carries the one surviving part of v2 0011 (story 8). The fade strip and the peak
meter are cut.

## What to build

- **A clip** is any mixed output sample whose absolute value is above 1.0, measured at the
  mixer's output.
- **It latches when heard,** on audible time.
- **It names its cause:**
  - the in-flight adjacency (queue or preview);
  - otherwise the attended track (or the preview's audible track).
- **`PlayerView` gains a nullable clip fact:** the count and the latest cause.
- **`ClearClip()`** is the only way to clear it. It survives Stop and Clear, but not a
  restart. There is no setting.
- **The shell** shows *2 clips · latest: C* as a light. Clicking it clears the light.

## Where the logic lives

- **Engine:** detection, the audible-time latch, attribution, and the count.
- **Shell:** formats the fact and holds no latch.

## Acceptance

- [ ] Two full-scale correlated tracks crossfaded equal-power latch a clip naming the
      adjacency, and only once the playhead reaches the clipped sample.
- [ ] A later clip on a single track raises the count to 2 and names the track.
- [ ] `ClearClip` clears it; Stop and Clear do not.
- [ ] A clip during a preview names the previewed adjacency.

## Tests

- **fake:** each case, with a fake latency so audible and rendered time differ.
- **form:** the light's text and the click.

## Smoke

- **Looks:** the light is dark by default, and legible when lit.
- **Sounds:** a boosted overlap lights it as you hear the distortion, not before.
