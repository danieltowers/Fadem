# 02: The audio output moves into a shared library for both hosts

Status: ready-for-agent
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

- [ ] The output library builds for `net8.0-windows` and `net48`. The App builds against it and
      no longer contains the output.
- [ ] The net8.0 suite is green, including the device loss and stopping-the-device tests. The
      net48 x86 target is unchanged and green.
- [ ] The Looks half of `docs/smoke.md` passes on standalone faderio:
  - the output mode shows in the title
  - forcing an unavailable exclusive mode shows the fallback notice and the shared mode
- [ ] The commit states both counts.
