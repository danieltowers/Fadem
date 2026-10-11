# 05: The Loops tab

Status: ready-for-agent
Blocked by: 03

Shell, both hosts. See the [spec](../spec.md), D19–D21, D23 and the *Loops tab* drawing. The
editor's slot has about no height to spare at the App's 1240×720 default and the MusicBee
sidebar can be ~330 px tall: measure overflow with `GetScrollInfo` before and after, and ask
before changing the transition editor's own layout.

## What to build

- **The tab strip (D20).** A two-tab strip at the top of the editor's slot, *Transition* |
  *Loops*, in both hosts. The row's ✎ opens Transition as today. A new **Loops…** item on the
  queue row menu calls `ShowLoops(row)` and opens Loops. The strip's height comes out of the
  slot; the transition editor beneath it must not overflow at the default sizes (measure).
- **The Loops tab (D19).** Renders `PlayerView.Loops`: the subject track's title line (as the
  editor's halves name their tracks), the layer selector (Global / This playlist, with "inherits"
  shown when the playlist has no list), a compact grid (Start · End · Count · Layer) of the
  effective list, then one row of boxes for the selected loop's Start and End (seconds box,
  From end tick, Capture, −1 / −0.1 / +0.1 / +1) and one row with Count (a box accepting a
  number or ∞), Add, Remove, Preview loop / Stop, Undo, Redo. Every button calls one engine
  member; the panel keeps no state of its own.
- Text boxes save on leave or Enter, as the editor's do; a refused write (overlap, out of
  track) shows the engine's notice and the box shows the draft the view returns.
- Capture buttons are live only when `CanCaptureLoop` says so.
- **Follow (D21).** The tab shows whatever the view's subject is; it never decides the track.
- **Manager (D23).** `TransitionManagerForm` lists tracks from `TracksInStore` (now including
  loop-only tracks) and shows a *Loops* line or grid per track from `LoopsInStore`, with
  **Remove loops** calling `RemoveLoopsInStore`, gone tracks included.

## Acceptance

- [ ] Headless: the strip switches tabs; the Loops tab's grid shows each loop as the view
      formats it; Loops… on a row makes that row's track the subject; each button invokes its
      one engine member (a recording sink).
- [ ] The transition editor's content height at 1240×720 and in a 330 px sidebar is unchanged
      or the change was agreed first; `GetScrollInfo` shows no new overflow on either tab.
- [ ] The manager lists a loop-only track and removes a gone track's loops.
- [ ] Looks half of `docs/smoke.md` run and reported item by item, both hosts, including the
      spec's new Smoke items.
- [ ] net8.0 and net48 x86 counts stated in the commit.
