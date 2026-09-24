# 15: Editor: every adjacency, and unsaved edits

Status: ready-for-agent
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

- [ ] On the last row under repeat-all, this → next opens last → first. With repeat off, it is
      disabled.
- [ ] Previous → this opens the adjacency into the row.
- [ ] Under repeat-one, this → this opens A→A, and it fires at the track's natural end.
- [ ] With unsaved edits, switching adjacency prompts. Cancel keeps the edits; Discard drops
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
