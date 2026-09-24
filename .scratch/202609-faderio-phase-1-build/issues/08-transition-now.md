# 08: Transition now

Status: resolved
Blocked by: 06

Decided in [What do skip, repeat and end-of-queue do?](../../202609-faderio-phase-1-restage/issues/02-what-do-skip-repeat-and-end-of-queue-do.md)
(Transition now). **Transition now** is in `CONTEXT.md`.

## What to build

A new button and one engine command, `TransitionNow()`. It starts the next adjacency's
transition immediately. That is the adjacency ⏭ would reach, published by issue 06. The ticket
holds the rules:

- Only the fade-out point moves, to the playhead.
- A fade-out that would run past `Length` is shortened.
- The non-overlapping shape stops the outgoing track now.
- An unauthored adjacency resolves as the natural boundary would: a hard cut, or the default
  crossfade when that setting is on.

The button is disabled while a transition is in flight, when there is no next adjacency, and
while paused or stopped. The shell reads all three from the view.

## Where the logic lives

- **Engine:** the command, the resolution and the moved fade-out point.
- **Shell:** one button, enabled from view state without arithmetic.

## Acceptance

- [x] With an authored crossfade, Transition now mid-track starts the fade at the playhead and
      plays the rest as authored.
- [x] A fade-out that would pass `Length` is shortened.
- [x] The non-overlapping shape stops the outgoing track now and starts the incoming one at
      its fade-in point.
- [x] An unauthored adjacency hard-cuts from 0:00, or plays the default crossfade from now
      when that is on.
- [x] The button is disabled in each of the three cases.

## Tests

- **fake:** each case, including the wrap under repeat-all and the next row under repeat-one.
- **form:** enablement follows the view.

## Smoke

- **Sounds:** Transition now mid-song fades into the next track as authored, starting from
  where you pressed.

## Comments

- Built 2026-09-24. `TransitionNow()` resolves the adjacency ⏭ would reach
  (`IndexSkippedTo`) afresh, since under repeat-one the scheduled boundary is the loop's. It
  cancels the scheduled boundary and starts the transition now.
- **The moved point is a resolver input.** `TransitionResolver.Resolve` takes an optional
  `fadeOutAt`: the fade-out point moves there, and nothing else. A fade-out that would then
  pass `Length` is shortened by the same rule as an authored one. The rule is now one helper,
  `ShortenToEnd`, so the default crossfade shortens too when a track is shorter than the
  default duration. The engine already clamped that case, so nothing sounds different.
- **"The playhead" is where decoding is.** Audio already in the device buffer cannot be
  refaded, so the fade starts at the decode position and the listener hears it begin one
  device buffer after the press. A latency test pins this.
- **The shell reads one flag, `View.CanTransitionNow`, not three fields.** The engine also has
  to do nothing while a hard cut or the last track's finish is still in the device buffer.
  `Incoming`, `Transport` and `NextAdjacency` cannot show that, so the button would have been
  live for about one buffer while the press was dropped. The engine now publishes the
  condition as a flag, computed at the same instant as the playhead. The command guards on
  that same flag, so the two cannot disagree. ADR 0003 has an addendum.
- **Open edge.** When a crossfade's fade-in outlasts its fade-out, Transition now is live once
  the outgoing track falls silent, even though the incoming one is still fading in. A press
  then puts the new fade-out on top of that fade-in. The ticket does not say whether this
  counts as "in flight".
- **The button** is ⤳ at 13 pt, between ⏭ and shuffle, with the tooltip "Transition now".
  The arrows in Segoe UI Symbol draw too small at the transport's 9 pt.
- **Tests.** `TransitionNowTests` (15) covers each acceptance case, the repeat-all wrap, the
  next row under repeat-one, the four no-op cases, and the latency cases. Four resolver tests
  cover the moved point. Two form tests check that enablement follows the view and that a press
  is one call.
