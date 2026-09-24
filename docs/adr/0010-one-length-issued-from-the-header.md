# A track has one length, issued from its header and proven equal to what decodes

Status: accepted

v2 carried two lengths per track: a tag duration read by TagLib and the decoder's frame count.
The seek bar used one as denominator and the other as numerator, so on any file whose tags
disagreed with its audio the bar was proportionally wrong along its whole travel, and authoring
by ear started from the wrong place. The engine now issues **one `Length`**, learned by the
add-time probe from the stream header (FLAC's sample count, WAV's data size, MP3's frame count),
and the real-input matrix asserts on every promised cell that this equals the decoded length
**exactly, in frames**. `Track.Duration` is deleted; TagLib remains for title, artist and
ReplayGain only.

Decided in [ticket 10](../../.scratch/202609-faderio-v3/issues/10-which-length-is-a-track.md);
built in [ticket 12](../../.scratch/202609-faderio-v3/issues/12-rework-the-engine-seam.md)
(probe, deletion, truncated-file behaviour) and
[ticket 14](../../.scratch/202609-faderio-v3/issues/14-fill-the-real-input-matrix.md) (the
fourth assertion).

## Considered options

**Open the file at add time and take the decoder's length.** Exact by definition, and what four
independent seam designers chose unprompted in ticket 04. Rejected after
[ADR 0009](0009-authoring-exactness-is-bought-in-the-decoder.md): a FLAC open now pre-scans every
frame header, so learning length by opening would read the whole library on a folder add.

**Keep tag duration as a listing convenience under its own glossary term.** Rejected: nothing in
the slice or the restaging fog needs a duration the engine has not issued, and a second quantity
is the disease this ticket exists to cure.

**A tolerance on header-versus-decoded.** Rejected: for WAV and FLAC the header is authoritative
and for MP3 the frame count is an integer either way. A format that cannot reach equality has its
probe scan frames instead, at add-time cost, and the doc row says so.

## Consequences

From-end fade points resolve against `Length`, so an authored point expressed from the end moves
only if the file itself changes.

Audio that ends before `Length` says it should (a truncated file, a lying header) is one
`Notice`, and the track ends where the audio ends; the boundary fires there. Never silent, never
a skip, never per block.

"Duration" is reserved for fades. "Length" is the track's.

## Addendum: a tagged MP3's length is the encoded source (2026-09-24)

The decision stands; MP3's header reading gets richer. Decided in
[restage ticket 13](../../.scratch/202609-faderio-phase-1-restage/issues/13-trim-tagged-mp3s-or-keep-whole-frames.md)
from the research in [restage ticket 08](../../.scratch/202609-faderio-phase-1-restage/issues/08-should-mp3-length-trim-the-encoders-gapless-padding.md);
built in [build issue 18](../../.scratch/202609-faderio-phase-1-build/issues/18-trim-tagged-mp3s.md).

"MP3's frame count" above now reads: **MP3's frame count, less the encoder's delay and padding
when a valid LAME tag gives them.** The probe (`Mp3Header`) reads the LAME extension after the
Xing/Info fields and trusts it only if all three checks pass: the tag CRC matches, MusicLength
matches the audio bytes on disk within 10 KiB or 5%, and delay ≤ 2880, padding ≤ 2304 and
delay + padding < frames × 1152. Then `Length = frames × 1152 − delay − padding`, the encoded
source's own sample count, and **0:00 is its first sample**. A bare file, or a tag that fails,
keeps whole frames silently: still promised, no notice.

`Mp3TrackReader` makes the decode equal it by offset arithmetic over NAudio's exact seek: its
position `p` is the ACM reader's `p + delay + D`. `D`, where the ACM codec puts the source's
first sample after the encoder's delay, measured 529 on every fixture (both rates, mono and
stereo, CBR tagged and bare, and VBR). That is the standard decoders' figure, and `Mp3DecoderOffsetTests` pins
it. When a tag's padding is less than `D` (61 library files record padding 0, 60 of them
written by an old ffmpeg), the source's last `D − padding` samples lie past the last frame,
where no decoder reaches. They are silence, so the decode still ends at `Length`.

One thing the spec leaves loose: its tag CRC covers "the first 190 bytes" of the frame. LAME
covers the frame up to the CRC; ffmpeg covers 190 bytes with the CRC field zero and a short
frame zero-padded. Those differ for mono, so either passes.

`CONTEXT.md` is unchanged: **Length** already means the value the engine issues. From-end
points on a trimmed file measure from the source's real end. No MP3 transition was persisted,
so nothing migrated.

This also qualifies [ADR 0009](0009-authoring-exactness-is-bought-in-the-decoder.md)'s "every
decoder left in the chain is open source": MP3's is NAudio's open wrapper over the closed ACM
codec, as it always was, and trimming now takes one number from it, pinned by a test.
