# 03: The queue, the playlists and their labels

Status: resolved
Blocked by: 02

Shell. See the [spec](../spec.md), "The look": section labels and the queue.

## What to build

- **Already done in issue 02**, so the queue was never white rows on a dark list: rows on Well
  in Text, unreadable rows and ✎ when not authored in Dim, selection in Raised, and the header
  drawn on Well with its headings in Dim. The open playlist is on Raised.
- **`QueueRowStyle`** draws in the palette:
  - rows on Well;
  - the playing row's text in Accent;
  - selection in Raised;
  - unreadable rows in Dim;
  - ✎ in Text when authored and Dim when not.
- **The header**: drawn as small bold capitals in Dim on Well, with an Edge hairline under it and
  between columns. The header's empty end is dark too.
- **`PlaylistSidebar`**: the open playlist bold in Accent on Raised, not black on yellow.
  **`PlaylistNameBar`**: Text, or Dim with no playlist.
- **Pane titles** (`PaneTitle`, and the App's equivalents): small bold capitals in Accent.
- **The drag's drop line** (`QueueDrag`): in Accent.
- **The column rules below the last row.** Windows' dark list themes (`DarkMode_Explorer`, and
  `DarkMode_ItemsView` too, as tried in issue 02) draw a blue-grey vertical rule at each column
  edge down the list's empty part. Find what draws them and stop it: an owner-drawn background
  for the empty area, or a list style.

## Acceptance

- [x] Headless: the playing row's cells are in Accent, a selected row is drawn in Raised, and
      the open playlist is in Accent on Raised. No `SystemColors.Window`, `SystemColors.Info` or
      `RowMarks.Selected` grey is drawn when a palette is set.
- [x] Looks, both hosts: the queue and the playlists sit in dark wells with capital headers. The
      playing row reads at a glance. The section stripes keep their own colours.
- [x] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments

2026-10-06, built.
- The playing (attended) row's cells are in Accent; its ✎ stays Text when authored and Dim when
  not, so the pencil still says authored or not on that row. Unreadable stays Dim over Accent.
- The header draws its headings in small bold capitals (7.5 pt) in Dim, on Well with Edge rules.
- `PaneTitle.Create` now makes a `PaneTitle.Title`, a label that paints its text in small bold
  capitals; `StudioTheme` sets it in Accent. Its `Text` stays as given ("Playlists"), so tests
  and UIA still find it by name. The App has no other pane titles: its queue's title is
  `PlaylistNameBar`, already Text or Dim.
- `PlaylistSidebar`: the open playlist bold in Accent on Raised.
- `QueueDrag.Palette`: the drop line is set to Accent each time it shows, because the theme
  colours every panel inside the list as Chrome.
- **The column rules** are `DarkMode_Explorer`'s list part (`LVP_COLUMNDETAIL`): two pixels,
  `#3A3B3F` and `#16202E`, at each column edge down a details view's empty part. Tried on the
  live list: `Explorer` drops them but brings a white frame. `DarkMode_ItemsView`,
  `DarkMode_DarkTheme`, `DarkMode_CFD` and `DarkMode` are not ListView themes: each falls back to
  the light theme, white frame and all. (So the issue 02 note that `DarkMode_ItemsView` drew them
  too is wrong: on the list it is the light theme.) The fix: `StudioTheme` subclasses every themed
  ListView and answers `WM_ERASEBKGND` with a plain fill of its BackColor in details view. The
  rules go, scroll bars and frame stay dark. A Shell test fails without it.
- The header's empty end is dark (`#1C1C1C` under Duophonic, the dark header theme's own), one
  shade off the Well's `#121212`. Left as is.

Looks (App), by screenshot in the built-in palette and in Duophonic's `palette.json`: the queue
and playlists sit in dark wells, the header in capitals, PLAYLISTS lit, the playing row in the
accent, no column rules (pixel-sampled across the empty part). A selected row's Raised was checked
headlessly, by drawing the queue.

Looks (MusicBee), in the user's Duophonic skin after installing the build: PLAYLISTS, QUEUE and
TRANSITION EDITOR in indian red capitals, the playing row in indian red, the header in capitals,
the open playlist red on Raised, no rules down the queue. Section stripes were not on screen (no
sections in the open playlist); `QueueRowStyle` leaves them alone.

Tests: net8.0 Core 1152, Shell 184, App 189; net48 x86 Core 1131, Shell 184, MusicBee 103;
0 failed. The new queue pixel test first failed in a full run on a ClearType fringe and on the
frame's pixels. It now takes the row's commonest colour and scans only the rows, and passed
three App runs in a row.
