# 04: Shuffle and repeat are remembered

Status: ready-for-agent
Blocked by: 03

Decided in [D11](../../202609-faderio-interim-improvements/decisions.md), Q34.

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

- [ ] Turn shuffle on, quit, relaunch: on. Set repeat-all in library A, switch to B: B's own
      value (default off); back to A: repeat-all.

## Tests

- **fake:** persisted on toggle; restored on open; independent per library.

## Smoke

- None (no shell change).

## Comments
