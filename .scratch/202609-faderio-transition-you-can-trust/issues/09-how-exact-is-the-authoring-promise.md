# How exact is the authoring promise?

Type: grilling
Status: resolved
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
[ticket 07](07-migrate-faderio-core.md) and does not wait on this ticket. What this
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

Resolved 2026-09-22 over two grilling rounds plus one fact-finding pass on Media Foundation and
the FLAC decoders available on NuGet. Recorded as
[ADR 0009](../../../docs/adr/0009-authoring-exactness-is-bought-in-the-decoder.md); the
tolerance is now a stated number in [`docs/supported-audio.md`](../../../docs/supported-audio.md).

**The promise is 1 ms, at every promised cell, globally.** One number, one meaning, the
tolerance line of the supported-audio doc. Below anything audible and below one MP3 frame
(26 ms) or one FLAC block (93 ms). The human error at capture (150–250 ms, deliberately
uncompensated per ticket 03) is the whole error budget; the engine adds nothing to it. Not
per-transition: at 1 ms there is nothing to relax, and a per-transition tolerance would be UI
and store complexity describing an engine shortcoming the engine should not have.

**The author sees nothing.** Exactness is a property of *Promised* (glossary updated), not a
per-file condition the UI reports. A format that cannot hit 1 ms leaves the table and its files
are rejected at add time; that is the only signal.

**From-end points are measured against the decoded length**, and the completeness cell (frames
rendered equal frames the decoder reports) is the proof. Displayed length, decoded or tagged,
is ticket 10's.

**Ticket 12's S3 acceptance test tightens from 5 ms to 1 ms.** Same number everywhere.

**FLAC: swap the decoder.** The fact-finder established why FLAC lands late: Media Foundation
resumes at the *next block boundary* (the +67 ms on our fixture is exactly target-to-next-4608-
frame-block), NAudio's `MediaFoundationReader` receives every sample's timestamp and ignores
it, Microsoft documents `SetCurrentPosition` as inexact and tells the caller to advance to the
target itself, and the FLAC decoder inside Media Foundation is undocumented. The fix is
`BunLabs.NAudio.Flac` 2.0.1 (MS-PL, netstandard2.0, NAudio 2.x): a managed re-port of CSCore's
decoder that pre-scans every frame header on open and, on seek, lands on the frame before the
target and decodes-and-discards to the exact sample. Exact by construction, 8/16/24-bit, a
`WaveStream` that drops into the existing wrapper. **Fallback** if it fails a matrix cell: our
own Media Foundation reader that seeks to target minus one block and trims by sample timestamp
(150–250 lines on NAudio's public interop; timestamp accuracy after a FLAC seek is unverified).
Rejected: a decode-ahead buffer in the engine (fixes the symptom in the mixer, 21 MB for two
voices) and ffmpeg via FFMediaToolkit (ships native FFmpeg 7 binaries).

**Media Foundation leaves the production chain entirely.** With FLAC on its own decoder and
M4A/WMA rejected, the catch-all branch in `AudioSources.cs`, the warm-up-read workaround and
the reader's dropped-first-seek behaviour all go. Every decoder left (`WaveFileReader`,
`Mp3FileReader`, the FLAC decoder) is open source and readable when a cell goes red.

**MP3 needs no treatment.** `Mp3FileReaderBase.Position` seeks to the containing frame and
skips the intra-frame offset after decoding; ticket 02's "exact" was structural, not luck.

**The pre-scan cost is accepted with a budget.** Opening a 26-minute FLAC now reads every
frame header once (a couple of hundred megabytes, sequentially). The probe at add time reads
only the STREAMINFO header and is unaffected. Budget: open in under 500 ms on the library's
longest file, measured once in the task ticket and recorded there; exceeding it means moving
the pre-scan to the background at add time. This is a smoke check, not a suite test, because a
26-minute FLAC cannot be checked in.

**Hand-offs.** New task ticket [Make FLAC seek exact](15-make-flac-seek-exact.md): the decoder
swap, the Media Foundation removal, the pinned 100 ms test replaced by a 1 ms landing
assertion, first decode of the 24-bit and 96 kHz FLAC fixtures, and the long-file open-time
measurement. Ticket 14 spreads the 1 ms landing across the FLAC grid once 15 lands. Ticket 12
tightens S3 to 1 ms. M4A and WMA stay rejected; with a 1 ms promise their measured 15 ms and
71 ms have no way back short of a decoder that seeks them exactly, which nobody has asked for.


## Noted from ticket 03

[Ticket 03](03-what-position-does-set-from-here-capture.md) moved the playhead onto a measured
device clock (`IWavePosition.GetPosition()`) rather than the 100/50/150 ms constants. That
leaves one **known, unmeasured residue** for this ticket to price:

`GetPosition()` reports bytes on the *output* stream. Under WASAPI-exclusive that stream is
post-resampler at device rate, so the resampler's internal buffering adds a skew that the
device counter does not account for. `AudioClient.AudioClockClient` (the true hardware clock)
is strictly more precise and remains available if this residue turns out to matter.

Also relevant: ticket 03 settled that capture does **not** compensate for human reaction time
(150-250 ms), on the grounds that a stored point must equal the measured one. That puts the
dominant source of authoring error firmly in the human, not the engine — which this ticket
should account for when deciding how exact the promise actually needs to be.

## Noted from ticket 05

