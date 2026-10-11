# What exactly is in the narrow slice?

Type: grilling
Status: resolved
Blocked by: 01

## Question

The user chose a narrow vertical slice over restaging all 14 of v2's Phase 1 issues, because
the thing that killed v2 is that nothing was provable until everything was written.

The slice is roughly: **load two tracks, author the boundary between them by ear, hear the
fade land where you put it.** That sentence needs turning into a boundary sharp enough to
build to and to stop at.

To settle:

1. What is the minimum UI? A queue that holds two tracks, transport, a real seek bar, the
   transition editor for one boundary. Is a visualiser in or out? It is the fastest way to
   *see* a fade happening, which argues for in; it is also pure surface area, which argues
   for out.
2. Which of the four transition parameters are authorable in the slice? All four, or just the
   fade-out point and fade-out duration, with the incoming side following once the outgoing
   side is trustworthy?
3. Does the slice persist anything? Reusing v2's JSON store costs almost nothing and is
   already tested, but an in-memory transition would prove the audio loop sooner.
4. Which audio formats must work in the slice? The honest answer depends on ticket 01 and
   ticket 02: if compressed seeking is inaccurate, the slice may need to be WAV-only to be
   provable at all, with formats as the next ticket rather than a hidden assumption.
5. What is explicitly deferred, written down so it does not creep back in: repeat, shuffle,
   preview, ReplayGain, settings dialog, M3U, multi-track queues, end-of-queue behaviour.
6. **What is the acceptance test the user personally performs?** The slice is done when the
   user does a specific thing and hears a specific result. Write that sentence down.

## Answer

**The slice is: two tracks in a queue, one boundary authored by ear, the fade lands where you
put it — provable in the app and in a rendered file, on three output modes.**

### The acceptance test

Written first, because everything below is derived from what it requires.

> **In the app.** With two tracks in the queue, I play the outgoing track, press capture at a
> moment I choose by ear, set a fade duration, save, then play the boundary — and the outgoing
> track begins fading at the moment I chose, **not audibly later**. I can iterate: seek back,
> listen, adjust, listen again.
>
> **Out of the app.** I render that same boundary to a file and hear the same thing without
> launching the UI.
>
> **Measured.** The rendered fade onset is within **5 ms** of the captured point, asserted
> automatically.
>
> **On three output modes.** The same test on WASAPI shared, WASAPI exclusive and WaveOut, and
> the fade lands in the same place on all three.

**Material matters and is part of the test.** v2 would have passed a loosely worded version of
this: "I set a fade point and heard a crossfade" was *true* in v2, and the defect — a fade
100-250 ms late — is inaudible on a sustained pad. So the falsifying material is a track with an
**unambiguous percussive transient**: capture aimed at the hit, and the fade must start *on* the
hit, not after it. The existing authored boundary in the user's store (two tracks
of a prog album) rides along as a realism check on real prog material, where the defect is hardest
to hear.

**Why 5 ms.** WAV seeks exact, so the engine's own error should be ~0; 5 ms is tight enough to
catch any latency-class regression (those are 50-150 ms) while leaving the actual exactness
promise to [ticket 09](09-how-exact-is-the-authoring-promise.md). It is a slice-local tripwire,
not a product claim.

**Why three output modes.** This is what converts "the playhead is measured, not guessed" from
an assertion into a demonstration. The three modes differ by 50/100/150 ms; under a hardcoded
constant the test would provably fail on two of them.

### In the slice

**Playhead.** All of [ticket 03](03-what-position-does-set-from-here-capture.md): measured
device clock, distinct types, attended voice, and the post-seek drain honesty. None of it
deferred. The authoring loop *is* seek → listen → capture → seek → listen, so the post-seek
window is not an edge case here — it is the main path, hit on every iteration.

**Formats: 44.1 kHz WAV and FLAC.** WAV is the exact control; FLAC is the user's actual library.
This reverses the more rigorous WAV + constant-bitrate-MP3 pairing, deliberately: a slice that
cannot be pointed at your own music is a slice you will believe less, and belief is the
deliverable. FLAC's ~31 ms residue (after the warm-up read) sits inside the measured quantity
and is accepted for now — the by-ear half runs on WAV, where the answer is exact.

**Transition parameters: all four, from-start only, curve fixed at equal-power.** Outgoing-side
only would never put two voices in the air at once, and that overlap is exactly where the
attended-voice rule lives. Curve is a knob with no correctness content here. From-end is
excluded because it resolves against a track length, and
[ticket 10](10-which-length-is-a-track.md) established that faderio currently has two
disagreeing lengths — from-end is the one case where a wrong length silently *moves an
already-authored point*.

**Persistence: reuse v2's `JsonTransitionStore`.** Confirmed standalone — 155 lines, a
`(string path)` constructor, eager load, depends only on `TransitionSpec` and an atomic-write
helper, 12 tests. Authoring by ear is iterative and unbounded in time: you set a point, live
with it, come back tomorrow unconvinced. A slice that forgets on exit cannot demonstrate that.

