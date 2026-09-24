# 11: Device loss and the output fallback chain

Status: resolved
Blocked by: 09

Decided in [What happens when a file or the device fails?](../../202609-faderio-phase-1-restage/issues/05-what-happens-when-a-file-or-the-device-fails.md)
(Device loss, When the output fails at launch). Carries v2 0014's
intent. Closes audit findings 11 and 12. It takes over the map's last fog item, **Unmeasured
audio paths**, as Sounds items.

## What to build

- **Device loss** pauses at the heard playhead with a notice and releases the voices. The
  engine is never half-stopped, and it never resumes by itself.
  - **Mid-transition,** it lands paused on the incoming track at its heard position.
  - **Play** reopens the current default device. If that device's rate differs, the output
    converts, and a notice suggests a restart. The engine rate never changes.
  - **With no device at all,** Play leaves playback paused with a notice.
- **At launch,** the output falls back exclusive → shared → WaveOut, with a notice at each
  step. The engine takes the rate of the output actually opened, and the title names the mode
  in use.
- **Confirm** whether NAudio's shared-mode `WasapiOut` resamples automatically on a rate
  mismatch. If it does not, the output must convert explicitly.

## Where the logic lives

- **Engine:** pausing on `Stopped` with an error, and reopening on Play through
  `IAudioOutput`. `FakeAudioOutput` gains the ability to fail and to be reopened.
- **Shell:** the `NAudioAudioOutput` fallback chain and reopening, and the title.

## Acceptance

- [x] On the fake, an output failure mid-track pauses at the heard playhead; Play resumes from
      there.
- [x] Mid-transition, the same failure lands paused on the incoming track.
- [x] A reopen at a different rate raises the restart notice, and the engine rate is
      unchanged.
- [x] No device: Play stays paused, with a notice.

## Tests

- **fake:** every engine case above.
- **human:** the `NAudioAudioOutput` paths. Automated tests of the real output are out of
  scope (spec).

## Smoke

- **Looks:** the title names the mode actually in use after a forced fallback.
- **Sounds** (these also retire the fog item **Unmeasured audio paths**):
  - unplug headphones mid-song; playback pauses and nothing plays from the speakers. Replug
    and press Play; it resumes where it paused.
  - with another app holding the device exclusively, launch in exclusive mode; faderio falls
    back to shared and says so.
  - play a whole real album on WASAPI exclusive, through several queue advances and a track
    over 20 minutes long; it is clean throughout.

## Comments

- Built 2026-09-24. `IAudioOutput` gains `int Reopen()`: it opens the current default device
  for the same source at the same rate, returns that device's own rate, and throws when there
  is none. `FramesEmitted` now holds what was heard at a failure until `Stop`.
- **Landing.** On `Stopped` with an error, the engine lands on what the view shows the listener
  hearing: `Incoming` mid-transition, otherwise `Attended`, at its heard playhead. It releases
  every voice, resets the timeline, and reopens that one track as a fresh voice at full volume.
  So a hard cut or the last track's tail still in the device buffer is not lost; it plays when
  Play is pressed. `_outputLost` makes the engine's transport read Paused while there is a
  track to resume. Play, PlayRow and Play-from-stopped reopen through one `PlayOutput`.
- **Rates.** If the reopened device's rate differs from the engine's, a notice says faderio
  converts and suggests a restart. The engine rate never changes. With no device, Play stays
  paused with a notice and tries again next time.
- **Confirmed: shared-mode `WasapiOut` resamples by itself.** NAudio.Wasapi 2.2.1 passes
  `AUTOCONVERTPCM | SRC_DEFAULT_QUALITY` (`ldc.i4 0x88000000` in its IL). A silent probe on a
  48 kHz device opened a 44.1 kHz stream: `Init` succeeded, `OutputWaveFormat` stayed 44100, and
  it pulled about 44.1k source frames a second. Exclusive mode resamples through NAudio's DMO,
  and WaveOut through the Windows mixer. No explicit converter is needed.
- **Launch chain.** `NAudioAudioOutput` opens its player in the constructor, falling back
  exclusive → shared → WaveOut with a notice each step, so the engine takes the rate of the
  output actually opened. `Mode` names it and the title shows it. When nothing opens, the title
  says "no audio output", `Initialise` reports the output as lost, and Play tries the chain
  again. Reopen walks the same chain from the mode in use. For exclusive mode, the device rate
  it reports is the rate the stream actually opened at.
- **Smoke-only `--unavailable=exclusive,shared`** treats those modes as taken, so the Looks
  item can force the fallback.
- All output notices are posted through the sink from launch; the startup list is gone.
- **For the Sounds run (not reachable by any fake):**
  - `WaveOutEvent` on the default mapper follows the default device and may keep playing on the
    speakers after an unplug, never failing. The unplug item is meant for WASAPI.
  - WASAPI notices a lost device only while rendering. A loss while paused surfaces on the next
    Play: the engine lands paused again, and a second Play reopens.
  - After a failure the heard position is estimated as rendered minus latency, because an
    invalidated device's clock throws when asked. It can be up to a buffer off.
- **Tests.** `DeviceLossTests` (14), all on the fake: a mid-track failure pauses at the heard
  playhead and nothing resumes by itself; Play reopens and resumes there; mid-transition lands on
  the incoming track; a hard cut in the buffer lands on the outgoing track; the last track's
  tail is kept; loss while paused or stopped; a rate change raises the restart notice and the
  same rate does not; no device; PlayRow, Stop and ⏭ after a loss. `PresenterTests` covers the
  chain order, the title and `--unavailable=`.
