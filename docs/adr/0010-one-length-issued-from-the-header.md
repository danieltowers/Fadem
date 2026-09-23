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
