# Build the shell

Type: task
Status: resolved
Blocked by: 12, 11

## Question

Nothing to decide: the rewritten WinForms shell, its presenter, and the headless-Form tests, on
the seam from [ticket 12](12-rework-the-engine-seam.md) and the render harness from
[ticket 11](11-render-a-boundary-to-a-file.md). The slice as scoped by
[ticket 05](05-what-exactly-is-in-the-narrow-slice.md) — no more, no less.

The shell was never fat in v2; it was unreachable. This one is reachable from the first commit:
the second test project on `net8.0-windows` with `InternalsVisibleTo` exists before the first
control does.

### What "done" means

- Add-files, a plain list, a context menu on a row for "edit transition (this → next)",
  transport, a seek bar with `Maximum` in milliseconds, and the transition editor with four
  parameter fields, two capture buttons, Save, and one "Render this boundary" button. Nothing
  from ticket 05's deferral list.
- `PostingSink` marshals to the UI thread; a `WindowsFormsSynchronizationContext` exists
  before the form does, so nothing is ever dropped.
- The presenter holds exactly ticket 06's list: seconds parse/format, `RowOf`, `AdjacencyAt`
  with its bounds check, `Summarise(AddReport)`, `OpenTransitionStore`. Every method tested.
- **The placement rule in `CLAUDE.md` holds on every diff**: one intent one call, format never
  compute, hold no engine state.
- Headless-Form tests with `ImmediateSink`: add two files → two rows; `PlayRow` → transport
  playing; `Advance(3 s)` → `_seek.Value == 3000`; `_capture.PerformClick()` → field equals
  `View.Attended.Playhead`; `_seek.Maximum == Attended.Length.Milliseconds`.
- Output mode selectable by command-line argument; no settings dialog.
- A file that fails to open produces a message, not a crash. End of queue stops cleanly.
- **`docs/smoke.md` passes**: the Looks half by screenshot in this session, the Sounds half
  by the user.
- **Ticket 05's acceptance test passes**, on percussive material and on the store's authored boundary,
  on WASAPI shared, exclusive and WaveOut. That is the slice, and the map's destination.

## Answer

Built, 2026-09-23. `src/Faderio.App` (WinExe, net8.0-windows) and `tests/Faderio.App.Tests`.
**Core: 165 tests pass. App: 26 tests pass.** The Looks half of the smoke checklist was run on
screenshots of the real app on WASAPI shared; the Sounds half is the user's and is not claimed.

### What was built

- **`NAudioAudioOutput`**: WASAPI shared (100 ms), WASAPI exclusive (50 ms), WaveOut (150 ms),
  chosen by `--output=wasapi-shared|wasapi-exclusive|waveout`. The engine's rate is the device's
  mix rate read at construction (this machine: 48 kHz, so 44.1 kHz files are resampled once on
  the way in, per ADR 0008); WaveOut declares 44.1 kHz. `FramesEmitted` comes from
  `IWavePosition.GetPosition()` clamped to what has been rendered; the latency constant is only
  the fallback for a player that cannot be asked. A stop requested from inside a render (the
  engine's own end-of-queue) is signalled as end-of-stream so the player stops itself: calling
  `WasapiOut.Stop()` from its own thread would deadlock on `Join`. A WASAPI failure falls back to
  WaveOut with a notice.
- **`PostingSink`**: `IEngineSink` over the UI thread's `SynchronizationContext`, created before
  the engine and the form so nothing is dropped; a null context delivers inline for the
  headless tests.
- **`Presenter`**: exactly ticket 06's list. `Fmt` (m:ss), `FmtRemaining` (rounds up, so it never
  reads 0:00 mid-track), `FmtSeconds` (display only, three decimals), `ParseSeconds` (unset,
  invalid and negative are three different answers), `AdjacencyAt` with its bounds check,
  `Summarise(AddReport)` (a sentence for every outcome, including "all rejected", pointing at
  `docs/supported-audio.md`), `ParseOutputMode`, `OpenTransitionStore` (corrupt file moved
  aside, visibly).
- **`MainForm`**: Add files, transport, a seek bar whose `Maximum` is the attended length in
  milliseconds and whose `MouseUp` seeks `Playhead.FromMilliseconds(Value)` straight through, a
  time readout, the queue list with a context menu "Edit transition (this → next)" enabled only
  where a next row exists, and the right-docked editor. `Render(PlayerView)` on a 100 ms timer
  and on every publication: rows rebuilt only when `QueueVersion` moves, the current-row
  highlight from `IsCurrent` on every render. Two fields of its own: the shown queue version and
  a seek-drag flag.
- **`TransitionEditorPanel`**: four fields, two Capture buttons (enabled only while a voice is
  attended), Save, Render. The captured `Point` is kept and saved exactly; the text box shows
  three decimals, and typing over it falls back to `Point.FromSeconds`. Save goes through
  `engine.Author`, so a pending boundary reschedules.
- **Launch conveniences** for smoke runs and for the user: plain arguments are files to queue,
  `--play` starts them, `--edit-first` opens the editor on the first boundary.

### Tests (seams S4 and S5)

