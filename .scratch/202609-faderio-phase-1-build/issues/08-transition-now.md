# 08: Transition now

Status: ready-for-agent
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

- [ ] With an authored crossfade, Transition now mid-track starts the fade at the playhead and
      plays the rest as authored.
- [ ] A fade-out that would pass `Length` is shortened.
- [ ] The non-overlapping shape stops the outgoing track now and starts the incoming one at
      its fade-in point.
- [ ] An unauthored adjacency hard-cuts from 0:00, or plays the default crossfade from now
      when that is on.
- [ ] The button is disabled in each of the three cases.

## Tests

- **fake:** each case, including the wrap under repeat-all and the next row under repeat-one.
- **form:** enablement follows the view.

## Smoke

- **Sounds:** Transition now mid-song fades into the next track as authored, starting from
  where you pressed.
