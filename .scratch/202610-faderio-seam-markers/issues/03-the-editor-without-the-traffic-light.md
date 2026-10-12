# 03: The editor without the traffic light

Status: resolved
Blocked by: 01

Shell, `TransitionEditorPanel`, both hosts. See the [spec](../spec.md), "The answer: G3
soft": the halves are numbered, and a status line for the light's red.

## What to build

- **No traffic light**: `TrafficLight` goes from `TransitionEditorPanel`, and
  `StudioTheme`'s case for it.
- **Numbered halves**, as the queue numbers its rows: "20 · Last Light" above the fade-out,
  "21 · Northbound" above the fade-in. The numbers are the first match's rows from issue 01's
  query, looked up in the view's queue; with no match, the names alone. Repeat-one's
  this → this gives the same number twice.
- **The speaker on the half heard**, from `Heard`: "🔊 20 · Last Light · playing" in the text
  colour (`Outgoing` on the upper half, `Incoming` on the lower); the other half stays dim.
  This replaces the light's green and amber.
- **A status line under the selector**, dim, replacing the light's red: "Playing *x*, not in
  this transition" for `OtherTrack`, "*y* plays next, not *b*" for `OutgoingIntoAnother`.
  Nothing heard, no line. The names come from the view; the shell computes nothing.
- No tint and no frame yet (issue 04).

## Tests and docs

- `EditorTrafficLightTests` goes; its cases become tests of the speaker and the status line,
  one for each `AdjacencyHeard`.
- `EditorThemeTests`: the light's case goes.
- New: the halves' numbers, with a match, without, and under repeat-one.
- `docs/smoke.md`: the traffic light item becomes the speaker and the status line; "Each half
  names its track" gains the numbers; the editor theme item loses the light.

## Acceptance

- [x] The halves are numbered; the half heard has 🔊 and "· playing"; the status line says
      when something else plays or another track comes next. No traffic light.
- [x] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs and is reported item by item.

## Comments

2026-10-12, built. One departure: "the names come from the view" became the engine's. Review found
that `view.Attended` and `view.NextAdjacency` do not follow `Heard`'s rules: in a crossfade into
the outgoing track, `NextAdjacency` is the attended row's next, not the outgoing row's; under
repeat-one the track plays into itself, which `NextAdjacency` never gives; and a preview's track
is not the attended one. So `Heard` became `Hearing`, an `AdjacencyHearing` carrying the
`AdjacencyHeard` with the track playing and, while `OutgoingIntoAnother`, the track the outgoing
row plays into, from the same rows (CLAUDE.md test 2); six Core tests pin those three cases and
the plain ones. Wording the spec did not give: "Nothing plays next, not *b*" after the last row.
The halves are numbered by `Presenter.RowNumber` in the view's queue, as the # column numbers
them. The halves' ink follows their text. Looks smoke by `heard-smoke.ps1` (captures, UI
Automation, a posted double-click with the cursor set on the row): stopped, the outgoing, the
incoming and another track playing, and the outgoing into another. With the status line showing,
the editor at the default window size gains its scroll bar and the Undo row is cut at the bottom;
without it, nothing scrolls. Left for the user's eye: whether the line should take less room.
