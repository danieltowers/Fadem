# Spec — faderio v3: loops inside a track

Status: ready-for-agent
Blocked by: none

Raised 2026-10-10: "introduce a loop count tool to repeat sections of track?? this would be
useful but we might want to fade it out while the loop is playing!! insert multiple loops per
track". Grilled and decided 2026-10-10/11; every recommendation below was accepted, with one
change (the panel is docked, D19).

The words are in [CONTEXT.md](../../CONTEXT.md) under **Loops**: *Loop*, *pass*, *Loop
preview*. "Section" is taken (a run of rows) and is never used for this.

## Problem

faderio authors the handoff between two tracks to the millisecond, but it plays each track
exactly as the file has it. A DJ or a set-builder wants more than that from a track they know:
hold the breakdown for four more bars, extend an outro so the mix-out has room, come in on the
hook and let it ride. Today the only ways are to edit the file or to queue the track twice with
an A→A transition, which repeats the whole thing.

## Solution

A **loop**: a span inside a track, from a loop start to a loop end, heard a set number of times
— its count — before the track plays on. A track can have several. A loop is authored like a
fade point (typed, captured by ear, nudged), kept in the library like a transition (globally,
with a playlist override), shown on the seek bar while it plays, and auditioned before it is
saved. The transition to the next track happens on the loop's last pass, and an open-ended loop
cycles until the user mixes out with **Transition now** — a fading voice keeps looping, which
is the "fade it out while the loop is playing" case.

```
 Seek bar while looping (both hosts)
 ┌──────────────────────────────────────────────────────────────┐
 │━━━━━━━━━━━━━━━━━┣━━━━━━●━━━━━━┫────────────┣━━━━━┫───────────│
 │                 └─ pass 2/4 ──┘            └ ∞ ──┘           │
 └──────────────────────────────────────────────────────────────┘

 The editor's slot, Loops tab (both hosts, fits the editor's height)
 ┌ Transition │ Loops ──────────────────────────────────────────┐
 │  Evening Song — Example Band                   [Global ▾]    │
 │  ┌──────────┬──────────┬────────┬────────────┐               │
 │  │ Start    │ End      │ Count  │ Layer      │               │
 │  │ 1:04.250 │ 1:12.000 │ 4      │ Global     │               │
 │  │ -8.000 ◂ │ -0.000 ◂ │ ∞      │ This list  │               │
 │  └──────────┴──────────┴────────┴────────────┘               │
 │  Start [ 64.250 ] □ From end  [Capture] [−1][−0.1][+0.1][+1] │
 │  End   [ 72.000 ] □ From end  [Capture] [−1][−0.1][+0.1][+1] │
 │  Count [ 4 ▾ ]   [Add] [Remove]  [Preview loop] [Undo][Redo] │
 └──────────────────────────────────────────────────────────────┘
```

## Decisions (accepted 2026-10-11)

### The thing

- **D1 — Loop and pass.** The authored span-plus-count is a *loop*; one hearing of it is a
  *pass*. Avoid section, A-B repeat, region, segment, cycle.
- **D2 — The count is total hearings.** 1 is heard once (a parked loop you can turn back up),
  2 is heard twice. Nothing special-cases "no loop". **∞** is a distinct value, open-ended.
- **D3 — A loop belongs to the track, globally, with a playlist override.** The same two layers
  the transition editor names (Global / This playlist). Per-row loops were rejected as the odd
  one out; track-only is the honest subset if ever needed.
- **D4 — Both ends are fade-point-shaped.** Each of loop start and loop end is a position
  measured from the track's start or from its end, with the same box, From-end tick, Capture
  and ±0.1 s / ±1 s nudges a fade-out point has.
- **D5 — Several loops, disjoint, in track order.** No overlap, no nesting, no cap.
- **D6 — An override replaces the whole list.** A playlist either inherits the track's loops or
  has its own complete list; an empty list means "no loops in this playlist". Per-loop merging
  would need ids in the file and a mental model nobody can predict.

### Playback

- **D7 — Points and Length never move.** The track's length and every point stay what they are.
  A fade-out point inside a span starts the transition on the **last pass**. Unrolling the
  timeline (Length grows, from-end points resolve against it) was rejected: it rewrites Length
  for every consumer and makes a from-end point move when a count changes.
