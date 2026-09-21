# Can we seek sample-accurately in compressed formats?

Type: research
Status: resolved
Blocked by: —

## Question

The product promises that an authored fade point lands **exactly** where the author put it.
That promise is only as good as the seek underneath it.

A code assessment of v2 reported, without measuring it, that `WaveStreamTrackSource.Position`
(`src/Faderio.Core/Playback/AudioSources.cs:69-77`) sets position by byte arithmetic
(`frame * BlockAlign`), and that:

- `Mp3FileReader` snaps to the nearest MP3 frame, roughly 26 ms;
- `MediaFoundationReader`, used for FLAC, M4A, AAC and WMA, treats both `Position` and
  `Length` as approximations, which would also make `TotalFrames` an estimate.

`TotalFrames` drives every from-end fade point and end-of-track detection, so if it is an
estimate, from-end authoring drifts too.

**How accurately can we actually seek and measure length in each supported format?** Give a
number per format, in samples and milliseconds.

If the error is large, the sample-accurate promise needs redesigning rather than restating.
Options worth costing while answering: decode-to-PCM ahead of the boundary and seek in the
decoded buffer, cache a decoded window around each authored point, or accept a documented
tolerance and stop claiming sample accuracy. The choice shapes both the engine and the
authoring UI, so it belongs on the map before the slice is built.

## Answer

**No, not on every format. Three are exact, two are merely close, and FLAC is broken outright
in a way that silently discards the authored fade-in point.**

Measured with pink noise, which is unique at every lag so a landing point can be recovered by
cross-correlation rather than inferred. Ground truth per format came from a full sequential
decode through the engine's own `ITrackSource`. Six seek targets, three deliberately not
aligned to any plausible frame boundary.

| Format | Typical seek error | Worst | Length error | Verdict |
|---|---|---|---|---|
| WAV | 0 | 0 | 0 | exact |
| MP3, constant bitrate | 0 | 0 | 0 | exact |
| OGG | 0 | 2.9 ms | 0 | effectively exact |
| M4A / AAC | 9.3 ms | 15.2 ms | reports 34.1 ms short | approximate |
| WMA | 37.4 ms | 71.2 ms, direction varies | reports 11.3 ms long | poor |
| FLAC | first seek ignored entirely | same | 0 | **broken** |

**FLAC discards the authored fade-in point.** A seek issued before any read is silently
dropped, and the track plays from its first sample. Round-trip error on an authored fade-in
point of two seconds was exactly minus two seconds: the authored value is thrown away
completely rather than approximated.

Confirmed independently against the production path. `TransitionMixer.AddVoice`
(`src/Faderio.Core/Playback/TransitionMixer.cs:112`) sets `source.Position = sourceStart`
immediately after constructing the voice, before any read happens, and FLAC falls through to
the `MediaFoundationReader` default branch in `AudioSources.cs:45`. So every FLAC transition
in the real app takes this path.

Issuing a single read before the seek drops the error from 2267 ms to 31 ms. Setting position
to zero first does not help. MP3, OGG, M4A and WMA are insensitive to call ordering.

**Two claims from the earlier code assessment were wrong.** MP3 does *not* snap to 26 ms
frames: it measured exact at all six positions, including unaligned ones. Byte arithmetic in
`WaveStreamTrackSource` is exact rather than approximate. The claim that Media Foundation
treats length as an approximation was true, and worse than implied: M4A and WMA report wrong
durations, so every from-end fade point on those formats is wrong before any seek happens.

**Not tested, and one matters a lot.** Variable bitrate MP3 was not covered, only constant
bitrate, and VBR is both the common real-world case and the known weak spot. The MP3 row must
not be read as covering it. Also untested: AIFF, end-to-end round trips for OGG, M4A and WMA,
files longer than eight seconds, and seek accuracy at rates other than 44.1 kHz.

**Consequence for the map.** The mitigation choice is a real decision with real costs, so it
became [ticket 09](09-how-exact-is-the-authoring-promise.md) rather than being settled here.
The one-line warm-up read that rescues FLAC is recorded against
[ticket 07](07-migrate-faderio-core-into-v3.md) as a repair, since it is near-free and
removes the only catastrophic case whatever ticket 09 decides.
