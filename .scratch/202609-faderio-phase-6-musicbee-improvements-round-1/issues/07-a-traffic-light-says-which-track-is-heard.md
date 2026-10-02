# 07: A traffic light says which of the editor's tracks is heard

Status: resolved
Blocked by: 06

Phase 6, improvements round 1, both hosts (the editor panel is shared). Raised 2026-10-02:
"Id like a traffic light system in the editor panel to make it more intuitive for the user. The
traffic light should show at the top right of the editor
  - track loaded AND in editor as primary track = Green
  - track loaded AND in editor as secondary track = AMBER
  - track loaded does not match any track in the editor = RED"

## What to build

A round light at the top right of the editor, on the heading's line, beside the adjacency's
name. It is green while the open transition's outgoing track is heard, amber while its incoming
track is heard (and the outgoing one is not), and red while something else is. Its tooltip says
which track and what that means. With nothing loaded it is grey; with no adjacency open it is
hidden.

## Implementation decisions (defaults chosen by the agent, 2026-10-02; open to change)

- **Primary is the outgoing track**, the one named first in the heading ("a → b"), whose
  fade-out point the editor sets. **Secondary is the incoming track.**
- **"Loaded"** is a track the listener has in hand: the attended voice, playing or paused, and
  a live crossfade's incoming voice. During a preview, the preview's voices that are heard,
  and not the held queue. Stopped is nothing loaded: grey.
- **Green wins.** When both tracks are heard (a crossfade a → b, a preview's transition, or
  repeat-one's a → a), the light is green.
- **The engine says it:** `Heard(adjacency)` returns `AdjacencyHeard`: `Nothing`,
  `OtherTrack`, `Incoming` or `Outgoing`. Tracks match by file path, as capture matches them
  (issue 06). The shell maps the value to a colour and compares no tracks (CLAUDE.md test 2).
  It is read without the lock, from the published view, on every render.
- **The words.** The Presenter words the tooltip: "Playing *a*, this transition's first track",
  "Playing *b*, this transition's second track", "Playing a track that is not in this
  transition", and "Nothing is playing". The light's accessible name is the same text, so UI
  Automation can read it.
- **No new height.** The light sits in the heading's row, so the editor's height budget (issue
  06) is unchanged.

## Testing decisions

Core, on both targets, through the engine: stopped is `Nothing`; A playing with B → C open is
`OtherTrack`; B playing is `Outgoing`, paused too; C playing is `Incoming`; a crossfade A → B
seen from B → C is `Outgoing`; a preview of B → C is `Outgoing` in its pre-roll and `Incoming`
in its tail.

`Faderio.Shell.Tests` (both targets), the panel against a real engine: red, green and amber with
their tooltips; hidden with no adjacency open.

Both **Looks** halves gain an item for the light.

## Acceptance criteria

- [x] Green while the editor's first track plays, amber while only its second plays, red while
      another track plays, grey when stopped.
- [x] The light sits at the editor's top right, in both hosts, and its tooltip says which track.

## Comments

- **Resolved 2026-10-02.** Built as decided above.
  - **Engine:** `PlaybackEngine.Heard(adjacency)` and the `AdjacencyHeard` enum. `SamePair` is
    now shared with capture.
  - **Panel:** a "●" label docked to the right of the heading, in a 26 px heading row that
    replaces the old title label's own row. Its colour comes from `LightColor`; its tooltip and
    accessible name come from `Presenter.FmtHeard`.
  - **Tests:** `AdjacencyHeardTests`, 6 on both targets; `EditorTrafficLightTests`, 5 on both
    targets.
  - **Smoke (standalone):** editor on tone-a → tone-b.
    - ✅ Stopped: grey, "Nothing is playing".
    - ✅ tone-a playing: green, "Playing tone-a, this transition's first track".
    - ✅ tone-b playing (⏭): amber.
    - ✅ tone-c playing (⏭): red.
    - ✅ No scroll in the editor; Render still in view.
  - Not yet seen in MusicBee: the running MusicBee holds the build from before this issue.
