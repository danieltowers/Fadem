# Migrate Faderio.Core into v3

Type: task
Status: resolved
Blocked by: 01, 05

## Question

Nothing to decide here: this is the manual work that unblocks building the slice. It earns
its place on the map because no slice ticket can start until v3 has a compiling, tested core.

The user settled the shape at charting: move `Faderio.Core` into the fresh v3 repo, treat
v2 as read-only reference, rewrite the shell rather than port it.

What this involves:

- Copy `src/Faderio.Core/` and `tests/Faderio.Core.Tests/` from
  the v2 build (`v2-GLM-5.3-flash`, kept outside this repo), excluding `bin/` and `obj/`.
- Copy `CONTEXT.md` and `docs/adr/` across; they are the domain record and still apply.
- Create the solution and `Directory.Build.props`. v2 targets `net8.0`; the machine has the
  .NET 9 SDK only, so confirm the net8.0 targeting pack is present or settle on net9.0.
- Delete `tests/Faderio.Core.Tests/UnitTest1.cs`, an untouched xUnit template containing an
  empty test.
- Run the suite and record the real number of passing tests. Note that a stale log in v2
  (`run3.txt`) reports 86, a code assessment reported 93, and the source declares 87 `[Fact]`
  or `[Theory]` attributes. Establish the true figure rather than repeating any of them.
- Do **not** bring `src/Faderio.App/` across. It is being rewritten, and two of its files
  read as a working thin shell while actually holding the queue-ownership bug and the
  11-position seek bar, so keeping them around invites reuse.
- Commit as a clearly labelled import so the provenance of this code stays visible in v3's
  history.

## Known defects to carry a fix for

Found and confirmed by
[ticket 01](01-is-the-audio-output-clean-across-real-formats.md). These are routine repairs,
not decisions, so they belong here rather than on tickets of their own. Each needs a
regression test, because the entire suite passed with all three present.

- **Inverted position conversion**, `src/Faderio.Core/Playback/SourceNormalizer.cs:70-71`.
  The getter divides by `_framesPerInnerFrame` and should multiply; the setter multiplies and
  should divide. `TotalFrames` on line 65 is already correct. Every file above 44.1 kHz is
  truncated by (rate / 44100)², so a 48 kHz track ends 15 % early. Two characters.
- **Missing extensible encoding**, `src/Faderio.Core/Playback/AudioSources.cs:135`.
  `ConvertToFloat` has no `WaveFormatEncoding.Extensible` case, so ordinary 24-bit WAVs throw
  on every block and render as silence with a status notice each time. One switch case.
- **Fragile Vorbis seek**, `src/Faderio.Core/Playback/AudioSources.cs:172`. Setting `Position`
  before any `Read` or `TotalFrames` access throws out of NVorbis. Latent today only because
  the engine happens to touch `TotalFrames` first.
- **FLAC ignores the authored fade-in point**, found by
  [ticket 02](02-can-we-seek-sample-accurately-in-compressed-formats.md).
  `TransitionMixer.AddVoice` (`src/Faderio.Core/Playback/TransitionMixer.cs:112`) seeks
  immediately after opening the source, before any read, and the Media Foundation reader that
  handles FLAC silently drops that first seek. The incoming track enters at its first sample
  and the authored value is discarded entirely. One warm-up read before the seek takes the
  error from 2267 ms to 31 ms. Apply it regardless of what
  [ticket 09](09-how-exact-is-the-authoring-promise.md) decides, since it is near-free and
  removes the only catastrophic case. The residual 31 ms is ticket 09's business.

Do not let these fixes drift into a redesign of the conversion layer; that policy question is
[ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md).

Blocked by ticket 01, now resolved, because if the engine corrupted real music files there
would be nothing worth migrating. Still blocked by ticket 05, because the slice's scope
decides whether the store, ReplayGain and preview code come along now or later.

## Answer

**Done. v3 has a compiling, tested core: 141 tests pass, up from a true baseline of 92, with
every known defect repaired red-first and the real-input matrix started.**

### What was done

