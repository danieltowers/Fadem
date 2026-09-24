# 07: Shuffle and repeat controls

Status: resolved
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

- [x] The toggle and button show the view's state after every publication, including changes
      made elsewhere.
- [x] Each press is exactly one engine call.

## Tests

- **form:** the state shown follows the view; each press sends the right command.

## Smoke

- **Looks:** the shuffle and repeat controls read clearly in all states, including repeat-one.
- **Sounds:** repeat-all wraps from the last track to the first through their adjacency.

## Comments

- Built 2026-09-24. Both controls are `CheckBox`es with `Appearance.Button` and
  `AutoCheck = false`: pressed in while on, plain while off. They never check themselves;
  `Render` sets them from the view, so a change made elsewhere shows on the next publication.
- **A press reads the view, not the button.** Shuffle sends `SetShuffle(!View.Shuffle)`. Repeat
  sends `SetRepeat(Presenter.NextRepeat(View.Repeat))`, both read at the moment of the press. A
  stale button therefore cannot undo a change made elsewhere. ADR 0003 has an addendum adding
  the cycle to the presenter's list.
- **Repeat-one shows "🔁1", not 🔂.** In the Looks screenshot the "1" inside 🔂 could not be
  read at 9 pt, so repeat-one looked like repeat-all. Off and all share 🔁 and differ by being
  pressed in. The two buttons are 44 px wide to fit "🔁1".
- **Tooltips** name each state ("Shuffle: on", "Repeat: one"). They are set only when the text
  changes, because `Render` runs every 100 ms and re-setting a tip closes it.
- **Tests:** four form tests: the state follows the view, including changes made through the
  engine directly; each shuffle press and each repeat press is one publication with the right
  state; and a press cycles from the view, not from what the button last showed. Presenter
  tests cover the cycle, the glyphs and the tips.
- **Smoke:** `docs/smoke.md` gains a Looks item for the two controls and a Sounds item for
  repeat-all wrapping.
