# 11: Device loss and the output fallback chain

Status: ready-for-agent
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

- [ ] On the fake, an output failure mid-track pauses at the heard playhead; Play resumes from
      there.
- [ ] Mid-transition, the same failure lands paused on the incoming track.
- [ ] A reopen at a different rate raises the restart notice, and the engine rate is
      unchanged.
- [ ] No device: Play stays paused, with a notice.

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