- **D8 — A fading voice keeps looping.** A voice follows its own programme, loops included,
  whether or not it is fading. That is the "fade out while the loop is playing" case; it also
  gives the open-ended case for free.
- **D9 — What ends an open-ended loop.** Transition now (the authored transition plays from the
  playhead; the voice keeps looping as it fades) or Skip (hard cut, as today). There is no
  transition to cue: `CanCueTransition` is false while the attended track's programme has an
  open-ended loop ahead of or around the playhead.
- **D10 — A span is always entered at pass 1**: played into, seeked into from before it, or
  started inside it at a fade-in point. A seek within the span stays on its pass. A seek past
  the loop end leaves it behind. ⏮ and re-starting the row begin at pass 1.
- **D11 — Editing a sounding loop takes effect at once and never moves the playhead.** A count
  now below the current pass makes this pass the last; a span moved off the playhead is left
  behind; a deleted loop stops looping.
- **D12 — The seam is a fixed declick splice, not a fade.** A few milliseconds, equal-power,
  not authorable, not a "fade" in the glossary sense. Cutting exactly at the points would click
  on every loop not placed on a zero crossing.
- **D13 — A loop that does not fit is passed over with a notice.** End past the length, or
  start not before end: the loop is kept as authored, the track plays straight, the notice
  names the loop. Never clamped silently.
- **D14 — Preview and Render honour loops** in their window, so a transition's preview is what
  the queue will play. The existing test (preview samples ≡ `RenderBoundary`) still holds.
- **D15 — Loop preview.** From shortly before the loop end, across the splice, to shortly after
  the loop start, once, on the preview mixer with the queue held. In the first version.

### Shell

- **D16 — The playhead jumps back each pass**, and the seek bar shows each span and the
  sounding loop's "pass 2/4" (∞ for open-ended). The jump *is* the information. Scrubbing
  inside the span lands on the current pass.
- **D17 — Undo is one stack.** Loop edits join the authoring undo/redo history, interleaved
  with transition edits.
- **D18 — Live loop in/out from the transport is out of scope.** Capture in the panel is how a
  loop is born. Noted as a follow-up.
- **D19 — The Loops panel is docked in the editor's slot** (changed from the floating-window
  recommendation). It must fit the same height the transition editor fits, including the
  MusicBee sidebar at ~330 px: a compact grid plus one row of boxes and one row of buttons.
- **D20 — A two-tab strip, Transition | Loops**, at the top of the slot. The row's ✎ opens
  Transition; a new **Loops…** item on the row menu opens Loops. The strip makes the state
  visible and reversible.
- **D21 — The Loops tab shows the attended row's track, by the editor's own follow rule.** It
  is the outgoing track of the editor's adjacency, so switching tabs never changes subject and
  there is no second follow mechanism.
- **D22 — The store is `transitions.json`, schema version 2**, with a `loops` map keyed by
  path and a per-playlist override map, one file as ADR 0006 has it. A version-1 file reads as
  before with no loops.
- **D23 — The Transition Manager lists a track that has loops**, with or without transitions,
  so a gone track's loops can be removed as its transitions can.

Recorded as [ADR 0017](../../docs/adr/0017-loops-are-authored-per-track.md).

## Placement (the three CLAUDE.md tests)

- **One intent, one engine call.** Add / remove / set a loop's ends, count and layer, Capture a
  loop end, nudge it, Preview a loop, Undo: each is one engine member. The shell never
  sequences two.
- **The shell formats; it never computes.** The view carries each loop's span as resolved
  positions within the track (so a from-end start is already a `Playhead`-shaped position), the
  sounding loop's pass and count, and the text the grid shows. The seek bar maps positions to
  pixels, as it does for fade marks.
