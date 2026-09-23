# Render a boundary to a file

Type: task
Status: resolved
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

Done, 2026-09-23, in the same session as [ticket 13](13-build-the-shell.md), which needed it for
its Render button.

- **`WavFileAudioOutput`** (Core): the output seam bound to a 32-bit float WAV through
  `WaveFileWriter`. Deterministic, faster than real time, no latency: `FramesEmitted` equals
  what was rendered, and `Stop()` resets it like every other output. `Advance(Frames)` pulls
  the engine in device-sized blocks.
- **`Renderer.RenderBoundary(Adjacency, ITransitionStore, wavPath, preRoll?, tail?)`**: a fresh
  engine on that output with a `RecordingSink`, both tracks through the production probe (a
  rejected file throws, naming it), `PlayRow` on the outgoing row, a seek to the fade-out point
  minus the pre-roll (5 s by default), then blocks until the attended voice has been the
  incoming one for the tail (5 s) or playback ends. Returns a `RenderReport` with the path, the
  rendered length and where in the file the fade-out point sits.
- **Three tests** (`RendererTests`): the fade in the file starts within 1 ms of where it was
  authored, measured with the same extrapolating onset detector as the S3 test; the file is
  frame-for-frame the audio the fake output renders in memory for the same boundary, so the
  artifact the human hears is the artifact the suite measured; and a rejected file is refused
  loudly.

The shell's "Render this boundary" button calls this with a path from a save dialog and reports
the rendered length and the fade-out point's position in the file on the status strip.


## Noted from ticket 06

Two additions to "done":

- **A round-trip test**: render one boundary through this output *and* through
  `FakeAudioOutput`, and assert the file's samples match the in-memory blocks. The artifact the
  human hears must be the artifact the suite measured.
- **The in-app driver** is one "Render this boundary" button in the transition editor, writing
  to a chosen path. One button, no options. It is not preview.

The S3 end-to-end tests themselves run against the fake, in memory, for speed; this harness is
for the human and for that one round-trip test.

## Noted from ticket 04

The shape is decided: `Renderer.RenderBoundary(Adjacency a, ITransitionStore store, string wavPath)`,
a static entry point with no engine event plumbing, driving a `WavFileAudioOutput` adapter
that drains synchronously. The harness passes an in-memory or temp-path `ITransitionStore` so
it never touches the user's file. `IEngineSink` gets a `RecordingSink` or console adapter here.

## Noted from ticket 12

The seam is built; this ticket implements it once more, to a file:

- **`IAudioOutput`** is now `SampleRate` (declare 44.1 kHz, or the file's rate), `Initialise(IFrameSource)`,
  `Play/Pause/Stop`, `State` (`Transport`), `FramesEmitted` (`Frames`; reset by `Stop`) and
  `event Action<Exception?> Stopped`. A file output has no latency: emitted equals rendered, as
  the fake does with `Latency = 0`. Pull the engine with `IFrameSource.Read(buffer, offset, Frames)`
  and write through `WaveFileWriter`; `FrameSourceSampleProvider` adapts to NAudio if useful.
- **`Renderer.RenderBoundary(Adjacency, ITransitionStore, string wavPath)`** stays the static
  shape from ticket 04: a fresh `PlaybackEngine(fileOutput, store, new RecordingSink())`, two
  rows from `AddTracks`, `PlayRow` on the outgoing row, `Seek` to pre-roll before the fade-out
  point, and advance the file clock until the incoming voice has run its tail. The engine
  constructor takes `PlayerSettings` and `Random` as optional extras.
- The S3 in-memory assertion this ticket mirrors is
  `SeamTests.CaptureThenAuthor_TheFadeStartsWhereTheListenerHeardIt_Within1ms`; its
  `FadeOnset` extrapolation is the measuring instrument to reuse on the file.
