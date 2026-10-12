# 03: The editor without the traffic light

Status: ready-for-agent
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

- [ ] The halves are numbered; the half heard has 🔊 and "· playing"; the status line says
      when something else plays or another track comes next. No traffic light.
- [ ] `dotnet test` passes on net8.0 and net48 x86, and the commit states both counts; the
      Looks smoke runs and is reported item by item.

## Comments
