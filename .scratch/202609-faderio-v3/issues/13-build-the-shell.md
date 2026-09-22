# Build the shell

Type: task
Status: open
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
- **Ticket 05's acceptance test passes**, on percussive material and on the authored boundary,
  on WASAPI shared, exclusive and WaveOut. That is the slice, and the map's destination.

## Answer

_(pending)_
