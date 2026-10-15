# 02: A move carries in one call

Status: ready-for-agent
Blocked by: 01

Core only. See the [spec](../spec.md), decisions 4, 5 and 10.

## What to build

- The move entry points (`Move`, `MoveAfter`, `MoveSection`) take the user's answer: carry or not,
  and which already-authored pairs to replace. One call moves the rows and writes the carry, so
  the shell never sequences a move and an author (CLAUDE.md test 1).
- The write uses 01's plan, made inside the same call, so nothing can change between plan and
  write.
- Writes layer by layer; Replace writes the layer being heard (decision 5). Kept pairs are not
  touched.
- The whole carry is one undo step in transition history, restoring every pair it wrote. The
  history today records one adjacency per entry, so it needs a grouped entry; `Undo()` and
  `Redo()` must still tell the shell which pair to show (the first is fine).
- One notice: "Carried transitions to N new pairs (K kept)."
- The pending boundary reschedules after the write, as a move already does; a transition in flight
  plays out.

## Tests

On net8.0 and net48 x86:

- The spec's example, carried: the store holds the three new pairs, and A → B, B → C, X → Y are
  unchanged.
- Don't carry: a plain move, the store unchanged.
- Kept and replaced conflicts, global and playlist-only.
- One undo restores every carried pair; redo writes them again.
- The notice.
- Carrying into the next boundary before its fade starts: the carried transition is the one heard.

## Acceptance

- [ ] As above, through the existing move entry points with no second engine call.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts.
