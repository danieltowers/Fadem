# Can MusicBee's BASS play WMA to faderio's 1 ms promise?

Research requested on 2026-10-05 after "we certainly need WMA". WMA is rejected
([ADR 0007](../adr/0007-audio-is-promised-or-rejected.md)) because Windows Media Foundation, the
decoder ticket 02 measured, missed seeks by up to 71 ms and misreported length. The plugin has
a second decoder available: the BASS stream MusicBee hands a plugin through
`Player_OpenStreamHandle` (`src/Faderio.MusicBee/MusicBeeInterface.cs`). This file gives facts
and options only; no decision is recorded here.

The measurements are a permanent test, `tests/Faderio.Core.Tests/MusicBeeBassWmaTests.cs`
(net48 x86 only, on MusicBee's own DLLs through `MusicBeeBass.cs`), over fixtures from
`Fixtures/generate.sh`. All 29 cases pass on BASS 2.4.17.0 and basswma 2.4.5, the DLLs of the
installed MusicBee 3.6.

## Short answer

- **BASS's WMA decoding is Windows'.** `basswma.dll` is an 18 KB add-on over the Windows Media
  Format runtime (`BASS_WMA_GetWMObject` hands back that runtime's reader). That runtime is not
  Media Foundation, the route ticket 02 measured.
- **BASS's own seek is not exact.** It lands **late by 0.1 to 1.6 ms** on files from Windows'
  encoder, which is what Windows Media Player rips. 15 of 75 seeks missed 1 ms, the worst by
  0.6 ms. On ffmpeg's files it lands **one 2048-sample block early, about 45 ms**. So the seek
  depends on how the file was written, and fails the promise either way.
- **`BASS_POS_DECODETO` from the start of a fresh stream is exact.** It decodes forward and
  discards to the target. It landed on the sample at every target on every file, both
  encoders.
- **Exact seeking costs decode time.** BASS decodes WMA at about 265× real time (Release build,
  this machine). A fade-in point 10 s in costs about 40 ms to reach; one in the last minute of a
  26-minute track, about 6 s.
- **The start is clean, the end is padded.** Both encoders' files decode from the source's
  first sample, with no encoder delay to trim. Windows' files carry 17 to 43 ms of codec tail
  past the music, and BASS's length includes it (to the millisecond: 16 frames short at
  44.1 kHz, exact at 48 kHz). ffmpeg's files lose the source's last partial block, and BASS's
  length disagrees with what it delivers.

## What was measured, and how

Fixtures: 2 s of the matrix's pink noise and 1 kHz tone as WMA 9 Standard, at 44.1 and 48 kHz,
mono and stereo, from two encoders:

- `ffmpeg`: ffmpeg's `wmav2`, 128 kbps stereo, 64 kbps mono. Four cells.
- `windows`: Windows' own encoder, reached through MusicBee's `basswma.dll`
  (`Fixtures/encode-wma.ps1`, 32-bit PowerShell), 128 kbps stereo, 48 kbps mono. Windows offers
  mono only up to 48 kbps at 44.1 kHz and none at 48 kHz, so three cells.

Each file is opened as a BASS decode-only float stream, as a plugin would open the handle
MusicBee gives it. Methods are the real-input matrix's: a seek runs on a fresh stream before any
read, the first 1024 frames read are located by cross-correlation in a sequential decode of the
same file, and the offset is the landing. 25 targets per file: 24 spread through it and one
100 frames before its audio ends, inside the last block. Length and alignment are checked
against the lossless FLAC fixture of the same noise.

| Encoder | BASS seek lands | DECODETO lands | Decode vs source | BASS length vs decode |
|---|---|---|---|---|
| Windows | 0.1 to 1.6 ms late | on the sample | from sample 0, then 17 to 43 ms of padding | ≤ 1 ms short (16 frames at 44.1 kHz) |
| ffmpeg | about 2048 frames (45 ms) early | on the sample | from sample 0, last partial block lost | disagrees (88156 vs 88064 at 44.1 kHz) |

Tone purity is at least 0.999 on every cell. Every file decoded as `BASS_CTYPE_STREAM_WMA`, so
basswma handled it, not BASS's Media Foundation route.

## What it means for the promise table

A promised row needs four things on every cell (`docs/supported-audio.md`): completeness,
purity, seek landing within 1 ms, and the add-time header length equal to the decoded length.

- **Seek landing** is met only by decoding from the start. A reader would open the stream,
  `DECODETO` the fade-in point, and for any later seek open a fresh stream and do it again.
  Seeking backwards always needs a fresh stream.
- **Length** has no exact header source yet. The ASF header's duration is in milliseconds, and
  the decode includes the codec tail. A length taken from a full decode at add time would be
  exact but costs the same decode time. Whether the tail should count (as MP3's untagged padding
  does today) is a decision, like ADR 0010's.
- **Completeness and purity** hold on the fixtures.

## Open questions

- **Standalone faderio has no BASS.** These DLLs come with MusicBee. Standalone would need its
  own route: BASS shipped with faderio (licensed, not free for commercial use), or the Windows
  Media Format runtime called directly with the same forward-decode trim.
- **`Player_OpenStreamHandle` itself is unmeasured.** These tests open MusicBee's DLLs directly.
  Whether the handle MusicBee returns is a decode stream with the same flags, and whether
  `useMusicBeeSettings` adds resampling or ReplayGain, has to be checked inside MusicBee.
- **The deep-seek cost.** 6 s to reach the end of a 26-minute WMA is acceptable for a boundary's
  incoming track, opened by a worker in advance ([ADR 0015](../adr/0015-rendering-never-opens-a-file.md)),
  but not for a seek the user makes while listening. Possible answers: decode the whole file to
  memory once (a 26-minute stereo float track is about 550 MB, too large for the 32-bit plugin),
  or keep an index of decoder states. Neither is measured.
- **Other encoders and WMA versions.** Only WMA 9 Standard was measured. WMA Pro and WMA
  Lossless also come from Windows Media Player; basswma would hand them to the same runtime, but
  their seek is unmeasured.
- **Why BASS's seek lands late on Windows' files.** Not investigated. ASF timestamps are in
  milliseconds, which would explain errors under about 1 ms but not 1.6 ms.
