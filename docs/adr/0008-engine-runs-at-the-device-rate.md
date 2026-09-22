# The engine runs at the output device's sample rate, fixed at launch

Status: accepted

v2 mixed at a hard-coded 44.1 kHz (`TransitionMixer.SampleRate`, `EngineSampleRate`,
`EngineTime.SampleRate`) because that is what the code happened to do, never because anyone
decided it. Both conversion defects ticket 01 found lived in the layer that constant created, and
a 48 kHz file on a 48 kHz device was resampled twice, once into the engine and once back out. The
engine now takes its rate from the output adapter at launch and mixes there. A file at the device
rate is never resampled; a file at any other promised rate is resampled once, on the way in.

Decided in [ticket 08](../../.scratch/202609-faderio-v3/issues/08-what-audio-does-faderio-promise-to-play-correctly.md);
built in [ticket 12](../../.scratch/202609-faderio-v3/issues/12-rework-the-engine-seam.md),
where the three constants were being replaced anyway.

## Considered options

**Keep fixed 44.1 kHz.** Matches CD audio and the whole of the user's FLAC library. Rejected:
never better than device rate and strictly worse whenever file and device agree, which is every
48 kHz device.

**Adopt each source's rate.** Rejected: a crossfade mixes two voices that may differ in rate, so
something must convert somewhere, and the mixer is the worst place for it.

## Consequences

On 44.1 kHz files through a 44.1 kHz device the engine behaves identically to before, so the
slice is unaffected. The fake output declares 44.1 kHz and the existing tests do not move. The
WaveOut fallback defaults to 44.1 kHz.

The transition store stays rate-agnostic: points are `TimeSpan`, converted to frames at load
against the engine rate in force.

Authoring exactness must be the same at every promised rate. The real-input matrix runs the
seek-landing assertion per rate with one tolerance, and if the resampler's latency shows up in
the landing the engine compensates it rather than the tolerance widening.
