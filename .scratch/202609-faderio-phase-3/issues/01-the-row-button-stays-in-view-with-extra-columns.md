# 01: ✎ stays in view with extra columns

Status: resolved
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

- [x] With all eight columns shown at the default window size, ✎ is in view with no sideways
      scroll bar, and Path, Album and Artist are narrower than their layout widths.
- [x] Widening the window gives Artist, then Album, then Path their own widths back, and
      `columns.json` is unchanged throughout.
- [x] Squeezed past every floor (all eight columns at the 960 px minimum), the list scrolls
      sideways rather than cutting a column below its floor.
- [x] The default four behave exactly as today.

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

- Built 2026-09-27, with the give-way order as proposed. **Shell only:** `MainForm.GiveWay` lists
  Path (80), Album (60) and Artist (60). `FitColumns` works out how far the title's 100 px floor
  plus every shown giver's own width overshoots the room, and takes that from the givers in order,
  each down to its floor. Because the widths come from the layout's widths and the list's width
  alone, they are given back in the reverse order as room returns. A giver the user dragged
  narrower than its floor keeps that width. It is never widened to the floor. Every fitted width
  goes through `_widthsSet`, so none is recorded as the user's.
- **Changed here:** a width the user drags on any column except Title now fits the rest again,
  Artist included. Artist used to be left out only because the fit set its width. A `_fitting`
  flag now covers that for every width the fit sets. The upshot: widening Artist by hand makes
  the title give up room at once, as Album and Path already did. Before, the list scrolled until
  the next resize.
- **Tests:** five in `HeadlessFormTests.Columns.cs`: all eight at 1240 px fit, with the three
  givers between their floors and their layout widths and the fixed four at theirs; widening in
  20 px steps to 2000 px keeps the order and leaves `columns.json` byte-identical; at the 960 px
  minimum the floors hold and the sum exceeds the list; the default four fill as before, with
  Artist alone giving way at 960 px; a Path dragged to 50 px stays at 50.
- **Looks smoke** (fresh temp library and machine folder, widths read with `LVM_GETCOLUMNWIDTH`,
  the sideways bar from `WS_HSCROLL`): all eight ticked through the header menu at 1240 px:
  48,100,83,60,70,110,84,80,28 plus the scroll bar = the list's 680 px, ✎ in view, no sideways bar.
  Widening: Artist 200 by 1400 px, Album 200 by 1500, Path 360 by 1800, then the title grows.
  `columns.json` unchanged. At 960 px: every giver at its floor, sum 640 in a 400 px list, sideways
  bar shown. The default four at 960 px: Artist 137, ✎ in view, no sideways bar.
