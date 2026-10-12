# 01: The open pair is a tab on the editor

Status: ready-for-agent
Blocked by: none

Replaces the queue's ✎ column, bold where a transition is saved and plain where not, and the
editor's traffic light. The look was settled by a prototype on the branch
`prototype/seam-markers` (rounds 1–8, 2026-10-12), never for main: the code is
`src/Fadem.Shell/SeamPrototype.cs` there, with hooks marked `// PROTOTYPE` in `QueueRowStyle`,
`TransitionEditorPanel` and `MainForm`, and each round's capture is
`.scratch/202610-seam-prototype/round-N.png` on that branch. G3 soft is
`round-7-short.png` and `round-7-deep.png`. The prototype matches the queue's tracks to the
editor's in the shell, which test 2 of CLAUDE.md forbids, so its look carries over and its
code does not.

## The question

The ✎ weights read poorly: bold and plain pencils do not say "saved" and "not saved", and
nothing in the queue says which pair the editor has open. The traffic light's colours had to
be learned, and it sat far from the tracks it was about. What shows the user, at a glance,
which seams have a transition, which pair the editor is on, and whether what is playing is
part of it?

## The answer: G3 soft

- **Seam buttons.** The ✎ column holds a button on each seam, centred on the line between
  the two rows, half in each. A saved transition is a key with a crossfade mark (⋈: the
  outgoing ramp falling, the incoming rising over it). An unsaved seam shows **+** only while
  the mouse is on the row above it or on the button, and is otherwise empty. The open pair's
  seam is filled in the accent, its mark in white. The last row has no seam under it. The
  whole column is the buttons: a click in a cell's lower half is that row's seam into the next,
  in its upper half the seam above. Over a button the cursor is the hand and the tooltip reads
  "Edit the transition: *a* → *b*" or "Add a transition: *a* → *b*". Under the mouse a button
  is raised.
- **The open pair is tinted.** Its two rows are on the Well mixed 16 % toward the accent; a
  playing row's accent text is the text colour there.
- **The tint runs straight into the editor.** It carries across the gap from the rows' cells
  to the editor, level with the pair, with no bend. The whole editor is in it: its ground,
  heading strip, labels, check boxes and radio buttons; the text boxes, keys and lists keep
  their own.
- **One outline round both.** 2 px, the Well mixed 60 % toward the accent, every corner
  rounded at 8 px inside and out, anti-aliased. Where it crosses a seam button on the pair's
  top or bottom edge it breaks, so the button sits on it.
- **A gap for the tab.** The editor's pane sits 16 px further from the queue, so the tab
  reads as a link.
- **The halves are numbered** as the queue numbers its rows: "20 · Last Light" above the
  fade-out, "21 · Northbound" above the fade-in. The half heard reads "🔊 20 · Last Light ·
  playing", in the text colour; the other stays dim. The mouse on a half lights its row in
  the queue (the tint mixed 35 % further toward the accent).
- **A status line for the light's red**, under the selector, dim: "Playing *x*, not in this
  transition" (`AdjacencyHeard.OtherTrack`), and "*y* plays next, not *b*"
  (`OutgoingIntoAnother`). The light's green and amber are the speaker on the half heard.
  Nothing heard, no line.
- **A pair out of view.** A stub at the queue's top or bottom edge, "▲ 20–21 Last Light →
  Northbound" (▼ below), in the tint, with the tab running from it; a click scrolls the pair
  into view and leaves the selection as it was. Built in round 4; kept as built (decisions,
  below).

Rejected on the way, with the reason given:

- A "]" bracket over the pair (round 1): the buttons were not obvious.
- Two kinds of edit button, ✎ and the seam's (round 2): "why are there two types of edit
  buttons?"
- The tab bending up into a tinted heading (round 4, F2): "it looks odd when there's a big
  list."
- A spine down the editor's edge (G2), or from the heading to the tab (G4); G3 at a 28 %
  tint. G3 soft was chosen, its heading tinted too.

## Decisions

Settled with the user, 2026-10-12.

1. **Which rows are the open pair: an engine query.** The editor holds an `Adjacency`, two
   tracks; the queue shows rows. Matching them in the shell breaks test 2, and keeping the row
   the editor was opened from fails when Sync editor or the manager opens it, with no row. So
   the engine answers it: given an adjacency, the row pairs whose this → next is that
   adjacency, each as its outgoing and its incoming row's `RowId`, both given so the shell never
   works out which row comes next. It goes by `AdjacenciesOf`'s rules, as ⏭ does: `Next`, and
   `Self` under repeat-one, matched as `Heard` matches (`SamePair`), read from the published
   snapshot without the lock, since the queue asks on every paint. Every match is returned, in
   the order playback reaches them from the playing row (from the top when nothing plays), so
   the shell takes the first for decision 2 without ordering them itself. `Next` skips
   unreadable rows and wraps under repeat-all, so a pair need not be two touching rows:
   decision 3.
2. **The same pair twice: all tinted, one tab.** Every match is tinted and its seam button
   filled, since one edit changes them all. The outline and the tab go to the query's first
   match, the one heard next.
