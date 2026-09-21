# How exact is the authoring promise?

Type: grilling
Status: open
Blocked by: —

## Question

Surfaced by [ticket 02](02-can-we-seek-sample-accurately-in-compressed-formats.md), which
measured what the engine can actually deliver. The spec promises sample accuracy. On real
formats it cannot currently keep that promise, so either the engine changes or the promise
does.

Measured reality: WAV, constant-bitrate MP3 and OGG are exact. M4A is off by up to 15 ms and
misreports its own length by 34 ms. WMA is off by up to 71 ms in either direction. FLAC
silently ignores the authored point and starts from the first sample.

Note that a near-free repair, one warm-up read before the first seek, takes FLAC from two
seconds of error to 31 ms. That repair is already recorded against
[ticket 07](07-migrate-faderio-core-into-v3.md) and does not wait on this ticket. What this
ticket decides is what happens after it.

To settle:

1. **What tolerance is acceptable at a transition?** This is a listening question, not an
   engineering one. On a beat-matched handoff, 35 ms is audibly late and 71 ms is clearly
   wrong. On a slow fade between ambient tracks, neither may matter. The answer probably
   differs by how the transition is used, which may mean the tolerance is per-transition
   rather than global.
2. **Which mitigation, and for which formats?** Costed by ticket 02:
   - Warm-up read: a few lines, no memory, fixes FLAC's catastrophic case only.
   - Decode ahead of the boundary into PCM and seek inside the decoded buffer: exact for every
     format, roughly 10.6 MB per voice for a 30 s pre-roll, about 21 MB for two voices, and
     the decode must start one to two seconds before the boundary.
   - Cache a decoded window around each authored point: about 3.5 MB per point for plus or
     minus five seconds, exact, but needs invalidating whenever the author moves a point.
   - Document a tolerance and drop the sample-accuracy claim: free, but the honest tolerance
     is then 35 ms for M4A and 75 ms for WMA.
3. **Is exactness worth buying for every format, or only where it is cheap?** A defensible
   position is that WAV, MP3 and FLAC get exactness because they are what real libraries are
   made of, while M4A and WMA get a documented tolerance.
4. **What does the author see when exactness is not available?** Silently authoring a point
   that will not be honoured is what makes the current behaviour so bad. If a format cannot
   hit the authored point, the UI should say so rather than pretend.
5. **Does the from-end fade point need separate treatment?** It is measured against
   `TotalFrames`, which M4A and WMA report wrongly. That error exists before any seek and is
   not fixed by any of the mitigations above.
6. **Variable bitrate MP3 is unmeasured** and is the common real-world case. Constant bitrate
   measured exact, but that says nothing about VBR. Settle whether this needs measuring before
   the decision or can be absorbed by whichever mitigation is chosen.

Relates to [ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md): both are
about what faderio promises. That one covers sample rates and bit depths, this one covers
positional accuracy. They may want answering together.

## Answer

_(pending)_
