# 01: ✎ stays in view with extra columns

Status: ready-for-agent
Blocked by: —

Found in [playlists 06](../../202609-faderio-phase-2-playlists/issues/06-configurable-columns.md)'s smoke.

## What happens today

`MainForm.FitColumns` makes Title fill what the other columns leave, and lets Artist alone give
way when that would squeeze Title below 100 px. It takes Artist down to 60 px, and gives the width
back up to the user's own as room returns. No other column gives way. With Album, Format and
Replay gain shown in the default 1240 px window, the columns are wider than the list. The list
then scrolls sideways and ✎, the far-right column, is out of view until the user scrolls or
narrows a column. Interim transport 07 promised ✎ in view at the 960 px minimum width, with no
sideways scroll bar. That still holds for the default four columns, but not beyond them.

## What to build

- **More columns give way.** When the shown columns do not fit, they shrink in this order, each
  to its floor, before the list scrolls:
  1. Path, to 80 px
  2. Album, to 60 px
  3. Artist, to 60 px (as now)

  Title keeps its 100 px floor and still fills. #, Length, Format, Replay gain and ✎ never
  shrink: their contents are short and fixed.
- Each column that gave way takes width back, up to the user's own width in the layout, as room
  returns, in the reverse order: Artist first, then Album, then Path. A fitted width is never the
  user's and is never written to `columns.json` (the `_widthsSet` rule from playlists 06 covers
  it).
- Only when every column that gives way is at its floor does the list scroll sideways.
- The give-way order is a proposal. If the user prefers another (widest first, say), change the
  list above before building. The rule "shrink to a floor, take back to the user's width" stays.

## Where the logic lives

Shell only: `FitColumns` and the floors beside it. `ColumnLayout` is unchanged.

## Acceptance

- [ ] With all eight columns shown at the default window size, ✎ is in view with no sideways
      scroll bar, and Path, Album and Artist are narrower than their layout widths.
- [ ] Widening the window gives Artist, then Album, then Path their own widths back, and
      `columns.json` is unchanged throughout.
- [ ] Squeezed past every floor (all eight columns at the 960 px minimum), the list scrolls
      sideways rather than cutting a column below its floor.
- [ ] The default four behave exactly as today.

## Tests

- **form:** with the form's handle made at a fixed size, all eight shown: the column widths plus
  the scroll bar fit the list's client width, and each shrunk column is at or above its floor. The
  form widened: widths return to the layout's, in order. The layout file is unchanged. At the
  minimum size: floors hold and the sum exceeds the width.

## Smoke

- **Looks:** extend the columns item: with Album, Format, Replay gain and Path ticked at the
  default size, ✎ is in view with no sideways scroll bar; widening the window gives the
  columns their widths back.

## Comments
