# Supported audio

What faderio promises to play and author correctly, and what it refuses. Decided in
[wayfinder ticket 08](../.scratch/202609-faderio-v3/issues/08-what-audio-does-faderio-promise-to-play-correctly.md);
policy recorded in [ADR 0007](adr/0007-audio-is-promised-or-rejected.md) and
[ADR 0008](adr/0008-engine-runs-at-the-device-rate.md).

**This table is the contract.** Every row names the real-input matrix test that proves it
(`tests/Faderio.Core.Tests/RealInputMatrixTests.cs`). A row without a passing test is
unpromised and must not be in this table. A test cell without a row here is a proof of nothing
the product claims; add the row or delete the cell. Rejection messages quote this file.

Two tiers, nothing in between: a file is **promised** or it is **rejected** when it is added.
Nothing plays on a best-effort basis.

## Promised

A file is promised when its format, sample rate, bit depth and channel count all appear in one
row. Every promised cell is proven four ways through the production decoder chain:
**completeness** (every frame the file holds is rendered), **purity** (a test tone comes out as
that tone), **seek landing** (a seek lands where it was asked to, within the tolerance below)
and **length** (the length the add-time probe reads from the header equals the decoded length,
exactly, in frames; [ADR 0010](adr/0010-one-length-issued-from-the-header.md)). A format whose
header cannot give an exact length has its probe scan frames instead, and its row says so.

**Tolerance: 1 ms, at every promised cell, globally.** An authored point plays within 1 ms of
where it was placed, on every format, rate, depth and channel count in the table. There is no
per-format or per-transition tolerance. Set in
[ticket 09](../.scratch/202609-faderio-v3/issues/09-how-exact-is-the-authoring-promise.md),
recorded in [ADR 0009](adr/0009-authoring-exactness-is-bought-in-the-decoder.md). A cell that
cannot reach it leaves this table; the tolerance never widens to meet a cell.

Each row is proven by five theories over its `*Cells()` member data, one per assertion plus a
second seek landing inside the decoder's last block (the seek the package FLAC decoder silently
dropped, ticket 15): `<Format>_ReadsToCompletion`, `<Format>_ToneIsPure`,
`<Format>_HeaderLengthEqualsDecoded`, `<Format>_SeekBeforeFirstRead_LandsWithin1ms` and
`<Format>_SeekIntoLastBlock_LandsWithin1ms`. WAV cells are generated at test time; FLAC and
MP3 cells are checked-in fixtures from `Fixtures/generate.sh`, a tone and a pink-noise file per
cell, and a missing fixture is a red cell.

| Format | Sample rates | Bit depths | Channels | Proven by | Status |
|---|---|---|---|---|---|
| WAV (PCM) | 44.1, 48, 96 kHz | 16, 24 | mono, stereo | the five `Wav_*` theories over `WavCells()` | proven, 12 cells ([ticket 14](../.scratch/202609-faderio-v3/issues/14-fill-the-real-input-matrix.md)) |
| FLAC | 44.1, 48, 96 kHz | 16, 24 | mono, stereo | the five `Flac_*` theories over `FlacCells()` | proven, 12 cells (ticket 14; the first landing in [ticket 15](../.scratch/202609-faderio-v3/issues/15-make-flac-seek-exact.md)) |
| MP3, constant bitrate with a Xing/Info header | 44.1, 48 kHz | n/a (decodes to PCM) | mono, stereo | the five `Mp3_*` theories over `Mp3Cells()`, variant `CbrInfo`, and over `TaggedMp3Cells()` `Mp3_TaggedDecode_IsTheSourceFromItsFirstSample`, `Mp3_LameEncoderString_IsTrimmedLikeFfmpegsLavc` and `Mp3_TagFailingValidation_KeepsWholeFrames`: with a valid LAME tag the length is the source count (the tag's frame count less its delay and padding); otherwise the tag's frame count, whole frames | proven, 4 cells, 8 theories each (ticket 14; trimmed in [issue 18](../.scratch/202609-faderio-phase-1-build/issues/18-trim-tagged-mp3s.md)) |
| MP3, constant bitrate with no Xing/Info/VBRI header | 44.1, 48 kHz | n/a | mono, stereo | the five `Mp3_*` theories, variant `CbrBare`: the length is the audio bytes over the frame size, whole frames, no frame scan | proven, 4 cells (ticket 14) |
| MP3, variable bitrate with a Xing header | 44.1, 48 kHz | n/a | mono, stereo | the same eight theories as `CbrInfo`, variant `Vbr`: as `CbrInfo` | proven, 4 cells, 8 theories each (ticket 14; trimmed in issue 18) |

Every promised cell seeks to 1 ms; no cell is pinned. A variable-bitrate MP3 without a Xing
header is not a row: the probe would have to scan every frame, and no library file is one
(ticket 08 survey).

**MP3 length has two rules** ([ADR 0010](adr/0010-one-length-issued-from-the-header.md),
addendum):

- **With a valid LAME tag** (`CbrInfo` and `Vbr`), the length is the encoded source's own
  sample count: whole frames less the encoder's delay and padding, and 0:00 is the first
  encoded sample. A tag is valid when its CRC matches, its MusicLength matches the audio bytes
  on disk within 10 KiB or 5%, and delay ≤ 2880, padding ≤ 2304 and delay + padding is less
  than the whole frames. No encoder string is required. `Mp3TrackReader` trims the decode to
  it by the encoder's delay plus the ACM decoder's own, 529, pinned by
  `Mp3DecoderOffsetTests`. `Mp3_HeaderLengthEqualsDecoded` asserts the source count (88200 at
  44.1 kHz, 96000 at 48), `Mp3_TaggedDecode_IsTheSourceFromItsFirstSample` that the decode is
  the generator's source PCM from sample 0, and `Mp3_LameEncoderString_IsTrimmedLikeFfmpegsLavc`
  the same for a `LAME` encoder string beside ffmpeg's `Lavc`. A hard cut between two such files
  has no gap.
- **Otherwise** (`CbrBare`, or a tag that fails a check), the length is whole 1152-frame
  blocks, a little longer than the audio that was encoded: the encoder's delay (25 ms at
  44.1 kHz) and padding stay inside it, silently. `Mp3_TagFailingValidation_KeepsWholeFrames`
  and the `CbrBare` cells assert it.

