# 12: Settings: a file, a dialog and the default crossfade

Status: resolved
Blocked by: 09

Decided in [Which settings exist, and where do they live?](../../202609-faderio-phase-1-restage/issues/10-which-settings-exist-and-where-do-they-live.md).
**Default crossfade** is in `CONTEXT.md`. Carries v2 0013's intent. Closes audit finding 14,
except ReplayGain, which is issue 13.

## What to build

- **`PlayerSettings`** keeps output mode, the unauthored-boundary choice with N (1–30 s), and
  `ReplayGainEnabled`. `SkipBehaviour` (already gone in issue 06), `PreviewPreRollSeconds`
  and `VisualiserVisible` go.
- **The resolver:** a **blank authored duration resolves to a fixed built-in 5 s**, never to
  N (`TransitionResolver.cs:105`).
- **The engine owns `settings.json`** through `SettingsStore`. `PlayerView` carries the
  current settings.
  - `ChangeSettings(settings)` saves the file and applies each value.
  - A changed default crossfade reschedules any boundary whose transition has not started.
  - Output mode is saved for the next launch.
  - A corrupt file is moved aside, with a notice. A newer schema raises a notice and loads
    defaults.
- **`Program`** reads only the output mode from the store before building the engine.
  `--output=` is removed.
- **A Settings… dialog** with OK and Cancel. When a fallback is active, it shows the mode in
  use beside the mode chosen.

## Where the logic lives

- **Engine:** storage, applying values and the reschedule.
- **Shell:** the dialog. It reads from the view, sends one call and holds nothing between
  calls.

## Acceptance

- [x] Settings survive a restart.
- [x] A corrupt file is moved aside with a notice, and defaults load.
- [x] Changing N does not alter an authored transition with a blank duration.
- [x] Switching to *Crossfade for N s* makes the next unauthored boundary crossfade, if its
      transition has not started. One in flight plays out.
- [x] A new output mode applies at the next launch; the dialog says so.
- [x] `--output=` is gone, and `docs/smoke.md` no longer uses it.

## Tests

- **unit:** `SettingsStore` corrupt-file handling.
- **unit:** the resolver's fixed 5 s.
- **fake:** `ChangeSettings` and the reschedule.
- **form:** the dialog round-trip.

## Smoke

- **Looks:** the dialog shows the three settings, the next-launch note, and the fallback mode
  when one is active.
- **Sounds:** with *Crossfade for 8 s* on, two unauthored tracks crossfade over 8 s.
