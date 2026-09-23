# What is preview?

Type: prototype
Status: resolved
Blocked by:

## Question

v2 issue 0010: hear a transition before trusting it. Pre-roll the outgoing track (5 s by
default, with 2/5/10 s choices), play the authored transition in real time, run at least 5 s
past the fade-in point, then stop. v2 built it with its own mixer and its own output device.

Two designs are on the table:

- **Render, then play.** `Renderer.RenderBoundary` already renders pre-roll, transition and
  tail to a file. Preview plays that file. It is exactly what the engine would do, but there
  is a render delay before sound starts.
- **A second engine** over its own output. It starts instantly, but it cannot open
  WASAPI-exclusive while the main engine holds it.

A third option is **preview through the main engine**: pause the queue, seek the outgoing
track to pre-roll, play the boundary, then restore. Prototype the options that survive and
let the user react to how each behaves from the editor. Decide the design, what happens to the
main queue during a preview, and whether the pre-roll length is a setting (feeds ticket 10).

## Answer

Prototyped and grilled with the user on 2026-09-23. `CONTEXT.md` gains **Preview**.

**Assets** (branch `prototype/preview`, commit `548bf63`, never merged):
`src/Faderio.Core/Playback/PROTOTYPE-preview.html` is a double-click state model with seven
walkthroughs; the user accepted every rule below in it.
`tests/Faderio.Core.Tests/PrototypePreviewRenderTiming.cs` is the render timing probe.

### The mechanism: preview voices in the main engine

The engine holds the queue exactly where it is and plays a separate pair of voices for the
adjacency on the same output. The preview runs through the production boundary code, so what
you hear is what the queue would play. When it ends, there is nothing to restore.

- **Render, then play: rejected.** A render of a real boundary took 0.7–1.0 s for 27–30 s of
  audio (Debug, both authored FLAC boundaries, three runs each), so every preview would open
  with about a second of silence. It also still needs a second output to play the file.
- **A second engine: rejected.** Its own `IAudioOutput` cannot open while the main engine
  holds the device in WASAPI-exclusive.
- **Moving the queue: rejected.** Restoring the row, playhead and transport becomes state the
  engine has to rebuild. Moving the queue's playhead also lets its own transition fire
  mid-preview.

### What it plays

- **The editor's unsaved draft**, resolved as the engine resolves it. A partly authored draft
  plays its effective result; an unauthored adjacency plays as a hard cut. The store is
  untouched until **Save**.
- **From 5 s before the fade-out point to 5 s after the later fade completes**, then it stops.
  For a hard cut, the fade-out point is the outgoing track's end. Neither length is a setting.
- **Render this boundary stays** as it is: a file for a waveform editor and a measured test
  input, which is a different job from listening.

### The queue during a preview

- **Held.** A queue that was playing is held at its position; paused or stopped stays so.
- **When a preview ends** by itself, by **Stop preview**, or because the editor opened another
  adjacency, a queue that was playing **stays paused** where it was held. It does not resume.
- **Queue buttons end the preview, then act** on the held queue: Play, Pause, ⏭, ⏮, seek and
  Transition now. They are never disabled for a preview.
- **Rows removed during a preview do not stop it.** A preview plays tracks, not rows. When it
  ends, the queue shows what the removal left. *Corrected by
  [What does queue management add?](12-what-does-queue-management-add.md): a held queue whose
  playing row is removed lands paused on the next row, not Stopped.*

### The editor during a preview

- **Capture reads the preview's voices**: fade-out from the outgoing voice, fade-in from the
  incoming one, never the held queue. Capturing fade-in before the incoming voice has started
  does nothing and says why. Capture therefore names the side it reads.
- **A running preview keeps the draft it started with.** Editing fields does not change what
  plays; **Preview again** restarts it with the current draft. **Save** during a preview
  writes to the store and the preview plays on.

### What the engine publishes

- `Preview(adjacency, draft)` starts or restarts a preview; `StopPreview()` ends one. Each is
  one user intent (placement test 1).
- `PlayerView` gains a nullable `Preview` field: the adjacency, its phase, and its voices'
  playheads. `Attended` and `Incoming` stay the queue's, so the main window's seek bar, time
  readout and now-playing row keep showing the held queue, marked held. The preview's
  position appears in the editor.
- A clip heard during a preview goes into the one clip light
  ([Does the visualiser come back?](03-does-the-visualiser-come-back.md)): it names the
  adjacency during the overlap, otherwise the preview's audible track.

### Consequences for other tickets

- [Which settings exist, and where do they live?](10-which-settings-exist-and-where-do-they-live.md)
  gets nothing: pre-roll and tail are fixed.
- [What completes the editor?](09-what-completes-the-editor.md) and
  [What happens when a file or the device fails?](05-what-happens-when-a-file-or-the-device-fails.md)
  each carry a note.
