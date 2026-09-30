# 11: The transition editor and preview in MusicBee

Status: needs-triage
Blocked by: 05, 09, 10

Phase 5, after the [MusicBee spike](../spec.md) passed ([brief](../brief.md) decisions 1 and
10, ADR 0012). Staged at the verdict; it needs its own spec before an agent builds it.

## What to build

The shared transition editor panel in the plugin's view: authoring an adjacency's transition,
capture by ear, and preview, as in standalone faderio. Preview plays through the plugin's
output, so it takes and lets go of the device as Play and Stop do.

## Acceptance criteria

- [ ] A transition authored in MusicBee plays the same in standalone faderio, and the reverse.
- [ ] Preview plays the draft and leaves the queue where it was.
- [ ] The MusicBee Looks smoke covers the editor; the Sounds half gains preview.
