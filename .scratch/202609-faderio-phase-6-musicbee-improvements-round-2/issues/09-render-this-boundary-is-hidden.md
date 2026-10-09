# 09: Render this boundary is hidden

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "hide transition rendering button".

## What to build

Hide the transition editor's **Render this boundary** button (`TransitionEditorPanel._render`).
Keep it built and wired, as the hidden shuffle and repeat are (`Visible = false`), so it can come
back with one line. Its row in the editor's layout goes with it, and the height it frees goes to
the editor (it is the button the editor's height budget hid first).

Rendering stays in the engine and its tests; only the button goes.

## Acceptance

- [ ] No Render this boundary button in the editor, in either host; no empty gap where it was.
- [ ] Remove transition and the other buttons in its row, if any, keep their place and size.
- [ ] Shell and App tests that press the button set it visible first, or test the render through
      the panel's method; none skipped.
- [ ] Smoke items that use Render are marked as hidden, not deleted.
