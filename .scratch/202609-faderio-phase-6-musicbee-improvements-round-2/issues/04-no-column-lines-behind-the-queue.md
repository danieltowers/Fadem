# 04: No column lines behind the queue

Status: resolved
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

## Comments

### 2026-10-10, investigated: not reproduced in the current build

- The screenshot the issue describes is from the plugin at 08:45 on 2026-10-09 (section "F",
  rules down every column edge and in the one-pixel gaps between rows). That is two hours before
  `dfc5bba`, which refuses `LVN_HOTTRACK` (`NoHotRow`) so a grouped list no longer fades the row
  under the mouse in and out, and draws rows and headers on opaque bitmaps.
- A capture of the installed plugin today (built 2026-10-10 19:49), the queue in a section
  (FOO BAR) on Duophonic: no rules below the last row or between rows.
- Every paint path a test can drive leaves the ground plain: a full repaint, a part of the list
  invalidated, a cell changed, a row selected, a row made hot by `LVM_SETHOTITEM` with
  `NoHotRow` off. `WM_PRINTCLIENT`, with or without `PRF_ERASEBKGND`, paints no ground at all:
  the ground, and so the rules, come only from the list's erase, which `EmptyGround` answers.
- So nothing in `src` changed. `QueueColumnRuleTests` pins it: no rule below the last row,
  grouped and not, and none at a column's edge in the gaps between a section's rows, on Duophonic
  and CalciumMETRO, net8.0 and net48. With `EmptyGround`'s erase removed, 6 of its 6 cases fail.
- Not checked: a real mouse over the rows and headers. Automation can't make Windows' hover fades
  happen, and that is the path most likely to have drawn the rules. Scroll bars weren't checked
  either, but no code changed.

**For the user:** in MusicBee, with sections shown, sweep the mouse over the rows and the section
headers, then look below the last row and between rows. No rules means this resolves. Rules mean
a screenshot, and this goes back to ready-for-agent.

### 2026-10-10, resolved

The user swept the mouse over the rows and section headers in MusicBee with sections shown: no
rules below the last row or between rows.
