# Authoring exactness is 1 ms, bought in the decoder, not the mixer

Status: accepted

The spec promised sample-accurate transitions and v2 could not keep it: FLAC, the user's whole
library, ignored the authored fade-in point entirely, and after a workaround still landed 67 ms
late. The promise is now **1 ms at every promised cell**, one global number in
[`docs/supported-audio.md`](../supported-audio.md), and it is met by making every promised
decoder seek to the sample rather than by buffering around an inexact one or documenting a
tolerance. FLAC moves from Windows Media Foundation to a managed decoder
(`BunLabs.NAudio.Flac`, a re-port of CSCore's) that pre-scans frame headers on open and trims to
the exact sample on seek. Media Foundation leaves the production chain entirely.

Decided in [ticket 09](../../.scratch/202609-faderio-transition-you-can-trust/issues/09-how-exact-is-the-authoring-promise.md),
built in [ticket 15](../../.scratch/202609-faderio-transition-you-can-trust/issues/15-make-flac-seek-exact.md).

## Why 1 ms and why global

The human error at capture is 150 to 250 ms and is deliberately uncompensated ([ADR 0001](0001-playhead-from-the-device-clock.md)),
so the engine's job is to never add to it. 1 ms is below anything audible, below one MP3 frame
and below one FLAC block. A per-transition tolerance was considered and rejected: at 1 ms there
is nothing to relax, and it would be UI and store complexity describing an engine shortcoming
the engine should not have.

## Considered options

**Keep Media Foundation and document 100 ms for FLAC.** Rejected: audibly late on a hit, on the
one format the user authors in. Microsoft documents `SetCurrentPosition` as inexact and tells
the caller to advance to the target itself; NAudio's reader receives each sample's timestamp and
ignores it; the FLAC decoder inside Media Foundation is undocumented.

**Our own Media Foundation reader that seeks early and trims by timestamp.** Kept as the
fallback. Rejected as the first choice because the timestamps' accuracy after a FLAC seek is
unverified and one independent project reports them discontinuous.

**A decode-ahead buffer in the engine.** Exact for any decoder, about 21 MB for two voices,
decode starting seconds before the boundary. Rejected: fixes the symptom in the mixer and keeps
an inexact decoder in the chain.

**ffmpeg via FFMediaToolkit.** Exact and maintained. Rejected for shipping native FFmpeg 7
binaries to decode a format a 2021 managed package handles.

## Consequences

The managed decoder's correctness is ours to prove; the real-input matrix's completeness and
purity cells across the FLAC grid are that proof, and MS-PL lets the source be vendored and
fixed if a cell fails.

Opening a FLAC now reads every frame header once. The add-time probe reads only the stream
header and is unaffected; the engine's open of the incoming voice carries the cost, budgeted at
500 ms on a 26-minute file. Over budget, the pre-scan moves to the background at add time.

M4A and WMA, measured at 15 ms and 71 ms, have no way back to the promised table short of a
decoder that seeks them exactly. (Amended 2026-10-05: WMA found one, the Windows Media Format
runtime, exact through an index of its samples built by one decode at add time,
[ADR 0016](0016-wma-through-the-windows-media-format-runtime.md). M4A still has none.)

Every decoder left in the chain is open source and readable when a matrix cell goes red.

## Addendum: the MP3 decoder is not open source (2026-09-24)

"Every decoder left in the chain is open source" holds for WAV and FLAC. MP3 decodes through
NAudio's open wrapper over Windows' closed ACM codec, as it did when this was written. Since
[build issue 18](../../.scratch/202609-faderio-phase-1-build/issues/18-trim-tagged-mp3s.md)
faderio takes one number from that codec, its decoder delay (529), and
`Mp3DecoderOffsetTests` pins it; see the addendum to
[ADR 0010](0010-one-length-issued-from-the-header.md).

## Addendum: a cut frame is kept from the decoder, not fixed in it (2026-09-24)

The package decodes with unsafe code, and a truncated FLAC killed the process: a Rice code's
unary run over the zeros past the cut ran off the end of the frame buffer, an access violation
.NET cannot catch. Found in [build issue 10](../../.scratch/202609-faderio-phase-1-build/issues/10-unreadable-tracks-are-passed-over.md),
built in [build issue 21](../../.scratch/202609-faderio-phase-1-build/issues/21-a-truncated-flac-crashes-the-app.md).

The decision stands, and the package is not vendored for this. `FlacTrackReader` finds the last
whole frame by the CRC-16 every FLAC frame ends with, and hands the package a stream that ends
there, so no cut frame reaches the decoder. That covers a cut anywhere, including inside the final
frame, and a tail of zeros where a copy never reached, which a comparison of the pre-scan with
STREAMINFO's sample count would not. The track ends where its whole audio ends, which is
[ADR 0010](0010-one-length-issued-from-the-header.md)'s one notice.

Still open: the unsafe decode trusts the frame's contents. A frame damaged in place rather than
cut, with a run of zeros longer than the rest of the decoder's 128 KB frame buffer, could still
read past it. The noise-damaged file of
issue 10 throws a managed exception instead, and no such file has been seen.

## Addendum: the pre-scan's frame table is checked, not trusted (2026-09-24)

Seeks are exact only if the package's frame table is, and in the author's music folder it was
wrong in 112 of 245 FLACs ([build issue 22](../../.scratch/202609-faderio-phase-1-build/issues/22-flacs-ending-in-a-tiny-frame-raise-a-false-notice.md)).
In 110 it lacked a final frame under 16 bytes, a CD rip's block of digital silence: the
pre-scan never looks for a header in a stream's last 16 bytes, and the decoder reads 16 bytes for
every header, so the frame was neither counted nor played and ADR 0010's notice fired at every
play. In 5 (3 of them among those 110) it held a frame that is not there: bytes inside a real
frame that parse as a header, CRC-8 and all. That frame's block was added to the length, every
seek after it landed late by that block (256 to 4096 samples), and a seek just after it decoded
it.

The decision stands, and the package is still not vendored. `FlacTrackReader` hands the package
a stream that reads 16 bytes of zeros past its end, which a frame's body never reaches, so both
the pre-scan and the header read find a short final frame. It drops a scanned frame whose number
is out of step with the frames around it, keeps its own table of the frames that are really
there for the length and for its sums, and asks the package's seek for the frame before the
target by the offset the package's own table gives it, which is never a phantom. A phantom whose
number happens to be in step would still be taken for a frame.
