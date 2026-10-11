# What position does "set from here" capture?

Type: grilling
Status: resolved
Blocked by: —

## Question

This is the defect that made v2's headline feature unusable, and it is a domain question
before it is a technical one.

`PlaybackEngine.CurrentPosition` (`src/Faderio.Core/Playback/PlaybackEngine.cs:115-126`)
returns the **decoder's** read position. The transition editor writes that straight into the
fade-point box when the user presses the capture button. The decode position leads what the
listener actually hears by the whole output buffer, 100 ms on WASAPI shared and 150 ms on
WaveOut, plus up to one mixer block. So every point authored by ear is stored **late**, and
the fade fires noticeably after the moment the user chose.

v2's spec named this exact latency slop as the prototype's headline bug. It was fixed on the
trigger side and reintroduced on the authoring side.

**Which position does the player expose, and to whom?** The questions to settle:

1. Is there one position or two? A *decode* position the engine schedules against, and an
   *audible* position the human interacts with, are different quantities. If they are two,
   both need names in `CONTEXT.md` and it must be impossible to reach for the wrong one.
2. Where is the latency compensation applied: inside the engine, at the output boundary, or
   in the shell? Whichever it is, the shell must not be able to get it wrong.
3. Is the compensation a constant per output mode, or queried from the device at runtime?
   WASAPI can report actual latency; a hardcoded constant is a guess that will be wrong on
   some hardware.
4. The seek bar, the time readout and the visualiser all display position too. Do they use
   the audible position as well, so that what the user sees matches what they hear?
5. Human reaction time is itself 150 to 250 ms and is *not* what this ticket is about, but
   it interacts: should capture also offer a nudge control, so the user can trim the captured
   point after the fact rather than trying to press the button earlier?

There is a second, separate defect in the same area, and it should be settled here too:
during a crossfade `_currentVoice` becomes the incoming track the instant the transition
spawns, so the position readout jumps to the new track while the old one is still audible,
and seeking then scrubs the wrong track. **Whose position is shown while two tracks are
sounding at once?**

## Answer

**There are two position quantities, they have names, and only one of them is public.** The
shell can no longer reach the wrong one, because the wrong one is not on the surface.

### The two quantities

Named in `CONTEXT.md`:

- **Playhead** — where the listener is hearing *right now* within a voice's track. The only
  position the shell ever sees: displayed, scrubbed, and captured.
- **Decode position** — how far the decoder has read ahead of the playhead. Engine-internal,
  used to schedule transitions. It never crosses the seam.

They are **distinct types**, not two properties of one type, so handing one to something
expecting the other does not compile (Q9a). Scoped to the position quantities only; whether
frame counts generally deserve units is left to
[ticket 04](04-what-is-the-seam-between-the-shell-and-the-engine.md).

`CONTEXT.md` also gained **voice**, **attended voice** and **capture**, and `point` now
explicitly contrasts with playhead: a point is authored and stored, a playhead is live and
transient. Capture is the act of turning one into the other.

### Where the playhead comes from

**The output boundary owns it** (Q4b). `IAudioOutput` grows a member reporting frames actually
emitted by the device; the engine derives each voice's playhead from it:

```
audible timeline frame = frames actually emitted by the device
voice playhead         = voice.SourceStart + (audible timeline − voice.TimelineStart)
```

Every `MixerVoice` already carries `TimelineStart` and `SourceStart`, so the playhead is
derivable for **any** voice, not just the current one. That is what makes the crossfade
answer below a property read rather than a feature.

The engine is not told a latency constant to subtract — it asks the device where it is. The
measurement stays where the measurement is available.

**Correctness condition:** frames-emitted and the mixer timeline must share an origin. Both
are frame counts from "start of this output session", and the playhead is wrong by exactly
however much they disagree. Output recreation (v2: on output-mode change) and engine reset
(v2: `ResetForRestart`) must be the *same* reset, or the playhead drifts after a stop or an
output switch.

### Measured, not guessed

**Query the device, with the constant as a fallback only** (Q5c). `IWavePosition.GetPosition()`
is implemented by both `WasapiOut` and `WaveOutEvent`, so one interface check covers both. A
constant per output mode is wrong on some hardware by construction; a measurement is right by
construction. The fallback exists so an exotic output cannot crash the app, not as a routine
path.

Known residue, deliberately not chased here: `GetPosition()` returns bytes on the *output*
stream, so conversion goes through `OutputWaveFormat`, and under WASAPI-exclusive that stream
is post-resampler at device rate — the resampler's internal buffering is a further unmeasured
skew. Recorded against [ticket 09](09-how-exact-is-the-authoring-promise.md).
`AudioClient.AudioClockClient` is strictly more precise and remains available if the
`IWavePosition` path proves insufficient.

### What the shell sees

