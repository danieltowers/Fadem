# Audio is promised or rejected, nothing in between

Status: accepted

v2 played files it could not play correctly and said nothing: 24-bit WAVs rendered as silence,
files above 44.1 kHz were truncated, FLAC ignored its authored fade-in point, and the test suite
was green throughout. The engine now has two tiers only. A file is **promised** when its format,
sample rate, bit depth and channel count sit in a row of [`docs/supported-audio.md`](../supported-audio.md),
and every row names the real-input matrix test that proves it. Anything else is **rejected** when
it is added, with a structured reason the shell formats. The table and the matrix change in the
same commit: a row without a test is unpromised, a test without a row proves nothing the product
claims.

Decided in [ticket 08](../../.scratch/202609-faderio-transition-you-can-trust/issues/08-what-audio-does-faderio-promise-to-play-correctly.md),
which holds the survey of the user's library (903 files: MP3, FLAC and WAV, all 16-bit, all
stereo, all 44.1 kHz bar four 48 kHz MP3s) that sized the first table.

## Considered options

**Three tiers: promised, best-effort, rejected.** Keep the `MediaFoundationReader` catch-all
and play unproven formats with a one-time "unverified" notice. Rejected: a best-effort tier is
v2's failure with a label on it, and the library holds no file that would land in it. When a
format matters, it is promoted by adding a proof, not by loosening the gate.

**Promise Ogg Vorbis as "some files".** NVorbis 0.10.5 cannot seek into any Ogg paged by
ffmpeg's defaults. Rejected: a promise the gate cannot enforce at add time is not a promise.
The reader is deleted rather than kept broken; the finding is recorded in the table so the
research is not lost.

**Promise a file length.** Rejected: length is a property of a file, not a format, and the
suite's two-second fixtures cannot prove a claim about half-hour files. The doc says so, and
the smoke checklist covers it by ear and eye.

## Consequences

Rejection happens once, at add time, so a queued track is a track that will play. Play-time
failures are one notice and a skip, never a notice per block.

The probe needs the header only, so it cannot tell constant from variable bitrate MP3. The MP3
promise therefore covers both, which is why variable bitrate gets its own matrix row rather than
being left unmeasured.

M4A, WMA, AIFF, Ogg, more than two channels, 8- and 32-bit PCM and rates other than 44.1, 48 and
96 kHz are rejected today. Each has a named way back in the table; none is out of scope.

Amended 2026-10-05: WMA 9 Standard came back by its way, a decoder that seeks it exactly
([ADR 0016](0016-wma-through-the-windows-media-format-runtime.md)). Its probe is the first to
read audio, not only a header: one full decode at add time.
