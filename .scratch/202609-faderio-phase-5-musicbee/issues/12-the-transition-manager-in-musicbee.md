# 12: The transition manager in MusicBee

Status: needs-triage
Blocked by: 05, 09, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 10, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

The shared transition manager opened from the plugin's view: the library's transitions listed,
changed and removed as in standalone faderio, **gone** tracks included. MusicBee's
auto-organise moves files, and transitions are keyed by path (ADR 0005), so tracks it moves
show as gone here.

## Acceptance criteria

- [ ] The manager lists, changes and removes the library's transitions from inside MusicBee.
- [ ] A gone track's transitions can be removed, not changed.
- [ ] The MusicBee Looks smoke covers the manager.
