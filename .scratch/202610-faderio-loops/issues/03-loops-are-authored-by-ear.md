# 03: Loops are authored by ear

Status: ready-for-agent
Blocked by: 01, 02

Core only: the intents the Loops tab (issue 05) will call. See the [spec](../spec.md), D4,
D11, D15, D17, D21, D23 and *Placement*. One user intent is one engine call.

## What to build

- **The loop draft in the view.** As the editor has an adjacency and a draft, the engine has
  the **loops subject**: the track the Loops tab shows, following the attended row's track by
  the editor's follow rule (`EditorFollow`; D21), and a `LoopsOf` the view carries as
  `PlayerView.Loops`: the subject track, both layers (global list, playlist list or "inherits"),
  the selected loop, and the draft of the selected loop's start, end and count with each
  formatted as the grid and boxes show it (seconds text, From-end flag, "∞").
- **Intents**, each one member, each on the undo stack (D17), each saving to the chosen layer:
  `ShowLoops(RowId)` (the Loops… row-menu item: sets the subject and stops following until the
  next follow, as picking a row does for the editor), `SelectLoop(index)`, `AddLoop(layer)` (a
  new loop after the last, parked at count 1, ends defaulting to the playhead and +4 s, or the
  track's end), `RemoveLoop`, `SetLoopEnd(side, Point, fromEnd)`, `SetLoopCount(int?)`,
  `NudgeLoopEnd(side, TimeSpan)`, `CaptureLoopEnd(side, fromEnd)` (the playhead of the voice
  playing the subject track, including a preview's, as `Capture` does; `CanCaptureLoop` says
  when), `SetLoopsLayer(layer)` (switching a playlist from "inherits" to its own list copies
  the global list as the starting point; switching back removes the override), and
  `ClearPlaylistLoops` (the override becomes an empty list, D6).
- A write that would overlap another loop or run out of the track is **refused as a notice**
  and the draft stays; the store is never written out of order.
- **Live effect (D11).** Every write to a sounding track's loops replaces the voice's programme
  in place (issue 02). Issue 02's rules apply; the playhead never moves.
- **Loop preview (D15).** `PreviewLoop(index, draft)`: on the preview mixer with the queue held,
  from `PreviewPreRoll` before the loop end, across the splice, to the same after the loop
  start, once, then stops; `StopPreview` ends it early. `CaptureLoopEnd` works from it.
- **Undo (D17).** Each intent above is one undo step on the same stack as transition
  authoring; `NextUndo` / `NextRedo` name it ("Undo loop count").
- **Manager (D23).** `TracksInStore` includes tracks that have loops in either layer;
  `AdjacenciesInStore(Track)` is unchanged; a new `LoopsInStore(Track)` lists its loops with
  the playlist each override belongs to, and `RemoveLoopsInStore(Track)` removes all of them,
  gone tracks included.

## Acceptance

- [ ] Add, set each end (typed, nudged, captured, from-end), set count, set ∞, remove, switch
      layer, clear: each round-trips through the store and the view shows the result formatted.
- [ ] Capture during playback of the subject track sets the end to the playhead within the
      1 ms promise; `CanCaptureLoop` is false when nothing sounding is that track.
- [ ] An overlap or out-of-track write is refused with a notice and the file is unchanged.
- [ ] Undo and redo of each intent, interleaved with an `Author` of a transition, restore the
      store and the view step by step.
- [ ] Loop preview's samples are the loop's seam as the queue would play it (≡ a render of the
      same window); the queue's playhead is where it was afterwards.
- [ ] A track with loops and no transitions appears in `TracksInStore`; a gone track's loops
      are listed and removable.
- [ ] net8.0 and net48 x86 counts stated in the commit.
