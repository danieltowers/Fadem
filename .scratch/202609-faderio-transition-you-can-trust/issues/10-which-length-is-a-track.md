# Which length is a track's length?

Type: grilling
Status: resolved
Blocked by: —

## Question

Surfaced by [ticket 03](03-what-position-does-set-from-here-capture.md) while surveying every
consumer of position. It was not visible at charting, and it is not a position bug — it is a
*denominator* bug that corrupts authoring by ear independently of everything ticket 03 settled.

A track has **two independent notions of length**, and nothing reconciles them:

- `Track.Duration` (`Domain/Track.cs:9`), read from **tag metadata** by `TrackMetadataReader`.
- `ITrackSource.TotalFrames`, the **decoded** length.

The seek bar uses the first as its denominator and the second (via position) as its numerator
(`MainForm.Display.cs:84-107`). On any file whose tags disagree with its decoded length, the
bar is **proportionally wrong along its whole travel** — the thumb sits at 50% when the
listener is at 47%, and scrubbing lands somewhere other than where it points.

That matters here specifically because ticket 03 settled that the user authors **by ear** and
the seek bar is how they navigate to the moment they want to listen to. A bar with a wrong
scale sends them to the wrong place before capture ever happens.

Tags and decoders disagree more often than is comfortable: VBR MP3 headers, files with
trailing garbage, truncated downloads, tags written by a different encoder than the audio.
[Ticket 02](02-can-we-seek-sample-accurately-in-compressed-formats.md) already found the
compressed formats behaving differently from each other on seek.

The questions to settle:

1. **Which length is authoritative for the UI?** The decoded length is the one the playhead
   actually lives in, so mixing it with a tag-derived denominator is a unit error. But decoded
   length may not be known until the file has been read.
2. **Is tag duration worth keeping at all**, or is it purely a library-listing convenience
   (showing durations without decoding every file)? If both survive, they need distinct names
   in `CONTEXT.md` — this is the same disease as playhead versus decode position.
3. **What happens when they disagree by a lot?** Silently prefer one, or surface it as a
   damaged file? Ticket 03 established a preference for honesty over convenient lies.
4. **Does a fade-out point measured "from the end" use which length?** This is the sharp one:
   an authored point expressed from the end of the track resolves against a length, and if
   that length is the wrong one the authored point moves. That is a stored-data consequence,
   not just a display one.

## Answer

Resolved 2026-09-23 in one grilling round. Recorded as
[ADR 0010](../../../docs/adr/0010-one-length-issued-from-the-header.md); `CONTEXT.md` gained
**Length**.

**The question moved before it was answered.** The engine already uses the decoded length
everywhere it matters (`ScheduleBoundary` and the resolver take `TotalFrames`), so from-end
points already resolve against decoded frames and the denominator bug was a v2 shell defect
that died with that shell. Tag duration survives only on `Track.Duration`, consumed by the M3U
writer and nothing else. Meanwhile ticket 09 changed the cost of "open the file at add time":
a FLAC open now pre-scans every frame header, so learning the decoded length by opening would
read the whole library on a folder add. The real question was whether the engine can issue the
decoded length *without decoding*, and what proves it did.

**One length, issued by the probe from the header, proven equal to the decoded length.** The
add-time probe (ticket 08) reads the stream header anyway; it now also issues `Length`: FLAC's
STREAMINFO sample count, WAV's data-chunk size over block align, MP3's Xing/Info frame count
× 1152 or, for bare constant-bitrate files, file size over frame size. The real-input matrix
gains a **fourth assertion on every promised cell: probe length equals decoded length, exactly,
in frames.** No tolerance: for WAV and FLAC the header is authoritative and for MP3 the frame
count is an integer either way. A cell that cannot reach equality makes the probe scan frames
for that format, at add-time cost, and the doc row records it.

**`Track.Duration` is deleted.** TagLib stays for title, artist and ReplayGain. The engine
issues one `Length`; the M3U writer, when restaging brings it back, formats that. The disease
ticket 03 cured for position (two quantities, one public) is cured the same way here: one
quantity. Glossary: **Length** is the engine-issued length of a track, which the playhead lives
within, from-end points resolve against, and the seek bar spans. "Duration" is reserved for
fades.

**Audio that ends before `Length`** (truncated file, lying header): one `Notice` naming the
file and the shortfall, the track ends where the audio ends, the boundary fires there (a hard
cut if the fade had not started). Never silent, never a skip, never per block. One engine-seam
test in ticket 12: a WAV whose header claims twice its data, through the fake output, asserting
the notice and where the boundary fired.

**From-end fade points resolve against `Length`.** Confirmed as the rule the code already
follows; outside the slice, so no test lands now, but restaging inherits the rule rather than
re-deciding it.

**Add-time cost.** Header reads keep adding fast for FLAC and WAV. If the matrix forces a frame
scan for some MP3 variant, roughly 10 MB of reads per file is accepted at add time. Folder adds
are restaging's problem and sit in the fog.

**Hand-offs.** Ticket 12: the probe issues `Length`; `Track.Duration` goes; the truncated-file
test. Ticket 14: the fourth assertion on every cell, `Mp3Cells()` included, and a probe-length
helper next to `Landing()`. `docs/supported-audio.md` names the fourth assertion.


## Noted from ticket 04

A strong signal, recorded not decided: **four independent interface designers, given no
steer on length, all chose the decoded length**, issued by the engine via an internal `Track`
constructor, learned by opening the file at add time. All four also named the cost: adding a
folder is slower than v2's tag read.

The seam commits only to *engine-issued* length — `Track` cannot be constructed by the shell —
which holds whichever way this ticket goes. But the unanimous default is worth weighing.
