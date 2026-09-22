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

## Noted from ticket 08

[Ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md) adds four items to this
ticket's "done", all engine-side; see `docs/supported-audio.md` and ADRs 0007 and 0008:

- **Engine rate comes from the output adapter at launch.** `TransitionMixer.SampleRate`,
  `NormalizedTrackSource.EngineSampleRate` and `EngineTime.SampleRate` are replaced by one
  launch-time value. `FakeAudioOutput` declares 44.1 kHz so existing tests do not move; the
  WaveOut fallback defaults to 44.1 kHz. Store points stay `TimeSpan`, converted at load.
- **Probe at add time.** The add command opens the file, reads the header and checks format,
  rate, depth and channels against the promised table. A file that fails is never queued. The
  result is a **structured** rejection (the four probed properties plus which one failed) that
  travels as a `Notice`; the shell formats it and links the doc. Rejected today: >2 channels,
  8/32-bit PCM, rates other than 44.1/48/96 kHz, AIFF, M4A/WMA, Ogg, anything without a row.
- **The `MediaFoundationReader` catch-all in `AudioSources.cs` becomes an explicit `.flac` case**
  plus rejection. The `.aif/.aiff` case is removed with it.
- **Delete Ogg support**: `VorbisTrackSource`, the three `Ogg_*` matrix tests, `ramp.ogg` and
  its `generate.sh` line, and the NVorbis package reference. The paging finding lives in the
  doc's Rejected table.

Play-time failures (corrupt frame, device gone) are one `Notice` and a skip, never per block.
