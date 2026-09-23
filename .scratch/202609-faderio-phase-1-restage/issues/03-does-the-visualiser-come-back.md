# Does the visualiser come back?

Type: grilling
Status: resolved
Blocked by:

## Question

v2 issue 0011 built a bottom strip showing both voices' fade levels live, a peak meter and a
clipping indicator, pitched as authoring feedback. The slice cut it (ticket 05 of the
predecessor map): the render harness is a better instrument for proving where a fade lands,
and ADR 0001 requires the bars to move with the playhead, not the decode position, so it is
no longer free.

On a daily-driver player, does it come back, and as what?

- **Cut** for good.
- **Peak meter and clip light only**, which a listener uses for loudness and ReplayGain.
- **The full strip**, on audible time.

If it returns, say what the engine must publish in the `View` for it (placement test 2: the
shell formats, never computes).

## Answer

Grilled with the user on 2026-09-23. **It comes back only as a clip light.** `CONTEXT.md`
gains **Clip**, in a new *Output* section.

### Cut

- **The fade-level strip.** The render harness proves where a fade lands. Live bars would only
  redraw the curve the author already chose, and they do nothing for a daily driver.
- **The peak meter.** On a daily driver it is decoration. "How loud is it" is a question for
  the ears; the part a listener can act on is the clip light.

The engine publishes no fade gains and no running level.

### Why a light is needed

Clipping is real on this seam. `TransitionMixer` sums voices in float with no clamp and no
limiter. ReplayGain boosts up to +12 dB and ignores peak tags. At the midpoint of an
equal-power crossfade both voices sit near 0.707, so two loud masters exceed full scale. The
device path clips silently. Of *indicate*, *prevent* or *leave it*, the user chose to indicate.

### The clip light

- **Threshold.** A clip is any mixed output sample whose absolute value is above 1.0, measured
  at the mixer's output: after summing and gain, and before the device. That point catches
  both causes and is the value the device would clip. There is no headroom margin;
  inter-sample peaks and resampling are the device's business.
- **Audible time.** The light latches when the listener *hears* the first clipped sample, not
  when the mixer renders it (ADR 0001).
- **Attribution.** A clip names its cause. It is the in-flight adjacency (*A → B*) if two voices
  overlap, otherwise the attended track. The cause is named by tracks, not rows, so it stays
  valid after its rows are removed.
- **Several clips.** The light shows a count and the latest cause (*2 clips · latest: C*). The
  latest is what the listener most likely just heard; the count says something was missed.
- **Clearing.** Clicking the light clears it through one engine command, and does nothing
  else. Opening the editor on the named adjacency is left out: where the editor can point is
  still open in [What completes the editor?](09-what-completes-the-editor.md), and a build
  issue can add it afterwards.
- **Lifetime.** The latch survives Stop and Clear. It does not survive a restart. There is no
  setting to switch it off.

### What the engine publishes

`PlayerView` gains a nullable clip fact: the count, and the latest cause (an adjacency's two
tracks, or one track). The engine gains one command that clears it. The shell formats the fact
and holds no latch (placement tests 2 and 3).

### Consequences for other tickets

- [When does ReplayGain switch, and is it on by default?](06-when-does-replaygain-switch-and-is-it-on-by-default.md)
  stays free to adopt peak-aware gain (`REPLAYGAIN_TRACK_PEAK`). The light covers overlap
  clipping whatever that ticket decides, and a boosted track that clips lights it until then.
- [Which settings exist, and where do they live?](10-which-settings-exist-and-where-do-they-live.md)
  gets nothing from this ticket: the light has no setting.
