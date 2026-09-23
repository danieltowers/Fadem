# Map — faderio v3: a transition you can trust

Label: `wayfinder:map`

## Destination

faderio v3 plays two tracks with an authored crossfade that lands **exactly where the user
placed it by ear**, with `Faderio.Core` migrated from the v2 build and a rewritten WinForms
shell. The map is done when that loop is trustworthy: the user can add two tracks, set the
fade points by listening, and hear the handoff happen where they put it. Restaging the rest
of Phase 1 is routine work handed off once the way there is clear.

**Reached, 2026-09-23.** Every ticket is resolved and the user ran the Sounds half of
`docs/smoke.md` on the authored boundary from the store: it passed. What remains on this map is fog
for the restaging effort, not steps on this route.

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
  Recorded as [ADR 0003](../../docs/adr/0003-command-surface-and-view.md).

- [Migrate Faderio.Core into v3](issues/07-migrate-faderio-core-into-v3.md):
  Done. Core and tests imported verbatim (true baseline **92**), then the four known defects
  repaired red-first with regression cells, the store's serialisation quirks fixed, and the
  real-input matrix started: **141 tests pass**. Measured along the way: NVorbis rejects
  ffmpeg's default Ogg paging on any seek; FLAC seek lands +67 ms late after the warm-up,
  pinned at 100 ms; and a −3 dB fixture level that nearly became a false decoder finding.
  v2's three ADRs imported as 0004–0006.

- [What audio does faderio promise to play correctly?](issues/08-what-audio-does-faderio-promise-to-play-correctly.md):
  **Two tiers, nothing in between.** A file is *promised* when its format × rate × depth ×
  channels is a row in `docs/supported-audio.md`, every row naming the matrix test that proves
  it; everything else is *rejected* at add time with a structured reason. Promised now: WAV and
  FLAC at 44.1/48/96 kHz, 16/24-bit, mono/stereo; MP3 in three variants (CBR with Info tag, CBR
  bare, VBR) at 44.1/48 kHz. Rejected with a named way back: >2 channels, 8/32-bit, other
  rates, AIFF, M4A/WMA, and **Ogg Vorbis, whose reader is deleted** over the NVorbis paging bug.
  **Engine rate = output device rate, fixed at launch.** Length is not a column. The library
  survey (903 files, all 16-bit stereo, all 44.1 kHz bar four 48 kHz MP3s) is in the ticket.
  Recorded as [ADR 0007](../../docs/adr/0007-audio-is-promised-or-rejected.md) and
  [ADR 0008](../../docs/adr/0008-engine-runs-at-the-device-rate.md).

- [How exact is the authoring promise?](issues/09-how-exact-is-the-authoring-promise.md):
  **1 ms, at every promised cell, globally**; not per-transition; the author sees nothing
  because exactness is a property of Promised. From-end points measure against the decoded
  length, proven by the completeness cell. Ticket 12's S3 test tightens to 1 ms. FLAC misses
  because **Media Foundation lands on the next block boundary and NAudio never trims**; the fix
  is the managed `BunLabs.NAudio.Flac` decoder (exact by construction), Media Foundation leaves
  the chain entirely, and a new task ticket, [Make FLAC seek exact](issues/15-make-flac-seek-exact.md),
  builds it. MP3 already trims within the frame. Pre-scan cost on open accepted with a 500 ms
  budget on the longest library file.
  Recorded as [ADR 0009](../../docs/adr/0009-authoring-exactness-is-bought-in-the-decoder.md).

