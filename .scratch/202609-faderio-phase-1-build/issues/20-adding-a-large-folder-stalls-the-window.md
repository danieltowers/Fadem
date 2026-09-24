# 20: Adding a large folder stalls the window

Status: needs-triage
Blocked by: —

Found during the Looks smoke run for [issue 04](04-remove-rows-and-clear-the-queue.md) on
2026-09-24. Not caused by it: the HEAD build before issue 04 stalls the same way.

## What happened

The whole `MUSIC` library (903 tracks, warm cache) was added as a launch argument while
`WM_NULL` was sent to the main window every 20 ms (`SendMessageTimeout`, 5 s limit):

- The working tree timed out once at 5012 ms and again at 5014 ms, starting 150–170 ms after
  the window appeared.
- The HEAD build timed out at 5003 ms and 5014 ms.
- A launch with no arguments never went over 21 ms.

Every run ended with all 903 rows. The Looks item "while adding a folder's worth of files, the
window stays responsive" fails at this size. It passed at 208 tracks (a folder of several albums,
pings ≤ 5 ms).

## Suspected cause (from reading the code, not measured)

- The add publishes once per file, and the sink posts a render for each publication.
- `MainForm.Render` sets `BackColor` on every list item each time, so the work grows with the
  square of the row count while rows arrive.
- On a warm cache the rows arrive faster than the UI thread renders them.

## To decide

- Whether `Render` touches only the rows whose current state changed.
- Whether renders during an add coalesce to the latest view, rather than one per file.
