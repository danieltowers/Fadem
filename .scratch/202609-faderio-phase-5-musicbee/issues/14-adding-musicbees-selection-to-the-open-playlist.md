# 14: Adding MusicBee's selection to the open playlist

Status: needs-triage
Blocked by: 05, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 9, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

A context-menu item in MusicBee that adds the selected tracks
(`Library_QueryFilesEx("domain=SelectedFiles")`) to faderio's open playlist. Every file is
probed, and rejections are listed together as **Add folder…** lists them. Drag-and-drop is out
of scope.

## Acceptance criteria

- [ ] The selection is added to the open playlist in one engine call.
- [ ] Rejected files are listed together, each naming what disqualifies it.
- [ ] Headless tests drive it through the plugin's entry point with a fake MusicBee.
