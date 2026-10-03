# 11: The seek bar's fade marks and wedges can be dragged

Status: resolved

Phase 6, improvements round 1, both hosts (the seek bar is shared). Raised 2026-10-03:
"- [ ] ability to drag the boundary markers (click and drag a red marker)
 - [ ] ability to move the transition regions (click and drag the grey wedge)"

Feasibility was looked into before this was written; the recommendations below were accepted as
they stand.

## What to build

Authoring by hand on the seek bar. The red marks and grey wedges it draws for the attended track
(`VoiceView.Marks`: the fade-in from previous → this, the fade-out into this → next) become
handles:

- **A red mark drags.** Its end of the fade moves to where it is dropped; the other end stays
  put, so the fade gets longer or shorter.
- **A grey wedge drags.** The whole fade slides by as far as the mouse moved; its duration is
  kept.

While dragging, the seek bar draws the mark or wedge following the mouse, as it draws the thumb
during a seek drag. Nothing is saved until release, which saves once: one authoring, one undo
step, one notice, and the boundary rescheduled if it is coming up, exactly as a save from the
editor (`Author` / `AuthorOverride`).

## Implementation decisions (accepted 2026-10-03)

- **An edge resizes, the wedge moves.**
  - Fade-out start mark → the fade-out point moves and its duration changes so the end stays.
  - Fade-out end mark → the fade-out duration.
  - Fade-in start mark → the fade-in point moves and its duration changes so the end stays.
  - Fade-in end mark → the fade-in duration.
  - A wedge → its point shifts by the drag, its duration kept.
  - A drop past the other edge, or outside the track, is clamped or refused by the engine
    (its `Refusals`), never by the shell.
- **Seek or drag is decided by where the press lands.** A press within ±4 px of a mark drags
  the mark, with the ↔ (SizeWE) cursor shown on hover. A press inside a wedge but outside the
  middle band (the groove and the thumb's height, ±9 px about the centre) drags the wedge, with
  the move cursor on hover. A press in the middle band always seeks, as today, so a click to
  seek never moves a fade. The thumb wins over a mark it covers.
- **Which layer is written: where the value came from.** If the open playlist's override sets
  the parameter being dragged, the override changes (`AuthorOverride`); otherwise the global
  transition does (`Author`). The editor's Global / This playlist mode plays no part.
- **An unauthored adjacency becomes authored.** Marks from the default crossfade, or from blank
  durations, are dragged into a saved transition with explicit values: the parameters the drag
  sets, the rest as the resolver played them. A hard cut has no marks, so nothing to drag.
- **From end is kept.** A fade-out point stored from the end is saved from the end after a drag.
- **The engine does the arithmetic (CLAUDE.md tests 1 and 2).** Two engine commands, one per
  intent; the shell sends playheads only:
  - `MoveFadeMark(RowId row, FadeMark mark, Playhead to)`, with `FadeMark` one of
    `FadeInStart`, `FadeInEnd`, `FadeOutStart`, `FadeOutEnd`.
  - `MoveFade(RowId row, FadeSide side, Playhead from, Playhead to)`, with `FadeSide` one of
    `FadeIn`, `FadeOut`: grabbed at `from`, dropped at `to`.
  The engine finds the adjacency from the row (previous → this for a fade-in, this → next for a
  fade-out, by `AdjacenciesOf`), resolves the layer, converts from-end points, validates, saves
  and returns the refusals. The seek bar turns pixels into playheads, as it already does for a
  seek, and raises two events beside `SeekRequested`; each host wires them to the engine with
  the attended row.
- **The drawing during a drag is the seek bar's own.** It holds the dragged mark's playhead only
  while the button is down, as it holds the thumb's, and forgets it on release (CLAUDE.md test
  3). The marks it shows after release are the view's.
- **The editor reloads when the store changes.** Today it loads only on Open, Undo and Redo, so
  a drag on the transition it shows would leave stale fields that its next save could write
  back. On every render, when `PlayerView.StoreVersion` has moved and no box is being typed in,
  the editor loads its adjacency again. A box being typed in is already saved when the seek bar
  takes focus on the press.
