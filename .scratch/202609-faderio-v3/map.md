# Map — faderio v3: a transition you can trust

Label: `wayfinder:map`

## Destination

faderio v3 plays two tracks with an authored crossfade that lands **exactly where the user
placed it by ear**, with `Faderio.Core` migrated from the v2 build and a rewritten WinForms
shell. The map is done when that loop is trustworthy: the user can add two tracks, set the
fade points by listening, and hear the handoff happen where they put it. Restaging the rest
of Phase 1 is routine work handed off once the way there is clear.

## Notes

**Domain.** Vocabulary per `CONTEXT.md`. A transition belongs to an *adjacency*, the ordered
pair (outgoing track, incoming track), never to a track alone.

**This effort carries into execution.** Overriding wayfinder's plan-only default: the user
chose a proven vertical slice, so the map ends with working, audible code, not a document.

**Prior art, read-only.** `v2-GLM-5.3-flash` is reference
material and must not be modified. Its spec at
`tmp/planning/202609-faderio-build/spec.md` is good and still authoritative for intent. Its
`.scratch/202609-faderio-build/faderio-phase-1/` holds 14 issue files worth mining later.

**Settled while charting (grilling round 1):**

- Core is migrated into v3 rather than fixed in place in v2; v2 keeps its history untouched.
- The WinForms shell is rewritten wholesale, not patched.
- Stack stays .NET 8, WinForms, NAudio. The toolkit is not what failed.
- First target is the narrow slice, not all 14 Phase 1 issues.
- Verification is *both* automated assertions and an audible artifact the user can listen to
  without launching the app.

**Skills every session should consult:** `tdd`, `diagnosing-bugs`, `code-review`,
`domain-modeling`.

**Research leaves a permanent test.** Settled in ticket 06 after tickets 01 and 02 produced the
map's most decision-shaping findings and left no code behind. A harness that renders through
the production chain and measures something *is* an integration test; commit it as one.

**Why v2 failed, in one line.** Not a missing plan, not a broken engine, and *not* a fat
shell: a shell no test could reach, plus an authoring button that captured a position later
than what the user heard. The shell was 1033 lines, thin and well-decomposed, and shipped every
defect the user hit because no test project referenced it at all. Every session should keep
asking whether the current work could fail that way again.

## Decisions so far

<!-- one line per closed ticket: gist + link -->

- [Is the audio output clean across real formats?](issues/01-is-the-audio-output-clean-across-real-formats.md):
  The buzzing is fixed and 44.1 kHz is clean in every format, so the core is worth keeping.
  But files above 44.1 kHz are truncated by an inverted position conversion in
  `SourceNormalizer`, 24-bit WAVs render as silence, and the whole test suite passed anyway.
- [Can we seek sample-accurately in compressed formats?](issues/02-can-we-seek-sample-accurately-in-compressed-formats.md):
  No. WAV, constant-bitrate MP3 and OGG are exact; M4A is off by up to 15 ms and WMA by up to
  71 ms; **FLAC silently discards the authored fade-in point entirely** because the engine
  seeks before its first read. Variable-bitrate MP3 remains unmeasured.

- [What position does "set from here" capture?](issues/03-what-position-does-set-from-here-capture.md):
  Two position quantities, distinct types, only one public. The **playhead** (where the
  listener is *now*) is all the shell ever sees: displayed, scrubbed, captured. The **decode
  position** stays engine-internal for scheduling. The playhead is derived at the output
  boundary from a *measured* device clock, not the latency constants. Position belongs to a
  **voice**, never the player, so during a crossfade the outgoing voice stays attended until
  it falls silent. Capture stores the honest value; no reaction-time compensation.
  Recorded as [ADR 0001](../../docs/adr/0001-playhead-from-the-device-clock.md).

- [What exactly is in the narrow slice?](issues/05-what-exactly-is-in-the-narrow-slice.md):
  Two tracks, one boundary authored by ear, the fade lands where you put it — provable in the
  app *and* in a rendered file, on three output modes. All four transition parameters,
  from-start only, equal-power fixed. 44.1 kHz WAV (exact control) plus FLAC (the real
  library). v2's JSON store reused. All of ticket 03's playhead rework included. Visualiser,
  preview, settings dialog, M3U, repeat/shuffle and ReplayGain explicitly out. The render
  harness lands *before* the slice.

