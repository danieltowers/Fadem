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
