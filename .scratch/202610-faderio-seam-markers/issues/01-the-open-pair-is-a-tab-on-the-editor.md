# 01: The open pair is a tab on the editor

Status: needs-triage
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
  into view. Built in round 4 and working in G3 soft, but never reviewed on its own: check it
  with the user.

Rejected on the way, with the reason given:

- A "]" bracket over the pair (round 1): the buttons were not obvious.
- Two kinds of edit button, ✎ and the seam's (round 2): "why are there two types of edit
  buttons?"
- The tab bending up into a tinted heading (round 4, F2): "it looks odd when there's a big
  list."
- A spine down the editor's edge (G2), or from the heading to the tab (G4); G3 at a 28 %
  tint. G3 soft was chosen, its heading tinted too.

## Decisions to make before building

1. **Which rows are the open pair.** The editor holds an `Adjacency`, two tracks; the queue
   shows rows. Matching them in the shell breaks test 2. Either the engine answers it, a query
   giving the rows whose this → next is a given adjacency, or the editor keeps the row it was
   opened from as it keeps the adjacency. Opening by Sync editor and by the manager has no
   row, which favours the query.
2. **The same pair twice in a queue.** Mark every match, the first, or the one nearest the
   playing row?
3. **Pairs that are not neighbours.** Repeat-all's last → first, repeat-one's this → this,
   a pair split by a section header, a pair in a collapsed section. The outline assumes two
   touching rows.
4. **How the link is drawn.** No one control spans the queue, the gap and the editor, so the
   prototype draws on a click-through layered window over the main one
   (`UpdateLayeredWindow`, per-pixel alpha) and moves it after each render and each paint of
   the list. In MusicBee the plugin is a control inside MusicBee's window, where an owned
   top-level window may fight MusicBee's own popups and docking. The other way: each part
   paints its own piece (the queue its rows' outline, a gap panel the tab, the editor its
   frame).
5. **MusicBee's sidebar.** Narrow, the editor is a page over the lists, so there is nothing
   to link to. The seam buttons and the tint alone, or something else?
6. **A light skin.** Seen only on the dark studio palette. Check the 16 % tint and the outline
   on a light one (CalciumMetro).
7. **The 16 px gap** comes out of the queue. At the 960 px minimum width, the column fit
   (smoke: ✎ in view with no sideways scroll bar) must still hold.

## What to build

Once the decisions are made; split into issues if it is more than one session's work.

- Engine: decision 1's query, with tests on both runtimes.
- `QueueRowStyle`: the seam buttons and the pair's tint in place of the ✎ glyph
  (`Presenter.EditGlyph`, the bold and plain fonts go); `EditButtonAt` becomes the seam under a
  point. On hover, invalidate only the cells that change, as round 8 found: invalidating the
  whole list flickered.
- `TransitionEditorPanel`: without `TrafficLight` (and `StudioTheme`'s case for it); the
  numbered halves, the speaker, the status line and the tint.
- The link and the stub (decision 4), in both hosts as decision 5 says.

## Tests and docs that change

- `EditorTrafficLightTests` goes; its cases become tests of the speaker and the status line,
  one for each `AdjacencyHeard`.
- `HeadlessFormTests.AClickElsewhereOnARow_OpensNothing`: its click in the ✎ cell's upper half
  is now the seam above.
- `HeadlessFormTests.DroppingPastTheLastColumn_LandsByTheRowUnderTheCursor` failed under the
  prototype, probably the 16 px gap moving the queue's edge; not confirmed.
- `HeadlessFormTests.Columns` and `EditorThemeTests` refer to the ✎ glyph and the light.
- `docs/smoke.md`: the ✎ item, the traffic light item, the editor theme item's light, and
  "Each half names its track".

## Acceptance

- [ ] A saved seam shows ⋈ and an unsaved one nothing until the mouse is on its row or button;
      a click on either opens that pair, and saving or removing flips it.
- [ ] The open pair is tinted, the tint runs into a tinted editor, and one rounded outline goes
      round both, broken at any seam button it crosses.
- [ ] The halves are numbered; the half heard has 🔊 and "· playing"; the status line says
      when something else plays or another track comes next. No traffic light.
- [ ] Decisions 1–7 are each answered in code or in this file.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs, with its items updated.

## Comments
