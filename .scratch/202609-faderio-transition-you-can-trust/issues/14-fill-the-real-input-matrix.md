# Fill the real-input matrix

Type: task
Status: resolved
Blocked by: 07

## Question

Nothing to decide: build seam S2 from
[ticket 06](06-how-do-we-make-green-tests-and-a-broken-app-impossible.md) — real files through
the production decoder chain, three assertions per cell. Ticket 07 lands the skeleton; this
ticket fills it. Independent of the seam rework (12) and the shell (13), so it can run in
parallel with either.

The FFT and cross-correlation harnesses from tickets 01 and 02 do not exist as code. Their
methods are fully documented in those tickets' answers; this is where they are rebuilt, as
permanent tests, per the map's research rule.

### What "done" means

- **Cells**: WAV and FLAC × 16/24-bit × 44.1/48/96 kHz × mono/stereo. WAV generated at test
  time by `WaveFileWriter`; FLAC **checked in**, with the generating ffmpeg script beside the
  fixtures. Shaped so that a format [ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md)
  adds later is one row.
- **Three assertions on every cell**, through the production chain:
  1. Completeness — rendered frame count equals decoded length.
  2. Purity — ≥ 0.999 of spectral energy in band (ticket 01's method).
  3. Seek round-trip — pink noise, seek to a known offset, recover the landing point by
     cross-correlation (ticket 02's method).
- **FLAC seek is a pinned characterisation test** at ≤ 40 ms, named
  `Flac_SeekLandsWithin40ms_PendingTicket09`. Green, documented, catches a regression to
  2267 ms.
- Defects A and B from ticket 01 are provably fixed: the 48 kHz and 96 kHz cells complete,
  the 24-bit cells are not silent.
- No cell skips. A missing fixture is a failure.

## Answer

**Done. Every row of `docs/supported-audio.md` is proven, 36 cells, five theories each, and
every cell seeks to 1 ms.** Core 305 tests pass (from 170), App 26. No cell is pinned, no cell
skips, and nothing in `src/` changed: the decoders and the probe ticket 12 and 15 left were
already right for every cell, so this ticket is tests, fixtures and the doc.

### What was built

- **`RealInputMatrixTests` is the grid.** `WavCells()`, `FlacCells()` (16/24-bit × 44.1/48/96
  kHz × mono/stereo, twelve each) and `Mp3Cells()` (`Mp3Variant` `CbrInfo`/`CbrBare`/`Vbr` ×
  44.1/48 kHz × mono/stereo, twelve). Five theories per format, named
  `<Format>_ReadsToCompletion`, `_ToneIsPure`, `_HeaderLengthEqualsDecoded`,
  `_SeekBeforeFirstRead_LandsWithin1ms` and `_SeekIntoLastBlock_LandsWithin1ms`; the doc's
  "Proven by" column names them and its Status column reads "proven" on every row.
- **Ticket 02's method exists as code.** `TestAudio.PinkNoise` and `TestAudio.Locate`: the
  first block read after a seek is cross-correlated (cosine similarity, SIMD dot product)
  against a sequential decode of the same file, and the best offset is the landing. Nothing
  about the decoder's gain, delay or block size is assumed, which is what let the same
  `Landing()` serve WAV, FLAC and MP3 unchanged. It searches ±0.25 s of the target first and
  the whole file on a miss, so a red cell reports where the seek actually went. The ramp
  method is gone.
- **Fixtures: 48 files, 5.9 MB, checked in**, one tone and one pink-noise file per compressed
  cell, named `<signal>-<rate>-<bits>-<ch>.flac` and `<signal>-<variant>-<rate>-<ch>.mp3`, all
  from `Fixtures/generate.sh`. The five ticket-07/15 fixtures (`tone-1k*.flac`, `ramp.flac`,
  `tone-1k.mp3`) are replaced by their grid equivalents. A missing fixture fails the cell
  with the script's name.
- **Completeness is exact**, not ±100 frames: at every cell, resampled or not, the drain
  renders exactly `TotalFrames`. Ticket 09's question about the resampler's landing constant
  is answered: there is none. `WdlResamplingSampleProvider` delivers the rounded length to the
  frame, and every 48 and 96 kHz landing is within 1 ms with no compensation. The
  `Wav_SeekHalfway` allowance tightened from ±100 to ±1 (the midpoint rounds once on the way
  in).

### Measured

| Cell group | Completeness | Purity | Header = decoded | Landing at ¾ | Landing in last block |
|---|---|---|---|---|---|
| WAV, 12 cells | exact | ≥ 0.999 | exact | ≤ 1 ms | ≤ 1 ms |
| FLAC, 12 cells | exact | ≥ 0.999 | exact | ≤ 1 ms | ≤ 1 ms |
| MP3, 12 cells | exact | ≥ 0.999 | exact | ≤ 1 ms | ≤ 1 ms |

- **MP3 length is whole frames.** A 2 s tone encodes to 78 frames of 1152 at 44.1 kHz (89856
  frames) and 85 at 48 kHz (97920): LAME's encoder delay plus padding to a frame boundary.
  `Mp3FileReader` decodes exactly that many, and the probe issues exactly that many from all
  three variants: the Info tag's count (`CbrInfo`, `Vbr`) and the audio bytes over the frame
  size (`CbrBare`, so **no MP3 row forces a frame scan**; ticket 10's worry about VBR did not
  materialise because ffmpeg/LAME write a Xing tag on VBR by default). `AudioProbeTests`'
  "within two frames" is tightened to the exact count. A consequence worth knowing: an MP3's
  `Length` includes up to about 50 ms of encoder silence at each end, on the same timeline as
  the playhead, so the promise holds; whether to trim it (LAME's gapless info) is a restaging
  question, noted on the map.
- **MP3 seeks are sample-exact through NAudio**, before any read and into the last frame, on
  all twelve cells, both channel counts and both rates. Ticket 09's "MP3 already trims within
  the frame" is now proven rather than read from the source.
- **Suite time** went from 2 s to 11 s: 72 cross-correlations in an unoptimised build. The
  near-target search keeps it there; a full-file search on every cell was 30 s.

### Not done, by decision

- No AIFF, 8/32-bit, >96 kHz or Ogg cells: rejected formats, no row (ticket 08).
- No long-file or real-music cell; still fog on the map ("Audio paths still unmeasured").

## Noted from ticket 07

The skeleton exists: `RealInputMatrixTests` with all twelve WAV cells green on three assertions,
plus FLAC and OGG at 44.1 kHz stereo 16-bit. What this ticket adds is the compressed grid.
Tools already there: `TestAudio.WriteWavAt` (any rate/depth/channels, extensible header for
24-bit), `TestAudio.Purity` (Goertzel at an exact bin — window must hold whole cycles),
`Landing()` (self-calibrating seek measure; do not assume fixture gain, the ffmpeg upmix writes
−3 dB), and `Fixtures/generate.sh` (one ffmpeg line per fixture; Ogg needs `-page_duration
10000` or NVorbis will not seek). Pink-noise cross-correlation from ticket 02 is still unbuilt;
the ramp method has served so far.

## Noted from ticket 08

[Ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md) set the promised table in
`docs/supported-audio.md`; this ticket's job is to turn every "pending" row there green. The
cell list in "What done means" above is amended as follows:

- **FLAC**: as written, 16/24-bit × 44.1/48/96 kHz × mono/stereo, twelve fixtures, one
  `FlacCells()` member data feeding `Flac_ReadsToCompletion`, `Flac_ToneIsPure` and the seek
  landing theory. 24-bit FLAC is promised despite zero library files.
- **MP3, new**: three variants × 44.1/48 kHz × mono/stereo = twelve fixtures, one `Mp3Cells()`
  member data with a variant parameter. Variants: `CbrInfo` (ffmpeg default, writes an Info
  tag), `CbrBare` (`-write_xing 0`), `Vbr` (`-q:a 2`). If the VBR seek landing misses the
  tolerance, pin it `_PendingTicket09` like FLAC's; do not widen the tolerance.
- **Ogg**: removed. Ticket 12 deletes the reader, its tests and `ramp.ogg`. If this ticket
  runs first, leave them; do not add Ogg cells.
- **No AIFF, 8-bit, 32-bit or >96 kHz cells.** Those formats are rejected, and a cell without a
  doc row proves nothing the product claims.

Each theory name in the doc's "Proven by" column must exist verbatim when this ticket closes,
and the doc's Status column flips from "pending ticket 14" to "proven" in the same commit.

## Noted from ticket 09

The tolerance is set: **1 ms landing at every promised cell.** Consequences for this ticket:

- The seek-landing theory asserts 1 ms on every WAV, FLAC and MP3 cell. No pinned
  characterisation values remain once ticket 15 lands; if a cell cannot reach 1 ms, its row
  leaves `docs/supported-audio.md` rather than the tolerance widening. The VBR MP3 pin from
  ticket 08's note is the one permitted exception, and only until ticket 09's owner decides.
- **FLAC cells decode through `BunLabs.NAudio.Flac`** after ticket 15. If this ticket runs first,
  the FLAC seek cells will fail at 1 ms on Media Foundation; leave them red with a
  `PendingTicket15` name rather than loosening them, and land the rest of the grid.
- The ±100-frame allowance on resampled WAV cells is a resampler landing question, not a
  decoder one; ticket 08 decided the engine compensates known resampler latency rather than
  the tolerance absorbing it. Measure it, and if it is a constant, that constant is a finding
  for ticket 12.

## Noted from ticket 10

**A fourth assertion on every cell**: the length the add-time probe issues from the header
equals the decoded length, **exactly, in frames** (ADR 0010). Build a `ProbeLength(path)` helper
beside `Landing()` that reads only the header the way the production probe does, and a
`*_HeaderLengthEqualsDecoded` theory over each `*Cells()` member data. For MP3 this is where
the three variants earn their rows: Xing/Info frame count for `CbrInfo`, file size over frame
size for `CbrBare`, and `Vbr` is the one most likely to force a frame scan. A red cell here does
not widen to a tolerance; it changes how the probe learns the length for that format, and the
doc row records the add-time cost.

## Noted from ticket 12

The harness moved under you; the matrix skeleton is green on it:

- `TrackSources.Open(path)` (internal static) replaces `new NAudioAudioSourceFactory().Open(track)`;
  `NormalizedTrackSource.Wrap(source, engineRate)` takes the rate. `EngineRate` in the matrix is
  the literal 44100 the fake declares.
- `TestEngine.Add(paths)` / `Play(paths)` replace `SetQueueOf`; both go through the production
  probe, so an unpromised fixture throws in the harness rather than being queued.
  `TestEngine.TrackOf(path)` builds a track without the probe when a cell needs that.
- `FakeAudioOutput.Advance(int)` and `Advance(Frames)`; `FramesEmitted`; `Latency` (default 0).
- **The fourth assertion's helper exists**: `AudioProbe.Probe(path, out ProbeResult)` returns the
  header's shape and frame count. `AudioProbeTests` already asserts it on WAV cells and the two
  FLAC fixtures; the MP3 fixture `tone-1k.mp3` (CBR 320 kbps, Info tag, 2 s) is checked in and
  its probe length is asserted to within two frames — tighten to exact equality against
  `Mp3FileReader.Length` as the first MP3 cell.
- Ogg is gone (reader, cells, fixture, package). `generate.sh` documents the MP3 line.

## Noted from ticket 15

The FLAC decoder is now `FlacTrackReader` over `BunLabs.NAudio.Flac` 2.0.1 and the 44.1 kHz /
16-bit / stereo landing is proven at 1 ms in `Flac_SeekBeforeFirstRead_LandsWithin1ms`, with a
second landing inside the last block (`Flac_SeekIntoLastBlock_LandsWithin1ms`). Completeness and
purity are theories over `FlacToneFixtures()` (`tone-1k.flac`, `tone-1k-24bit.flac`,
`tone-1k-96k.flac`); add the rest of the grid there. The landing cells need a ramp fixture per
rate; only `ramp.flac` (44.1 kHz) exists. Two package facts worth knowing when a cell goes red:
the package's `Position` counter skips any read that reaches end of stream (repaired in the
subclass by counting frames), and its `FlacMetadataStreamInfo.MinBlockSize`/`MaxBlockSize` are
read little-endian and so are wrong (4608 reads as 18); the decoder does not use them, and the
add-time probe reads STREAMINFO itself.
