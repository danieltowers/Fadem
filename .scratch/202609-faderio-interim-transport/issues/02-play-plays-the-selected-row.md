# 02: Play plays the selected row

Status: ready-for-agent
Blocked by: —

Decided in [D4](../../202609-faderio-interim-improvements/decisions.md).

## What happens today

`Play()` (`Transport.cs`) resumes the attended voice, or with none opens the queue's current
row from 0:00. The `ListView` selection is shell state the engine never sees, so: A paused,
click row B, press ▶ → A resumes. Only a double-click (`PlayRow`) plays B.

## What to build

- **`Play(RowId? selected)`** replaces the no-argument `Play()` as the ▶ intent. The engine
  decides:
  - `selected` is null, or names the attended row → resume (paused) or start it from 0:00
    (launch / ended), exactly as `Play()` does today.
  - `selected` names any other row → that row plays from its start, as `PlayRow` does. Any
    in-flight transition is abandoned the way `PlayRow` abandons it.
  - `selected` names a row that no longer exists → resume as if null, no notice (the selection
    was stale, not wrong).
- `TogglePlayPause()` takes the same optional `RowId?` and only uses it on the play half.
- The shell passes the single selected row's id when exactly one row is selected; null
  otherwise (none or many).
- Double-click is unchanged: `PlayRow(row)`.

## Where the logic lives

- **Engine:** the resume-or-start decision. One intent, one call (placement test 1). The shell
  never compares the selection against `View.Attended` to pick a command.
- **Shell:** reads its own `ListView` selection and hands it over.

## Acceptance

- [ ] Paused on A, select B, ▶ → B plays from its start.
- [ ] Paused on A, A still selected (or nothing selected), ▶ → A resumes where it was.
- [ ] Paused on A, two rows selected, ▶ → A resumes.
- [ ] At launch with rows queued, select row 3, ▶ → row 3 plays.
- [ ] Mid-crossfade A→B, select D, ▶ (via pause first) → D plays; the crossfade is abandoned.

## Tests

- **fake output:** each acceptance line as an engine test; the stale-row case too.
- **form:** ▶ passes the selection; with none selected it passes null.

## Smoke

- **Looks:** pause, click a different row, ▶ — the marker moves to the clicked row and the
  time readout restarts from 0:00.

## Comments
