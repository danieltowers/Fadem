# 02: A voice plays its loops

Status: ready-for-agent
Blocked by: 01

Core only. See the [spec](../spec.md), D7–D14, D16 (the view half) and *Testing*. The mixer
today reads each voice forward and never re-seeks a source; the transition trigger is one
timeline frame computed from one linear source↔timeline mapping, and every seek re-derives it
(`SeekNow`, `ScheduleBoundary`, `TryComputeFade`). A loop's jump is a seek the engine makes.

## What to build

- **The voice's programme.** When a track voice starts (queue, boundary, preview or render),
  it is given its effective loops. `MixerVoice` holds the list, the loop it is inside (if any)
  and the pass it is on. Entering a span — by reading into it, by a seek that lands in it from
  before, or by starting inside it — is pass 1 (D10).
- **The jump.** When a voice's read reaches a loop end with passes still to go, it sets
  `Source.Position` back to the loop start, notes a reference pair (as `NoteSeek` does) so the
  playhead is right, and cancels and re-schedules the pending boundary from the new reference
  (as `SeekNow` does). This runs inside the render path, so it must be no more than a position
  set on an in-memory source; a source that is not held in memory (over `MemoryCap`) seeks on
  the decoder as today. Measure the FLAC and WMA cost in a test and state it in the commit.
- **The splice (D12).** A fixed declick across the seam: the last few ms before the loop end
  ramp down and the first few ms from the loop start ramp up, equal-power, one constant, not a
  fade in the glossary sense and never shown as a wedge. Pin its width and curve in a test.
- **Last pass fires the boundary (D7).** The boundary trigger for a fade-out point inside a
  span is scheduled only once the final pass begins; before that, `ScheduleBoundary` treats a
  point inside a span with passes left as not yet reachable. A point after a span is scheduled
  from the mapping that holds after the loops are done. A fade-out point before a span fires
  as today, and the fading voice keeps looping (D8): `BeginFadeOut` changes nothing about the
  programme.
- **Open-ended (D9).** `Count == null` never leaves the span on its own. `CanCueTransition` is
  false while the attended voice's programme has an open-ended loop at or after the playhead.
  `TransitionNow` resolves `fadeOutAt: decodePosition` as today and the voice loops as it
  fades. `Next` cuts as today. With repeat off and the last row open-ended, playback does not
  finish on its own.
- **Seeks (D10)** by `SeekNow`: landing inside a span from outside it is pass 1; landing inside
  the span it is already in keeps the pass; landing past the loop end leaves the loop. The
  scheduled boundary follows from the resulting state.
- **Fade-in point inside a span (D10).** The incoming voice started at that point is in pass 1
  of that loop.
- **Live edits (D11).** When the store changes a sounding track's loops (issue 03 calls this),
  the voice's programme is replaced in place: count below the current pass → this pass is the
  last; playhead now outside every span → no loop; the playhead never moves. The boundary is
  re-scheduled.
- **Preview and Render honour loops (D14).** `StartPreview` and `RenderBoundary` give their
  voices the same effective loops the queue would.
- **The view (D16).** `VoiceView.Loops`: each effective loop as start and end positions
  (`Playhead`-shaped, within the track) and its count or ∞; `VoiceView.Pass`: the sounding
  loop's index, pass and count, or null. A passed-over loop (D13) is not in `Loops`; its notice
  is raised once when the voice starts.

## Acceptance

- [ ] A 2 s span at count 3 renders as the span three times then the tail; sample for sample
      against a hand-assembled reference outside the declick's width. Count 1 renders the
      straight track. Two loops each run their count.
- [ ] The declick's width and shape are pinned; a loop on a non-zero-crossing tone has no
      click (peak-to-peak step at the seam under a stated bound).
- [ ] Fade-out point inside the span: the fade begins on pass 3 of 3, at that point. Before the
      span: the outgoing voice loops under the incoming track until silent. After: fires after
      the loops.
- [ ] Open-ended: 20 s of render stays in the span; `CanCueTransition` is false; Transition
      now fades it out as authored; Skip cuts.
- [ ] Each seek rule in D10 by playhead and `Pass` in the view; each live-edit rule in D11 with
      the playhead unmoved.
- [ ] Preview samples equal `RenderBoundary` with a loop in the window.
- [ ] A WAV, FLAC, MP3 and WMA each loop in time (the jump lands within the 1 ms promise,
      ADR 0009) and the render is on time (no late block) for a source held in memory.
- [ ] net8.0 and net48 x86 counts stated in the commit.
