# Migrate Faderio.Core into v3

Type: task
Status: open
Blocked by: 01, 05

## Question

Nothing to decide here: this is the manual work that unblocks building the slice. It earns
its place on the map because no slice ticket can start until v3 has a compiling, tested core.

The user settled the shape at charting: move `Faderio.Core` into the fresh v3 repo, treat
v2 as read-only reference, rewrite the shell rather than port it.

What this involves:

- Copy `src/Faderio.Core/` and `tests/Faderio.Core.Tests/` from
  `v2-GLM-5.3-flash`, excluding `bin/` and `obj/`.
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

_(pending)_
