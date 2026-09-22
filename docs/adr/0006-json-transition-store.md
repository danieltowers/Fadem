# ADR 0003 — The transition store is a single JSON file

Status: accepted (imported verbatim from v2's ADR 0003 in wayfinder ticket 07; the JSON transition store it describes is reused in v3 by ticket 05)

**Status:** Accepted
**Date:** 2026-09-19

## Context

ADR 0002 settled that transitions live in a local store keyed by the ordered track pair, with
per-playlist override capability in the schema from Phase 1. The store's concrete format was
left open: JSON file or SQLite database.

Facts that shaped the choice:

- The store holds at most a few thousand entries — one per *authored* adjacency, far sparser
  than the library itself.
- Access is always "lookup by exact ordered pair" at a boundary; there are no queries,
  joins, or aggregation that a relational store would help with.
- The store must carry a `schemaVersion` (override schema exists from Phase 1; keying
  migrates from paths to stable IDs in Phase 2, per ADR 0002).
- The store is written when the user authors, and read into memory at startup.

## Decision

The transition store is a **single JSON file** (named `transitions.json`):

- First field: `schemaVersion`.
- Transitions keyed by ordered path pair (outgoing→incoming).
- A per-playlist overrides section shaped per ADR 0002 (any parameter subset; omitted
  parameters fall through to the global transition).
- Writes are atomic: write to a temp file, then rename over the target.
- The store is loaded into memory at startup and persisted on change.

## Consequences

- The store is human-readable, hand-editable, diffable, and trivially backed up.
- No additional database dependency.
- Phase 2's path→stable-ID key migration is a versioned schema change within the same file
  format (bump `schemaVersion`, migrate on load).
- If the store ever genuinely outgrows JSON (not expected at this scale), the versioned
  schema makes a one-time migration to another format tractable.

## Alternatives considered

- **SQLite** — better at scale and ad-hoc queries, but this store is never queried, only
  looked up by exact key; a heavier binary dependency for no benefit at this scale.
- **Sidecar files next to audio** — already rejected in ADR 0002.
