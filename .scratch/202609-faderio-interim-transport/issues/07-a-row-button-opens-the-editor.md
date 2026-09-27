# 07: A row button opens the editor

Status: resolved
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
- ~~Under repeat-one the last row's button opens "this → this" (the menu already offers it).~~
  Superseded by D9 ("The last row has none"); see Comments.
- Unsaved edits in the panel prompt Save/Discard/Cancel as they do for the menu path.
- The context menu keeps all three entries; the button is a shortcut, not a replacement.

## Where the logic lives

- **Shell only.** Same engine calls as the menu.

## Acceptance

- [x] Click the button on row 2 → the panel heads "Row 2 → Row 3" (whatever it reads today for
      the menu path).
- [x] Last row has no button ~~(or "this → this" under repeat-one)~~ in any repeat mode (D9).
- [x] With the column configurable later (playlists spec), this column is fixed and not part
      of the show/hide set.

## Tests

- **form:** the button calls the same path as the menu's "this → next"; the last row has none.

## Smoke

- **Looks:** every row but the last shows the glyph at the far right; clicking one changes the
  panel heading.

## Comments

- Built 2026-09-27. **The last row never has a button.** The issue gave it "this → this" under
  repeat-one; D9 says the last row has none, and the decisions file wins. It would also have been
  odd: under repeat-one every row has a this → this, not only the last, and every other row's
  button still opens this → next. Under repeat-all the last row has a this → next (last → first)
  in the menu, but no button either. This → this and the wrap stay on the menu.
- **Where "authored" comes from: the engine.** The queue did not know which boundaries are
  authored, and the panel's `IsAuthored` compares a draft, so neither could say. Reading the store
  per row per tick from the shell would break placement tests 1 and 2. `QueueRow.NextAuthored`
  (`bool?`) says whether the row's `RowAdjacencies.Next` (same rule, unreadable rows passed over)
  has a stored transition; null where the row has none. The snapshot builds it from a memo keyed
  by the pair of `RowId`s (a row's track never changes), cleared on every store write, so a
  publish during an add reads the store only for new pairs. `Author` and a removal now publish,
  so the mark changes on Save / Remove transition.
- **Shell:** a fifth, 28 px column, `✎` in Segoe UI Symbol 11 pt (the list's 9 pt draws it at
  eight pixels): bold and black while authored, plain and grey while not. Its width can't be
  dragged. A click on the cell calls `EditTransitionAt`, the same path as the menu's this → next
  and `--edit-first`, so unsaved edits ask as they do there. A double-click on the button does
  not play the row. Cells are styled one by one now (`UseItemStyleForSubItems = false`), so the
  row marker and the unreadable grey are set on every cell.
- **Keeping it at the far right.** The column sits right of Length, which is only the list's
  far right when the columns fill it. The Title column now takes the slack (the Artist column
  gives way below a 100 px title), fitted after the list finishes sizing: fitted during the
  resize, the list kept a horizontal scroll bar for its old widths. At the 760 px minimum the
  button stays in view.
- ListView raises `MouseClick` at `Cursor.Position` (from NM_CLICK), not at the posted click's
  point, so a smoke that clicks the button must park the real cursor on it. Written into
  `docs/smoke.md`.
- Tests: `NextAuthoredTests` (5, engine: unauthored/last row, save and remove publish the mark,
  the mark follows moved rows, repeat-all wrap, unreadable row passed over and the voice's row
  agreeing) and five form tests (the button opens what the menu's this → next opens; rightmost
  column, none on the last row even under repeat-all; bold/grey; unsaved edits ask; a click
  elsewhere on the row is not a press).
