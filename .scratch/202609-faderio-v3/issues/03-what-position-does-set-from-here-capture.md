# What position does "set from here" capture?

Type: grilling
Status: open
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

_(pending)_