- [How do we make "green tests, broken app" impossible?](issues/06-how-do-we-make-green-tests-and-a-broken-app-impossible.md):
  Six seams instead of one: the fake output (kept), **real files** through the production
  decoder, the engine end to end, a presenter, the Form headless, and the human. A real-input
  matrix asserts **completeness, purity and seek round-trip** on every cell — v2 asserted
  length nowhere. Capture becomes an engine operation returning a typed point. FLAC fixtures
  checked in; FLAC seek pinned at 40 ms pending ticket 09. Smoke checklist at `docs/smoke.md`,
  split into a Looks half the agent runs and a Sounds half the user runs. `dotnet test` before
  every commit, reported in the message, by discipline not hook.
  Recorded as [ADR 0002](../../docs/adr/0002-six-seams.md).

- [What is the seam between the shell and the engine?](issues/04-what-is-the-seam-between-the-shell-and-the-engine.md):
  A command surface and an immutable `View`, nothing else. No live collection crosses the
  seam; every command is total; state is published after the lock through an `IEngineSink`
  the shell supplies. Rows keyed by engine-issued `RowId`. Frames never cross the shell seam.
  `Author()` reschedules a pending boundary. Reached by Design It Twice — four designs
  converged on the substance and differed only in packaging. Placement rule in `CLAUDE.md`.

## Not yet specified

- **Restaging the rest of Phase 1.** Once the slice is trustworthy, how the remaining
  features (queue management, repeat/shuffle, preview, visualiser, ReplayGain, settings,
  file robustness) come back, and in what order. Likely mines v2's 14 issue files, but
  whether those are reusable as written is unknown until the slice lands.
- **The fate of the preview feature.** v2 built it with its own mixer and its own output
  device. Whether that is the right design, or whether preview should just be the offline
  render played back, is unclear until the render harness exists. Ticket 04 added a
  constraint: a second engine over its own output cannot open WASAPI-exclusive twice, so
  "preview as a second engine" is unavailable in that mode.
- **Stable track identity.** The store keys transitions by full file path. v2's spec deferred
  real identity to Phase 2 but claimed the schema could absorb the change. Untested, and it
  touches the store format.
- **What happens to v2's transition store data.** There *is* data: one authored transition in
  `%APPDATA%aderio	ransitions.json`, a FLAC boundary with a 37.132 s fade-out point and a
  20 s fade. Schema version 1, keyed by ordered full path pair, reads cleanly. The open part is
  now narrower: v3 reuses the same store and would share that path, so whether the two builds
  can coexist on one machine without schema drift is unexamined.
- **Where the audible-time discipline stops.** Ticket 03 put the playhead, the seek bar, the
  time readout and the visualiser on audible time. Whether *every* user-visible signal owes
  the same honesty — end-of-track notifications, queue advance, ReplayGain switchover — is
  unexamined, and only some of those surfaces exist yet.
- **Audio paths still unmeasured.** Between them, tickets 01 and 02 could not cover AIFF,
  8-bit WAV, sources with more than two channels, the WASAPI exclusive resampling path, real
  music as opposed to test signals, files longer than eight seconds, or seek accuracy at rates
  other than 44.1 kHz. Whether any of these matter depends on how tickets 08 and 09 scope the
  promise. Variable-bitrate MP3 is the sharpest of these and is already a question inside
  ticket 09.

## Out of scope

Beyond this map's destination; returns only if the destination is redrawn.

- **Library manager** (browse by artist/album, stable identity, album gain) — v2 Phase 2.
- **Playlist manager** and the per-playlist override authoring UI — v2 Phase 3.
- **Cross-platform support.** NAudio pins this to Windows, by decision.
- **MusicBee plugin interop.** Abandoned in the prototype era; the engine has to be ours.
- **Metadata editing or tagging tools.**
- **Sidecar transition files.** Rejected in v2's ADR 0002; the store stays player-owned.
