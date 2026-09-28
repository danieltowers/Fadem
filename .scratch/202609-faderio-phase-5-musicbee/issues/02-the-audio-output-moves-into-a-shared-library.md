# 02: The audio output moves into a shared library for both hosts

Status: resolved
Blocked by: 01

Part of the [MusicBee spike](../spec.md). A prefactor: the plugin needs the same output
standalone faderio has.

## What to build

`NAudioAudioOutput` leaves the App and moves into a new library targeting `net8.0-windows` and
`net48`, which the App references. The move keeps:

- WASAPI exclusive, then shared, then WaveOut fallback, and its notices
- the mode-changed event the title follows
- reopening after device loss
- the unavailable-outputs smoke argument

Standalone faderio behaves exactly as before. This library is the seed of the shared shell
library later phase 5 issues grow; in this ticket it holds the output only.

## Acceptance criteria

- [x] The output library builds for `net8.0-windows` and `net48`. The App builds against it and
      no longer contains the output.
- [x] The net8.0 suite is green, including the device loss and stopping-the-device tests. The
      net48 x86 target is unchanged and green.
- [x] The Looks half of `docs/smoke.md` passes on standalone faderio:
  - the output mode shows in the title
  - forcing an unavailable exclusive mode shows the fallback notice and the shared mode
- [x] The commit states both counts.

## Comments

- Built 2026-09-28. The new library is **`src/Faderio.Shell`**, targeting `net8.0-windows;net48`
  and referencing Core. It holds `Audio/NAudioAudioOutput.cs`, moved with history
  (`git mv`), now in namespace `Faderio.Shell.Audio`. The App references it and no longer
  contains the output.
- **The mode's name moved with it.** The output's fallback notices named modes through
  `Presenter.FmtOutputMode`, which lives in the App. That method is now
  `OutputModeNames.Of` in the same namespace, and the title, the settings dialog and the notices
  all call it. When the Presenter moves into this library later, it can take the name back.
- **One net48 edit:** `Math.Clamp` does not exist there, and Core's `Compat.Clamp` is internal,
  so the device-clock clamp is spelled `Math.Min(Math.Max(frames, 0), rendered)`. Nothing else
  needed changing. Both targets build with no warnings.
- **Tests:** Core net8.0 853 passed, Core net48 x86 288 passed, App 274 passed, 0 failed. These
  are the same counts as before the move. `OutputFallbacks_RunExclusiveSharedWaveOut_FromTheModeAsked`
  still covers the chain, through the moved class.
- **Smoke (Looks), on the built `faderio.exe` with fresh `--library` and `--machine` folders:**
  - The window opens without an error dialog. The title reads "Untitled — faderio — shared-lib ·
    WASAPI shared at 48000 Hz", which is this device's mix rate.
  - `--play` on two generated tones plays: the marker is on row 1, "One" is named, the thumb
    has moved and the readout shows 0:04 / -0:04.
  - Settings… shows "WASAPI shared" in the output-mode box.
  - With WASAPI exclusive chosen and `--unavailable=exclusive`, the title names WASAPI shared
    at 48000 Hz. The strip says "WASAPI exclusive unavailable (the device is in use
    (--unavailable)); trying WASAPI shared." and shows "1 notice". Settings… then reads "In
    use now: WASAPI shared" under the chosen exclusive.
  - With `--unavailable=exclusive,shared` the title names WaveOut at 44100 Hz. The strip
    shows "…trying WaveOut." and "2 notices".
  - Only the Looks items this move could reach were run. The rest of the list covers queue,
    editor and sidebar code that did not change.
