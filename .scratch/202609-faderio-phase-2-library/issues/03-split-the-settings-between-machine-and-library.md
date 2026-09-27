# 03: Split the settings between machine and library

Status: resolved
Blocked by: 01

Decided in [D11](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What happens today

One `PlayerSettings` in `%APPDATA%\faderio\settings.json`: `OutputMode`, `UnauthoredAdjacency`,
`DefaultCrossfadeSeconds`, `ReplayGainEnabled`. Window size is hard-coded 1040×600, centred.

## What to build

- **Machine settings** (`%APPDATA%\faderio\settings.json`, engine-owned `MachineSettings`):
  `OutputMode`, `Library` (last opened path). Schema-versioned, atomic, `.corrupt` recovery as
  today.
- **Library settings** (`<library>/.faderio/settings.json`, engine-owned `PlayerSettings`):
  `UnauthoredAdjacency`, `DefaultCrossfadeSeconds`, `ReplayGainEnabled` (issue 04 adds shuffle
  and repeat).
- `ChangeSettings` splits into `ChangeMachineSettings` and `ChangeSettings` (library). The
  Settings… dialog shows both in two groups, "This machine" and "This library", and calls the
  right one per group. `OutputMode` still takes effect at the next launch.
- `View.Settings` carries the library half; `View.MachineSettings` the other.
- **Window geometry** (`%APPDATA%\faderio\window.json`, shell-owned): size, position,
  maximised. Written on close, read on open, ignored if off-screen. This is shell state, so the
  shell owns the file; it is not `PlayerSettings`.
- The existing AppData file is **not** migrated: on first run after this change the machine
  file is rewritten with defaults for the keys it knows, and the library file starts at
  defaults (D13).

## Where the logic lives

- **Engine:** both settings files and both commands.
- **Shell:** the dialog's two groups; `window.json`.

## Acceptance

- [x] Change the default crossfade in library A, open library B: B shows the default; back to A:
      A's value.
- [x] Change the output mode, switch libraries: unchanged (machine-level).
- [x] Resize and move the window, quit, relaunch: same place and size.

## Tests

- **fake:** the two stores round-trip independently; a corrupt machine file recovers; the
  library half swaps with `OpenLibrary`.
- **form:** each dialog group calls its own command.

## Smoke

- **Looks:** Settings… shows the two groups.

## Comments

- Built 2026-09-27. **`MachineSettings`** gains `OutputMode` beside issue 02's `Library`;
  `PlayerSettings` loses it and is the library's three. `AudioOutputMode` moved to
  `MachineSettings.cs`. An `outputMode` key left in a library's file is ignored.
- **D13 for the output mode.** `MachineSettings` is now schema 2. A schema-1 file is either issue
  02's (library only) or the player's settings from before the library; either way its library
  stays and its output mode loads as the default, so the old AppData mode is not migrated. The
  file is rewritten in the new shape the next time the engine saves it (a library opening, or a
  machine setting changed), not at launch.
- **`ChangeMachineSettings(MachineSettings)`** saves the machine file and publishes; nothing
  changes now (ADR 0008). The `Library` in its argument is not used: the remembered library is
  the engine's to write. Same as saved is no change and no notice, as for `ChangeSettings`,
  which now says so too. Notices: "Saved machine settings.", "Not saved: machine settings. …"
  (unwritable file, or a run with no machine file). `View.MachineSettings` carries it, defaults
  when the run keeps none.
- **The dialog.** Two group boxes, **This machine** (output mode, the next-launch note, "In use
  now") and **This library** (unauthored boundaries, ReplayGain). On OK each group is sent to
  its own command only if it now says other than it opened on (`MachineChanged` /
  `LibraryChanged`), so changing one setting is one call, and a change undone is none.
- **`window.json`** is the shell's `WindowGeometry`: x, y, width, height and maximised, the
  normal bounds even when closed maximised. Written on close after the unsaved-edits prompt,
  read in `MainForm`'s constructor. It is used only when at least 100 px by 15 px of the title
  bar lands on a screen's working area; otherwise, or if the file is missing or bad, the window
  opens at 1040×600, centred. No notices: it is cosmetic.
- **`--machine=<folder>`** (new, for smoke runs) puts `settings.json` and `window.json` in that
  folder. Without it, `--library=` still keeps no machine files, as issue 02 decided; that now
  also means the default output mode and a centred window. The smoke doc launches with both, so
  the output-mode and window items can be run without touching the user's AppData.
- Tests: `MachineSettingsTests` (14): the two stores round-trip independently, missing / corrupt
  / newer machine files, an old or unknown output mode, `ChangeMachineSettings` (saved, the
  library kept, no change, no file, unwritable), `ChangeSettings` leaves the machine file, and
  the first two acceptance items. `SettingsStoreTests`: an `outputMode` left in a library's file.
  Form: the two groups, each group calling its own command, a change undone sending nothing, and
  the window's place (restored, maximised, off-screen, unreadable, no file). `WindowGeometryTests`
  for the on-screen rule and the file. Presenter: `--machine=` parsing.
- Smoke (Looks, the items this issue touches), run with `--library=` and `--machine=` in temp
  folders: the dialog shows **This machine** and **This library**; crossfade 12 s and WaveOut
  saved to their own files; Library B shows Hard cut and WaveOut, back to A shows 12 s; moved to
  150,90 at 900×560, relaunched there with WaveOut in the title; closed maximised, it reopens
  maximised and restores to 150,90 900×560; saved off-screen, it opens centred at 1040×600; a
  corrupt library `settings.json` is moved aside with a notice and the library group shows the
  defaults. `%APPDATA%\faderio\` was untouched.
