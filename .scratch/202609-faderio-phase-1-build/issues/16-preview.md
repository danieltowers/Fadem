# 16: Preview

Status: ready-for-agent
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

- [ ] Each rule in the prototype's seven walkthroughs holds on the fake seam.
- [ ] A preview in WASAPI exclusive mode plays: there is no second output.
- [ ] Capture fade-in before the incoming voice has started does nothing and says why.

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
