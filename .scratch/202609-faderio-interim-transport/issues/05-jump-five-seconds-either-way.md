# 05: Jump five seconds either way

Status: ready-for-agent
Blocked by: 04

Decided in [D7](../../202609-faderio-interim-improvements/decisions.md).

## What to build

- **`SeekBy(TimeSpan offset)`** on the engine: moves the attended voice's playhead by `offset`,
  clamped to `[0, Length]`. Negative goes back. With no attended voice, or while stopped, it is
  a no-op with no notice (nothing to move; the buttons are disabled anyway).
- A jump may land inside a transition window; it is handled exactly as `Seek(Playhead)` to
  that target would be. No special casing.
- Two buttons in the transport row, either side of the seek bar: **−5 s** and **+5 s**. Fixed.
  Enabled exactly when the seek bar is enabled.

## Where the logic lives

- **Engine:** the arithmetic and the clamp (placement test 2: the shell never adds to
  `View.Attended.Playhead`).
- **Shell:** two buttons calling `SeekBy(±5 s)`.

## Acceptance

- [ ] At 0:10, +5 s → 0:15; −5 s → 0:10.
- [ ] At 0:02, −5 s → 0:00.
- [ ] 3 s before the end, +5 s → the track ends and the adjacency plays as authored (or the
      queue advances).
- [ ] 2 s before a fade-out point with a 4 s fade, +5 s → lands inside the fade; the crossfade
      is already in progress at the right gain (fake-output test).
- [ ] Paused: the jump moves the readout; ▶ resumes there.

## Tests

- **fake output:** each acceptance line; clamp at both ends; no-op when stopped.
- **form:** the buttons call `SeekBy` with ±5 s and follow the seek bar's enabled state.

## Smoke

- **Looks:** press +5 s twice; the readout advances by ten seconds.

## Comments