`PresenterTests` cover every presenter method. `HeadlessFormTests` build the real `MainForm` on
an STA thread over the fake output and an inline sink, never shown, and poke the controls: two
files → two rows with titles and lengths; a four-channel file → no row and the reason on the
status strip; `PlayRowAt` then 3 s on the fake → `_seek.Maximum == 5000`, `_seek.Value == 3000`,
readout "0:03 / -0:02", the right row highlighted; Capture with 100 ms of fake latency writes
"1.5" and keeps the exact point equal to `View.Attended.Playhead`; Save authors the adjacency and
confirms; a bad number saves nothing and says why; the editor refuses the last row; and the
frame-exact captured point (12345 frames) is what the store receives, not the "0.28" displayed.
A button click in a never-shown form is fired through `OnClick` by reflection, since
`PerformClick` refuses on an invisible control.

### Smoke, Looks half (screenshots of the real app, WASAPI shared at 48 kHz)

- [x] The window opens without an error dialog.
- [x] Two tracks in the list, each with a title and a length, no blank row.
- [x] The seek bar spans the transport area between the buttons and the readout.
- [x] While playing the thumb moves (captures at 0:04, 0:06 and 0:19; then the second track).
- [x] Elapsed and remaining shown; remaining rounds up and read -0:18 / -0:16 mid-track.
- [ ] Context menu enabled on the first row, disabled on the last: **not exercised by
      screenshot**; covered by `EditTransition_IsOfferedOnEveryRowButTheLast` and the menu's
      Opening handler.
- [x] The editor opens with its title fully visible ("Outgoing Tone 440 → Incoming Tone 660").
- [x] Four fields and both Capture buttons visible without scrolling at the default size.
- [x] Save and Render visible and enabled once a boundary is selected.
- [ ] Capture writes the readout's number: **not exercised by screenshot**; covered by
      `Capture_WritesThePlayheadIntoTheField_AndKeepsTheExactPoint`.
- [ ] Save confirms on the status strip: **not exercised by screenshot**; covered by
      `Save_AuthorsTheAdjacency_AndConfirmsInTheStatusStrip`.
- [x] At 760 px wide the transport, the readout and the editor's buttons all remain visible.
- [x] Adding a four-channel WAV leaves the list at two rows and the status strip reads "Added 2
      tracks; rejected 1 — quad.wav: 4 channels is not promised; only mono and stereo are. See
      docs/supported-audio.md."
- [ ] The library's longest FLAC: **not run**; the library's path is not recorded on the map.

Two things fixed on the way, both found only by the screenshot: the time readout was anchored
off-screen because the transport panel's anchors were computed before it was docked (now a
`TableLayoutPanel`), and the pause glyph rendered as a box in the default font (Segoe UI
Symbol on the four transport buttons).

**Observed once, not reproduced.** On the very first launch the play button still read ▶ and
the thumb sat at 0 six seconds after `--play`; three later launches played immediately. A
device probe through `NAudioAudioOutput` on WASAPI shared and WaveOut reported Playing with the
playhead at 0.58–0.59 s after 600 ms both times. The user's Sounds half should watch for it.

### What this ticket does not claim

Ticket 05's acceptance test on percussive material and the store's authored boundary, on three output modes,
is the user's listening test. The measured half exists (`SeamTests`, `RendererTests`, at 1 ms);
the by-ear half is `docs/smoke.md`'s Sounds section. Launch for it with:

```
dotnet run --project src/Faderio.App -- "outgoing.flac" "incoming.flac" --edit-first --output=wasapi-shared
```

then `--output=wasapi-exclusive` and `--output=waveout`.


## Noted from ticket 07

The second test project (`net8.0-windows`, referencing `Faderio.App`) was deferred to here: it
cannot exist before the shell does. `InternalsVisibleTo("Faderio.Core.Tests")` is already on
Core; add the App project's own when it appears.

## Noted from ticket 12

The surface is final; read `PlaybackEngine`, `PlayerView.cs`, `IEngineSink.cs`, `IAudioOutput.cs`
and `Domain/Units.cs`. Deviations from ticket 04's sketch, all packaging:

- `Length`, `Playhead` and `Point` carry ticks, not frames; the shell sees `Milliseconds`,
  `TotalSeconds` and `TimeSpan`. `Point.TimeSpan` feeds `FadePointSpec`; `Point.FromSeconds` is
  the only parse path. `VoiceView.Remaining` is a `Length`. `QueueRow` is `(Id, Track, IsCurrent)`
  and the length is `Track.Length`.
- `AddReport` is `(Added, Rejected)` where each `Rejection` carries the path, the probed
  `AudioShape` (may be null) and the `RejectedProperty`; `Summarise(AddReport)` formats these
  and points at `docs/supported-audio.md`. Never compute from them.
- The engine constructor is `PlaybackEngine(IAudioOutput, ITransitionStore, IEngineSink,
  PlayerSettings? = null, Random? = null)`. Output mode is chosen by building the right
  `IAudioOutput` at launch; there is no `ApplySettings`.
- **`PostingSink`** is yours: `IEngineSink` over a `SynchronizationContext`, created before the
  form so nothing is dropped. **`NAudioAudioOutput`** is yours too: wrap
  `new FloatToBytesWaveProvider(new FrameSourceSampleProvider(source))`, report `SampleRate`
  from the device, `FramesEmitted` from `IWavePosition.GetPosition()` in bytes over the output
  format, and reset it on `Stop`. `Stopped` is `Action<Exception?>`.
- `IsCurrent` flips at the audible handover, not at `QueueVersion`; render the current-row
  highlight from `View.Queue` on every tick, and rebuild rows only when `QueueVersion` moves.
- `Capture()` returns null when stopped; the capture button is disabled then.

## Comments

**2026-09-23, the user:** the Sounds half of `docs/smoke.md` passed, run on the authored boundary
from the store. The by-ear half this ticket declined to claim
is now claimed by the one party who can.