**Every surface is in playhead coordinates** (Q6), including **seek input**: "seek to P, then
read the playhead" returns P. A bar that displays one quantity and writes another visibly
recoils on every drag.

The **visualiser is held back to audible time** as well. It reads no position at all — it is a
level meter pushed once per rendered block — which means its bars currently move as early as
the decode position does. A meter that flashes before the sound is the same lie in a different
organ, and the state is already pushed per block, so it only needs delaying.

By consequence, **`TrackChanged` fires at the audible handover**, not when the voice is
created.

### Whose playhead, while two tracks sound at once

**Position is a property of a voice, never of the player** (Q2a). The player answers only
"which voice is the user attending to?" — the **attended voice** — and that is the **outgoing**
voice until it falls silent.

This is the direct repair of v2's second defect. There, `_currentVoice` flipped to the incoming
track from inside the mixer's render loop, in an event pass that fires every due handle
*before any audio for that block is mixed*, and `AddVoice` set the source position immediately.
So the readout, the track identity and `TrackChanged` all jumped to the incoming track's
fade-in point one full block *plus* device latency before that track was audible at all — while
the outgoing track was still the loud one. The user scrubbing back a few seconds mid-crossfade
would scrub a track they were not listening to.

**Seeking during an in-flight transition cancels the transition** and returns to plain playback
of the attended voice (Q8a). Scrubbing the outgoing voice while the incoming carries on
underneath is incoherent to listen to; refusing the seek is a dead control that gives no reason
for being dead.

### The post-seek drain

Seeking does not flush the device buffer, so the listener keeps hearing the old position for up
to a full buffer. **The playhead stays honest through that window** (Q7a): it reports the
still-audible old audio, then jumps when the new point becomes audible.

This costs each voice a short *history* of (timeline, source) reference pairs rather than the
single fixed pair it has today, because buffered audio belongs to the pre-seek mapping. Snapping
immediately was the cheaper option and was rejected on principle: the invariant is
**unconditional** — *the playhead always describes sound the listener can actually hear* — and
an unconditional invariant is testable, where a conditional one is a footnote someone violates
later. Footnotes-someone-violates-later is the precise shape of the v2 defect.

### Capture

**Capture writes the honest value into an editable field** (Q3a). No nudge control, no
configurable reaction-time offset.

Human reaction time (150–250 ms) is real and systematic, but compensating for it in code would
mean the stored point no longer equals the measured one — reintroducing exactly the class of
bug this ticket exists to kill: a number that means something other than it says. The user
corrects by typing. Whether a nudge affordance is worth adding later is a UI question, not a
correctness one.

### Why v2 got this wrong

Worth recording, because it was not an oversight. `tmp/planning/202609-faderio-build/plan/design.md:12-22`
states: *"Output latency does not affect fade timing or alignment (triggers are driven from the
mixer's sample position, not wall-clock); it only shifts audibility."*

That is **true for the trigger side and false for the authoring side** — and the authoring side
is where the human is. The reasoning was correct for one half of the problem and never
revisited for the other. It was then written into an issue's acceptance criteria as
*"Position bar shows elapsed/remaining time and tracks the decode position"*, so the defect
shipped as a satisfied requirement.

The trigger side needs no change: transitions remain scheduled against the mixer timeline.

### Facts this rests on

From a read-only survey of `v2-GLM-5.3-flash`:

- `PlaybackEngine.CurrentPosition` (`PlaybackEngine.cs:114-126`) returns
  `_currentVoice.Source.Position` — the decoder's cursor — despite a doc comment claiming it
  reads the mixer's sample position. It advances in whole device blocks on the render thread.
- No latency subtraction exists anywhere in `src/`. The only occurrences of "latency" are the
  three constants handed to NAudio constructors (100 / 50 / 150 ms).
- `IAudioOutput` (`IAudioOutput.cs:25-40`) has no position or latency member, so the seam
  cannot carry this fact as written — it has to grow either way.
- The concrete player is held as `IWavePlayer` (`NAudioAudioOutput.cs:28`), which has no
  position member. That is precisely what erased access to the `GetPosition()` both players
  expose via `IWavePosition`.
- Only two UI sites read position: `UpdateDisplay` (`MainForm.Display.cs:84-107`, 100 ms timer,
  feeding both the time readout and the seek bar) and capture
  (`TransitionEditorPanel.cs:135-153`, sampled at the click instant with no correction).

### Surfaced, not settled here

- **Track length has two independent sources.** The seek bar's denominator is
  `Track.Duration`, read from *tag metadata*, while position comes from the decoder. On any
  file whose tags disagree with its decoded length, the bar is proportionally wrong along its
  whole travel — which corrupts authoring by ear independently of everything decided above.
  Raised as [ticket 10](10-which-length-is-a-track.md).
- **Exclusive-mode resampler skew**, noted against ticket 09.
- **Typed frame units generally**, noted against ticket 04.
