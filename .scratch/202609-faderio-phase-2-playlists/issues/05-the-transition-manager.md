# 05: The transition manager

Status: resolved
Blocked by: 04

Decided in [D24](../../202609-faderio-phase-2-interim-improvements/decisions.md).

## What to build

- **Engine:** `AdjacenciesInStore(Track)` — every adjacency in the library's store in which the
  track is outgoing or incoming, each with its global spec, the open playlist's override (or
  null), the effective result, and a `PlaylistOnly` flag. Plus `TracksInStore()` — every track
  named by any key, with title/artist where the file can still be probed, else the path. Both
  read the store only; nothing is decoded. Ordering: outgoing first, then incoming, then by
  the other track's title.
- **Window:** modeless `TransitionManagerForm`, opened from a **Transitions…** button in the
  top bar (and from the queue's context menu, "Transitions for this track…").
  - A picker at the top over `TracksInStore()`, pre-selected to the queue's selected row's
    track if there is one and it is in the store; otherwise the first.
  - A grid: Direction (→ / ←), Other track, then for each of the four parameters and the curve
    a "global | override" pair, then Effective, and the playlist-only flag. Values formatted
    by the Presenter's existing formatting.
  - Per row: **Preview** (`Preview(adjacency, effective)`) and **Preview global**
    (`Preview(adjacency, global)`); one **Stop preview**. The status line mirrors the panel's.
  - Double-clicking a row opens the editor panel on that adjacency, in its current mode
    (the panel edits; this window does not).
  - Follows every publish: a change in the store re-renders the grid.
- Preview here obeys the glossary's **Preview**: the queue is held where it was.

## Where the logic lives

- **Engine:** the two queries and the preview calls. The shell never reads the store file.
- **Shell:** the window.

## Acceptance

- [x] Track A is in three authored adjacencies (A→B, A→C, D→A); the manager lists all three
      with the right direction, and B's override in the open playlist shows beside the global.
- [x] Preview plays the override's fade-out point; Preview global plays the global's.
- [x] Author a new adjacency in the panel while the manager is open → it appears.
- [x] A track whose file is gone still lists by path.

## Tests

- **fake:** `AdjacenciesInStore` ordering, flag, both layers; `TracksInStore` with a missing
  file.
- **form:** the grid renders the fixture; the two preview buttons pass the right spec.

## Smoke

- **Looks:** Transitions… opens the window listing the boundary authored during the run.

## Comments

- Built 2026-09-27. **Engine:** `PlaybackEngineManager.cs` holds `AdjacenciesInStore(track)` and
  `TracksInStore()`. Each reads the store's keys under the lock (`ITransitionStore` gains `Pairs()`
  and `OverridePairs(playlist)`), then resolves tracks outside it: a queued track is the queue's own
  `Track`, any other is probed as an add probes it, and one that cannot be probed (gone, or no longer
  promised) is a `Track` with its path and no title. A result is a `StoredAdjacency(Adjacency,
  IsOutgoing, Layers)` with `Other` and `PlaylistOnly`, in `Domain/StoredAdjacency.cs`; `Layers` is
  what `Transition(adjacency)` returns. Ordered outgoing first, then incoming, each by the other
  track's title.
- **What "in the store" means.** A pair is listed when it has a global transition or the *open*
  playlist overrides it. Another playlist's override is not listed, since the manager shows only the
  open playlist's layer, and `TracksInStore` names the same pairs' tracks, so a track picked always
  has a row. A track into itself (A→A) is listed once, as outgoing.
- **Preview is `PreviewStored(adjacency, layer)`, not `Preview(adjacency, spec)`.** `Preview`'s
  global layer plays a draft *under the stored override* (issue 04), so passing the global spec
  would have played the effective transition. `PreviewStored` reads the store: `Playlist` plays the
  override over the global, `Global` plays the global alone, as a playlist without an override
  plays it. A stored transition was checked when saved, so nothing is refused. The preview body
  is shared with `Preview` (`PlayPreview`), so the queue is held exactly as it is there.
