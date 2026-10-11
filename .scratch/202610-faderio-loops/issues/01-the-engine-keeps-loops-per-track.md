# 01: The engine keeps loops per track

Status: ready-for-agent
Blocked by: none

Core only, no shell, nothing sounds yet. See the [spec](../spec.md), D1–D6, D13, D22 and *The
store*. Read the **Loops** terms in `CONTEXT.md` first.

## What to build

- `LoopSpec` in `Domain/`: `Start` and `End` (each a `FadePointSpec`, position plus FromEnd),
  `Count` (int? — null is open-ended). Count 1 is valid. Immutable record, JSON by name, nulls
  omitted, as `TransitionSpec` is.
- The store (`ITransitionStore`, `JsonTransitionStore`): `Loops(path)`, `SetLoops(path, list)`,
  `RemoveLoops(path)`, `LoopPaths`; and the override set `PlaylistLoops(playlist, path)`,
  `SetPlaylistLoops`, `RemovePlaylistLoops`, `PlaylistLoopPaths(playlist)`, where an **empty list
  is a value** (D6) distinct from "no override". `RenamePlaylist` and `RemovePlaylist` carry
  the loop overrides with them. Held writes (`Holding` / `WriteHeld`) cover loops as they cover
  transitions.
- `StoreFileModel` schema version **2**: `loops` and `playlistLoops` maps as the spec lays
  them out. A version-1 file reads with no loops and is written back as 2. A write rejects a
  list whose resolved spans overlap or are out of order (the engine sorts by resolved start
  before writing; the check is on the resolved spans, so two from-end/from-start loops are
  compared on the same axis).
- A `LoopResolver` beside `TransitionResolver`: given the layered lists (override list if the
  playlist has one — empty included — else the global list) and the track's `Length`, yields
  the **effective loops**: absolute start and end within the track, count, in order. A loop
  whose end is past the length or whose start is not before its end is **passed over**: it is
  left out of the effective list and reported as a notice naming the track and the loop
  ("Loop 2 of <title> is past the end of the track and is not played").
- No engine intent yet beyond what the resolver and store need; `PlaybackEngine` gains a
  `LoopsOf(Track)` query returning both layers (as `Transition(Adjacency)` returns
  `TransitionLayers`) so issue 03 has its read path.

## Acceptance

- [ ] A store round-trips a track's loops and a playlist's list, including an empty override
      list, and a null count. The file says `"schemaVersion": 2`.
- [ ] A schema-1 `transitions.json` loads with every transition intact and no loops; saving it
      writes version 2 and keeps every transition.
- [ ] Two overlapping loops are refused on write with a reason; two adjacent loops (end of one
      equals start of the next) are accepted.
- [ ] Resolution against a 180 s length: a from-end start of 8 s is 172 s; a loop ending at
      181 s is passed over with a notice; a loop with start 60 s and end 60 s is passed over.
- [ ] A playlist with an empty override list resolves to no loops; a playlist with no override
      resolves to the global list.
- [ ] `RenamePlaylist` moves its loop lists; `RemovePlaylist` drops them.
- [ ] Tests pass on net8.0 and net48 x86; files under `TestTemp`.