- **Both hosts.** The plugin gets it with the shared seek bar. In MusicBee's sidebar the bar's
  travel is short, so a drag there is coarse; the editor's nudges fine-tune.

## Known limits (accepted)

- **Precision is a pixel**: about 0.3 s on a 4-minute track across the App's bar, several
  seconds in the sidebar. The editor's ±0.1 s and ±1 s nudges fine-tune.
- **The fade-in marks are previous → this**, already heard; a drag changes them for the next
  time that boundary plays.
- **A transition belongs to its pair of tracks**, so a drag changes it wherever that pair is
  adjacent.
- **No keyboard path**: the editor's fields and nudges remain the keyboard way.

## Testing decisions

Core, on both targets, through the engine, each asserting the store and the view's marks after:

- Each of the four marks dragged on an authored transition changes exactly the parameters
  above; the other edge is unchanged.
- A wedge drag shifts the point by `to − from` and keeps the duration, for each side.
- A from-end fade-out point stays from-end, at the moved position.
- With a playlist override setting the dragged parameter, only the override changes; without
  one, only the global does, with a playlist open.
- An unauthored adjacency under the default crossfade becomes authored with explicit values.
- A drop past the other edge is refused with the engine's reason and nothing is saved.
- One drag is one undo step; Undo puts the transition back.
- A drag on the coming boundary while playing reschedules it (the fade starts at the new point).
- A stale row, or a fade the row does not have, does nothing and says so in a notice.

`Faderio.Shell.Tests` (both targets), the seek bar alone: a press near a mark raises the mark
event on release with the drop's playhead; a press in a wedge outside the band raises the fade
event with grab and drop; a press in the band seeks and raises neither; nothing is raised before
release; the cursor over a mark is SizeWE. The editor against a real engine: it shows the new
values after another caller authors its adjacency, and keeps a box being typed in.

`Faderio.App.Tests` and `Faderio.MusicBee.Tests`: a drag on the form's seek bar reaches the
engine for the attended row.

Both **Looks** halves gain an item: the cursors over a mark and a wedge, the mark and wedge
following the mouse, the saved result and the editor showing it. The **Sounds** half gains one:
drag the coming fade-out start and hear the transition begin there.

## Acceptance criteria

- [x] Dragging a red mark moves that end of its fade; the other end stays.
- [x] Dragging a grey wedge slides its fade, keeping its length.
- [x] A click in the bar's middle band still seeks and never moves a fade.
- [x] Each drag saves once, to the layer the value came from, as one undo step.
- [x] The editor shows the dragged values at once.
- [x] Works in faderio and in the MusicBee plugin.

## Comments

Built 2026-10-03.

- **Engine.** `PlaybackEngineFadeDrag.cs`: `MoveFadeMark` and `MoveFade`, with `FadeMark` and
  `FadeSide` beside `FadeMarks` in `PlayerView.cs`. Both resolve the adjacency as it plays,
  move the fade, and save through `Author` or `AuthorOverride`, so the refusals, the notice,
  the undo step and the reschedule are theirs. A stale row, a missing fade and a hard cut each
  say "Nothing moved: …".
- **Layer.** A start mark sets two parameters, the point and the duration. If the override sets
  either of them, both go into the override, so one drag stays one undo step. The global's
  blanks are made explicit as the global alone plays them, so nothing of an override leaks
  into it. The curve is left blank, because blank already plays equal-power.
- **Wedge clamp.** A wedge stops where its whole duration still fits in the track, not at the
  track's end. Otherwise a fade-out dragged past the end would shrink to nothing.
- **Seek bar.** A drag that ends where it started raises nothing. The bar's own clamp to its
  travel means a drop off either end arrives at 0 or the length.
- **Editor.** It reloads on a render once `StoreVersion` has moved and no box has typed text
  pending, as the transition manager already does with `_shownStore`.
