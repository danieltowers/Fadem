# Carry: transitions follow a move

Raised 2026-10-15: "option to preserve fade after dragging tracks, confirmation dialog. what if
the new combo already has a fade set, it needs to say so." Grilled the same day; every
recommendation was accepted. The terms **carry**, **fade-out half** and **fade-in half** are in
`CONTEXT.md`.

## The question

Transitions are keyed by track pair (ADR 0005), so a move never loses one: A → B stays in the
store and plays again wherever A meets B. But the boundaries the move creates are usually
unauthored, so work the user did by ear stops being heard. What should a move offer, and how does
it say when a new pair already has a transition?

## The example

The queue is `A B C … X Y`, and B is dragged between X and Y. The move breaks A → B, B → C and
X → Y. It creates A → C, X → B and B → Y.

## Decisions

1. **Carry is a copy, half by half.** Each new adjacency takes the **fade-out half** its outgoing
   row had at its old next boundary, and the **fade-in half** its incoming row had at its old
   previous boundary. So A → C gets A's fade-out from A → B and C's fade-in from B → C. X → B gets
   X's fade-out from X → Y and B's fade-in from A → B. B → Y gets B's fade-out from B → C and Y's
   fade-in from X → Y. A new adjacency with nothing to take is not written.
2. **The old adjacencies keep their transitions.** A → B may be heard in another playlist, and it
   is right again if B is dragged back.
3. **The curve** comes from the outgoing row's old transition. If that one set none, it comes from
   the incoming row's. If neither set one, it stays blank and the default curve plays.
4. **Each half keeps its layer.** Parameters from the global transition go to the new pair's
   global transition. Parameters from the open playlist's override go to the new pair's override
   in that playlist. With nothing overridden, a carry writes globally.
5. **Already authored** means something authored would be heard for the new adjacency in the open
   playlist, from either layer. It is kept unless the user ticks **Replace**. Replace swaps the
   whole transition in the layer being heard: if the pair has an override in the open playlist,
   the carried halves, merged, become that override and the global transition is untouched.
   Otherwise the carry writes layer by layer as in decision 4, replacing the global transition.
6. **Which boundaries are new.** Every boundary between rows that exists after the move and did not
   before. It follows rows, not tracks, so a track in several rows is fine. A block moved in one
   piece keeps its inner boundaries, which are not new. A scattered selection (B and E dropped
   between X and Y) makes inner boundaries like B → E, and they are new. Boundaries are the ones
   the snapshot's adjacency rules give: unreadable rows passed over, repeat-all's wrap included.
   Under shuffle, it is the order the user sees.
7. **What triggers it:** a row reorder by dragging, and a section move. Not a drop of new tracks
   and not a removal; those could get the same offer later.
8. **When it asks.** A library setting, "When a move breaks authored transitions": **Ask**
   (default), **Always carry**, **Never carry**. Under Ask, the dialog appears only when at least
   one authored half would land on a new adjacency. Under Always carry, it still asks when a new
   adjacency is already authored, because that is the case the user wants to be told about. Under
   Never carry, a move is a plain move.
9. **The dialog comes before the move.** The rows stay where they are until the user answers. It
   lists each new adjacency on its own line with the halves it would get. A line that is already
   authored says so ("already has a transition", or "already has a transition in this playlist")
   and has a **Replace** tick box, off by default. The buttons are **Carry**, **Don't carry** and
   **Cancel**. Cancel leaves the queue as it was. A **Don't ask again** box sets the setting to
   Always carry or Never carry, by the button pressed; it is not offered with Cancel.
10. **One intent, one engine call.** The move and the carry are a single engine call. The carry is
    one undo step that restores every pair it wrote; the move itself has no undo, as now. One
    notice: "Carried transitions to 3 new pairs (1 kept)."
11. **Both hosts**, through the shared shell.

No ADR: a carried transition is an ordinary transition, edited or removed like any other, so
nothing here is hard to reverse.

## Where the logic lives

- **Engine:** working out the new adjacencies and their halves, whether to ask, the write, the undo
  step, the notice, the setting.
- **Shell:** the dialog. It renders the engine's plan line by line and passes back the user's
  answer. It never works out a boundary or a half itself (CLAUDE.md tests 1 and 2).

## Issues

1. [The engine plans a carry](issues/01-the-engine-plans-a-carry.md)
2. [A move carries in one call](issues/02-a-move-carries-in-one-call.md)
3. [The carry setting](issues/03-the-carry-setting.md)
4. [The carry dialog](issues/04-the-carry-dialog.md)
