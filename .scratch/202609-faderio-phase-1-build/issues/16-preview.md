# 16: Preview

Status: resolved
Blocked by: 10, 15

Decided in [What is preview?](../../202609-faderio-phase-1-restage/issues/04-what-is-preview.md), with the prototype on branch
`prototype/preview` (`548bf63`, never merged). Also covered: the preview section of
[What happens when a file or the device fails?](../../202609-faderio-phase-1-restage/issues/05-what-happens-when-a-file-or-the-device-fails.md)
and the correction in [What does queue management add?](../../202609-faderio-phase-1-restage/issues/12-what-does-queue-management-add.md).
**Preview** is in `CONTEXT.md`. Carries v2 0010's intent.

## What to build

**`Preview(adjacency, draft)`** and **`StopPreview()`**.

- **What it plays:** a separate pair of preview voices in the main engine plays the editor's
  unsaved draft, resolved as the engine resolves it.
  - It runs from 5 s before the fade-out point to 5 s after the later fade completes.
  - For a hard cut, the fade-out point is the outgoing track's end.
- **The queue is held** exactly where it was.
  - When a preview ends, a queue that was playing **stays paused**.
  - Queue buttons end the preview first, then act.
  - Rows removed during a preview do not stop it. If the playing row is removed, the held
    queue lands paused on the next row.
- **Capture names its side.** During a preview it reads the preview's voices: fade-out from
  the outgoing voice, fade-in from the incoming one.
- **A running preview keeps the draft it started with.** **Preview again** restarts it with the
  current draft. Save does not interrupt it.
- **Opening another adjacency ends it**, after issue 15's prompt.
- **`PlayerView` gains a nullable `Preview`** field: the adjacency, the phase, and the voice
  playheads. `Attended` and `Incoming` stay the queue's.
- **Failures:**
  - an unreadable track means the preview does not start, and the queue is never held;
  - a decode error ends that voice;
  - device loss ends the preview.
- **Render this boundary** stays unchanged.

## Where the logic lives

- **Engine:** everything above, through the production boundary code.
- **Shell:** Preview, Preview again and Stop preview in the editor. The preview's phase and
  position are shown in the editor; the main window shows the held queue, marked held.

## Acceptance

- [x] Each rule in the prototype's seven walkthroughs holds on the fake seam.
- [ ] A preview in WASAPI exclusive mode plays: there is no second output.
- [x] Capture fade-in before the incoming voice has started does nothing and says why.

## Tests

- **fake:** each walkthrough (happy path, tweak and re-preview, capture, queue buttons, row
  removed, unauthored, device loss), plus the rendered samples of a preview matching a render
  of the same draft.
- **form:** the editor's buttons and the held marker.

## Smoke

- **Looks:** the editor shows the preview's phase and position; the main window shows the queue
  held.
- **Sounds:**
  - preview a draft, capture a better fade-in point while it plays, and preview again: the new
    point is heard.
  - the queue stays paused afterwards.

## Comments

- From issue 13: a ReplayGain toggle ramps every mixer voice that has an entry in the engine's
  voice-to-row map (`RampLiveVoicesToTheirGain`, `PlaybackEngineSettings.cs`). Preview voices
  must be reached too: give them their track's base gain when created, and make sure that
  loop covers them.
- From issue 14: `Capture(fromEnd: true)` returns `Length − playhead` of the attended voice's
  track. When Capture reads the preview's voices, the from-end form must measure against the
  previewed outgoing track's `Length` in the same way.
- Built 2026-09-24.
  - **Mechanism:** a second `TransitionMixer`, `_previewMixer`, in the main engine. The device
    reads it instead of the queue's mixer while a preview runs. It uses the queue's boundary
    pieces: the same `TransitionResolver` and `TryComputeFade`, and the new `AddTrackVoice` and
    `MixerVoice.BeginFadeOut`, which the queue now uses too. A test renders a draft through
    `Renderer.RenderBoundary`, previews the same draft, and finds the samples equal.
  - **Holding the queue:** nothing in the queue moves; its mixer is simply not read.
    - A playing device plays on, so the listener hears the queue up to where its mixer had got
      to, then the preview. A transition in flight is held too.
    - A paused device is stopped first. The moment it still held (at most one device buffer)
      is dropped rather than played before the preview, and the queue resumes after it.
    - When the preview ends, the device stops and its count returns to zero, but the queue
      carries on from where it was held. `_queueBase`, the queue timeline frame the device's
      count starts from, is zero except after a preview. It is reset with the timeline.
    - `_parked` replaces issue 11's `_outputLost` in the transport: a queue with a track on a
      stopped device is Paused.
    - A first build parked the queue as device loss does, rebuilt at its heard playhead. The
      review caught that mid-transition this dropped the transition and moved the main window to
      the incoming track. A test now holds a queue mid-crossfade and resumes it.
  - **Ending:** the incoming voice falls silent exactly at the preview's end (`SilenceFrom`).
    The preview ends once that is heard, as playback finishes once heard. A render that stops
    the device now returns end of stream (`Frames.Zero`), so no mixer moves on in that block.
  - **Queue buttons:** Play, Pause, Stop, ⏭, ⏮, seek, Transition now and play-row end the
    preview first. `TogglePlayPause` now asks the view's transport, not the device's. During a
    preview the device plays but the queue is paused, so ▶ plays the queue.
    - Transition now stays greyed during a preview. The held queue is paused, and Transition
      now never acts while paused.
    - `Clear`, and a finish caused by removing rows, leave the preview's device alone.
    - The prototype's queue-buttons walkthrough says "the queue plays B from 0:05" after ⏭.
      Its own model, and the rule, keep the queue paused, so ⏭ lands paused on B at 0:00.
  - **Failures:**
    - A draft with an impossible value is refused as `Author` refuses it. `Preview` returns the
      refusals, with a "Not previewed" notice.
    - An unreadable track marks every row holding that file, with a "Could not preview"
      notice. A track that opens clears those marks, since a mark clears when its file opens.
    - A preview voice that ends early or on a decode error gets the queue's notice. An outgoing
      voice that ends early fires the transition where it ended, as in the queue.
  - **Capture:** `Capture(CaptureSide, fromEnd)`. During a preview, a side whose voice is not
    heard returns null with a notice ("has not started yet", "has ended", "has fallen
    silent"). The editor now leaves the field as it was; before, a null capture blanked it.
  - **ReplayGain:** preview voices start at their track's base gain, and
    `RampLiveVoicesToTheirGain` ramps them too.
  - **Shell:** Preview (Preview again while one runs) and Stop preview, with a line under them:
    "Transition · outgoing 0:22 · incoming 0:04". The main window's readout reads
    "Held 0:05 / -0:35"; it is 40 px wider for the prefix. At the minimum window size, Save
    and the buttons below it are reached by scrolling the editor.
  - **Found on the way:** `NAudioAudioOutput` set its state to Stopped on every
    `PlaybackStopped`. NAudio posts that to the UI thread, so a Stop then a Play in one command
    was stopped again as soon as it started, and a preview froze at its first position. Only a
    failure changes the state now, since a clean stop is always one the output asked for. The
    Looks smoke caught it; no automated test reaches NAudio.
  - **Not done:** WASAPI exclusive on a real device is the Sounds half. The fake shows the
    preview plays on the engine's one output, and nothing opens a second.
  - ADR 0003 gains an addendum. `docs/smoke.md` gains a Looks item and two Sounds items.
