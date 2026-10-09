# 02: Sync editor sits at the right of the editor's title

Status: resolved
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "Move sync editor checkbox into far
right of the transition editor title area".

## What to build

**Sync editor** decides what the transition editor shows, so it belongs with the editor, not the
transport. Today it is on the track's line in the plugin (`PluginView._syncEditor`, docked right
beside ⚙) and after the transport buttons in the App (`MainForm._syncEditor`).

Move it into the editor's heading (`TransitionEditorPanel`, the panel holding `_title`), at its far
right, on the same line as the boundary's name ("No boundary selected" / "a → b"). The title keeps
the rest of the line and is cut short with an ellipsis before it pushes the checkbox off.

The checkbox stays the hosts' to wire (it toggles an engine setting through the host, as now), so
the panel offers a place for it rather than owning its behaviour, or owns it if that is simpler
and still one engine call per click. Its tooltip (`Presenter.SyncEditorTip`) is unchanged.

## Acceptance

- [ ] In both hosts, Sync editor is at the far right of the editor's title line, and gone from
      where it was.
- [ ] Ticking it still opens the editor on each starting track's transition, as before.
- [ ] A long boundary name ends in "…" before the checkbox; the checkbox is never cut.
- [ ] In MusicBee's sidebar (about 284 px) the title line still fits on one line.
- [ ] Tests that find the checkbox by its old place updated; smoke items that name its place updated.

## Comments

### 2026-10-09, built

The user chose the pane title row (TRANSITION EDITOR), not the boundary name's line, so the name
and the traffic light keep theirs. The App gains that pane title. In the plugin's sidebar the title
is hidden while the editor is a page, so Sync editor sits at the left of its Close editor bar
there (`PlaceEditor`). The App's editor scrolls a little again at 1240 x 660: the title row is
22 px.
