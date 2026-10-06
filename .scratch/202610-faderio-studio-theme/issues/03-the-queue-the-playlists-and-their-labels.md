# 03: The queue, the playlists and their labels

Status: ready-for-agent
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

- [ ] Headless: the playing row's cells are in Accent, a selected row is drawn in Raised, and
      the open playlist is in Accent on Raised. No `SystemColors.Window`, `SystemColors.Info` or
      `RowMarks.Selected` grey is drawn when a palette is set.
- [ ] Looks, both hosts: the queue and the playlists sit in dark wells with capital headers. The
      playing row reads at a glance. The section stripes keep their own colours.
- [ ] `dotnet test` passes on net8.0 and net48 x86. The commit states both counts.

## Comments
