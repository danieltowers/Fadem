# What is preview?

Type: prototype
Status: open
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
