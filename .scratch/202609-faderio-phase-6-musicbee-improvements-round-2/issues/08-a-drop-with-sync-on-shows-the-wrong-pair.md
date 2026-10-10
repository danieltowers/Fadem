# 08: A drop with Sync editor on leaves the editor on the old pair, lit green

Status: resolved
Blocked by: none
Deferred 2026-10-09 by the user, with issue 13: ask again later.

Phase 6, improvements round 2, a bug, seen in MusicBee. Raised 2026-10-09: "if i drag a track to
the faderio playlist with sync checked it keeps the original couple and lights up green - wrong".

## What happens

With **Sync editor** ticked, the editor is open on the playing track's transition into the next
(a → b). The user drags a track from MusicBee into the queue so that the playing track's next is
now the dropped track (a → x). The editor still shows a → b, and the traffic light
(`TrafficLight`, phase 6 issue 07) shows green, as if the editor's pair were the one about to be
heard.

## What should happen

To be confirmed in triage. Most likely: with Sync editor on, the editor follows the playing
track's transition into whatever is next now, so a drop that changes the next moves the editor to
a → x. If the editor stays on a → b (for example, the user has unsaved edits), the light must not
say green for a pair that is no longer adjacent.

## Notes for diagnosis

- Reproduce in both hosts: drop from MusicBee (plugin) and drop files or reorder rows (App),
  before and after the playing row, and while stopped.
- Check what the engine says the attended boundary is after the drop, and what the editor's pair
  and the light read from. CLAUDE.md: the shell holds no engine state between calls, so a stale
  pair kept by the editor across the drop is the likely cause.
- Leave a test that drops a row between the playing track and its next with sync on, and checks
  the editor's pair and the light.

## Acceptance

- [x] With Sync editor on, a drop that changes the playing track's next shows the new pair (or as
      decided in triage), and the light never shows green for a pair that is not adjacent.
- [x] The same for a reorder by dragging rows and for removing the next row.

## Comments

### 2026-10-10, triaged and built

Decided by the user: with Sync editor on, a drop, a move or a removal that gives the playing row
another next moves the editor to the playing row's new this → next, even off a pair opened by ✎
in between. And the light is red, not green, for a pair whose first track plays but whose next is
another track, Sync on or off.

- **The cause.** `TransitionEditorPanel.Follow` followed only a change of attended row; a drop
  changes the row's next, not the row. It now follows a change of either: the key is the row and
  the engine's `EditorFollow` together.
- **The light.** `PlaybackEngine.Heard` gave `Outgoing` (green) whenever the pair's first track was
  in hand, by path. It now gives `Outgoing` only while the row heard goes into the pair's second
  track, by ⏭'s rule (`AdjacenciesOf(row).Next`, so repeat-all's wrap counts) or into itself under
  repeat-one; otherwise the new `AdjacencyHeard.OutgoingIntoAnother`, which `TrafficLight` lights
  red and `Presenter.FmtHeard` words "Playing *a*, this transition's first track, but next comes
  another track". A preview is as before.
- That case remains reachable with Sync on: removing the playing row's next when it then is the
  last row leaves nothing to follow to, so the editor stays on the old pair, now red.
- Both hosts drop through `QueueDrag` → `PlaybackEngine.Add(paths, before)`, the call the tests make.
- Tests: Core `AdjacencyHeardTests` (next removed, a row moved in, the last row with repeat off,
  repeat-all into the first, repeat-one into itself); Shell `EditorFollowsPlaybackTests` (a drop,
  a move, a removal moving the editor, green; the last row's next removed, editor stays, not
  green) and `EditorTrafficLightTests` (red, with its words). README's light table and both
  smoke halves updated.
- Looks, in the App on three near-silent tones with Sync ticked: Alpha playing opened Alpha →
  Bravo, green; Bravo removed (Del) moved it to Alpha → Charlie, green; Charlie removed left it on
  Alpha → Charlie, red. Not checked by the agent: a drag from MusicBee into the plugin (the
  MusicBee smoke item), the light's tooltip on screen.
