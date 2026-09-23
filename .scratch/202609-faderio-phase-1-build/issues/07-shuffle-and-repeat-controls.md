# 07: Shuffle and repeat controls

Status: ready-for-agent
Blocked by: 06

Decided in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md) (Shuffle
and repeat controls). The behaviour itself was decided in
[What do skip, repeat and end-of-queue do?](../../202609-faderio-phase-1-restage/issues/02-what-do-skip-repeat-and-end-of-queue-do.md).
Carries v2 0003's intent (stories 5 and 6).

## What to build

Beside the transport buttons:

- A **shuffle toggle**.
- A **repeat button** cycling *off → all → one*.

Each shows its state from `View.Shuffle` and `View.Repeat`, and each press is one call
(`SetShuffle`, `SetRepeat`). The engine already has both commands, so this issue is shell
work.

## Where the logic lives

- **Shell only:** it formats view state. The next repeat mode in the cycle is a display
  choice over an enum, not engine arithmetic.

## Acceptance

- [ ] The toggle and button show the view's state after every publication, including changes
      made elsewhere.
- [ ] Each press is exactly one engine call.

## Tests

- **form:** the state shown follows the view; each press sends the right command.

## Smoke

- **Looks:** the shuffle and repeat controls read clearly in all states, including repeat-one.
- **Sounds:** repeat-all wraps from the last track to the first through their adjacency.
