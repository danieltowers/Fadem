# ADR 0002 — Transitions live in a local track-pair store: global by default, per-playlist override, playlists stay M3U-compatible

Status: accepted (imported verbatim from v2's ADR 0002 in wayfinder ticket 07; the JSON transition store it describes is reused in v3 by ticket 05)

**Status:** Accepted
**Date:** 2026-09-19

## Context

The prototype stored each transition in a `.dummy.xml` sidecar file next to the audio, which
raised the question the previous project died on: *is there a better way to store the dummy
track values?* The recorded tension:

- **Sidecar files** — portable, but litter the library and depend on every other player
  ignoring them.
- **A proprietary playlist format with embedded transitions** — transitions travel with the
  playlist, but lose interop with other players.
- **A separate store keyed by track pair** — no library pollution, no interop loss, but
  transitions do not travel with a playlist file.

## Decision

Transitions are stored in a **local store owned by the player, keyed by track pair**
(ordered adjacency). The relationship between transitions and playlists is:

- The pair-keyed transition is **global**: it applies wherever those two tracks are adjacent —
  in any playlist, or in plain queue playback.
- A playlist may carry a **per-playlist override** for an adjacency it contains. The override
  is **per-parameter**: it may specify any subset of the four parameters (plus fade curve), and
  every parameter it omits falls through to the global transition.
- Playlists remain stored in an **interoperable format (M3U)**; overrides live in the store,
  not in the playlist file. Exported playlists therefore work in other players.

## Consequences

- Authoring a transition once means every time those two songs touch, the handoff is right.
- The store's keying is path-based in Phase 1; when the library manager (Phase 2) introduces
  stable track identity, keying can be upgraded. The store schema is designed so keying can
  change without a format break.
- Playlists (and their override UI) arrive in Phase 3, but the *storage schema* carries
  override capability from Phase 1 so the format never has to migrate twice.
- A transition does not travel with an exported M3U — accepted loss; the interop benefit
  outweighs it.

## Alternatives considered

- **Sidecar XML next to audio files** (prototype's approach) — rejected: library pollution,
  fragile, and semantically wrong (a transition is a property of an adjacency, not a file).
- **Proprietary playlist format embedding transitions** — rejected: abandons M3U interop for a
  benefit (transitions travelling with the playlist) that the global store provides better.
