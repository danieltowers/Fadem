# 01: Each host has a palette, from its skin

Status: resolved
Blocked by: none

Shell and hosts. No visible change yet: this is the palette every later issue draws from. See the
[spec](../spec.md), "The colours", decisions 1–3.

## What to build

- **`Faderio.Shell/Theme/Palette`**, pure: the ten roles (Chrome, Well, Edge, Key, Text, Dim,
  Raised, Accent, Fade, Mark) and `IsDark`.
  - `Palette.From(SkinColours)`: from the raw reads, by the spec's rules. A black read where its
    source neighbours are not falls back to the role it derives from. An accent with too little
    saturation becomes amber `#FFB000`. Text with less than 4.5:1 contrast on Well is pushed to
    white or black. Raised, Fade and Mark are derived.
  - `Palette.Studio`: the built-in palette, B's graphite and amber.
  - `Palette.Load(path)` and `Save(path)`: `palette.json`, written atomically. A file that is
    missing, unreadable or of another shape loads as null.
- **The plugin** reads its `SkinColours` from `Setting_GetSkinElementColour` with the element,
  state and component numbers in the spec's table, and builds the palette as it makes its view.
  It writes the palette to the app's machine folder, `%APPDATA%\faderio\palette.json`, and
  keeps it for the view.
- **The app** loads `palette.json` from its machine folder (`--machine` for smoke runs), and
  uses `Palette.Studio` when there is none, keeping it for the main window.

## Acceptance

- [x] Duophonic's measured reads give `#232323`, `#1C1C1C`, `#121212`, `#2C2C2C`, `#E1E1E1`,
      `#6D6D6D` and accent `#CD5C5C`.
- [x] Monochrome reads give the amber accent. All-black reads give a usable palette, not black
      on black. A light skin's reads give `IsDark = false`, and its text keeps contrast.
- [x] Save then Load round-trips. A broken file loads as null.
- [x] The plugin, against a fake MusicBee answering Duophonic, holds that palette and has written
      `palette.json`. The app with that file holds it, and with none holds `Palette.Studio`.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-06, built. MusicBee answers 0 (alpha 0) for a colour the skin does not hold, and opaque
ARGB for one it does, so "not answered" is exact, not a guess from black. A real black (such as
an edge) is kept. The element, state and component numbers live in the plugin's `Host`, which
reads the skin again for every view MusicBee makes. `Plugin` passes `Host` a reader that takes
and returns ints only, so no Shell type reaches the class MusicBee reflects over before the
resolver exists (LoadingTests pass). The plugin's test constructor writes no `palette.json`
unless a test names a path. The App's choice is `Palette.ForMachine(machineFolder)`: a
`--library`-only smoke run keeps no machine folder, so it gets the built-in palette. Tests:
net8.0 Core 1152, Shell 177, App 185; net48 x86 Core 1131, Shell 177, MusicBee 102; 0 failed.