Ticket 05 put **FLAC inside the slice**, so this ticket's residue is now load-bearing rather
than theoretical. Two corrections to how ticket 02's result was being read:

- The warm-up read does not make FLAC exact. It drops the error from 2267 ms to **31 ms** — so
  fixed-FLAC is *approximate*, and by that measure worse than M4A's 15 ms worst case.
- The user's library is FLAC. Whatever exactness this ticket promises, 31 ms is the floor on the
  format they will actually use, unless the seek path changes.

Ticket 05 set a **slice-local** tolerance of 5 ms on WAV for its measured assertion, explicitly
as a latency-class tripwire (regressions there are 50-150 ms) and explicitly **not** as a product
claim. Setting the real number is this ticket's job.

## Noted from ticket 06

Two numbers this ticket now owns:

- **1 ms.** v2's shell formatted the captured position with `"0.###"` before saving it, so every
  capture was quantised to a millisecond regardless of what the engine knew. Ticket 06 made
  capture an engine operation returning a typed point in frames, so this quantisation no
  longer exists on the path — but it is the kind of loss this ticket should check for
  elsewhere (the store serialises `TimeSpan`, which is 100 ns ticks; fine, but confirm).
- **40 ms.** The FLAC seek cell of the real-input matrix is a *pinned characterisation test* at
  ≤ 40 ms (31 measured plus margin), named `Flac_SeekLandsWithin40ms_PendingTicket09`. When
  this ticket sets the real promise, that test is the one place that changes. It is
  deliberately green-and-documented rather than red-and-ignored.

## Noted from ticket 07

The pinned numbers, now measured on v3's own fixtures rather than inherited:

- **FLAC: +67 ms late** after the warm-up read, on a 2 s ffmpeg-encoded file, independent of
  warm-up size. Pinned at **100 ms** (`Flac_SeekBeforeFirstRead_LandsWithin100ms_PendingTicket09`).
  Ticket 02's 31 ms was a different file; the residual is file-dependent, which is itself a
  fact for this ticket.
- **OGG: within 40 ms** on a short-paged file, seek order irrelevant.
- **WAV: exact** at 44.1 kHz (ramp landing within 1 ms); ±100 frames on the resampled cells.

`RealInputMatrixTests.Landing()` is the measuring instrument: ground truth from a sequential
decode of the same file, so it assumes no encoder gain. Its failure message prints what the
decoder did. Tighten the pins here when this ticket decides.

## Noted from ticket 08

[Ticket 08](08-what-audio-does-faderio-promise-to-play-correctly.md) resolved the format side
of the promise and left this ticket three things:

- **The tolerance column.** `docs/supported-audio.md` promises a seek-landing assertion on every
  cell "within the tolerance ticket 09 sets". Round 1 of ticket 08 assumed the same 1 ms
  landing class at every promised rate; this ticket confirms or replaces that number, and it is
  then one column in the doc's table, not a per-format footnote.
- **M4A and WMA are rejected**, not promised inexactly. Their way back is this ticket's
  decision on whether an inexact seek is ever acceptable. If the answer is a documented
  tolerance, they return as rows with that tolerance; if not, they stay rejected.
- **MP3 VBR gets a matrix cell in ticket 14** rather than a research ticket. If it lands
  outside the tolerance, that cell is pinned like FLAC's and the mitigation is this ticket's
  to choose. The probe cannot tell CBR from VBR by header, so the promise covers both.

Also settled there and relevant here: the engine runs at the device rate (ADR 0008), so the
resampler is in the path only when file and device rates differ. The residue ticket 03 flagged
(post-resampler position skew under WASAPI-exclusive) is therefore file-dependent, not constant.

## Working notes — round 1 (2026-09-22, superseded by the Answer above)

Grilling round 1 is done; these are **decided**, not proposals. Do not re-ask them.

1. **The promise is 1 ms at every promised cell**, one global number, the tolerance column of
   `docs/supported-audio.md`. Below anything audible, below one MP3 frame (26 ms) and one FLAC
   block (93 ms). The human error at capture (150–250 ms, uncompensated by ticket 03's decision)
   is the whole error budget; the engine adds nothing to it.
2. **Global, not per-transition.** Nothing to relax at 1 ms; per-transition tolerance would be
   UI and store complexity describing an engine shortcoming the engine should not have.
3. **The author sees nothing.** Exactness is a property of *Promised*, not a per-file condition
   the UI reports. A format that cannot hit 1 ms leaves the table and is rejected. Glossary's
   **Promised** entry now says the guarantee includes authoring exactness.
4. **From-end points are measured against the decoded length**; the completeness cell (frames
   rendered == frames the decoder reports) is the proof. Displayed length is ticket 10's.
5. **Ticket 12's S3 acceptance test tightens from 5 ms to 1 ms.** Same number everywhere.
6. **The FLAC repair gets its own task ticket**, "Make FLAC seek exact", blocked by this ticket
   only, running in parallel with 12 and 14. It owns the decoder change and flips the pinned
   100 ms characterisation test into a 1 ms cell; ticket 14 proves it across the FLAC grid.

**Round 2 still to ask**, once the fact-finder on Media Foundation seek semantics and
alternative FLAC decoders reports: which mitigation for FLAC (trim by decoder timestamp after
seeking early, decode-ahead buffer, or a different decoder), and whether MP3's frame-boundary
seek needs the same treatment.

Then: write the answer, tolerance column into the doc, create the FLAC task ticket, note
tickets 12 and 14, resolve.
