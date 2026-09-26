# 05: The transition manager

Status: ready-for-agent
Blocked by: 04

Decided in [D24](../../202609-faderio-interim-improvements/decisions.md).

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

- [ ] Track A is in three authored adjacencies (A→B, A→C, D→A); the manager lists all three
      with the right direction, and B's override in the open playlist shows beside the global.
- [ ] Preview plays the override's fade-out point; Preview global plays the global's.
- [ ] Author a new adjacency in the panel while the manager is open → it appears.
- [ ] A track whose file is gone still lists by path.

## Tests

- **fake:** `AdjacenciesInStore` ordering, flag, both layers; `TracksInStore` with a missing
  file.
- **form:** the grid renders the fixture; the two preview buttons pass the right spec.

## Smoke

- **Looks:** Transitions… opens the window listing the boundary authored during the run.

## Comments
