# Rework the engine seam

Type: task
Status: open
Blocked by: 07

## Question

Nothing to decide: build the interface [ticket 04](04-what-is-the-seam-between-the-shell-and-the-engine.md)
settled, on the migrated core from [ticket 07](07-migrate-faderio-core-into-v3.md). This is the
first of three build tickets graduated from the map's fog once 04 — the last gating decision —
resolved.

Work through the seams in [ticket 06](06-how-do-we-make-green-tests-and-a-broken-app-impossible.md)'s
order, red before green, one slice at a time. The interface is the test surface; nothing reaches
past it.

### What "done" means

- `PlaybackEngine` has the surface in ticket 04's sketch and nothing else public.
  `PlaybackQueue`, `TransitionResolver`, `MixerVoice`, `EffectiveTransition` are `internal`.
  The four leaked mutables and two unlocked setters are gone.
- `IAudioOutput` carries `FramesEmitted` in `Frames`; `FakeAudioOutput.Advance(Frames)` is the
  clock; recreate-and-reset share one origin.
- `IEngineSink` exists with `ImmediateSink` and `RecordingSink`. `PostingSink` waits for the
  shell ([ticket 13](13-build-the-shell.md)).
- **Playhead** derived from `FramesEmitted` through a per-voice reference history, honest
  through the post-seek drain ([ticket 03](03-what-position-does-set-from-here-capture.md)).
  `Capture()` returns it as a `Point`, unquantised.
- **Attended voice** flips at the audible handover, in the same `View` publication as
  `IsCurrent`. Seek cancels a transition in flight. `Author()` reschedules a pending boundary.
- Every command total; `RowId` stale → `Notice`.
- **The S3 null test passes**: advance the fake to a known frame, `Capture()`, `Author()`,
  render through the fake, find the fade onset within 5 ms of the captured point. This is the
  slice's measured assertion ([ticket 05](05-what-exactly-is-in-the-narrow-slice.md)), and
  it is this ticket's acceptance criterion.
- A `RecordingSink` test asserts which `Version` moved `AttendedRow` and at what
  `FramesEmitted`.

### Not in this ticket

The shell and presenter (13). The real-input matrix (14). The render harness's file output
([ticket 11](11-render-a-boundary-to-a-file.md)) — though `Renderer.RenderBoundary`'s static
shape is decided here and 11 fills it.

## Answer

_(pending)_