3. **Pairs that are not neighbours.**
   - *Rows between* (an unreadable row playback skips, a section header): one outline round
     everything from the outgoing row to the incoming one; only the two pair rows are tinted,
     and what lies between keeps its own colours inside the outline. The seam button stays on
     the outgoing row's bottom edge, where that pair's ✎ is today.
   - *Repeat-all's last → first*: each row tinted and outlined on its own; the tab joins the
     outgoing (last) row, through the stub when it is out of view, so the tab always leaves
     from the side of the editor's upper half, the fade-out.
   - *Repeat-one's this → this*: the one row tinted and outlined, the tab joining it; no seam
     button filled, since this → this is not a seam between rows.
   - *In a collapsed section*: the section's header stands in for the hidden rows, tinted and
     outlined with the tab joining it; with the other row in view, the outline runs from it to
     the header as with rows between. A click on the header still only expands; opening a pair
     never expands a section.
4. **How the link is drawn: painted pieces, no overlay window.** The queue paints the pair's
   tint and its part of the outline, in `QueueRowStyle`'s off-screen paint; a 16 px gap panel
   between the queue and the editor's pane paints the tab, and repaints when the list scrolls;
   the editor paints its own frame. In both hosts the queue and the editor's pane are sibling
   children of one parent (`MainForm`, `PluginView`), Fill and Dock Right, so the gap panel
   docks right between them. The list's border sits where the tint runs through: it goes, or
   is painted over. The pieces must meet exactly; the Looks smoke checks it. The prototype's
   layered window was never run inside MusicBee, where it would also have to follow MusicBee
   moving the panel and could sit over MusicBee's menus.
5. **MusicBee's narrow layout: no link.** Under the narrow width the editor is a page in the
   lists' place, with Close editor above it, and following playback never opens it. The queue
   keeps its seam buttons and the tint; on the editor's page the numbered halves are the only
   link, with no tab or outline.
6. **A light skin: checked in the smoke.** The tint and the outline are mixes of the palette's
   Well and Accent, so they follow the skin. A Looks item, "the open pair on CalciumMetro",
   checks them; the mixes change only if it fails.
7. **The 16 px gap: always, out of the queue.** In both side-by-side layouts the gap is part of
   the layout from construction, whether or not a pair is open, so the queue's columns never
   jump as the editor opens or closes. At the 960 px minimum the queue had about 59 px of slack
   before a sideways scroll bar, about 43 with the gap (worked out from the layout constants,
   not measured: the 960 px smoke item measures it). MusicBee's `PlaceEditor` resets the
   pane's width on every resize, so the gap must be its own control, not the prototype's
   padding; `NarrowWidth` goes from 560 to 576 so the queue keeps the room it has today.

The stub, reviewed with the same decisions: kept as built; it follows the tab's match, the one
heard next, even while another match is in view, so scrolling never moves the tab to another
row; a click scrolls the pair into view and leaves the selection alone.

## What to build

Split into issues if it is more than one session's work.

- Engine: decision 1's query, with tests on both runtimes: a match, none, the pair twice (in
  playback order, from the playing row and from the top), a stale adjacency, repeat-all's
  last → first, repeat-one's this → this, and a pair across an unreadable row.
- `QueueRowStyle`: the seam buttons and the pairs' tint in place of the ✎ glyph
  (`Presenter.EditGlyph`, the bold and plain fonts go); `EditButtonAt` becomes the seam under a
  point. The outline round the first match, as decision 3 draws it, and the stub. On hover,
  invalidate only the cells that change, as round 8 found: invalidating the whole list
  flickered.
- The gap panel and the tab (decision 4), in `MainForm` and in `PluginView`'s wide layout;
  `NarrowWidth` 576.
- `TransitionEditorPanel`: without `TrafficLight` (and `StudioTheme`'s case for it); the
  numbered halves, the speaker, the status line, the tint and the frame.

## Tests and docs that change

- `EditorTrafficLightTests` goes; its cases become tests of the speaker and the status line,
  one for each `AdjacencyHeard`.
- `HeadlessFormTests.AClickElsewhereOnARow_OpensNothing`: its click in the ✎ cell's upper half
  is now the seam above.
- `HeadlessFormTests.DroppingPastTheLastColumn_LandsByTheRowUnderTheCursor` failed under the
  prototype. Not confirmed why: possibly the gap shrinking the list after its handle exists,
  when `QueueColumns` defers its fit through `BeginInvoke` and a headless test pumps no
  messages; possibly the prototype's static fields shared between forms. A gap present from
  construction should not hit the first.
- `HeadlessFormTests.Columns` and `EditorThemeTests` refer to the ✎ glyph and the light.
- `docs/smoke.md`: the ✎ item, the traffic light item, the editor theme item's light, and
  "Each half names its track"; new items for the open pair (twice, across a row, wrapped,
  collapsed, out of view), on CalciumMetro, and in MusicBee wide and narrow.

## Acceptance

- [ ] A saved seam shows ⋈ and an unsaved one nothing until the mouse is on its row or button;
      a click on either opens that pair, and saving or removing flips it.
- [ ] The open pair is tinted, the tint runs into a tinted editor, and one rounded outline goes
      round both, broken at any seam button it crosses.
- [ ] Every match is tinted; the tab goes to the one heard next, through the stub when out of
      view; rows between, a wrap, repeat-one and a collapsed section draw as decision 3 says.
- [ ] The halves are numbered; the half heard has 🔊 and "· playing"; the status line says
      when something else plays or another track comes next. No traffic light.
- [ ] In MusicBee, wide: the same link, the gap panel between; narrow (under 576 px): the seam
      buttons and tint only.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs, with its items updated, at 960 px and on CalciumMetro.

## Comments

- 2026-10-12: decision 1 is the engine query, as the user chose.
- 2026-10-12: decisions 2–7 and the stub settled with the user in one grilling; Status to
  ready-for-agent.