**Imported verbatim, then repaired — two commits.** The import (`8bc92c4`) copies
`src/Faderio.Core` and `tests/Faderio.Core.Tests` from v2 commit `91cb980` unchanged, minus
`bin/`, `obj/`, the 31 checked-in `.trx` artefacts, and `UnitTest1.cs`. `Directory.Build.props`
as-is; `faderio.sln` new; `.gitignore` gains v2's entries (which v2 had and ignored).
`src/Faderio.App` deliberately not imported. The repairs are a separate commit so each is a
reviewable diff against a green baseline.

**The true test count is 92**, not 86 (stale log), 87 (attribute count) or 93 (estimate). The
87 `[Fact]`/`[Theory]` attributes expand to 92 cases.

**Stack unchanged.** `net8.0` builds under the 9.0.318 SDK with the 8.0.31 runtime present.
No TFM change.

### Repairs, each red before green

| Defect | Fix | Regression test |
|---|---|---|
| **A** — `NormalizedTrackSource.Position` inverted, files above 44.1 kHz end early | two operators swapped | completeness and seek-halfway at 48/96 kHz; `Engine_PlaysWholeTrack_ThenFallsSilent` |
| **B** — no `Extensible` case, 24-bit WAVs silent | resolve the sub-format GUID from `WaveFormatExtraData` byte 6 | every 24-bit cell (all three assertions) |
| **C** — Vorbis seek-before-read throws | **not reproducible as stated**; ctor touches the length anyway | `Ogg_SeekBeforeFirstRead_DoesNotThrow_AndLands` |
| **FLAC** — Media Foundation drops a seek before its first read | warm-up read on first seek | `Flac_SeekBeforeFirstRead_LandsWithin100ms_PendingTicket09` |
| `IsEmpty` leaks into JSON | `[JsonIgnore]` | file text does not contain it |
| `Curve` serialises as an integer | `JsonStringEnumConverter` | by name; v2's exact integer shape still loads |

Plus `Track` gets an `internal` constructor (ticket 04) and `InternalsVisibleTo` for the tests.

### Three things measured that were not known

1. **Defect C is really an encoder-compatibility issue.** Seek order makes no difference to
   NVorbis 0.10.5. What it rejects — with `GranulePos mismatch`, on *any* seek, sequential
   decode fine — is **ffmpeg's default 1 s Ogg page duration**. With 10 ms pages it seeks
   within 40 ms. Ticket 02's "effectively exact" OGG result was on a compatible file. Real-world
   files paged by ffmpeg's defaults will not seek in this player. → ticket 08.
2. **FLAC seek lands +67 ms late** after the warm-up, on this fixture, independent of warm-up
   size (1 frame or 4096). Ticket 02 saw 31 ms on its own 8 s file. Pinned at 100 ms. → ticket 09.
3. **A false finding, caught.** An early version of the landing measure assumed the fixture's
   peak and reported FLAC 392 ms *early* and OGG ~430 ms early — two unrelated decoders, one
   offset, which was the tell. ffmpeg's `-ac 2` upmix writes these fixtures at −3 dB; a raw
   byte read of the WAV confirmed it. The landing measure now self-calibrates against a
   sequential decode of the same file — ticket 02's ground-truth method — and assumes no gain.
   Recorded in the fixture script so nobody "fixes" the decoders for it.

### The matrix skeleton (seam S2)

`RealInputMatrixTests`: WAV cells generated at test time via `TestAudio.WriteWavAt` across
16/24-bit × 44.1/48/96 kHz × mono/stereo (24-bit with an extensible header, as tools write it),
three assertions per cell — **completeness** (frames read equals expected, and `Position` equals
`TotalFrames` after exhaustion), **purity** (`TestAudio.Purity`, Goertzel at an exact bin,
≥ 0.999), **seek** (halfway-then-count on every cell; exact ramp landing at 44.1 kHz). Compressed
cells from three checked-in fixtures (58 KB: `tone-1k.flac`, `ramp.flac`, `ramp.ogg`) with
`Fixtures/generate.sh` beside them. `Landing()` reports what the decoder did in its failure
message. Ticket 14 fills the grid.

### Not done here, and why

- **Internal `Frames` type** — belongs with the seam rework ([ticket 12](12-rework-the-engine-seam.md)),
  where `FramesEmitted` is introduced; doing it during migration would be the redesign the
  ticket forbids.
- **Second test project for the shell** — cannot exist before the shell does. Lands with
  [ticket 13](13-build-the-shell.md).
