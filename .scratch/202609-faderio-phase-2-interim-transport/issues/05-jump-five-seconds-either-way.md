# 05: Jump five seconds either way

Status: resolved
Blocked by: 04

Decided in [D7](../../202609-faderio-phase-2-interim-improvements/decisions.md).

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

- [x] At 0:10, +5 s → 0:15; −5 s → 0:10.
- [x] At 0:02, −5 s → 0:00.
- [x] 3 s before the end, +5 s → the track ends and the adjacency plays as authored (or the
      queue advances).
- [x] ~~2 s before a fade-out point with a 4 s fade, +5 s → lands inside the fade; the crossfade
      is already in progress at the right gain (fake-output test).~~ Superseded by D7 ("treated as
      any seek"): the jump lands past the fade-out point, the track plays flat, and no transition
      is forced — see Comments.
- [x] Paused: the jump moves the readout; ▶ resumes there.

## Tests

- **fake output:** each acceptance line; clamp at both ends; no-op when stopped.
- **form:** the buttons call `SeekBy` with ±5 s and follow the seek bar's enabled state.

## Smoke

- **Looks:** press +5 s twice; the readout advances by ten seconds.

## Comments

- Built 2026-09-26. `SeekBy(TimeSpan offset)` reads the attended playhead from `View` after
  `ObserveClock`, as `Capture` does, so the offset is from what the listener hears; it then runs
  `Seek`'s body (`SeekNow`, extracted). `Playhead.Plus(offset)` (internal) floors at zero; the
  seek already clamps to the track's last frame. Stopped: `View.Attended` is null, so nothing
  happens and nothing is said.
- **Acceptance line 4 contradicted D7 and the engine.** It asked for a jump past the fade-out point
  to land mid-crossfade; D7 says a jump is any seek, and a seek past the fade-out point never
  forces the transition (`SeekSkipSemanticsTests.SeekPastTheAuthoredFadeOutPoint_DoesNotForceTheTransition`).
  The decisions file wins, so `SeekByTests.ForwardPastTheFadeOutPoint_IsAnySeek_TheTransitionIsNotForced`
  pins the D7 behaviour: the track plays flat from where it lands and hard-cuts at its end.
- Shell: **−5 s** and **+5 s** in columns either side of the seek bar (the transport table is 10
  columns), enabled exactly when the bar is. The row still fits at the window's 760 px minimum.
- Tests: `SeekByTests` (6, engine) and two form tests (the buttons move ±5 s and follow the bar's
  enabled state; their columns flank the bar). Issue 01's row test now orders by column and
  includes the two buttons.
