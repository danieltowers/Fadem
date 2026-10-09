# 04: No column lines behind the queue

Status: ready-for-agent
Blocked by: none

Phase 6, improvements round 2, both hosts. Raised 2026-10-09: "remove the divider lines in the
background of the playlist area".

## What to build

The queue list shows thin vertical rules at every column's edge, down through the rows and the
empty space below the last row. They are Windows' dark list theme (`DarkMode_Explorer`), its
`LVP_COLUMNDETAIL` part. `StudioTheme.EmptyGround` was meant to paint the empty part plain (studio
theme issue 03), but a screenshot of the plugin on 2026-10-09 still shows the rules, in the rows'
gaps and all the way down the empty part, with sections shown (the list in groups).

Find what still draws them, and remove them everywhere in the queue: between rows, below the last
row, with and without sections, in both hosts. The column header's own separators (drawn by
`QueueRowStyle.DrawHeader`) stay.

## Acceptance

- [ ] No vertical rules in the queue's rows or its empty part, with sections and without, in both
      hosts, on a dark and a light skin.
- [ ] Scroll bars stay dark on a dark skin (the reason the dark list theme is applied at all).
- [ ] A test that captures the list (as `SectionGroupThemeTests` does) finds no Edge-coloured
      column rule below the last row, and none between rows, grouped and not.
