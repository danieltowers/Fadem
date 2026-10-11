# What audio does faderio promise to play correctly?

Type: grilling
Status: resolved
Blocked by: —

## Question

Surfaced by [ticket 01](01-is-the-audio-output-clean-across-real-formats.md), which found the
engine correct at 44.1 kHz and broken above it. It was not visible at charting.

The engine normalises everything to 44.1 kHz stereo float and mixes there
(`SourceNormalizer.EngineSampleRate`). That was never a stated decision, it is just what the
code does, and the two defects ticket 01 found both live in the conversion layer that choice
creates. Before fixing them, decide what is being promised.

To settle:

1. **Is 44.1 kHz stereo the right internal format?** It matches CD audio and most libraries.
   It also means every 48 kHz file, which is everything sourced from video, gets resampled on
   the way in. Resampling is not free and it is not lossless.
2. **Should the engine instead adopt the source rate, or the device's rate?** A player that
   runs at the device rate avoids one conversion, but a crossfade mixes two tracks that may
   have different rates, so something must convert somewhere. Where is the least harmful
   place?
3. **What bit depths must work?** 24-bit is normal for anything called hi-res, and today it
   renders as silence. Is 24- and 32-bit support required, or is the honest answer that
   faderio targets 16-bit 44.1 kHz libraries and rejects the rest loudly?
4. **What should happen to a file the engine cannot play correctly?** Today it fails silently
   with a status notice per block. Refusing the file clearly at load time is more honest than
   playing it wrong. What does the user see?
5. **Does the authoring promise hold at every supported rate?** A fade point authored on a
   48 kHz file must land where it was authored, which is the same guarantee as for 44.1 kHz.
   Whatever conversion survives this decision has to preserve it.

Note that fixing the two defects is cheap and does not need this ticket: the inverted
conversion is a two-character change and the missing encoding is one switch case. Those are
recorded against
[Migrate Faderio.Core into v3](07-migrate-faderio-core.md). This ticket exists
because patching them without deciding the policy would leave the same trap for the next
person.

## Answer

Resolved 2026-09-22 over two grilling rounds. The contract is
[`docs/supported-audio.md`](../../../docs/supported-audio.md); the policy is
[ADR 0007](../../../docs/adr/0007-audio-is-promised-or-rejected.md) and the rate decision is
[ADR 0008](../../../docs/adr/0008-engine-runs-at-the-device-rate.md). `CONTEXT.md` gained
**Promised**, **Rejected** and **Probe**.

**Two tiers, nothing in between.** A file is *promised* when its format × rate × depth × channels
is a row in the doc, and every row names the matrix test that proves it. Everything else is
*rejected* at add time with a structured reason (the four probed properties and which one
failed) that the shell formats and links to the doc. No best-effort tier: that is v2's failure
with a label on it, and the library holds nothing that would land in it. The
`MediaFoundationReader` catch-all becomes an explicit `.flac` case plus rejection.

**Promised now.**

| Format | Rates | Depths | Channels | Proof |
|---|---|---|---|---|
| WAV | 44.1 / 48 / 96 kHz | 16 / 24 | mono, stereo | proven, 12 cells |
| FLAC | 44.1 / 48 / 96 kHz | 16 / 24 | mono, stereo | 1 cell proven, 11 pending ticket 14 |
| MP3 × {CBR+Info, CBR bare, VBR} | 44.1 / 48 kHz | n/a | mono, stereo | pending ticket 14 |

24-bit FLAC earns its row with zero library files because it is the actual hi-res download
format. MP3 gets three variants because the probe cannot tell CBR from VBR from the header, so
the promise must cover both or the gate cannot enforce it. If VBR seek misses the tolerance the
cell is pinned and ticket 09 owns the fix.

**Rejected now, each with a named way back:** more than two channels (downmix is a restaging
feature); 8-bit, 32-bit int and 32-bit float PCM; rates other than 44.1/48/96; AIFF (reader
exists, nobody has a file); M4A and WMA (inexact seek and wrong length per ticket 02, revisit
after ticket 09); **Ogg Vorbis** (NVorbis paging bug from ticket 07: `VorbisTrackSource`, its
tests, the `ramp.ogg` fixture and the NVorbis package are deleted rather than kept broken; a
research ticket if the format is ever wanted).

**Engine rate = output device rate, fixed at launch** (round 1, ADR 0008). Same 1 ms landing
class at every promised rate, one landing row per rate, resampler latency compensated by the
engine if it shows.

**Length is not a column.** Frame counts are 64-bit, so no arithmetic limit; the suite's
fixtures are 2 s so long files are unproven by the suite, and the doc says so. The Looks half
of `docs/smoke.md` gains an item: a 26-minute FLAC shows its length and seeks into its last
minute.

**Probe at add time**, header only; a queued track is a track that will play. Play-time
failures are one notice and a skip, never per block.

**The library survey** (903 files, all probed) is in the working notes below and need not be
re-run.

