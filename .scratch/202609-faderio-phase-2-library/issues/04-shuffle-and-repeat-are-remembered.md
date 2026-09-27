# 04: Shuffle and repeat are remembered

Status: resolved
Blocked by: 03

Decided in [D11](../../202609-faderio-phase-2-interim-improvements/decisions.md), Q34.

## What to build

- `Shuffle` (bool) and `Repeat` (`RepeatMode`) join the library `settings.json`.
- `SetShuffle` and `SetRepeat` persist as a side effect, the way `ChangeSettings` does: the
  value is in force once it is on disk, atomic write, no new command.
- Opening a library applies its shuffle and repeat before the first playlist opens (the
  playlists spec) so the queue order is right from the start.
- The Settings… dialog does **not** show them; the transport toggles are their UI.

## Where the logic lives

- **Engine only.**

## Acceptance

- [x] Turn shuffle on, quit, relaunch: on. Set repeat-all in library A, switch to B: B's own
      value (default off); back to A: repeat-all.

## Tests

- **fake:** persisted on toggle; restored on open; independent per library.

## Smoke

- None (no shell change).

## Comments

- Built 2026-09-27. **`PlayerSettings`** gains `Shuffle` (default off) and `Repeat` (default
  `Off`); `Normalised` turns a repeat no version knows into `Off`. Still schema 1: missing keys
  load as the defaults. The engine's `_shuffle` / `_repeat` fields are gone; `_settings` is the
  one place both live, so `OpenLibrary` (and the constructor) bring the library's before
  anything enters the queue with no extra step.
- **`SetShuffle` / `SetRepeat`** save through a new private `KeepSettings(next, what)`, which
  `ChangeSettings` now uses too: in force once on disk. Success is **silent** (the toggle shows
  it; "Saved settings." per click would be noise). An unwritable file is "Not saved: shuffle.
  The settings file could not be written: …" and nothing changes.
- **No library open: refused**, as `ChangeSettings` is ("Not saved: repeat. No library is
  open."), so "in force once on disk" holds everywhere. Say if the toggles should instead work
  unsaved when no library is open.
- **`ChangeSettings` keeps the shuffle and repeat in force**; the ones in its argument are not
  used, as `ChangeMachineSettings` ignores `Library`. Consequence in the shell (untouched): with
  shuffle or repeat on, the dialog's `LibraryChanged` is true on every OK, because its
  `LibraryChoice` is built fresh; the engine sees no change and says nothing, so nothing is
  visible.
- Tests: `ShuffleAndRepeatRememberedTests` (13): each toggle saves and says nothing, leaves the
  listening settings alone, an unwritable file and no library refuse with a notice,
  `ChangeSettings` keeps them, relaunch restores both, a restored shuffle orders added rows
  from the start and off restores the added order, a restored repeat-all wraps, a hand-edited
  repeat loads as off, A/B switching keeps each library's own, a library that cannot open
  brings the defaults.
- dotnet test: 910 passed, 0 failed (Core 732, App 178). An earlier full run had
  `AddTimeBudgetTests` (150 ms budget) fail at 327 ms under load; it passed alone and on the
  next full run.
