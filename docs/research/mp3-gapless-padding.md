# Should MP3 length trim the encoder's gapless padding?

Research for [restage ticket 08](../../.scratch/202609-faderio-phase-1-restage/issues/08-should-mp3-length-trim-the-encoders-gapless-padding.md).
This file gives facts and options only. The decision goes into the build issues through ticket 11.
Sources were read on 2026-09-23. Line numbers refer to the files as fetched that day.

## Short answer

- **Where the numbers are.** LAME records encoder delay and padding in its extension to the
  Xing/Info frame. They sit in two 12-bit fields at byte 141 from the `Xing`/`Info` tag, when
  all four Xing fields are present. The VBRI header has a delay field but no padding field.
  Apple writes the same information to an ID3 comment called `iTunSMPB`.
- **What faderio does today.** Neither NAudio nor faderio's probe reads those fields. NAudio
  2.2.1 skips the Info frame and decodes every other frame whole. That is why today's `Length`
  is `frames × 1152`.
- **What trimming would change.** For the `CbrInfo` and `Vbr` variants, trimming works from the
  header alone. `Length` would become `frames × 1152 − delay − padding`. For our fixtures that
  is exactly the source's sample count: 88200 at 44.1 kHz and 96000 at 48 kHz. ffmpeg decodes
  them to exactly those counts.
- **The `CbrBare` variant has no information.** A bare CBR file has no tag of any kind, so its
  padding cannot be learned from the header. Trimming all three variants the same way is
  therefore not possible from headers. Any trim option splits the MP3 rows.
- **One number is still unknown.** Trimming the start exactly needs the decoder's own output
  offset. Standard decoders emit a 529-sample decoder delay. NAudio's default MP3 decoder is
  the closed-source Windows ACM codec, and its offset has not been measured.

## 1. Where encoder delay and padding are recorded

### The LAME tag (Info Tag rev 1)

