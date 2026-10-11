# Is the audio output clean across real formats?

Type: research
Status: resolved
Blocked by: —

## Question

The user ran the v2 build and heard **continuous buzzing** instead of music. The codebase
documents that exact symptom as a frames-versus-samples convention mismatch, at
`src/Faderio.Core/Playback/FramesToSamplesProvider.cs:1-60`, and claims two adapters fixed it:
`FramesToSamplesProvider` and `FloatToBytesWaveProvider`. The comment says the bug "affected
every format" and was found in the field with FLAC.

Every existing test feeds synthetic WAV through `tests/Faderio.Core.Tests/TestAudio.cs`. No
test opens an MP3, FLAC, OGG, M4A or WMA file, and none uses a mono or non-44.1 kHz source.
So a format-specific corruption is invisible to the whole suite.

**Is the buzzing genuinely gone across real formats, or does it survive on some path?**

This is the ticket the migration decision rests on. If the engine corrupts real music files,
the "keep Core" verdict collapses and the slice looks completely different.

Measure, do not read: render offline through the production provider chain, at the real
device block size, and analyse tonal purity and playback rate per format. Include a crossfade
between two different formats, since mixing is the engine's actual job.

## Answer

**The buzzing is genuinely fixed. The engine is still not safe for a real library, for two
reasons nobody had looked for.**

Measured by rendering through the production provider chain offline at the real 4410-frame
device block, and analysing with a Hann-windowed FFT. Tonal purity is the fraction of
spectral energy within two bins of the reference tone.

**Clean at 44.1 kHz, every format.** WAV, MP3, FLAC, OGG, M4A/AAC and WMA all render at
purity 0.999 or better, correct duration, correct rate, mono and stereo alike. The
block-quarter corruption the code comments describe does not reproduce anywhere, at any of
four block sizes. A crossfade from MP3 into FLAC puts 0.9999 of its energy in band. What the
user heard was the pre-fix build; the two adapters do what they claim.

**Defect A — every file above 44.1 kHz is truncated.** `SourceNormalizer.Position`
(`src/Faderio.Core/Playback/SourceNormalizer.cs:70-71`) has its getter and setter inverted:
the getter divides by `_framesPerInnerFrame` where it should multiply, and the setter
multiplies where it should divide. `TotalFrames` on line 65 is correct, so the two disagree.
Combined with the end-of-voice check at `TransitionMixer.cs:235`, a track ends early by a
factor of (rate / 44100)².

Confirmed independently by arithmetic, not just by measurement: at 48 kHz the factor is
0.91875² = 0.844, predicting a 15.6 % early cut against 15.0 % measured. At 96 kHz it
predicts 78.9 % against 77.5 % measured. Rates below 44.1 kHz escape by luck. **Audibly: a
48 kHz track stops 15 % early, and an authored fade-out is chopped mid-curve.**

**Defect B — ordinary 24-bit WAVs produce silence.** `ConvertToFloat`
(`src/Faderio.Core/Playback/AudioSources.cs:135`) has no `WaveFormatEncoding.Extensible`
case and throws on the default branch. `LockingOutput` catches it, so the app emits silence
plus one status notice *per block*, about sixty in six seconds. ffmpeg and most tools write
extensible for all 24- and 32-bit PCM. Verified: no `Extensible` case exists in that switch.

**Defect C, latent** — `VorbisTrackSource.Position` (`AudioSources.cs:172`) throws out of
NVorbis if set before any `Read` or `TotalFrames` access. Production survives only because
the engine happens to touch `TotalFrames` first. A reordering would break OGG playback.

**Not tested:** AIFF, 8-bit WAV, more than two channels, the WASAPI exclusive resampling
path, and real music rather than sine tones. Steady-tone purity would not catch a subtle
time-varying artefact.

**Consequence for the map.** The core is worth keeping: defect A is a two-character fix and
defect B is one switch case. But the test suite passed with all three present, which is
direct evidence for the verification ticket, and the sample-rate question it exposes is now
its own ticket.
