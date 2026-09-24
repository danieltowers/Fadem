# 20: Adding a large folder stalls the window

Status: resolved
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

## Comments

- Built 2026-09-24. Reproduced first: the whole `MUSIC` library as a launch argument, `WM_NULL`
  every 20 ms, timed out at 5000–5012 ms in 3 of 3 warm runs. A launch with no arguments stayed
  ≤ 11 ms.
- **The cause was the posting, not the rendering.** `PostingSink` posted every publication, and
  WinForms runs every pending post in one pass without pumping. Posts queued during the pass run
  in it too. Rendering with a stopwatch showed no render over 20 ms. `Render` already rebuilt
  only the new rows.
- **Decided: renders coalesce to the latest view, in the sink.** `IEngineSink` already promised
  "at most one pending delivery"; `PostingSink` did not keep it. Now one render is pending and it
  shows the latest view. Coalescing alone still left pings of 100–700 ms: the worker publishes
  every ~1.5 ms and a render takes 5–20 ms, so a new post was always waiting and the pass never
  ended until the add did. So the next render is posted only once the UI thread has been idle
  (`Application.Idle` → `PostingSink.UiThreadIdle`). A notice renders any view still waiting
  first, so no view published before it shows after it. ADR 0003 gains an addendum.
- **Decided: `Render` still visits every row, but sets a row's colour only when it changes**, as
  it already did for the mark and the text. Visiting 4515 rows is well inside a frame.
- **Tests:** `PostingSinkTests` (6), with a UI thread that has not reached its queue: a 50-file
  add is one render of the whole queue; after a render the next waits for idle; idle posts only
  when something is waiting; a publication after idle posts at once; notices are never
  coalesced; a notice follows the views published before it.
- **Device, WASAPI shared, same script:** max ping 17–28 ms in 7 runs of the library (903
  tracks, then 968 after the library grew during the session); every run ended with all rows.
  The library five times over (4515 rows): max 33 ms, *Adding N of M…* and **Cancel** shown
  throughout, both gone at the end. Cancel, pressed once 1200 rows were listed, kept 1551 and
  none arrived in the next 3 s.
- **Not covered:** native modal loops (moving the window, a message box, a file dialog) raise no
  idle, so pushed renders wait for them to end; the form's 100 ms timer renders meanwhile.
