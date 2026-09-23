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

| Format | Sample rates | Bit depths | Channels | Proven by | Status |
|---|---|---|---|---|---|
| WAV (PCM) | 44.1, 48, 96 kHz | 16, 24 | mono, stereo | `Wav_ReadsToCompletion`, `Wav_ToneIsPure`, `Wav_SeekHalfway_ReadsHalfTheFrames` over `WavCells()` | proven (12 cells) |
| FLAC | 44.1, 48, 96 kHz | 16, 24 | mono, stereo | `Flac_ReadsToCompletion` and `Flac_ToneIsPure` over `FlacToneFixtures()`, `Flac_SeekBeforeFirstRead_LandsWithin1ms`, `Flac_SeekIntoLastBlock_LandsWithin1ms` | 44.1 kHz / 16-bit / stereo: all four assertions proven, seek landing at **1 ms** ([ticket 15](../.scratch/202609-faderio-v3/issues/15-make-flac-seek-exact.md)); 44.1 kHz / 24-bit and 96 kHz / 16-bit stereo: completeness and purity proven; the remaining cells and their landings land in [ticket 14](../.scratch/202609-faderio-v3/issues/14-fill-the-real-input-matrix.md) |
| MP3, constant bitrate with a Xing/Info header | 44.1, 48 kHz | n/a (decodes to PCM) | mono, stereo | `Mp3_*` theories over `Mp3Cells()`, variant `CbrInfo` | pending ticket 14 |
| MP3, constant bitrate with no Xing/Info/VBRI header | 44.1, 48 kHz | n/a | mono, stereo | same theories, variant `CbrBare` | pending ticket 14 |
| MP3, variable bitrate | 44.1, 48 kHz | n/a | mono, stereo | same theories, variant `Vbr` | pending ticket 14; if seek landing misses the tolerance the cell is pinned and ticket 09 owns the fix |

"Pending" rows exist because this table is written ahead of ticket 14, which fills the
matrix. They are not a second tier: ticket 14 lands before the shell
([ticket 13](../.scratch/202609-faderio-v3/issues/13-build-the-shell.md)) can add a file, so
no user ever meets a pending row. A row ticket 14 cannot turn green is removed from this table
before then, and those files are rejected.

**Sample rate policy.** The engine runs at the output device's rate, fixed at launch
([ADR 0008](adr/0008-engine-runs-at-the-device-rate.md)). A file at any other promised rate
is resampled once, on the way in. Authoring exactness is the same at every promised rate:
the seek-landing assertion runs per rate with one tolerance, and the engine compensates the
resampler's known latency if it has to.

**Decoders.** WAV through NAudio's `WaveFileReader`, MP3 through `Mp3FileReader`, FLAC through
`BunLabs.NAudio.Flac` 2.0.1 behind `FlacTrackReader`, which repairs two defects in the package
(a position counter that stops short at end of stream, and a seek into the last block that did
nothing) and leaves the decode and the exact seek to the package. Windows Media Foundation is
not in the production chain: its FLAC decoder is undocumented and landed seeks on the next block
boundary. Every decoder that remains is open source, so a red cell can be read to its cause.

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
