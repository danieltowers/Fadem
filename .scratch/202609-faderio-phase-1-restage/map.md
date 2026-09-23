# Map — faderio v3: restage the rest of Phase 1

Label: `wayfinder:map`

## Destination

A staged, ordered set of **ready-for-agent build issues** that bring back the rest of v2's
Phase 1 on v3's seam, written to `.scratch/202609-faderio-phase-1-build/` as a `spec.md` plus
numbered issues. The map is done when every decision those issues depend on is settled and the
issues exist; no feature code is written on this map.

## Notes

**Domain.** Vocabulary per `CONTEXT.md`. A transition belongs to an *adjacency*, never to a
track alone. The playhead is the only public position; frames never cross the shell seam.

**Plan, don't do.** Wayfinder's default holds on this map, by decision: unlike its
predecessor, this effort ends with issues, not code. Build sessions run from the issues under
the standing rules in `CLAUDE.md` (placement tests, `dotnet test` in every commit message, the
Looks half of `docs/smoke.md` for any shell change).

**The goal is a daily driver.** Phase 1 comes back so the user can listen to real queues in
faderio. Stage by what blocks a real listening session first: queue management (folders, M3U,
reorder), then broken files that skip rather than stop, then settings that survive a restart;
preview, visualiser and ReplayGain after. Dependencies still bind.

**The bar is v2's intent, not v2's features.** In scope: queue management (v2 0003); skip,
repeat, shuffle and the end-of-queue fade-out (0008); preview (0010); the visualiser and peak
meter (0011); ReplayGain (0012); settings persistence and a dialog replacing launch arguments
(0013); robust file handling (0014); and the editor's completions (curve selection, from-end
points, delete or unauthor). A ticket may cut or redesign any of them.

**v2's issue files are mined, never reused.** They sit in
`v2-GLM-5.3-flash\.scratch\202609-faderio-build\faderio-phase-1\`
(read-only) and were written against a seam that no longer exists. Take intent and acceptance
criteria from them; every build issue written here passes the three placement tests in
`CLAUDE.md`. v2's spec at `tmp/planning/202609-faderio-build/spec.md` in that repo stays
authoritative for intent.

**The engine is ahead of the shell.** `PlaybackEngine` already exposes `Remove`, `Clear`,
`Next`, `Previous`, `PlayRow`, `SetShuffle`, `SetRepeat` and `Unauthor` with no shell over them.
Much of the restage is shell and semantics, not engine; the audit ticket says exactly how much.

**v2 is retired for listening.** It stays read-only reference; v3 owns
`%APPDATA%\faderio\transitions.json`.

**Predecessor.** [faderio v3: a transition you can trust](../202609-faderio-v3/map.md) reached
its destination on 2026-09-23. Its fog for this effort was carried forward into the tickets
below and the sections that follow.

**Skills every session should consult:** `grilling`, `domain-modeling`, `codebase-design`
for anything that adds an engine member, `tdd` for task tickets that measure.

## Decisions so far

<!-- one line per closed ticket: gist + link -->

- [Audit the engine against v2's intent](issues/01-audit-the-engine-against-v2s-intent.md):
  the engine already covers skip, repeat, shuffle, seek, ReplayGain, settings storage and
  add-time rejection, with tests at the fake seam. Most of those have no shell yet. Missing:
  folder add, reorder, M3U as a queue command, settings that change at runtime, preview and
  the visualiser. To fix: the end-of-queue fade borrows an arbitrary A→x transition, decode
  failures after a file opens are not skipped, device loss leaves the engine half-stopped,
  and `Remove`/`Clear` are untested.
- [What do skip, repeat and end-of-queue do?](issues/02-what-do-skip-repeat-and-end-of-queue-do.md):
  ⏭ lands on the incoming track, or hard-cuts from 0:00 outside a transition, and the let-finish
  setting is dropped. A new **Transition now** button plays the next adjacency's transition
  from the playhead. ⏮ wraps under repeat-all, and ⏭ leaves repeat-one. The end-of-queue fade
  is cut, and *Stopped* follows the playhead.
- [Does the visualiser come back?](issues/03-does-the-visualiser-come-back.md):
  only as a clip light. The fade strip and the peak meter are cut. The light latches on
  audible time when the mix goes over full scale, and names the cause: the in-flight adjacency
  or the attended track, with a count. One engine command clears it, and it has no setting.
- [What is preview?](issues/04-what-is-preview.md): preview voices in the main engine play
  the editor's unsaved draft, from 5 s before the fade-out point to 5 s after the fades. The
  queue is held and stays paused afterwards. Queue buttons end a preview, and Capture reads its
  voices. Render stays; render-then-play and a second engine are rejected.

## Not yet specified

- **Unmeasured audio paths.** The WASAPI-exclusive resampling path, real music as opposed to
  test signals, and files longer than two seconds are not proven by the real-input matrix.
  Queue advance and long files will exercise them for the first time; whether any needs a
  permanent test is open. The audit adds that no automated test reaches `NAudioAudioOutput` at
  all: its device clock, WaveOut fallback and stop handling rest on the smoke checklist.
- **Queue persistence across restarts.** v2 never persisted the queue itself. Whether a daily
  driver needs it may surface from the settings or robust-file tickets.

## Out of scope

Beyond this map's destination; returns only if the destination is redrawn.

- **Stable track identity.** v2's spec puts it in Phase 2; the store stays keyed by full path.
- **Formats rejected today** (AIFF, M4A, WMA, Ogg Vorbis, more than two channels, 8/32-bit,
  rates beyond 96 kHz). The library holds none; each keeps its named way back in
  `docs/supported-audio.md`.
- **The per-playlist override UI.** v2 Phase 3; the store already carries the schema.
- **An authored end-of-queue fade-out ("A → end").** Cut by
  [What do skip, repeat and end-of-queue do?](issues/02-what-do-skip-repeat-and-end-of-queue-do.md):
  it needs a store key, an editor mode and a glossary term to serve the last track of a
  session. It returns as a fresh effort if daily use misses it.
- **v2/v3 store coexistence.** v2 is retired for listening, so the two never share the file.
- Everything the predecessor ruled out: library manager, playlist manager, cross-platform,
  MusicBee interop, tag editing, sidecar transition files.
