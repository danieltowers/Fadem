# 05: The transition editor saves itself

Status: resolved
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

- [x] The editor has no Save or Revert button, in either host.
- [x] A finished change is saved without a click.
- [x] Undo and Redo, buttons and Ctrl+Z / Ctrl+Y, step through every transition change.
- [x] Leaving the editor never prompts.

## Comments

- **Resolved 2026-10-02.** Built as specified above.
  - **The panel:** `_save` and `_revert` became `_undo` and `_redo`. A box's TextChanged marks
    it pending; Leave or Enter runs `Commit`, which saves only a pending box whose fields
    differ from the store. Capture, the nudges, From end and the curve save at once
    (`CommitNow`). `UndoChange` and `RedoChange` commit first, then call the engine, then load
    the adjacency returned, in the step's layer when that is the open playlist's.
    `ProcessCmdKey` maps Ctrl+Z and Ctrl+Y. `Refresh` sets the buttons from
    `NextUndo`/`NextRedo`, with tooltips from `HistoryStep.Describe`. Remove transition and
    Remove override reload the fields.
  - **Gone:** `UnsavedEdits`, `ConfirmLeave`, `Dialogs.AskUnsavedEdits` and both Compat
    dialogs, the hosts' `AskUnsavedEdits`. `PlaylistSidebar` takes an `Action` to leave the
    editor, since leaving can no longer be cancelled.
  - **Tests:** `EditorSavesItselfTests`, 8 on both targets. The App lost 8 prompt tests (Cancel
    and Discard paths, Revert, "asks nothing"), and about 15 tests were rewritten to "a box
    being typed in is saved, then the editor moves on". The nudge and Remove override tests
    now expect saves. With the commit before an undo removed, the Ctrl+Z test fails.
  - **Smoke.** Standalone, the build on a scratch library, driven by UIA and sent messages:
    ✅ Undo, Redo and Remove transition with no Save or Revert, Undo and Redo greyed at first;
    ✅ text set without Enter saves nothing; Enter saves ("Saved tone-a → tone-b.", the store
    holding 0.5 s); ✅ Undo puts 0.5 back after a save of 1, Redo puts 1 back, Remove
    transition empties the fields and Undo refills them, each with its strip message. Text
    that is not a number: "Not saved: … is not a number of seconds", nothing stored (seen by
    accident: a SetFocus took two stray keystrokes into the box). Sent messages are needed:
    a posted Enter is eaten by WinForms' message-loop preprocessing, and SetFocus takes the
    user's focus. MusicBee: ✅ loads with no box, the editor shows Undo, Redo and Remove
    transition. ✅ (the user) saves, Undo and Redo, keys, and no prompt on leaving, in MusicBee.
    Not seen by the session: the tooltips (WinForms tooltips are not UIA help text) and
    Ctrl+Z / Ctrl+Y (WinForms reads modifiers from the real key state).
