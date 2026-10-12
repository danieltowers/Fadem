# 05: The link in MusicBee

Status: ready-for-agent
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

- [ ] In MusicBee, wide: the same link as the standalone app, the gap panel between; narrow
      (under 576 px): the seam buttons and tint only.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs in MusicBee and is reported item by item.

## Comments
