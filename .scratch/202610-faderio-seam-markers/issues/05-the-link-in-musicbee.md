# 05: The link in MusicBee

Status: resolved
Blocked by: 04

The MusicBee plugin. See the [spec](../spec.md), decisions 5 and 7.

## What to build

- **Wide layout**: issue 04's gap panel between `_queuePane` and `_editorPane` in `PluginView`,
  from construction. `PlaceEditor` resets the editor pane's width to 340 on every resize, so
  the gap is its own control, not padding on the pane.
- **`NarrowWidth`** from 560 to 576, so the queue keeps the room it has today.
- **Narrow layout** (the editor as a page in the lists' place): no gap panel, tab or outline.
  The queue keeps its seam buttons and tint; the editor's page is linked only by its numbered
  halves.
- The Now Playing takeover's fresh `PluginView` gets the same.

## Tests and docs

- `PluginTests`: the gap panel sits between the queue and the editor in the wide layout and
  survives a resize; the narrow layout switches at 576 px and shows no gap panel.
- `docs/smoke.md`: MusicBee Looks items for the open pair, wide and narrow.

## Acceptance

- [x] In MusicBee, wide: the same link as the standalone app, the gap panel between; narrow
      (under 576 px): the seam buttons and tint only.
- [x] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs in MusicBee and is reported item by item.

## Comments

2026-10-12, built. `PluginView` makes a `PairLink` as `MainForm` does and docks its gap between the
queue's pane and the editor's, so `PlaceEditor` setting the pane's width afresh never touches it.
`NarrowWidth` is `560 + PairLink.GapWidth`. A new `PairLink.Linked`, false in the sidebar layout,
takes the gap to 0 px wide and the tab, the outline, the stub and the editor's tint away at once;
the queue's rows keep their tint, which `QueueRowStyle` gives. Set true again, the gap is back at
once and the link at the next render. The Now Playing takeover makes its view by the same
`Host.AttachView`, so it has the same.

Why 0 px, not hidden: the view is made at its default 150 px, so narrow, before MusicBee's panel
sizes it. A hidden gap has no window when the others' are made, and WinForms, which keeps its
controls in the order of their windows, moved it to the editor's far side
(`InAMainPanel_TheGap_SitsBetweenTheQueueAndTheEditor_AsThePanelIsSized` failed that way first).

Looks smoke in the portable MusicBee (Fadem in the FADERIO tab's main panel, a light skin), by
captures and posted messages: the gap between the panes; with 8 → 9 open, the tint from the rows
over the queue's frame and the gap into the tinted editor, one rounded outline, the seam button on
it, no step at 6×; the window restored to 1200 × 520, the stub "▼ 8–9 Leave It … → I'm Running" at
the queue's bottom edge, and its click scrolling the pair in with the selection unchanged, the band
over the scroll bar; maximised again, whole. Not seen in MusicBee: the sidebar layout (in this
setup the panel shows only in the FADERIO tab, and MusicBee's window is never under 790 px), so
the sidebar's half rests on the headless tests; and a dark skin.

Found, not fixed (issue 04's shape): with the pair at the queue's very bottom, the tab's bottom edge
is the list's, 2 px above the editor's, and the outline steps down 2 px where they meet. Also: at
MusicBee's 790 px minimum the queue is about 220 px wide, its seam column off to the side.
