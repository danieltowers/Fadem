# 14: The default fade curve is a setting

Status: resolved
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-10: "i need the ability to set the
default curve type in settings".

## What is there today

Equal-power is fixed as the default in several places: the curve a transition with no curve set
plays (`TransitionResolver`), the default crossfade's curve, the editor's curve list for a new
transition in the global layer (and so what it saves), and the test for unsaved edits
(`IsAuthored`, "a missing curve is equal-power").

## What to build

A library setting, as the blank duration is (`8f2f6f2`): `PlayerSettings.DefaultCurve`,
Equal-power by default, under Settings… > This library as **Default fade curve**, in both hosts.

- A transition with no curve of its own plays it; one with a curve keeps its curve.
- The default crossfade (an unauthored adjacency, with it on) plays it.
- The editor's curve list shows it for a global transition with no curve, so a new transition is
  saved with it. A playlist override's "(global)" is unchanged.
- A change reschedules a pending boundary, as the blank duration does, and the editor shows the
  new default at once.
- A render follows it (it runs on the library's settings).
- A file without the key reads as Equal-power; an unknown number is brought back to Equal-power.
  An unknown name fails the file's read as any of its enum settings does today (moved aside, with
  a notice): a tolerant reader would be its own change.

## Acceptance

- [x] Settings… shows Default fade curve in both hosts; OK saves it to the library's settings.json.
- [x] A blank curve, the default crossfade and a new transition in the editor take it.
- [x] A transition saved with its own curve is unchanged by it.

## Comments

### 2026-10-10, built

- **Engine.** `PlayerSettings.DefaultCurve` (Equal-power, `Normalised` brings an unknown number
  back). `TransitionResolver.DefaultCurve` fills a blank curve and the default crossfade's;
  `IsAuthored` compares a blank curve as it; a change reschedules a pending boundary. A render
  runs on the library's settings already. `TransitionLayers` carries `DefaultCurve` and
  `GlobalCurve` (the stored curve, else the default), which the editor's global curve list shows.
- **Editor.** On a change of the library's settings, from either host, the next render loads the
  open pair again (unless a box is being typed in), so the new default and blank duration show
  at once: the plugin had not shown a new blank duration until the pair was reopened. A save
  leaves the curve list alone, so a pick a save refused stays.
- **Settings.** "Default fade curve:" under This library in the App's Settings and the plugin's
  Configure…, between Blank fade durations and ReplayGain; the plugin's greyed with no library.
- Tests: Core (resolver, settings change and reschedule, settings file, format fixture, render),
  Shell `EditorDefaultCurveTests`, App and MusicBee settings round trips. README, CONTEXT.md
  (Default curve) and both smoke halves updated.
- Looks, App, fresh library, `--edit-first`: the editor on Alpha → Bravo showed Equal-power;
  Settings showed "Default fade curve: Equal-power" between the blank durations and ReplayGain,
  themed; S-curve and OK: "Saved settings.", the editor's curve S-curve at once,
  `"defaultCurve": "SCurve"` in settings.json, no transitions.json written. Not checked by the
  agent: the plugin's Configure… on screen.
