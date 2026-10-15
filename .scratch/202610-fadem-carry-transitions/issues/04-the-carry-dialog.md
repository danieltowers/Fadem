# 04: The carry dialog

Status: ready-for-agent
Blocked by: 01, 02, 03

Shared shell, both hosts. See the [spec](../spec.md), decision 9.

## What to build

- On a row drop (`QueueDrag.MoveRows`) and a section move, the shell asks the engine for the plan
  (01). If the plan says ask, it shows the dialog before anything moves; otherwise it makes the
  move with the setting's answer.
- The dialog, themed like the section-name dialog in `Dialogs.cs`:
  - one line per new adjacency, "X → B: X's fade-out, B's fade-in", from the plan's values;
  - an already-authored line says "already has a transition" or "already has a transition in this
    playlist", with a **Replace** tick box, off by default;
  - **Carry**, **Don't carry**, **Cancel**; Cancel makes no engine call;
  - **Don't ask again**, which sets the setting (03) by the button pressed, and is not offered
    with Cancel.
- The answer goes to the move in one call (02).
- Behind a `Func<…>` property, as `ConfirmClear` is, so form tests replace it.

## Tests

Form tests: a drop that needs no asking moves at once; one that asks shows the plan's lines;
Carry, Don't carry and Cancel each make the right call (or none); Replace ticks reach the call;
Don't ask again sets the setting.

## Acceptance

- [ ] As above, in Fadem and in the MusicBee plugin.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts.
- [ ] The **Looks** half of `docs/smoke.md`, reported item by item, plus: the dialog over a real
      drag in both hosts, with a conflict line showing.
- **Sounds** (the user's): drag a track with authored transitions to a new place; the new
  boundaries play as carried.
