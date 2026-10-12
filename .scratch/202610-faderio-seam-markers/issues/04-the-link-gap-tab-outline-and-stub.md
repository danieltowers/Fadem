# 04: The link: gap, tab, outline and stub

Status: resolved
Blocked by: 02, 03

Shell and the standalone app. See the [spec](../spec.md), "The answer: G3 soft" (the tint
runs into the editor, one outline round both, a gap for the tab, a pair out of view) and
decisions 3, 4 and 7, with the stub's paragraph after them. The look is `round-7-short.png`
and `round-7-deep.png` on `prototype/seam-markers`, where an overlay window drew it; here it is
painted in pieces, with no overlay.

## What to build

- **The gap panel**: 16 px, docked right between `_queueList` and `editorPane` in `MainForm`,
  there from construction whether or not a pair is open. It paints the tab: the tint, level
  with the tab's pair, straight across, and the outline's top and bottom edges. It repaints
  when the list scrolls or its rows change.
- **The queue's piece**: the outline round the tab's match (the query's first), 2 px in the Well
  mixed 60 % toward the accent, 8 px corners, anti-aliased, broken where it crosses a seam
  button. The list's border goes or is painted over where the tint runs through it.
- **The editor's piece**: the whole editor tinted, its heading strip, labels, check boxes and
  radio buttons included (text boxes, keys and lists keep their own), and its frame in the
  outline's colour, open on the side the tab joins.
- **Decision 3's cases**: rows between (one outline round the span, only the pair tinted);
  repeat-all's last → first (each row outlined on its own, the tab from the last); repeat-one
  (one row); a collapsed section (its header stands in).
- **The stub**: at the queue's top or bottom edge while the tab's match is out of view, "▲
  20–21 Last Light → Northbound" (▼ below), in the tint, the tab running from it. A click
  scrolls the pair into view and leaves the selection alone.
- **A half under the mouse lights its row** in the queue: the tint mixed 35 % further toward
  the accent.
- The pieces meet with no step or seam. Nothing here computes from engine values: the rows
  come from issue 01's query, the positions from the controls.

## Tests and docs

- `HeadlessFormTests.DroppingPastTheLastColumn_LandsByTheRowUnderTheCursor` failed under the
  prototype, cause unconfirmed (the spec has the two candidates). It must pass here.
- New headless tests: the gap panel sits between the list and the editor in both directions of
  a resize; the tab follows the first match; the stub shows while that match is out of view
  and its click scrolls without selecting.
- `docs/smoke.md`: new Looks items for the open pair (in view, twice, across an unreadable row,
  wrapped under repeat-all, under repeat-one, collapsed, out of view), on CalciumMetro, and
  at 960 px with the seam column in view and no sideways scroll bar.

## Acceptance

- [x] The open pair is tinted, the tint runs into a tinted editor, and one rounded outline goes
      round both, broken at any seam button it crosses.
- [x] The tab goes to the match heard next, through the stub when it is out of view; rows
      between, a wrap, repeat-one and a collapsed section draw as decision 3 says.
- [x] At 960 px the queue's columns still fit; on CalciumMetro the tint and outline read.
- [x] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs and is reported item by item.

## Comments

2026-10-12, built. `PairLink` (shell, for both hosts; issue 05 wires MusicBee) holds which rows the
tab joins, from `RowPairsOf`'s first match, and works out one shape in the window's terms from the
controls' places: each piece paints its part of it. The list over its rows, in `QueueRowStyle`'s
off-screen paint (a new `Overlay`, drawn on every `WM_PRINTCLIENT`); the list's frame and scroll
bar level with the tab, by a hook installed last so it draws over the theme's hairline; the gap;
the stub, a control over the list; and the editor, which now paints its own frame (a hairline in
Edge, or the link's). Whenever the shape moves, a scroll found by the list's own paint or a resize,
every piece is drawn again. The shape is placed by the controls' `Location`s, not screen
positions: in the headless form the list's window is made before the form's and sits on WinForms'
parking window, so screen positions disagree. A collapsed section's header is tinted through a
new `StudioTheme.SetHeaderGround`.

Readings the spec left open: with rows between, the tab and the band past the cells run level
with the whole span, the row between included, while its cells keep their own colours. The band
crosses the list's scroll bar, painted over, as the prototype's did. The half that lights its row
is the half's title line, as in the prototype. Under repeat-one the stub has one number, "▲ 3
Night Bus → Night Bus". The stub is never wider than the rows leave room for.

Review: a doc comment misplaced in `Presenter`; the stub's numbers now from
`Presenter.RowNumber`; one `SeamBox` for the button's drawing and the outline's break; a bug, the
first row's own outline under repeat-all lost while the last row's tab was stubbed, now pinned by a
test.

Looks smoke by `link-smoke.ps1` (captures, UI Automation, posted messages, the cursor set on the
stub for its click): in view; twice, scrolled away, with the stub above and the second match
tinted; the stub's click scrolling the pair in, nothing selected; 960 px with no sideways scroll
bar; across a row playback passed over; repeat-all's last → first from the queue's menu;
repeat-one's this → this; a row into a collapsed section, and both rows in one; CalciumMetro. The
pieces meet with no step at 6×. Not shown by posted messages: the lit half (headless test only),
and whether scrolling by the thumb or the wheel leaves any trace or flickers: for the eye.
