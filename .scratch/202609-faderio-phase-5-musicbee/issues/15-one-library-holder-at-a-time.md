# 15: One library holder at a time

Status: needs-triage
Blocked by: 05

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decision 4, ADR
0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

One host holds a library at a time: while standalone faderio has a library open, the plugin
refuses to open it, and the reverse, each saying which host holds it. Today both can open the
same folder and write its store, playlists and settings under each other.

The holding belongs to the library, in Core, so both hosts get it from one source. A crashed
host must not leave the library held for ever.

## Acceptance criteria

- [ ] A library held by one host is refused by the other with a notice naming the holder.
- [ ] A host that crashed or was killed does not block the next open.
- [ ] Tests cover it on net8.0 and net48 x86.