Primary source: the [Mp3 Info Tag rev 1 specification](http://gabriel.mp3-tech.org/mp3infotag.html)
(LAME project; fetched over plain http because the https handshake fails).

- The LAME extension follows the classic Xing data. **LAME writes `Xing` for VBR and ABR, and
  `Info` for CBR.** The spec says so directly: "the 'Xing' identification string ... is replaced
  by 'Info' in case of a CBR file". The two layouts are otherwise the same. That is why the
  `CbrInfo` and `Vbr` variants can be handled by one code path.
- **Bytes `$B1`–`$B3`, "Encoder delays"**, stored as `[xxxxxxxx][xxxxyyyy][yyyyyyyy]`. These
  are "the 12 bit values (0-4095) of how many samples were added at start (encoder delay) in X
  and how many 0-samples were padded at the end in Y to complete the last frame". The spec's own
  formula: "#frames*(#samples/frame)-(these two values) = exact number of samples in original
  wav". The `$B1` offset assumes the 44.1 kHz stereo example, with `Xing` at `$24` and all four
  flag fields present. Measured from the tag string, the delay field is at +141. That is the
  offset ffmpeg's muxer writes to (`mp3enc.c` line 522: `xing_offset + 141`). Readers that do
  not assume the flags walk past whichever optional fields are present, then add 21 bytes
  (Rockbox `mp3data.c` lines 430–457).
- The other fields that matter for checking the tag:
  - **Encoder version string** at `$9C`, 9 characters, for example `LAME3.90a`.
  - **MusicLength** at `$B8`–`$BB`: the byte length from the LAME tag frame to the last music
    frame, when the file was encoded.
  - **MusicCRC** at `$BC`–`$BD`: a CRC-16 over the music frames.
  - **Tag CRC** at `$BE`–`$BF`.
- **What "delay" and "padding" mean in LAME's source.** The source is LAME via the
  [lameproject/lame mirror](https://github.com/lameproject/lame), which describes itself as an
  "unmonitored mirror" of lame.sourceforge.io.
  - `encoder.h` lines 42–57: "total Encode+Decode delay = ENCDELAY + 528 + 1", with
    `#define ENCDELAY 576`. The delay field therefore **excludes** the decoder's 529 samples.
  - `lame.c` lines 2150–2164: `end_padding = 1152 − ((delay + source) mod 1152)`, plus 1152 if
    the result is under 576. Padding is therefore always between 576 and 1727, so
    `frames × 1152 = delay + source + padding` exactly. Of that padding, the first 529 samples
    are where the decoder's delay carries real music out.
- **What gapless players do with the two numbers.** They skip `delay + 529` samples at the
  start and drop `padding − 529` samples at the end:
  - ffmpeg `mp3dec.c` lines 265–270: `start_skip_samples = start_pad + 528 + 1` and
    `first_discard_sample = −end_pad + 528 + 1 + frames × spf`.
  - Rockbox `codecs/mpa.c` lines 485–488: `start_skip = lead_trim + 529` and
    `stop_skip = tail_trim − 529` (`mpeg_latency[] = {0, 481, 529}`).

  Either way the audible length is `frames × spf − delay − padding`.

### How reliable the tag is

- **Readers disagree on when to trust it.**
  - ffmpeg honours the delay fields only when the version string starts with `LAME`, `Lavf` or
    `Lavc` (`mp3dec.c` lines 260–263). It does not require the tag CRC for them; it checks the
    CRC only before exporting ReplayGain (lines 290–297).
  - Rockbox reads the fields whatever the encoder string says. It only checks ranges: delay up
    to 2880 and padding up to 2304 (`mp3data.c` lines 458–466). Its own comment calls that check
    "rather silly" given the CRC.
  - Rockbox also throws the padding away when the Xing byte count differs from the file's real
    audio size by more than the larger of 10 KiB and 5% (`metadata/mp3.c` lines 80–94). This guards against
    files that were cut or edited after encoding.
- **The tag CRC covers only the tag.** It proves the tag is intact, not that the frames still
  match it. MusicLength compared with the audio size on disk is a cheap check at add time.
  MusicCRC needs a read of the whole file.
- **Other encoders.** The spec expects others may not write the tag ("it is unlikely that other
  encoders than Lame will implement it"; its examples include `GOGO`). LAME's own comment says
  Fraunhofer's delay differs ("set to 1160 to sync with FhG"). A non-LAME encoder's delay
  cannot be assumed to be 576.
- **ffmpeg-made files** use the same fields. Its libmp3lame wrapper reports
  `initial_padding = lame_get_encoder_delay + 528 + 1` (`libmp3lame.c` line 156). Its muxer
  writes `delay = skip − 529` and `padding = discard + 529` (`mp3enc.c` lines 392–396 and
  511–522). The encoder string is `Lavc<version>`.

### The VBRI header (Fraunhofer)

ffmpeg reads VBRI 32 bytes after the frame header: version (2 bytes), then "delay and quality"
(skipped, 4 bytes), then the byte count and the frame count (`mp3dec.c` lines 300–317). Rockbox
reads only the byte and frame counts (`mp3data.c` lines 470–490). **VBRI has one delay field and
no padding field, and neither open-source reader uses the delay.** VBRI is not a promised
variant. Neither NAudio's `XingHeader` nor faderio's `XingFrames` recognises it.

### iTunSMPB (Apple)

- **No primary Apple specification of `iTunSMPB` exists.** Apple's
  [TN2258](https://developer.apple.com/library/archive/technotes/tn2258/_index.html) defines
  priming and remainder for AAC and says only that "MP3 also has these data dependencies and
  delays". It does not mention `iTunSMPB`.
- The working definition therefore comes from ffmpeg's implementation (`mp3dec.c` lines
  319–381):
  - It is an ID3 `COMM` (or `TXXX`) frame holding hex fields for priming, remainder and the
    original sample count.
  - "Contrary to the LAME tag, the priming count covers the decoder delay."
  - ffmpeg uses it only when there is no LAME tag, and only when
    `priming + samples + remainder == frames × spf`.
- It lives in the ID3v2 tag, so reading it at add time means parsing ID3 frames. The probe
  only skips the ID3v2 tag today.

### foobar2000

foobar2000 is closed source and has no public specification of its gapless handling; its FAQ
says nothing on the subject. Its "Fix VBR MP3 header" and "Edit MP3 gapless info" commands
appear only in forum threads (for example
[hydrogenaud.io topic 125825](https://hydrogenaud.io/index.php/topic,125825.0.html)).
**This document relies on nothing from foobar2000.**

## 2. What faderio and NAudio do today

- **The probe** (`src/Faderio.Core/Playback/AudioProbe.cs`, `ProbeMp3` and `XingFrames`) reads
  the Xing/Info frame count and returns `frames × SamplesPerFrame`. For a bare file it returns
  `round(audio bytes ÷ average frame bytes) × 1152`. It never reads beyond the frame-count field.
- **NAudio 2.2.1**, from the [`Mp3FileReaderBase.cs`](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/Wave/WaveStreams/Mp3FileReaderBase.cs)
  and [`XingHeader.cs`](https://github.com/naudio/NAudio/blob/v2.2.1/NAudio.Core/FileFormats/Mp3/XingHeader.cs)
  source at tag v2.2.1:
  - `XingHeader.LoadXingHeader` accepts `Xing` or `Info` and parses frames, bytes, the TOC and
    the VBR scale. It **does not read the LAME extension**: it records `endOffset` and stops.
  - When the header is found, the Info/Xing frame is left out of the decode:
    `if (xingHeader != null) dataStartPosition = mp3Stream.Position;` (line 93).
  - `CreateTableOfContents` scans **every frame** when the file is opened.
    `Length = totalSamples × bytesPerSample`, where `totalSamples` is the sum of each frame's
    `SampleCount` (lines 163–186 and 274). The reader therefore always reports whole frames
    and **trims nothing**.
  - Seeking finds the frame through the table of contents, steps back 3 frames to warm the
    decoder up, and throws away the leftover samples inside the frame (lines 290–420). Ticket 14
    proved these seeks exact to the sample.
- **The decoder is Windows' ACM MP3 codec.** `new Mp3FileReader(path)` (`AudioSources.cs`
  line 35) builds an `AcmMp3FrameDecompressor` (`NAudio/Mp3FileReader.cs`). NAudio's wrapper is
  open source, but the ACM decoder behind it is not. `docs/supported-audio.md` says "Every
  decoder that remains is open source". For MP3 that holds only for the wrapper. NAudio's own
  comment says the ACM decoder's "first frame after a reset usually does not immediately yield
  decoded samples". **Where the 529-sample decoder delay lands in ACM output has not been
  measured.** All we know is that the total is `frames × 1152` (ticket 14).

### The fixtures, read byte by byte (no decoder involved)

- The LAME tags in the 16 tagged fixtures (`tests/Faderio.Core.Tests/Fixtures/*-cbrinfo-*`,
  `*-vbr-*`) all have encoder string `Lavc62.23` and delay 576.
- At 44.1 kHz: 78 frames and padding 1080, so `78 × 1152 − 576 − 1080 = 88200`, the source
  count.
- At 48 kHz: 85 frames and padding 1344, so `85 × 1152 − 576 − 1344 = 96000`.
- ffmpeg itself (ffprobe/ffmpeg 2026-02-26 build, not the engine) decodes `tone-cbrinfo-44100-1`
  to 88200 samples and `tone-vbr-48000-1` to 96000. It reports `start_time` 0.025057 s, which
  is 1105 = 576 + 529 samples. It decodes the bare file `tone-cbrbare-44100-1` to the full
  89856 samples with no trim.
- The 8 bare fixtures carry no Xing, Info or VBRI frame, and no `iTunSMPB` was written (the
  generator writes none).

### How much silence is involved

- **The ticket's "up to about 50 ms each end" overstates it.** From LAME's arithmetic, the
  total added is `delay + padding`: between 1152 and 2303 samples, or 26–52 ms at 44.1 kHz. The
  split between the two ends:
  - **Start:** always `576 + 529 = 1105` samples, 25.1 ms at 44.1 kHz and 23.0 ms at 48 kHz.
  - **End:** `padding − 529`, between 47 and 1198 samples, up to 27 ms at 44.1 kHz. For the
    2 s fixtures that is 551 samples (12.5 ms) at 44.1 kHz and 815 samples (17.0 ms) at 48 kHz.

## 3. What trimming would need, per promised variant

| Variant | Where the numbers come from | What trimming needs | Can it be exact? |
|---|---|---|---|
| `CbrInfo` (447 of 654 library files; ticket 08 survey) | LAME extension of the `Info` frame | Probe reads the encoder string, delay and padding; validates them; `Length = frames × 1152 − delay − padding` | Yes, where the tag is genuine and the file has not been edited since encoding. Whether the library's 447 carry a LAME extension (string, CRC) is **unsurveyed**. |
| `Vbr` (0 library files) | LAME extension of the `Xing` frame | Same code path as `CbrInfo` | Yes, on the same conditions |
| `CbrBare` (207 library files) | Nothing in the MPEG stream. Possibly an ID3 `iTunSMPB` (unsurveyed) | Without `iTunSMPB`: only a guess, since the encoder, its delay and the padding are all unknown | **No, from headers.** `delay = 576` would be right only for LAME, and the padding stays unknown in every case |

For the tagged variants, trimming needs the following. `src/` is untouched here; this is the
shape of the work, not a design.

1. **Probe.**
   - Locate the LAME extension after the optional Xing fields (Rockbox's walk).
   - Read the version string, delay and padding.
   - Validate them. The choices, from strictest down:
     - the tag CRC;
     - an encoder-string allowlist (ffmpeg's `LAME`/`Lavf`/`Lavc`);
     - a range check (Rockbox);
     - `delay + padding < frames × 1152`;
     - MusicLength against the audio bytes on disk (catches a file cut after encoding).
   - Decide what happens when validation fails: fall back to whole frames, or reject the file.
2. **Reader.** A wrapper in the pattern of `FlacTrackReader`. It opens `Mp3FileReader`, maps
   position `p` to `p + delay + D`, and ends the stream at `delay + D + Length`. Here `D` is the
   decoder's output offset: 529 for mpg123-style and ffmpeg decoders, **unmeasured for ACM**.
   NAudio's seek is already sample-exact, so this is offset arithmetic, not new decoding. A
   wrong `D` would not change `Length` or break the seek-landing cells, because they correlate
   against a sequential decode of the same file. It would leave up to 529 samples (12 ms) of
   the wrong audio at each end.
3. **A new measurement, left as a permanent test (CLAUDE.md).** Correlate the reader's first
   samples against the generator's source PCM, which is reproducible from the `lavfi`
   expressions in `generate.sh`. This finds `D` for ACM and pins it.
4. **An alternative for `D`.** NAudio's `Mp3FileReaderBase` accepts any `IMp3FrameDecompressor`.
   An open-source managed decoder such as NLayer would make `D` readable from source. That
   would be a decoder change with its own matrix run, and is beyond this ticket.

## 4. What it would change about `Length` and the matrix

- **Length.** For a tagged MP3, `Length` becomes the encoded source's sample count. For the
  fixtures that is 88200 or 96000 instead of 89856 or 97920. ADR 0010 already allows this: the
  probe still reads it from the header ("MP3's frame count" becomes frame count minus delay and
  padding). The sentence in ADR 0010's first paragraph that names MP3's frame count would need
  rewording. So would the `supported-audio.md` paragraph "An MP3's decoded length is whole
  1152-frame blocks".
- **The `*_HeaderLengthEqualsDecoded` and completeness cells.**
  - For `CbrInfo` and `Vbr`, the assertion can become stronger: header length equals decoded
    length, **and** equals the generator's known source count. Neither is true today.
  - For `CbrBare`, either the cells keep asserting whole frames (the format has two length
    rules, and the row must say so), or the row changes.
- **Possible new cells.**
  - A file written by `lame.exe` itself (encoder string `LAME`, which is what library files
    carry) next to ffmpeg's `Lavc` tag.
  - A tagged file whose LAME extension fails validation, to pin the fallback.
  - Possibly an `iTunSMPB` bare file, if that option is taken.
- **Authored points.**
  - A from-end point on a trimmed MP3 moves earlier by the trailing trim, 47–1198 samples.
  - If the timeline origin moves to the trimmed start, a from-start point moves by 1105
    samples (about 25 ms).
  - Any MP3 points persisted before such a change would need migrating or accepting. Whether
    any are persisted yet was not checked here.
- **Crossfades.** With trimming, the ~25 ms start and up to 27 ms end silences leave the
  overlap on tagged files. Bare files keep them under options A and B below.

## 5. Options (not decided)

- **A. Do not trim (status quo).** One rule for all three variants: `Length = frames × 1152`.
  Up to 52 ms of silence stays inside `Length`, split across the ends as above. No new code and
  no ACM measurement. The docs could state the split exactly.
- **B. Trim when a validated LAME tag is present; otherwise whole frames.** Exact for `CbrInfo`
  and `Vbr`. `CbrBare` keeps today's rule. MP3 gets two length rules, stated per row. Needs:
  the probe extension, the trimming reader, the ACM `D` measurement as a permanent test, and
  new cells.
- **C. Option B, plus `iTunSMPB` for bare files.** Covers iTunes-encoded bare files, if the
  library has any; the survey did not record this. Needs ID3 `COMM` parsing in the probe.
- **D. Option B, plus a guessed LAME start trim for bare files** (1105 samples at the start,
  none at the end). This is not exact and is wrong for non-LAME encoders. It conflicts with
  ADR 0010's "issued from the header" and with the rule that nothing plays best-effort.
- **E. Reject bare CBR.** This makes trimming uniform, at the cost of 207 library files (32%
  of the library's MP3s). Listed for completeness.

**A cheap step that would inform the choice.** A byte-only pass over the library's 447 Info
files and 207 bare files. It would count how many have a LAME extension that passes CRC and
MusicLength checks, which encoder strings occur, and whether any bare file has `iTunSMPB`. It
reads a few KB per file and no audio. Ticket 08's "do not re-run the survey" covered the survey
as done; this would be a narrower new question.

## Sources

- LAME Info Tag rev 1 spec: <http://gabriel.mp3-tech.org/mp3infotag.html>
- LAME source (mirror of lame.sourceforge.io): `libmp3lame/encoder.h`, `libmp3lame/lame.c`, `libmp3lame/VbrTag.c` at <https://github.com/lameproject/lame>
- NAudio v2.2.1: `NAudio.Core/Wave/WaveStreams/Mp3FileReaderBase.cs`, `NAudio.Core/FileFormats/Mp3/XingHeader.cs`, `NAudio/Mp3FileReader.cs` at <https://github.com/naudio/NAudio/tree/v2.2.1>
- FFmpeg master: `libavformat/mp3dec.c`, `libavformat/mp3enc.c`, `libavcodec/libmp3lame.c` at <https://github.com/FFmpeg/FFmpeg>
- Rockbox master: `lib/rbcodec/metadata/mp3data.c`, `lib/rbcodec/metadata/mp3.c`, `lib/rbcodec/codecs/mpa.c` at <https://github.com/Rockbox/rockbox>
- Apple TN2258: <https://developer.apple.com/library/archive/technotes/tn2258/_index.html>
- faderio: `src/Faderio.Core/Playback/AudioProbe.cs`, `AudioSources.cs`; `tests/Faderio.Core.Tests/Fixtures/generate.sh` and the checked-in MP3 fixtures (bytes read directly); ticket 14 and ticket 08 of `.scratch/202609-faderio-v3/`

## Addendum: the library survey (2026-09-23)

This is the "cheap step" from section 5, run with `docs/research/mp3-lame-tag-survey.py`. It
read headers only, over all 654 MP3s under `the user's music folder`.

- **551 have an Info frame, 103 are bare.** No file has a `Xing`/VBR frame. Every tag sits at
  the offset `AudioProbe.XingFrames` checks (byte 36 from the frame, or 21 for mono), so this
  is the split the probe sees. The predecessor survey's 447/207 was not reproduced; why was
  not investigated.
- **549 of the 551 carry a LAME-style extension, and 546 pass both the tag CRC and the
  MusicLength check** against the audio bytes on disk. Encoder strings: `LAME3.93` (224),
  `LAME3.98` (208), `Lavc57.x` (60), `LAME3.92` (34), `LAME3.99` (20), `Lavf` (3), empty (2).
  Every delay is 576. Padding ranges from 0 to 2304.
- **Of the 103 bare files, none has `iTunSMPB` and none has VBRI.** Option C would gain nothing
  on this library.
- **No MP3 transition is persisted** in `%APPDATA%\faderio\transitions.json`; both authored
  pairs are FLAC. Trimming would migrate nothing.