- **v2's ADRs** — imported as 0004–0006 with a provenance line, since the store they describe
  is the one we reuse. `CONTEXT.md` was already carried over in ticket 03.

### Facts this rests on

- `WaveFileReader` returns `WaveFormatExtraData`, not `WaveFormatExtensible`, for an extensible
  header; the sub-format GUID is at offset 6 of `ExtraData`.
- NVorbis 0.10.5 + ffmpeg default paging: `GranulePos mismatch: Page 3` on seek, both
  `libvorbis` and native `vorbis` encoders; `-page_duration 10000` seeks cleanly.
- Media Foundation FLAC: seek before first read dropped; after warm-up, +67 ms on a 2 s file.
- swresample mono→stereo upmix: 0.707 gain. Raw s16 at frame 66150 of the ramp: 15638 (0.477),
  not 22118 (0.675).

Commits: `8bc92c4` (import), then the repairs. Tests: 141 passed, 0 failed.

## Noted from ticket 05

Ticket 05 settled what the slice needs, which fixes this ticket's scope. The repairs to land
during migration, all small and all already diagnosed:

- **Defect A** — `SourceNormalizer.Position` getter/setter conversion is inverted, truncating
  every file above 44.1 kHz by (rate/44100)². Two characters.
- **Defect B** — `ConvertToFloat` has no `WaveFormatEncoding.Extensible` case, so ordinary
  24-bit WAVs render as silence. One switch case. **"WAV works" is false until this lands**, and
  the slice depends on WAV being the exact control.
- **Defect C, latent** — `VorbisTrackSource.Position` throws if set before any read; production
  survives only by accident of ordering.
- **FLAC warm-up read** — one read before the first seek, dropping error from 2267 ms to 31 ms.
  Near-free and removes the only catastrophic case, whatever
  [ticket 09](09-how-exact-is-the-authoring-promise.md) decides.
- **`TransitionSpec` serialisation** — the computed `IsEmpty` property leaks into the JSON for
  want of a `[JsonIgnore]`, and `Curve` would serialise as a bare integer with no
  `JsonStringEnumConverter`.

The store itself migrates as-is: `JsonTransitionStore` is 155 lines, takes a path in its
constructor, depends only on `TransitionSpec` and an atomic-write helper, and carries 12 tests.
Ticket 05 chose to reuse it rather than start in memory.

Not in this ticket: the render harness, which ticket 05 split out as
[ticket 11](11-render-a-boundary-to-a-file.md) and which this ticket blocks.

## Noted from ticket 06

Test-suite work that lands with the migration:

- **A second test project** on `net8.0-windows` that references `Faderio.App`, with an
  `InternalsVisibleTo` on the App project, so the Form can be instantiated headless.
- **Delete `UnitTest1.cs`** — an empty xUnit scaffold whose one test has no body and passes by
  doing nothing. **Do not check in `TestResults/`** — v2 has 30 `.trx` artefacts committed.
- **Completeness assertions on the existing synthetic fixtures**: rendered frame count equals
  decoded length. v2 asserted this nowhere. Cheap, and it is the assertion that would have
  caught defect A.
- **The real-input matrix skeleton** (ticket 06, seam S2): WAV cells generated at test time
  across 16/24-bit × 44.1/48/96 kHz × mono/stereo; FLAC cells from checked-in fixtures with the
  generating ffmpeg script beside them. The purity and seek-round-trip methods are documented
  in tickets 01 and 02 — their code did not survive and is rebuilt here.

## Noted from ticket 04

Three things the seam decision hands to the migration:

- **`Track` gets an internal constructor.** Only the engine issues a `Track`, so the shell can
  never fabricate a length. Which length the engine issues is ticket 10's call.
- **An internal `Frames` type** distinct from sample counts, as engine discipline. v2's
  frames-vs-samples bug lived inside the engine at the output adapter; this is where the type
  goes. Publicly, `Frames` appears only on `IAudioOutput`.
- **`ITransitionStore` stays as-is** and is handed to the engine. `Author()` lives on the
  engine and writes through it; the shell holds the store only to construct the engine.

This ticket is now unblocked and is the gate on [12](12-rework-the-engine-seam.md), [14](14-fill-the-real-input-matrix.md)
and, through 12 and 11, [13](13-build-the-shell.md). It is the next thing to build.
