# Rework the engine seam

Type: task
Status: resolved
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

Done, 2026-09-23. The engine now has the surface ticket 04 sketched and nothing else public;
**162 tests pass** (141 before; 21 new at seams S1 and S3, minus the three Ogg cells). Every
item of "done" above holds, plus the hand-offs from tickets 08, 09 and 10.

### What was built

- **Seam types.** `Frames` on `IAudioOutput`/`IFrameSource` only. `Length`, `Playhead`, `Point`
  carry ticks (100 ns) internally rather than frames, one packaging deviation from the sketch:
  the store already serialises `TimeSpan`, the engine rate is now a launch-time value, and one
  frame at any promised rate maps to ticks and back losslessly. `Point.FromSeconds` is the only
  text→point path; `Capture()` the only sound→point path; no public playhead→point conversion.
- **`IAudioOutput`** gained `SampleRate` (ADR 0008) and `FramesEmitted`; `Stop()` resets it, and
  the engine resets its mixer timeline in the same call, so the two clocks share one origin.
  `Transport` replaces `PlaybackState`. The output is injected, fixed at launch; `ApplySettings`
  and the output factory are gone. `FrameSourceSampleProvider` is the NAudio adapter's entry.
- **`IEngineSink`** with `ImmediateSink` and `RecordingSink`. Publication is queued under the
  lock and drained after it, coalesced to one `Changed` per command; a handler may re-enter.
  Tested: a handler that calls `Pause()` from inside `Changed`.
- **The `View`** is composed lock-free from an immutable snapshot plus the device's emitted frame
  count, so the playhead and the attended voice are honest on every poll without a publication
  per block. `VoiceView.Remaining` is a `Length`; `QueueRow` carries `Track` and `IsCurrent`,
  with the length on `Track.Length` (ADR 0010) rather than duplicated on the row.
- **Playhead** from a per-voice reference history (creation plus one pair per seek), clamped to
  the issued length. The fake output gained `Latency`; with 100 ms of it, the playhead reads
  0.9 s when 1.0 s is rendered, stays on the old audio through the post-seek drain, and jumps
  when the seek point becomes audible.
- **Attended voice** lags the current voice until the audible handover: the outgoing's fade end
  for a crossfade, the cut point for a skip or hard cut. `IsCurrent` flips in the same
  publication. The mixer now ends a voice when its fade-out completes rather than rendering
  its remaining minutes at zero gain, so an end-of-queue fade finishes playback at the fade's
  end.
- **`Seek` on the attended voice cancels a transition in flight**: the incoming voice is dropped
  and the outgoing one is current again. v2 scrubbed the incoming voice the listener could
  barely hear.
- **`Author()` and `Unauthor()` reschedule a pending boundary**; a boundary in flight is left
  alone. Adding rows, `SetShuffle` and `SetRepeat` reschedule too.
- **`AddTracks` probes** (`AudioProbe`, `SupportedAudio`): WAV fmt/data chunks, FLAC STREAMINFO,
  MP3 first frame plus Xing/Info count or size-over-frame-size. Rejections are structured
  (`Rejection` with `AudioShape` and `RejectedProperty`). Length is the header's claim; audio
  that ends early is one notice and the track ends there. A file that passes the probe and then
  fails to open is one notice and a skip.
- **Ogg gone**: `VorbisTrackSource`, its three cells, `ramp.ogg`, the NVorbis package. **Preview
  and visualiser gone** from the core: out of the slice (ticket 05), the fog holds preview's
  fate, and v2 has the code. `Track.Duration` gone; TagLib reads title, artist and ReplayGain.

### The S3 null test, at 1 ms

`CaptureThenAuthor_TheFadeStartsWhereTheListenerHeardIt_Within1ms`: with 100 ms of device
latency, render 1.6 s, `Capture()` returns 1.5 s (the audible position, frame-exact), author it
as the fade-out point, seek to 1.0 s, play through, and the crossfade's onset in the rendered
stream lands within 1 ms of where the point says. The onset detector extrapolates two threshold
crossings back to the onset frame; a single threshold would sit 140 frames late by the shape of
an equal-power curve, which is the kind of instrument error that would have hidden v2's defect
behind a tolerance. The RecordingSink test pins the handover: the version that moved
`AttendedRow` to the incoming track was published at emitted frame ≥ 66150 (A's fade end) and
before the next block; the version before it still attended A while B sounded underneath.

### Found on the way

- **v2's ReplayGain reader threw on every file carrying an ID3v2 tag object**: it asked TagLib for
  the TXXX frame as a four-letter frame id. No v2 test reached that line through a real file;
  the first `AddTracks` through the production probe and tags did. Fixed
  (`UserTextInformationFrame.Get`).
- A point authored at the audible playhead while the decode position is already past it (the
  device buffer's worth) cannot fire in the same playthrough: the audio is rendered. The
  engine schedules nothing, and the author hears it after a seek. The slice's loop is
  capture → save → seek back → listen, and the shell should not pretend otherwise.

### Hand-offs

Ticket 11: `IAudioOutput` is the shape a `WavFileAudioOutput` implements; `Renderer.RenderBoundary`
stays a static entry point over a fresh engine on that output. Ticket 13: the surface is final;
`PostingSink` is the one adapter left to write; the NAudio output wraps `FrameSourceSampleProvider`
in `FloatToBytesWaveProvider` and reports `FramesEmitted` from `IWavePosition`. Ticket 14: the
fake, the harness and the fixtures moved (see its note). Ticket 15: `TrackSources.Open` is the one
switch to change.


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

## Noted from ticket 09

Two amendments from [ticket 09](09-how-exact-is-the-authoring-promise.md):

- **The S3 acceptance test tightens from 5 ms to 1 ms.** The promise is 1 ms everywhere, and on
  the fake output with WAV there is no resampler and no decoder inexactness for 5 ms to absorb.
  A failure at 1 ms is a finding, not a tolerance problem.
- **The decoder switch is now ticket 15's**, not this one's. Ticket 08's note above said the
  `MediaFoundationReader` catch-all becomes an explicit `.flac` case; ticket 09 went further and
  removed Media Foundation from the chain, with FLAC on `BunLabs.NAudio.Flac`. Ticket 15 makes
  that change to `AudioSources.cs`. This ticket still owns the **probe and the structured
  rejection**; until 15 lands, the probe may treat `.flac` as promised and leave the decoder
  alone. Whichever lands second reconciles the factory switch.

## Noted from ticket 10

[Ticket 10](10-which-length-is-a-track.md) adds three items, all engine-side (ADR 0010):

- **The probe issues `Length`** from the stream header alongside format, rate, depth and
  channels: FLAC STREAMINFO sample count, WAV data-chunk size over block align, MP3 Xing/Info
  frame count × 1152 or file size over frame size for bare CBR. `QueueRow.Length` and
  `VoiceView.Length` carry it. If ticket 14's fourth assertion shows a format's header cannot
  give an exact length, the probe scans frames for that format instead.
- **`Track.Duration` is deleted.** `TrackMetadataReader` keeps title, artist and ReplayGain.
  The M3U writer is outside the slice; when it returns it formats the engine's `Length`.
- **Audio ending before `Length`** is one `Notice` naming the file and the shortfall; the track
  ends where the audio ends and the boundary fires there (hard cut if the fade had not started).
  One engine-seam test: a WAV fixture whose header claims twice its data, through the fake
  output, asserting the notice and where the boundary fired.
