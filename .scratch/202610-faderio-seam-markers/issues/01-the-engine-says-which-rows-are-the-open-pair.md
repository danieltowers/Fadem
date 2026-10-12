# 01: The engine says which rows are the open pair

Status: resolved
Blocked by: none

Core only. See the [spec](../spec.md), decision 1 (and 2, for the order).

## What to build

- A query on `PlaybackEngine`: given an `Adjacency`, every row pair whose this → next is it,
  each as the outgoing and the incoming row's `RowId`, so the shell never works out which row
  comes next: `RowPairsOf(Adjacency)` returning `IReadOnlyList<RowPair>`, with
  `RowPair(RowId Outgoing, RowId Incoming)` in `PlayerView.cs` beside `RowAdjacencies`.
- It goes by the snapshot's `AdjacenciesOf` rules, as ⏭ does: a row's `Next`, and its `Self`
  under repeat-one (a pair whose two `RowId`s are the same row). Matched as `Heard` matches
  (`SamePair`).
- In the order playback reaches them: from the attended row onward, wrapping as the queue
  wraps; from the top when nothing is attended. The first is the one heard next.
- Read from the published snapshot without the lock, as `AdjacenciesOf` and `Heard` are: the
  queue will ask on every paint.
- An XML summary in the house style, naming the build issue.

## Tests

A new Core test class, on net8.0 and net48 x86, beside `EveryAdjacencyAndUnsavedEditsTests`:

- A pair in the queue: its one row pair.
- An adjacency not in the queue, and one gone stale after its rows are removed: none.
- The pair twice: both, in playback order, from the playing row (the later match first when
  playing sits between them) and from the top when nothing plays.
- Repeat-all: the last row → the first.
- Repeat-one: this → this, as one row; and the row's this → next too, as ⏭ gives it.
- An unreadable row between: the pair across it.

## Acceptance

- [x] The query and its tests, as above. No shell change.
- [x] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts.

## Comments

2026-10-12, built. `PlaybackEngine.RowPairsOf` reads the current row from the view of the
snapshot it holds, so the order and the rows come from one published state; the snapshot's
`RowPairsOf` walks the queue once from that row, giving under repeat-one this → this before the
row's this → next, since the natural end plays it. `RowPairsOfTests`, six tests: a pair; two
tracks not neighbours until the row between goes (the stale case); the pair twice, from the top
and from a played row between; repeat-all's wrap; repeat-one; an unreadable row passed over.