**UI:** add-files, a plain list, a context menu on a row for "edit transition (this → next)",
transport, a real seek bar, and the transition editor. `PlaybackQueue` is Core and already
carries 9 tests, so an n-track queue is nearly free behind the UI — but folder recursion, M3U,
reorder, shuffle and repeat stay out.

**Output mode** is selectable by command-line argument or config line. No settings dialog.

**File robustness, narrowest sliver only:** a file that fails to open produces a message rather
than a crash. A slice that dies on a bad file wastes time during precisely the iterative loop it
exists to prove.

**End of queue:** playback stops cleanly. With two tracks you reach the end every single run, so
*something* must happen; the authored end-of-queue fade-out (`FindForOutgoing`) stays out.

### Explicitly deferred

Written down so it cannot creep back. Against v2's 14 Phase 1 issues:

M3U and queue management beyond add-files (0003) · preview, including the editor's Preview
button (0010) · **visualiser (0011)** · ReplayGain (0012) · settings persistence and dialog
(0013) · robust file handling beyond the sliver above (0014) · repeat, shuffle and end-of-queue
semantics (0008) · per-playlist overrides — the store supports them, no UI · fade-curve
selection · from-end measurement · transition delete/unauthor.

**The visualiser is the notable cut.** It was the ticket's own open question, and the render
harness settles it: a rendered file inspected in any waveform editor is a strictly better
instrument than a 10 Hz level meter for the one thing the slice must prove. It is also no longer
free — ticket 03 requires it be held back to audible time or its bars move before the sound. It
is the clearest case in the slice of surface area that *looks* like verification without being
it.

### Prerequisites, before the slice

Two things land first, both in [ticket 07](07-migrate-faderio-core.md)'s territory:

1. **The core migration**, including the repairs tickets 01 and 02 turned up: the inverted
   `SourceNormalizer.Position` conversion, the missing `Extensible` case that renders 24-bit
   WAVs as silence, the latent NVorbis ordering hazard, and the FLAC warm-up read. "WAV works"
   is false today.
2. **The render harness** — [ticket 11](11-render-a-boundary-to-a-file.md). It does not exist:
   there is no file-writing audio path anywhere in v2. Building it *first* means every
   subsequent session can hear its own work without a WinForms window, including sessions that
   break the UI. Shipping it after the slice would repeat the v2 pattern of nothing being
   provable until everything is written.

### A correction to the map

The charting session recorded "rewrite the WinForms shell wholesale", premised on *"an untested
~800-line shell"*. **That premise is false.** The shell is 1033 lines across 9 files and is
genuinely thin and well-decomposed: `MainForm` split into four partials by concern, panels
isolated, composition root separate, NAudio behind a factory. Core is 2642 lines.

The conclusion survives, on better grounds:

- Ticket 03 changes **every position consumer in the shell** — capture, the seek bar's read
  *and* write, the time readout, the visualiser's timing. Those are exactly the parts worth
  porting, and porting them means porting the defect and rewriting it anyway.
- The shell is not merely untested but **untestable as constituted**: there is no test project
  referencing `Faderio.App` at all. All 87 tests reference `Faderio.Core` only.

The map's Notes line is amended accordingly. The accurate lesson is not "the shell was too big"
— it is **the shell was thin, well-structured, and still shipped every defect the user hit,
because no test could reach it.** Size was never the disease.

### Facts this rests on

- Shell: 1033 lines / 9 files. Transition editor 187 lines (all four parameters, curve combo,
  from-end toggle on the fade-out point only, Preview, and a "Reset" that clears rather than
  reverts). Queue ~198 lines. Transport/display ~148. Visualiser 71. Settings dialog 87.
- Tests: 87 `[Fact]`/`[Theory]`, one test project, `Faderio.Core` only, **zero** touching the
  App. `FakeAudioOutput` (91 lines) already pulls the mixer deterministically in 4410-frame
  blocks and captures every rendered block.
- Store: `%APPDATA%\faderio\transitions.json`, schema version 1, keyed by ordered full-path
  pair. Contains one real authored transition — a FLAC boundary with a 37.132 s fade-out point
  and a 20 s duration.
- No offline render exists anywhere in v2; the only `WaveFileWriter` generates test fixtures.

### Surfaced, not settled here

- **`TransitionSpec` serialisation repairs** for ticket 07: the computed `IsEmpty` property
  leaks into the JSON for want of a `[JsonIgnore]`, and `Curve` would serialise as a bare
  integer with no `JsonStringEnumConverter`.
- **How the slice is actually built, and in what order** — left as fog rather than pre-sliced,
  because [ticket 04](04-what-is-the-seam-between-the-shell-and-the-engine.md) (the seam) and
  [ticket 06](06-how-do-we-make-green-tests-and-a-broken-app-impossible.md) (verification) both
  shape it and are still open.
