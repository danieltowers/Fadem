# 01: The engine plans a carry

Status: ready-for-agent
Blocked by: none

Core only. See the [spec](../spec.md), decisions 1–6 and 8.

## What to build

- A read-only query on `PlaybackEngine` that, given a move as the shell would make it
  (`Move(rows, before)`, `MoveAfter(rows, after)` or `MoveSection(section, places)`), returns the
  carry it would make, without moving anything:
  - each new adjacency (decision 6), as its two `RowId`s and its `Adjacency`;
  - the fade-out half and fade-in half it would take, each by layer, and the curve (decisions
    1, 3, 4);
  - whether it is already authored, and in which layer it is heard (decision 5);
  - whether the shell must ask (decision 8), so the shell never decides that itself.
- A move that breaks nothing authored plans an empty carry and does not ask.
- Read from the published snapshot and the store, as `RowPairsOf` is.
- Names are the implementer's; the plan type lives beside `RowPair` in `PlayerView.cs`.

## Tests

On net8.0 and net48 x86:

- The spec's example: A → C, X → B and B → Y, each with the right halves.
- A half from an override stays an override; a global half stays global; a pair authored on
  both layers splits by parameter.
- The curve: from the outgoing side, else the incoming side, else blank.
- An already-authored new pair, global and playlist-only, each reported with its layer.
- A block moved in one piece: its inner boundaries are not new.
- A scattered selection: its inner boundaries are new and take their halves.
- A track in two rows: halves follow the rows.
- Moving the last row: A's fade-out has no new boundary to land on and is dropped.
- Under shuffle, repeat-all's wrap, and an unreadable row between.
- A section move.
- Whether to ask, under each of the three settings, with and without a conflict (needs 03's
  setting; stub it until then).

## Acceptance

- [ ] The query and its tests. No shell change, no store write.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts.