Two facts pin the trimming reader's edges on one crafted file each:
`Mp3_TagPaddingShorterThanTheDecodersDelay_EndsInSilenceAtItsLength` (a tag whose padding is
under 529, as 61 library files record, ends in that much silence at its length) and
`Mp3_CutAfterEncoding_WithinTheMusicLengthAllowance_EndsWhereItsAudioEnds` (a file cut by less
than the allowance is trimmed by its tag but not padded: it ends where its audio ends, with the
engine's notice).

The `LAME`-string and failing-tag files are crafted at test time from the checked-in fixtures
(`CraftedMp3`), because `lame.exe` is not available to the generator.

**Sample rate policy.** The engine runs at the output device's rate, fixed at launch
([ADR 0008](adr/0008-engine-runs-at-the-device-rate.md)). A file at any other promised rate
is resampled once, on the way in. Authoring exactness is the same at every promised rate:
the seek-landing assertion runs per rate with one tolerance, and the engine compensates the
resampler's known latency if it has to.

**Decoders.** WAV through NAudio's `WaveFileReader`, MP3 through `Mp3FileReader` (behind
`Mp3TrackReader` when the file is trimmed), FLAC through
`BunLabs.NAudio.Flac` 2.0.1 behind `FlacTrackReader`, which repairs five defects in the package
(a position counter that stops short at end of stream, a seek into the last block that did
nothing, a frame cut short that crashed the process, so the decoder now stops after the last
whole frame, [build issue 21](../.scratch/202609-faderio-phase-1-build/issues/21-a-truncated-flac-crashes-the-app.md);
a final frame under 16 bytes that was never decoded, and a frame header found by chance inside a
frame that made every later seek land late,
[build issue 22](../.scratch/202609-faderio-phase-1-build/issues/22-flacs-ending-in-a-tiny-frame-raise-a-false-notice.md))
and leaves the decode and the exact seek to the package. Windows Media Foundation is
not in the production chain: its FLAC decoder is undocumented and landed seeks on the next block
boundary. Every decoder that remains is open source, so a red cell can be read to its cause,
except the Windows ACM codec behind NAudio's MP3 wrapper; the one number faderio takes from it,
its decoder delay, is pinned by `Mp3DecoderOffsetTests`.

**Length.** Length is a property of a file, not a format, so it is not a column. The matrix
fixtures are two seconds long; long-file playback is not proven by the suite. The library's
longest file is about 26 minutes, and frame counts are 64-bit, so there is no arithmetic
limit. The **Looks** half of [`docs/smoke.md`](smoke.md) checks that a 26-minute FLAC shows
its length and seeks into its last minute. Opening that file pre-scans every FLAC frame header;
ticket 15 measured it at about 250 ms, within the 500 ms budget.

## Rejected

Everything not in the table above is refused when the file is added, with the reason. In
particular:

| What | Why it is rejected | Where it could come back |
|---|---|---|
| More than two channels | The mixer is stereo; today such a file would play as garbage. | Downmix is a restaging feature. |
| 8-bit, 32-bit integer and 32-bit float PCM | Studio intermediates, not library formats. Unproven. | A row each, if anyone has one. |
| Sample rates other than 44.1, 48 and 96 kHz | Unproven. The resampler would very likely cope, but "very likely" is not a row. | A row per rate; WAV rows are free to generate. |
| AIFF | A reader exists but nobody has a file. Unproven. | One row and an ffmpeg fixture. |
| M4A / AAC, WMA | Ticket 02 measured seek error up to 15 ms and 71 ms and both misreport their own length. The promise is 1 ms. | Only a decoder that seeks them exactly; Media Foundation, the one that exists, does not. Nobody has asked. |
| Ogg Vorbis | NVorbis 0.10.5 cannot seek into any file paged by ffmpeg's defaults (ticket 07). The reader is deleted rather than kept broken. | A research ticket on a decoder that seeks any Ogg, if the format is ever wanted. |
| Opus, DSD, anything else | No reader, no row. | A row. |

**How rejection works.** The engine probes the file when it is added (`AudioProbe`, with this
table as code in `SupportedAudio`): it opens it, reads the header and checks format, sample
rate, bit depth and channel count against this table. A file
that fails is never queued; a queued track is a track that will play. The engine reports a
structured reason (the four probed properties and which one disqualified the file) and the
shell formats it and points here. Failures during playback (a corrupt frame, the device gone)
are one notice and a skip, never a notice per block.

## Changing this table

Adding a promised cell is: a fixture (checked in, generated by `Fixtures/generate.sh`), a cell
in the matching `*Cells()` member data, a green run, then the row. Removing one is the reverse.
The pull request that changes the table changes the matrix in the same commit.
