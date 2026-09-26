# 07: A row button opens the editor

Status: ready-for-agent
Blocked by: —

Decided in [D9](../../202609-faderio-interim-improvements/decisions.md). The "keeps its last
mode" half of D9 belongs to the playlists spec, where the mode is born.

## What happens today

The editor panel is reached by right-click → "Edit transition: this → next / previous → this /
this → this" (`MainForm.cs:177-197`), through `engine.AdjacenciesOf(RowId)`.

## What to build

- A rightmost column in the queue `ListView`, narrow, holding a glyph button (e.g. `⤳` or
  `✎`) on every row except the last. Clicking it opens the editor on the adjacency **this row →
  next row**, the same call the context menu's "this → next" makes today.
- The glyph shows filled/bold when that adjacency is authored (global transition exists),
  outlined when not — the queue already knows which boundaries are authored, or the panel's
  `IsAuthored` tells it; the builder records which.
- Under repeat-one the last row's button opens "this → this" (the menu already offers it).
- Unsaved edits in the panel prompt Save/Discard/Cancel as they do for the menu path.
- The context menu keeps all three entries; the button is a shortcut, not a replacement.

## Where the logic lives

- **Shell only.** Same engine calls as the menu.

## Acceptance

- [ ] Click the button on row 2 → the panel heads "Row 2 → Row 3" (whatever it reads today for
      the menu path).
- [ ] Last row has no button (or "this → this" under repeat-one).
- [ ] With the column configurable later (playlists spec), this column is fixed and not part
      of the show/hide set.

## Tests

- **form:** the button calls the same path as the menu's "this → next"; the last row has none.

## Smoke

- **Looks:** every row but the last shows the glyph at the far right; clicking one changes the
  panel heading.

## Comments
