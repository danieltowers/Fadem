# Map — faderio v3: restage the rest of Phase 1

Label: `wayfinder:map`

## Destination

A staged, ordered set of **ready-for-agent build issues** that bring back the rest of v2's
Phase 1 on v3's seam, written to `.scratch/202609-faderio-phase-1-build/` as a `spec.md` plus
numbered issues. The map is done when every decision those issues depend on is settled and the
issues exist; no feature code is written on this map.

**Reached 2026-09-23.** The spec and 18 issues are in
[`.scratch/202609-faderio-phase-1-build/`](../202609-faderio-phase-1-build/spec.md).

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
- [What happens when a file or the device fails?](issues/05-what-happens-when-a-file-or-the-device-fails.md):
  an unreadable track is passed over and its row kept, marked. The incoming track opens when
  its boundary is scheduled, so a failure re-targets the boundary. A decode error ends the audio
  there. Device loss pauses without resuming; the launch fallback is exclusive → shared →
  WaveOut. Notices go to a strip with a session list, and nothing is swallowed.
- [When does ReplayGain switch, and is it on by default?](issues/06-when-does-replaygain-switch-and-is-it-on-by-default.md):
  track gain from tags only, on by default, with 0 dB for untagged tracks and a toggle that
  applies at once to live voices. It is nearly inert: 2 of 342 sampled files are tagged. It
  also settles the audible-time rule: the `View` shows what is heard, and commands act as soon
  as possible.
- [Does adding a folder need a background probe?](issues/07-does-adding-a-folder-need-a-background-probe.md):
  yes. The 903-file library adds in 12.8 s cold (median 13 ms, max 112 ms, the slow files being
  FLACs with 7 MB of cover art). Today's add probes under the lock the audio thread takes, so
  adding must move off the lock and off the UI thread. `AddTimeBudgetTests` stays behind.
- [Should MP3 length trim the encoder's gapless padding?](issues/08-should-mp3-length-trim-the-encoders-gapless-padding.md):
  trimming is exact from the LAME tag for Info/Xing files and impossible from headers for bare
  CBR. In the library, 546 of 654 MP3s carry a valid tag and 103 are bare, none with
  `iTunSMPB`. The ACM decoder's offset is unmeasured. The choice itself graduates to a new ticket.
- [What completes the editor?](issues/09-what-completes-the-editor.md): from-end on the fade-out
  point only, converted on toggle. Curve is selectable per transition. Revert and Remove
  transition are separate, and a blank save unauthors. Entry points are this → next (wrapping
  like ⏭), previous → this, and this → this under repeat-one. Unsaved edits prompt, and
  `Author` validates.
- [Which settings exist, and where do they live?](issues/10-which-settings-exist-and-where-do-they-live.md):
  three settings: output mode (next launch), unauthored boundaries (hard cut, or a default
  crossfade of 1–30 s) and ReplayGain. A blank authored duration no longer borrows N. The
  settings live in `settings.json`, owned by the engine, and change through one
  `ChangeSettings` call. `--output=` goes.
- [What does queue management add?](issues/12-what-does-queue-management-add.md): one
  background `Add` for files, folders (recursive, natural order) and M3U, appending as files
  resolve, with progress and cancel. `Move` reorders by drag, and `Remove` handles several rows.
  `SaveAsPlaylist` writes UTF-8 `.m3u8` with absolute paths. Adding never starts playback, and
  duplicates are allowed.
- [Trim tagged MP3s, or keep whole frames?](issues/13-trim-tagged-mp3s-or-keep-whole-frames.md):
  trim when a LAME tag passes its CRC, MusicLength and range checks (546 of 654 library MP3s),
  from the first encoded sample; bare files and failing tags keep whole frames silently. A
  permanent test must pin the ACM offset, or the build stops. Staged late.
- [Stage the build issues](issues/11-stage-the-build-issues.md): the destination.
  [`spec.md`](../202609-faderio-phase-1-build/spec.md) and 18 `ready-for-agent` issues,
  daily driver first (adding, queue editing, transport, failures, settings), then authoring,
  then MP3 trimming.

## Not yet specified

None. **Unmeasured audio paths** graduated into build issue 11 as Sounds items; its automated
part is out of scope (below).

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
- **Loudness scanning** (computing gain for untagged files). Ruled out by
  [When does ReplayGain switch, and is it on by default?](issues/06-when-does-replaygain-switch-and-is-it-on-by-default.md):
  it is a library feature (Phase 2), and an external scanner can tag the files.
- **Queue persistence across restarts.** Ruled out by
  [Which settings exist, and where do they live?](issues/10-which-settings-exist-and-where-do-they-live.md):
  saving the queue as M3U is the manual way; automatic restore returns as a fresh effort.
- **Automated tests of `NAudioAudioOutput`.** Ruled out by
  [Stage the build issues](issues/11-stage-the-build-issues.md): ADR 0002 leaves the device to
  the human seam, and build issue 11 adds the Sounds items that prove its paths.
- **v2/v3 store coexistence.** v2 is retired for listening, so the two never share the file.
- Everything the predecessor ruled out: library manager, playlist manager, cross-platform,
  MusicBee interop, tag editing, sidecar transition files.
