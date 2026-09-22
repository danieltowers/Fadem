# Which length is a track's length?

Type: grilling
Status: open
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

_(pending)_

## Noted from ticket 04

A strong signal, recorded not decided: **four independent interface designers, given no
steer on length, all chose the decoded length**, issued by the engine via an internal `Track`
constructor, learned by opening the file at add time. All four also named the cost: adding a
folder is slower than v2's tag read.

The seam commits only to *engine-issued* length — `Track` cannot be constructed by the shell —
which holds whichever way this ticket goes. But the unanimous default is worth weighing.