- [Which length is a track's length?](issues/10-which-length-is-a-track.md):
  **One length, issued by the add-time probe from the stream header, proven equal to the decoded
  length exactly in frames** by a fourth assertion on every matrix cell. `Track.Duration` (the
  tag duration) is deleted; TagLib stays for title, artist and ReplayGain. Audio that ends
  before `Length` is one notice and the track ends there. From-end points resolve against
  `Length`. Opening files to learn length was rejected because a FLAC open now pre-scans the
  whole file (ticket 09). Glossary gains **Length**; "duration" is reserved for fades.
  Recorded as [ADR 0010](../../docs/adr/0010-one-length-issued-from-the-header.md).

- [Rework the engine seam](issues/12-rework-the-engine-seam.md):
  Built. `PlaybackEngine` has ticket 04's surface and nothing else public: commands, an
  immutable `View` composed lock-free from a snapshot plus the device's emitted frames, an
  `IEngineSink` drained after the lock. `IAudioOutput` carries `SampleRate` and `FramesEmitted`
  with a shared origin. Playhead from a per-voice reference history, honest through the
  post-seek drain; attended voice flips at the audible handover; seek cancels a transition;
  `Author()` reschedules. The add-time probe (WAV, FLAC, MP3 headers) issues `Length` and
  structured rejections. Ogg, preview, visualiser and `Track.Duration` deleted. **The S3 null
  test passes at 1 ms** with 100 ms of fake device latency. **162 tests pass.** Found on the way:
  v2's ReplayGain reader threw on every ID3v2-tagged file, unreached by any v2 test.

- [Render a boundary to a file](issues/11-render-a-boundary-to-a-file.md):
  Built. `WavFileAudioOutput` binds the seam to a float WAV; `Renderer.RenderBoundary` renders
  pre-roll, transition and tail through a fresh engine. Tested: the fade lands within 1 ms in
  the file, and the file is frame-for-frame what the fake renders in memory.

- [Build the shell](issues/13-build-the-shell.md):
  Built. `NAudioAudioOutput` (three modes, device rate, `FramesEmitted` from the device clock,
  a render-thread stop signalled as end-of-stream), `PostingSink`, a `Presenter` with exactly
  ticket 06's list, `MainForm` and the editor, launch arguments instead of a settings dialog.
  Headless-form tests over the fake output poke the real controls. **Core 165, App 26 tests
  pass.** The Looks half of the smoke checklist passed on screenshots of the real app, with
  two layout defects found and fixed only by the screenshot. **The Sounds half is the user's**
  and is the map's destination: it has not been run.

- [Make FLAC seek exact](issues/15-make-flac-seek-exact.md):
  Built. FLAC decodes through `BunLabs.NAudio.Flac` behind `FlacTrackReader`; the pinned 100 ms
  cell is a **1 ms** landing, red at +67 ms on Media Foundation first, and Media Foundation is
  out of the chain. The matrix caught two package defects (position stops short at end of
  stream; a seek into the last block did nothing), both repaired in the subclass with a second
  landing cell. 24-bit and 96 kHz FLAC decode clean. Opening the library's longest FLAC (26 min)
  pre-scans in about 250 ms, within the 500 ms budget: no background pre-scan needed.
  **Core 170, App 26 tests pass.**

- [Fill the real-input matrix](issues/14-fill-the-real-input-matrix.md):
  Done. **Every row of `docs/supported-audio.md` is proven: 36 cells, five theories each, every
  cell seeking to 1 ms**, no pin, no skip, no change to `src/`. WAV, FLAC and MP3 (three
  variants) × every promised rate, depth and channel count; 48 fixtures checked in with their
  ffmpeg script. Ticket 02's pink-noise cross-correlation is now the permanent seek measure.
  Found: completeness is exact even through the resampler (the ±100-frame allowance was never
  needed, so ticket 09's resampler constant is zero); MP3 length is whole 1152-frame blocks and
  the probe issues it exactly from all three variants, so no MP3 row scans frames; MP3 seeks
  are sample-exact through NAudio. **Core 305, App 26 tests pass.**

## Not yet specified

Nothing: this map's destination is reached. Its fog was carried forward on 2026-09-23 to
[faderio v3: restage the rest of Phase 1](../202609-faderio-phase-1-restage/map.md). Restaging,
preview, audible-time discipline, the folder-add probe, MP3 gapless padding and the M3U writer
became tickets or fog there. Stable track identity, the rejected formats and v2/v3 store
coexistence were ruled out of scope there; v2 is retired for listening.

## Out of scope

Beyond this map's destination; returns only if the destination is redrawn.

- **Library manager** (browse by artist/album, stable identity, album gain) — v2 Phase 2.
- **Playlist manager** and the per-playlist override authoring UI — v2 Phase 3.
- **Cross-platform support.** NAudio pins this to Windows, by decision.
- **MusicBee plugin interop.** Abandoned in the prototype era; the engine has to be ours.
- **Metadata editing or tagging tools.**
- **Sidecar transition files.** Rejected in v2's ADR 0002; the store stays player-owned.
