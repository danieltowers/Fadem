# Loops are authored per track, overridden per playlist as a whole list, in the transition store

Status: accepted (2026-10-11)

A **loop** is a span inside one track heard a set number of times, or open-ended, before the
track plays on (`CONTEXT.md`, *Loops*). It is the first authored thing in faderio that belongs
to a single track: [ADR 0004](0004-transitions-as-edges-not-dummy-rows.md) and
[ADR 0005](0005-track-pair-transition-store.md) make everything else belong to an adjacency, and
`Track` holds only what the probe read. Decided in the grilling recorded in
[.scratch/202610-faderio-loops/spec.md](../../.scratch/202610-faderio-loops/spec.md).

## The decision

- A loop is keyed by the **track's path**, globally, with a **playlist override**, the two
  layers the transition editor already names.
- A playlist's override is a **whole list** that stands in for the track's list, an empty list
  included. Lists are not merged.
- Loops live in **`transitions.json`**, schema version 2, as two more maps beside the
  transitions and their overrides — one file, as [ADR 0006](0006-json-transition-store.md) has it.
- A loop **never changes the track's length or moves a point**. A fade-out point inside a span
  starts the transition on the last pass; a fading voice keeps looping.

## Considered and rejected

- **Per row.** A loop on this place in this playlist only. It would be the one authored thing
  with no global layer, and the playlist file would need to carry it (M3U compatibility,
  [ADR 0005](0005-track-pair-transition-store.md)). The two real cases — "this edit always has a
  long outro" and "in this set I hold the breakdown" — are the two layers that already exist.
- **Per-loop merge of the override**, by position or by id. Transitions merge per field because
  a transition is one record; a list merged per element needs identity in the file and a rule
  the user cannot predict from "Global / This playlist". Replacing the list is the model those
  two words already describe.
- **Unrolling the timeline**: a track with loops gets a longer `Length`, from-end points resolve
  against it, the seek bar spans it. It is the "correct" model and it rewrites `Length` for every
  consumer ([ADR 0010](0010-one-length-issued-from-the-header.md)), makes a from-end fade point
  move when a count changes, and turns a position into something that happens several times.
  Keeping points as positions in the track costs one rule — the last pass fires the boundary —
  and nothing else moves.
- **A second file** for loops. Nothing in ADR 0006's reasons for one file stops applying when
  the file has a second kind of record; a second file would need its own hold-and-write and
  rename-playlist paths.

## Consequences

- `transitions.json` is version 2. A version-1 file reads as before with no loops.
- The engine, the manager and the editor now have a notion of "what is authored about this
  track" that is not an adjacency; the Transition Manager lists a track for its loops alone.
- The mixer makes seeks of its own, in the render path, at every loop seam, and re-derives the
  boundary trigger as a user's seek does. The seam has a fixed declick splice, which is not a
  fade and is never authorable.
- An open-ended loop has no coming transition: Cue transition is unavailable and playback does
  not finish on its own.