- **Following publishes.** `PlayerView.StoreVersion` moves on when the store is written, or another
  store or playlist is open at the next snapshot. The manager asks the two queries again only
  then (or when another track is picked), since they probe files. They run on the UI thread, so a
  store naming many tracks that are not queued probes each of them on every save.
- **Window.** `TransitionManagerForm`, modeless, owned by the main window, one instance reused;
  Esc and the close box hide it. A picker, a legend line ("Each value reads global | override in
  *playlist*."), a `DataGridView` with one "global | override" column per parameter and the curve
  ("–" where a layer sets none), Effective (the heading line's phrase, now `Presenter.FmtTransition`),
  Playlist only (✓), and **Preview** / **Preview global** button columns; **Stop preview** with the
  preview line beside it. **Transitions…** sits between Export playlist… and Open library…; the
  queue's menu has "Transitions for this track…", enabled with one row selected. With no row
  selected it opens on the store's first track: the attended marker is not a selection.
- **Not handled: a gone track in the editor.** A double-click on a row whose other track's file is
  gone opens the editor on a track of length 0, so a save there is refused as past its end.
- Tests: `TransitionManagerTests` (12, fake output): ordering and direction, the three layers,
  playlist-only in its playlist alone and absent from another, A→A once, no library, every track by
  title with a queued one the queue's own, a track not queued probed and a gone one by path, each
  preview layer read by where its pre-roll starts (and the queue held), a playlist-only transition's
  global preview as a hard cut, a gone file's preview, and `StoreVersion`. Store: `Pairs` and
  `OverridePairs` after a reload. Presenter (4): the track name, a row's cells, the playlist-only
  mark, the legend. Form (6, a new partial `HeadlessFormTests.TransitionManager.cs`): opens on the
  selected row's track with the fixture's three rows; on the first track with none selected, and
  the picker; the queue's menu; both preview buttons with the preview line and Stop preview; a
  double-click opens the editor in playlist mode; a save in the editor appears. Mutations checked:
  a global preview that keeps the override fails 2 engine tests and the form test; a
  `StoreVersion` that never moves fails its engine test and the "appears" form test.
- dotnet test: 1025 passed, 0 failed (Core 802, App 223). `AddTimeBudgetTests` (two timing tests)
  failed one run in three under the full suite's load; `HEAD` without this work fails the same
  two the same way, and both pass alone.
- Looks smoke, fresh temp library and machine folders, two generated 15 s tones with
  `--edit-first`: **Transitions…** sits between Export playlist… and Open library…. A global
  10 / 2 / 0 / 2 and Untitled's override of the fade-out point to 7 saved. Transitions… with no
  row selected opened "faderio — transitions" on "Incoming tone" (first by title): one row, ←,
  "Outgoing tone", "10 | 7", "2 | –", "0 | –", "2 | –", "Equal-power | –", the effective phrase, no
  ✓. The first run cut Effective short ("fade-out at …"); the window is now 1340 wide with
  narrower fixed columns, and on relaunch it read in full. **Preview** read "Pre-roll · outgoing
  0:04" two seconds in (from 2 s, the override's 7 − 5); **Preview global** "0:07" (from 5 s,
  10 − 5), the line beside Stop preview, which ended it and greyed out. Esc hid the window.
  Relaunched with the editor empty and "Outgoing tone" selected: opened on it, →, "Incoming tone".
  A double-click on the row opened the editor on Outgoing tone → Incoming tone in Global mode, 10 /
  2 / 0 / 2, with the heading line naming what Untitled plays. The queue's right-click menu on row 2
  had "Transitions for this track…" and it opened the manager on "Incoming tone". **Not smoked in the
  app:** a save in the editor appearing in the open manager (the form test covers it). A posted
  double-click reaches the grid only while no other window covers it (WinForms checks
  `WindowFromPoint`), so the manager and the main window were made topmost for a moment to post
  it.