**Hand-offs.** Ticket 12 builds the probe, the structured rejection, the device-rate engine and
the Ogg deletion. Ticket 14 fills the FLAC grid and adds the twelve MP3 cells. Ticket 09 sets
the tolerance column and decides M4A/WMA's way back. The restaging fog on the map records
AIFF, M4A/WMA, Ogg, downmix and the 32-bit depths.


## Noted from ticket 05

Ticket 05 scoped the **slice** to 44.1 kHz WAV and FLAC — WAV as an exact control, FLAC because
it is the user's actual library. That is a slice-local decision made for provability and belief,
and it deliberately does **not** pre-empt this ticket's answer about the product promise.

The relevant new fact: the user's real `transitions.json` contains one authored transition, and
it is a **FLAC** boundary. Whatever promise this ticket lands on, FLAC is not hypothetical
coverage — it is the format the user actually authors in.

## Noted from ticket 06

The real-input matrix is shaped so that **a format is one row**: format × rate × depth ×
channels, three assertions per cell (completeness, purity, seek round-trip). Whatever this
ticket promises, adding it to the suite is adding rows and, for compressed formats, checking in
a fixture. The matrix is the promise's enforcement, so the two should be written together.

## Noted from ticket 07

Two decoder facts measured while building the matrix skeleton, both for this ticket's promise:

- **NVorbis 0.10.5 cannot seek into an Ogg file paged by ffmpeg's defaults.** Sequential
  decode is fine; any seek throws `GranulePos mismatch`, with both `libvorbis` and ffmpeg's
  native encoder. Files with short pages (oggenc-style, or `-page_duration 10000`) seek within
  40 ms. Ticket 02's "effectively exact" OGG was on a compatible file. So OGG support as it
  stands is "some files" — a promise this ticket has to make honestly or fix (newer NVorbis,
  or a different decoder).
- **Media Foundation FLAC decodes faithfully** — a suspected −3 dB attenuation turned out to be
  the fixture. Recorded so it is not re-suspected.

## Working notes — round 1 (2026-09-22, superseded by the Answer above)

Grilling round 1 is done; these are **decided**, not proposals. Do not re-ask them.

1. **Engine rate = the output device's rate, set at launch.** Never worse than fixed 44.1 kHz
   and strictly better wherever file and device agree (a 48 kHz file on a 48 kHz device is
   resampled twice today). Becomes a launch-time value the output adapter supplies, implemented
   in ticket 12 where the rate constants (`TransitionMixer.SampleRate`,
   `NormalizedTrackSource.EngineSampleRate`, `EngineTime.SampleRate`) are being replaced anyway.
   Free for the slice: on 44.1 kHz files it behaves identically. Store is rate-agnostic
   (`TimeSpan` points, converted at load). Fake output declares 44.1 kHz; tests don't move.
   WaveOut fallback defaults to 44.1 kHz.
2. **More than two channels → reject at load** with the reason. Today such a file plays as
   garbage silently: `NormalizedTrackSource.Wrap` handles only mono and stereo, and the mixer
   assumes two samples per frame. Downmix is a feature for the restaging fog.
3. **Probe at add time.** Open, read the header, check against the rules, reject then — a
   queued track is a track that will play. Play-time failures (corrupt frame, device gone) are
   one notice and a skip, never per-block.
4. **The promise lives in `docs/supported-audio.md`**: one table, every row naming the matrix
   test that proves it. Rejection reasons quote it. A row without a doc line is unpromised; a
   doc line without a row is a lie.
5. **Equal exactness at every promised rate**: the same 1 ms landing tolerance, a landing row
   per rate (ticket 14), and the engine compensates the resampler's known latency if needed.

**The library survey is done** — all 903 files probed, not a sample. Do not re-run it.

| | count | rate | depth | channels |
|---|---|---|---|---|
| MP3 | 654 | 650 × 44.1 kHz, **4 × 48 kHz** | – | all stereo |
| FLAC | 245 | all 44.1 kHz | all 16-bit | all stereo |
| WAV | 4 | 44.1 kHz | 16-bit | stereo |

- **Every MP3 is CBR 320 kbps.** 447 carry an "Info" tag, 207 have no Xing/Info/VBRI header
  at all; ffprobe reports a flat 320000 on all 654. **Zero VBR-likely.** So the VBR gap from
  ticket 02 does not touch this library — but it is still the common real-world case.
- **Nothing exotic**: no mono, no multichannel, no hi-res, no 24-bit, no DSD, no
  OGG/M4A/WMA/AIFF/Opus. 14 files over 20 min (longest ~26 min); none over an hour.
- 15.4 GB, ~69 h of audio.

**Round 2 still to ask**, now with the survey in hand:

- Which **formats** are promised (tiers?), given what the library actually holds.
- Which **rates** and **bit depths** get rows (16/24/32/float; 44.1/48/88.2/96/176.4/192?).
- **MP3 VBR** — unmeasured since ticket 02 and the common real-world case.
- **Ogg / NVorbis paging** (ticket 07 finding) — promise "some files", fix, or research ticket.
- **AIFF, 8-bit, >20 min files** — promise, support-unpromised, or reject.

Then: write `docs/supported-audio.md`, resolve, cross-reference tickets 09, 12 and 14.
