# 05: The transition editor saves itself

Status: ready-for-agent
Blocked by: 04

Phase 6, improvements round 1, both hosts (the editor panel is shared). Raised 2026-10-02:
"lose the save button, changes in the transition panel should be saved automatically (needs
undo though)".

## What to build

The editor has no Save and no Revert. A change is saved when it is finished, and **Undo** and
**Redo** take the two buttons' place beside Remove transition, with Ctrl+Z and Ctrl+Y. Leaving
the editor never asks Save / Discard / Cancel again.

## Implementation decisions (settled with the user, 2026-10-02)

- **When a change saves: when it is finished.** A box saves on leaving it or on Enter. A
  button saves at once: capture, the − and + nudges, the curve list, From end, the layer
  selector's change of layer. A half-typed value is never saved or refused mid-typing. The save
  is the one `Author` or `AuthorOverride` call Save made, for the layer shown.
- **Undo and Redo, with Ctrl+Z and Ctrl+Y**, replace Save and Revert. They call 04's engine
  `Undo` and `Redo`, then load the adjacency returned, opening it if another was open. Live
  while the view says there is something to undo or redo; their tooltips say what.
- **A box being typed in** (its text not yet saved) when Ctrl+Z is pressed saves first, then
  undoes. That puts back the value from before the typing, which is what the user expects.
- **Text the engine refuses**, or that is not a number, shows its reason beside the field, as
  now, and the strip says "Not saved: …". Nothing is stored and the text stays to be fixed.
  Leaving the editor, the adjacency or the layer with such text drops it, with no prompt: what
  is stored stays.
- **Remove transition and Remove override** reload the fields from the store, now empty or
  global, instead of keeping an unsaved draft. Undo brings the transition back.
- **Leaving.** `ConfirmLeave` and its Save / Discard / Cancel dialog go, in both hosts:
  `UnsavedEdits`, `Dialogs.AskUnsavedEdits` and its two Compat dialogs, the hosts'
  `AskUnsavedEdits`. Opening another adjacency, switching layer, opening a playlist, closing
  the editor's page, switching library and closing the window save a finished-looking pending
  box first (as leaving it would), then go.
- **Keys.** Ctrl+Z and Ctrl+Y work while focus is anywhere in the editor. In a box, they
  replace the box's own text undo, by the rule above.
- **Notices** stay as the engine gives them: "Saved *A → B*." on each save, refusals and
  failures as now.
- **Preview** plays the fields as they stand, as now.

## Testing decisions

`Faderio.Shell.Tests` (both targets) for the shared panel, against a real engine:
- A duration typed and the box left saves it; Enter saves it; typing alone does not.
- A capture, a nudge, a curve choice and From end each save at once.
- Undo after a save puts the field and the store back, and Redo again; Undo with another
  adjacency's step opens that adjacency.
- Ctrl+Z in a box being typed in puts back the value from before the typing.
- Text that is not a number: its reason shown, nothing saved; opening another adjacency drops
  it with no prompt.
- Remove transition empties the fields; Undo refills them.
- The editor has no Save and no Revert; Undo and Redo follow the view.

The App's and the plugin's tests that answered the Save / Discard / Cancel prompt change to
the new rule: no prompt, a finished box saved. Both **Looks** halves change their editor items
the same way (no Save or Revert; Undo and Redo; no prompt on leaving).

## Acceptance criteria

- [ ] The editor has no Save or Revert button, in either host.
- [ ] A finished change is saved without a click.
- [ ] Undo and Redo, buttons and Ctrl+Z / Ctrl+Y, step through every transition change.
- [ ] Leaving the editor never prompts.

## Comments
