# 03: The attended-row marker in every state

Status: resolved
Blocked by: 01

Decided in [D5](../../202609-faderio-phase-2-interim-improvements/decisions.md). Glossary: **Attended
row** in `CONTEXT.md`.

## What happens today

`QueueRow.IsCurrent` is true on the queue's current row in every state (`PlaybackEngine.cs`),
but the shell paints the `SystemColors.Info` background only
`if row.IsCurrent && view.Transport != Transport.Stopped` (`MainForm.cs:324`). So at launch and
after the queue ends there is no marker, though the engine knows what ▶ would do.

## What to build

- The marker follows `IsCurrent` in every transport state.
- Two looks: **sounding** (playing or paused — the existing `Info` background) and **resting**
  (stopped: launch, ended). Resting is visibly dimmer — the same hue at reduced saturation, or
  a left-edge bar instead of a fill; the builder picks one and records it here.
- The marker is on the row `IsCurrent` names. After the queue ends the engine's current row is
  wherever `Play()` would restart (today: row 0, per phase-1 issue 06). The marker shows that;
  it does not pretend the last row played is still attended.
- The seek bar stays disabled and 0–0 while stopped (D3). The time readout shows `0:00 / -m:ss`
  for the attended row's length if `View.Attended` carries it, otherwise blank as today.

## Where the logic lives

- **Shell only.** The engine already publishes `IsCurrent`; the shell stops second-guessing it.
  If the builder finds `IsCurrent` is *not* set in some stopped state the marker should cover,
  that is an engine fix, recorded here.

## Acceptance

- [x] Launch with rows queued: row 0 carries the resting marker before anything is pressed.
- [x] Play, pause: sounding marker. Let the queue run out: resting marker on the row `Play()`
      would restart.
- [x] ▶ from resting always plays the marked row when nothing else is selected (issue 02).

## Tests

- **form:** a stopped `View` with `IsCurrent` on row *n* renders the resting marker on *n* and
  nowhere else; a paused one renders the sounding marker.

## Smoke

- **Looks:** at launch, the first row is marked (dim); after ▶ it is marked (full).

## Comments

- Built 2026-09-26. **Resting look:** `MainForm.RestingMarker`, the sounding mark's
  `SystemColors.Info` blended halfway to `SystemColors.Control` — (247,247,232) against the
  sounding (255,255,225) on this machine. Chosen over a left-edge bar, which would need an
  owner-drawn `ListView`.
- **Engine fix, decided by the user during the build:** at the end of the queue the engine left
  its current row on the last row played while a plain Play restarted from row 0, so the mark
  and ▶ disagreed (and Q24 of the grilling had said "last row played"). The user chose: the mark
  goes back to row 0 and ▶ plays it. `FinishPlayback` now sets the current row to 0, and Play's
  own restart-from-top branch is gone: Play starts the marked row. Without that second half,
  dragging the marked row after the end would have split them again
  (`AfterTheQueueEnds_MovingTheMarkedRow_PlayStillStartsTheMarkedRow`).
- At launch the queue's current row was already row 0; only the shell hid the mark.
- Tests: `AttendedRowMarkerTests` (4, engine: launch, end, end then move, end under shuffle) and
  one form test through launch → playing → paused → ended.
