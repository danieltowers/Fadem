# 17: The clip light

Status: resolved
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

- [x] Two full-scale correlated tracks crossfaded equal-power latch a clip naming the
      adjacency, and only once the playhead reaches the clipped sample.
- [x] A later clip on a single track raises the count to 2 and names the track.
- [x] `ClearClip` clears it; Stop and Clear do not.
- [x] A clip during a preview names the previewed adjacency.

## Tests

- **fake:** each case, with a fake latency so audible and rendered time differ.
- **form:** the light's text and the click.

## Smoke

- **Looks:** the light is dark by default, and legible when lit.
- **Sounds:** a boosted overlap lights it as you hear the distortion, not before.

## Comments

- Built 2026-09-24.
  - **Detection:** `TransitionMixer` raises `Clipped` for each run of mixed samples beyond
    full scale, via `ClipDetector`, which notes the voices sounding at each frame. A voice sounds
    from its start until its fade-out completes. Two voices make the cause the adjacency, in the
    order they started; one makes it the attended track. Queue and preview mixers alike.
  - **Audible latch:** `ClipLatch` holds each clip at the device frame that plays it
    (timeline − `_queueBase` for the queue, + `_previewOrigin` for a preview). The view reads it
    at the device's count, like a playhead; the next clock observation publishes it.
  - **What one clip is:** samples by the same voices within 1 s of the last are one clip. A
    crossfade or a boosted track that clips throughout counts once; clipping again after a quiet
    second counts again. The spec left this open.
  - **Stopping:** Stop and Clear latch what the device played, then drop what it still held
    (never heard), without publishing mid-command. A clip the device dropped when a preview held
    a paused queue lights once the resumed queue plays it.
  - **Clearing:** `ClearClip()` only. Clipping that goes on after the clear lights it again.
  - **Shell:** a status-strip label at the far right, "● No clips" greyed, or "● 2 clips ·
    latest: C" white on red, capped at 320 px with the whole text in its tooltip. A click calls
    `ClearClip()`.
  - **Found by the Looks run:** a 44.1 kHz file that steps from silence to 0.95, resampled to a
    48 kHz device, overshoots full scale for a few samples and lights the light. That is right:
    the engine resamples before the mixer's output. It is now a test.
  - ADR 0003 gains an addendum; `docs/smoke.md` gains a Looks item and a Sounds item.
  - **Not done:** the Sounds item (lights as you hear it, not before) is the user's.
