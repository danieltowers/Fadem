# Render a boundary to a file

Type: task
Status: open
Blocked by: 07

## Question

Nothing to decide: this is the manual work that makes every later session able to verify itself.
It earns its place because the slice's acceptance test has a half that cannot happen without it.

The map's Notes settled at charting that verification is **both** automated assertions **and**
an audible artifact the user can listen to without launching the app.
[Ticket 05](05-what-exactly-is-in-the-narrow-slice.md) then established that this artifact
**does not exist** — there is no file-writing audio path anywhere in v2, and the only
`WaveFileWriter` in that repo generates test fixtures. Ticket 05 put it *before* the slice
rather than inside or after it, so that every subsequent session can hear its own work even
when the UI is broken or absent.

The seam is already the right shape. `IAudioOutput` is
`Initialise(ISampleProvider, AudioOutputMode)` / `Play` / `Pause` / `Stop` / `State` / `Stopped`,
and the engine is pulled entirely through it. `FakeAudioOutput` (91 lines in v2's test project)
already pulls the mixer deterministically in 4410-frame blocks, faster than real time, with no
clock, capturing every rendered float block. A file-writing output is that class with a
`WaveFileWriter` where the `Blocks` list is.

### What "done" means

- A file-writing `IAudioOutput` implementation, deterministic and faster than real time.
- A way to render **one authored boundary** to a WAV file: pre-roll, the transition, and enough
  tail past the fade-in point to hear the handoff settle.
- Reachable without launching the WinForms shell — a console entry point, a test helper, or
  both. The point is that a session with no UI can still produce something the user can listen
  to.
- The rendered file is the input to ticket 05's measured assertion: fade onset within 5 ms of
  the captured point, on WAV.

### Notes

Ticket 05 settled that output mode is selectable by command-line argument or config line, with
no settings dialog. The render harness is effectively a fourth output mode, and it is the one
that makes the other three testable against each other.

This ticket is a **task**, not a decision. If a real design question surfaces while building it
— particularly about where the render entry point lives relative to the seam — that belongs to
[ticket 04](04-what-is-the-seam-between-the-shell-and-the-engine.md), not here.

## Answer

_(pending)_
