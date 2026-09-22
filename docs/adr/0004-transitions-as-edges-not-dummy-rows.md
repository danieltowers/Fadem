# ADR 0001 — Transitions are modelled as edges between adjacent tracks, not as dummy playlist rows

Status: accepted (imported verbatim from v2's ADR 0001 in wayfinder ticket 07; the JSON transition store it describes is reused in v3 by ticket 05)

**Status:** Accepted
**Date:** 2026-09-19

## Context

The prior implementation (MusicBee plugin era, then the CrossfadePlayer prototype) modelled a
transition as a **dummy track**: a silent pseudo-file with encoded parameters (`DUMMY_-N_+M`,
later a `.dummy.xml` sidecar) inserted between two real tracks in the playlist. The player's
queue machinery then had to treat these rows specially everywhere:

- dummy scan-ahead logic to find and merge dummy runs after the current item,
- shuffle orderings in which dummies land in the wrong place and their parameters are silently
  dropped,
- fake file paths leaking into M3U save/load,
- UI rows that look like tracks but are not.

This model was an artefact of the MusicBee plugin API, where inserting a fake playlist row was
the *only* available hook. In a standalone player, there is no such constraint.

## Decision

A transition is a **first-class object attached to an adjacency** — the ordered pair
(outgoing track, incoming track) at a boundary in a playback sequence. Nothing that is not a
real track ever occupies a queue row. The four authored parameters (fade-out point with
from-start/from-end addressing, fade-in point, fade-out duration, fade-in duration) are carried
by the transition object itself.

## Consequences

- Queue, shuffle, and playlist code deal only in real tracks; transition lookup becomes a
  simple pair-keyed query at each boundary.
- Shuffle becomes a *benefit*: any authored pair that happens to become adjacent gets its
  transition, with no ordering machinery to break.
- The authoring UI changes: instead of "insert a dummy between rows", you edit the transition
  attached to the boundary between two rows.

## Alternatives considered

- **Keep the dummy-row model** — maximises continuity with the prototype's code and the
  MusicBee-era workflow, but permanently couples unrelated queue logic to a legacy workaround.
- **Sidecar metadata on audio files** — rejected: litters the library, still frames the
  transition as a property of a *file* rather than of an *adjacency*.
