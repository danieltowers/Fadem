# 04: The link: gap, tab, outline and stub

Status: ready-for-agent
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

- [ ] The open pair is tinted, the tint runs into a tinted editor, and one rounded outline goes
      round both, broken at any seam button it crosses.
- [ ] The tab goes to the match heard next, through the stub when it is out of view; rows
      between, a wrap, repeat-one and a collapsed section draw as decision 3 says.
- [ ] At 960 px the queue's columns still fit; on CalciumMetro the tint and outline read.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs and is reported item by item.

## Comments