- **No engine state in the shell.** Which track the Loops tab shows, which loop is selected and
  the draft being edited are engine state in the view (as the editor's draft is today).
- **Frames never cross the seam.** Spans are `Point`s in the store, positions in the view;
  frames stay inside the mixer.

## The store

```json
{ "schemaVersion": 2,
  "transitions": { "<outPath>|<inPath>": TransitionSpec },
  "playlistOverrides": { "<playlistName>": { "<outPath>|<inPath>": TransitionSpec } },
  "loops": { "<path>": [ LoopSpec ] },
  "playlistLoops": { "<playlistName>": { "<path>": [ LoopSpec ] } } }
```

`LoopSpec` is `{ start: FadePointSpec, end: FadePointSpec, count: int | null }`, null count
being open-ended. A path under `playlistLoops` may map to an empty list (D6). Lists are kept
sorted by resolved start; the store rejects an overlap on write.

## Testing

Every test renders through the production chain, runs on net8.0 and net48 x86, and keeps its
files under `TestTemp`.

- **Store.** Round-trips loops and playlist lists, schema 2; a schema-1 file loads with no loops
  and is written back as 2; an empty override list is kept, not dropped; an overlap is refused.
- **Resolution.** From-end ends resolve against the track's length; a loop past the length or
  with start ≥ end is passed over with a notice and the render is the straight track.
- **Rendering.** A 2 s span at count 3 renders as the span three times then the tail, sample
  for sample apart from the declick's few ms; the declick's width and curve are pinned. Count 1
  renders the straight track. Two loops in one track each run their count.
- **Boundary.** A fade-out point inside a span fires on the last pass; before the span, the
  fading voice loops under the incoming track; after it, the fade fires after the loops. The
  incoming track's fade-in point inside a span begins pass 1.
- **Open-ended.** The voice cycles past where the fade-out point would have fired;
  `CanCueTransition` is false; Transition now fades it out as authored, looping; Skip cuts.
- **Seeks.** Each rule in D10, by the playhead and the pass reported in the view.
- **Live edits.** Each case in D11; the playhead does not jump.
- **Preview ≡ Render** with a loop in the window; loop preview plays end−pre-roll, splice,
  start+post-roll, once, with the queue held.
- **Undo.** Add, move, recount, remove and switch layer each undo, interleaved with a
  transition edit.
- **Manager.** A track with loops and no transitions is listed; a gone track's loops can be
  removed.
- **Shell (headless).** The seek bar paints a span and its pass text from the view; the Loops
  tab's grid shows each loop as the view formats it; the tab strip switches; the Loops… row
  menu item selects that row's track; Capture is live only when `CanCapture` says so.

## Smoke

Add Looks items to `docs/smoke.md`, in both hosts' halves: open Loops… on the playing row, Capture
a start and an end, set a count of 3, see the span on the seek bar, see the playhead jump back
with "pass 2/3", preview the seam, switch to Transition and back, undo. Sounds is the user's.

## Order

1. [01](issues/01-the-engine-keeps-loops-per-track.md) — the model and the store (Core).
2. [02](issues/02-a-voice-plays-its-loops.md) — the mixer plays them, the view shows them (Core).
3. [03](issues/03-loops-are-authored-by-ear.md) — authoring intents, loop preview, undo, manager (Core).
4. [04](issues/04-the-seek-bar-shows-the-loops.md) — the seek bar (Shell, both hosts).
5. [05](issues/05-the-loops-tab.md) — the tab strip and the Loops tab (Shell, both hosts).

## Out of scope

- Live loop in / loop out from the transport (D18).
- A per-pass gain step (each pass quieter).
- Dragging a span's ends on the seek bar, as fade marks can be dragged. Can follow.
- Nested or overlapping loops; beat detection or snapping of any kind.
- Unrolling the timeline (a longer Length, an unrolled seek bar).

## Answered questions (the grilling, 2026-10-10/11)

Round 1: the word (Loop/pass); count = total hearings, ∞ distinct; track-global with playlist
override; a point inside a span fires on the last pass; a fading voice keeps looping; disjoint
and ordered; the playhead jumps with a pass counter; a fixed declick at the seam. All accepted.

Round 2: both ends fade-point-shaped, count 1 allowed; ∞ ends by Transition now or Skip, Cue
disabled; the seek rules; a separate home for loops rather than the transition editor's halves;
loop preview in v1; an override replaces the whole list; same store file, manager lists the
track; a misfit loop is passed over with a notice. All accepted.

Round 3: the panel's shape — **docked, toggling with the editor** (the recommendation was a
floating window; changed); one undo stack; live edits take effect at once and never move the
playhead; a fade-in point inside a span begins pass 1; Preview and Render honour loops; live
loop in/out out of scope. Rest accepted.

Round 4: a two-tab strip Transition | Loops; the Loops tab follows the attended row's track by
the editor's rule. Accepted.

## Comments
