# What audio does faderio promise to play correctly?

Type: grilling
Status: open
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
[Migrate Faderio.Core into v3](07-migrate-faderio-core-into-v3.md). This ticket exists
because patching them without deciding the policy would leave the same trap for the next
person.

## Answer

_(pending)_

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
