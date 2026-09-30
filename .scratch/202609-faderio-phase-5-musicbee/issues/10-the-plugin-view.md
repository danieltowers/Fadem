# 10: The plugin view: playlists, queue and transport

Status: needs-triage
Blocked by: 05, 09

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decisions 5 and 7,
ADR 0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

A plugin view or dockable panel in MusicBee, composed from the shared controls, showing the
library's playlists, the open playlist's queue and faderio's own transport. It replaces the
spike's two Tools menu items as the way to play. Edits are written back to the library.
faderio playlists are not listed in MusicBee's playlist tree.

Carried from the spike:

- The spike shows notices as message boxes, which are modal and stack. The view gives notices
  a place, as standalone faderio's strip does.
- The spike has nowhere to show the output mode in use (see [13](13-the-output-device-setting.md)).
- Choosing or switching the open playlist inside MusicBee arrives here.

## Acceptance criteria

- [ ] The listener can open a playlist, play, pause, stop, skip and seek from the view.
- [ ] The view holds no engine state and computes nothing from engine values.
- [ ] The MusicBee Looks smoke covers the view, item by item.
