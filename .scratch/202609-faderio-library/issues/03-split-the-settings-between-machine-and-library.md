# 03: Split the settings between machine and library

Status: ready-for-agent
Blocked by: 01

Decided in [D11](../../202609-faderio-interim-improvements/decisions.md).

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

- [ ] Change the default crossfade in library A, open library B: B shows the default; back to A:
      A's value.
- [ ] Change the output mode, switch libraries: unchanged (machine-level).
- [ ] Resize and move the window, quit, relaunch: same place and size.

## Tests

- **fake:** the two stores round-trip independently; a corrupt machine file recovers; the
  library half swaps with `OpenLibrary`.
- **form:** each dialog group calls its own command.

## Smoke

- **Looks:** Settings… shows the two groups.

## Comments
