# 02: The queue's seam buttons and the tint

Status: ready-for-agent
Blocked by: 01

Shell, both hosts' queues. See the [spec](../spec.md), "The answer: G3 soft" (seam buttons,
the open pair is tinted) and decision 2. The look is `round-7-short.png` on
`prototype/seam-markers`; its code there does not carry over.

## What to build

- **Seam buttons in place of the ✎ glyph** (`QueueRowStyle`; `Presenter.EditGlyph` and the
  bold and plain fonts go). One button on each seam, centred on the line between two rows,
  half in each: ⋈ where `QueueRow.NextAuthored` is true; **+** where it is false, only while
  the mouse is on the row above or on the button; nothing on the last row. Raised under the
  mouse; the hand cursor and the tooltip, "Edit the transition: *a* → *b*" or "Add a
  transition: *a* → *b*".
- **The column is the buttons.** A click in a cell's lower half is that row's seam into the
  next, in its upper half the seam above. `EditButtonAt` becomes the seam under a point, and
  opens the editor on the row's `Next` from `AdjacenciesOf`, as ✎ does today.
- **The open pair's tint**, from issue 01's query for the editor's adjacency: every match's two
  rows on the Well mixed 16 % toward the accent, a playing row's accent text in the text
  colour; every match's seam button filled in the accent, its mark in white. A repeat-one
  this → this tints its row and fills no button.
- **Hover repaints only what changes**: the two seam cells under the old and the new mouse
  position, as round 8 found. Invalidating the whole list flickered.
- Both hosts get it, through `QueueRowStyle`. No outline, tab or stub yet (issue 04).

## Tests and docs

- `HeadlessFormTests.AClickElsewhereOnARow_OpensNothing`: its click in the ✎ cell's upper half
  now opens the seam above; keep a click outside the column opening nothing.
- `HeadlessFormTests.Columns`, `PluginTests` (✎ and its bold font, around
  `PluginTests.Editor.cs:343`) and `Presenter`'s `EditGlyph` tests: the seam button instead.
- New: a headless test that the open pair's rows, and each match's, are tinted and its seam
  filled; that a lower-half and an upper-half click open the right adjacency.
- `docs/smoke.md`: the ✎ item becomes the seam buttons (⋈, + on hover, the filled seam, the
  tooltip).

## Acceptance

- [ ] A saved seam shows ⋈ and an unsaved one nothing until the mouse is on its row or button;
      a click on either opens that pair, and saving or removing flips it.
- [ ] The open pair's rows are tinted and its seam filled, at every match.
- [ ] Moving the mouse down the list does not flicker.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs and is reported item by item.

## Comments
