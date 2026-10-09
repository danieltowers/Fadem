# 06: The accent only where it marks a state

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "remove highlight colour from track
timeline and headings and counter".

## What to build (decided 2026-10-09, grilling)

The palette's Accent (`Palette.Accent`, blue from issue 07) is kept for what marks a state, and
taken off what does not:

- **Headings go to Text**, still small bold capitals: the pane titles QUEUE and PLAYLISTS
  (`StudioTheme`, "a section label, lit") and the Settings groups' titles (the group box title
  drawn in `StudioTheme`). Disabled stays Dim.
- **The counter goes to Text**: the LED time readout (`Readout`, `readout.Led`), in its LED font.
  The seconds line under it stays Dim.
- **Refusal and warning text goes to Mark**, the red of the seek bar's fade marks (`Ink.Warning` in
  `StudioTheme.InkOf`).
- **These keep the accent:** the playing row (`QueueRowStyle`), the open playlist
  (`PlaylistSidebar`), the drag-drop insert line (`QueueDrag`), lit transport keys, ticked
  checkboxes, menu check marks (`StudioRenderer`), and the notices link.
- **The timeline is not changed here.** The seek bar's played part, thumb and fade wedges stay on
  the accent (so they turn blue with issue 07). What they become is deferred to issue 13.

## Acceptance

- [ ] QUEUE, PLAYLISTS and the Settings groups' titles in Text; the LED readout in Text; a refusal
      in Mark red; in both hosts, on a dark and a light skin.
- [ ] Each use listed as keeping the accent still shows it.
- [ ] The seek bar unchanged apart from issue 07's colour.
- [ ] Theme tests that pinned these uses to the accent updated; `docs/smoke.md`'s studio theme
      items updated to match.
