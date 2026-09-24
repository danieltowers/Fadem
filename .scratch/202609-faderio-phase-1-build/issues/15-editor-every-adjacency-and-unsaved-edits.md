# 15: Editor: every adjacency, and unsaved edits

Status: resolved
Blocked by: 06, 14

Decided in [What completes the editor?](../../202609-faderio-phase-1-restage/issues/09-what-completes-the-editor.md) (Entry points,
Leaving with unsaved edits). **Adjacency** in `CONTEXT.md` covers A→A.

## What to build

The row context menu:

- **This → next** targets the adjacency ⏭ would reach, published by issue 06. That wraps last
  → first under repeat-all, and the item is disabled on the last row with repeat off.
- **Previous → this** is new.
- **This → this** is new, and shown while repeat-one is on.

The engine supplies every target, so the shell never computes "next" or "previous".

Leaving with unsaved edits, by opening another adjacency or closing the editor, asks
**Save / Discard / Cancel**. With no unsaved edits, it asks nothing.

## Where the logic lives

- **Engine:** the previous and next targets for a row. Extend issue 06's target so a row's
  "previous" and "next" come from the engine.
- **Shell:** menu items, and the dirty check against the loaded transition.

## Acceptance

- [x] On the last row under repeat-all, this → next opens last → first. With repeat off, it is
      disabled.
- [x] Previous → this opens the adjacency into the row.
- [x] Under repeat-one, this → this opens A→A, and it fires at the track's natural end.
- [x] With unsaved edits, switching adjacency prompts. Cancel keeps the edits; Discard drops
      them.

## Tests

- **fake:** the target queries under each repeat mode and under shuffle.
- **form:** menu enablement and the prompt.

## Smoke

- **Looks:** all three menu items read clearly, and disabled ones look disabled.

## Comments

- From issue 14: the draft now includes the **From end** box and the **Fade curve**, so the
  dirty check must compare them as well as the four fields. `TransitionEditorPanel.Edit` is the
  load, and Revert calls it. After **Remove transition** the store holds nothing, so the filled
  fields are an unsaved draft and leaving should prompt.
- Built 2026-09-24.
  - **Adjacencies:** `PlaybackEngine.AdjacenciesOf(row)` returns `RowAdjacencies(Previous,
    Next, Self)` from the published snapshot, lock-free like `View`.
    - `Next` is the rule `View.NextAdjacency` uses from the current row.
    - `Previous` walks the queue's `IndexBefore`, now static, backwards, so it is the row
      whose ⏭ reaches this one.
    - Both pass over rows marked unreadable.
    - `Self` exists only under repeat-one. A marked row keeps all three, since its mark clears
      when its file opens again.
    - `Presenter.AdjacencyAt` and its `rowIndex + 1` are gone.
  - **Menu:** "Edit transition: this → next", "… previous → this" and "… this → this". The
    last is hidden unless repeat-one is on. All three are disabled unless exactly one row is
    selected.
  - **Unsaved edits:** the dirty check is `PlaybackEngine.IsAuthored(adjacency, draft)`: would
    saving the draft leave the store as it is? The shell keeps no copy of the stored transition,
    so a save the store could not write still counts as unsaved. The engine owns the rules:
    - a blank draft stores nothing;
    - a missing curve is equal-power;
    - a blank entry from before issue 14 counts as nothing.

    Text that is not a number is an unsaved edit too.
  - **Found on the way:** `TransitionEditorPanel` loaded a stored point through seconds, which
    loses a tick on about one value in sixteen. An untouched draft then differed from the
    store, and Revert then Save moved the point. It now loads through the new
    `FadePointSpec.Point`, exactly. A form test fails with the old load.
  - **Leaving:**
    - `TransitionEditorPanel.Open` asks Save / Discard / Cancel through a `TaskDialog`, and
      Save that is refused stays put.
    - Reopening the adjacency already open keeps its draft.
    - "Closing the editor" is closing the window, since the editor is a docked panel with no
      close control. `FormClosing` cancels on Cancel.
    - The private load is now `Load`; Revert calls it.
  - **Not done:** a duration stored at finer than a millisecond (only through the API) shows
    rounded, so opening it reads as unsaved. The editor has never held durations exactly.
  - ADR 0003 gains an addendum. `docs/smoke.md` replaces the menu item and adds the prompt item.
