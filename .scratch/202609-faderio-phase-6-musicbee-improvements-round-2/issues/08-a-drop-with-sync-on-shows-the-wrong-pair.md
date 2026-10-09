# 08: A drop with Sync editor on leaves the editor on the old pair, lit green

Status: needs-triage
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

- [ ] With Sync editor on, a drop that changes the playing track's next shows the new pair (or as
      decided in triage), and the light never shows green for a pair that is not adjacent.
- [ ] The same for a reorder by dragging rows and for removing the next row.
