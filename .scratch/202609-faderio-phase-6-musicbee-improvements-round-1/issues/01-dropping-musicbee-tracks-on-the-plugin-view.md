# 01: Dropping MusicBee tracks on the plugin's view

Status: resolved
Blocked by: none

Phase 6, MusicBee improvements round 1. Raised 2026-10-02: a track dragged from MusicBee's
track list onto faderio's view (the panel floating) is refused.

## The cause

The plugin's view never takes a drop. Phase 5 put drag-and-drop out of scope (brief decision 9
and "Out of scope"; issue 14), so `PluginView`'s queue list has no `AllowDrop` and no drop
handler. Only standalone faderio's `MainForm` list takes a drop, of files from Explorer
(`DataFormats.FileDrop`).

## What to build

Tracks dragged from MusicBee's track list and dropped on the plugin's view are added to
faderio's open playlist, as **Add to faderio's open playlist** adds the selection (issue 14).
Files dropped from Explorer are added the same way.

## Implementation decisions

- **Where the drop lands.** The queue list takes the drop. The tracks go to the end of the open
  playlist, as a file drop on standalone faderio does, not at the drop point.
- **What the drag carries.** Probed 2026-10-02 on 3.6.9403 (a WinForms window logging
  `GetFormats()` and each value): MusicBee's track drag is an ordinary shell drag, offering
  Copy, Move and Link, with `FileDrop`, `FileNameW`, `FileName` and the drag-image formats.
  `FileDrop` holds the dragged files' full paths, as Explorer's does. The view reads
  `FileDrop` alone, so a drop from MusicBee and one from Explorer are one path, and the paths
  go to the engine as they are, in the drag's order. The effect shown is Copy: the files stay
  where they are.
- **One call.** The dropped paths go to `Host.Add`, the one `Engine.Add` the context item
  makes. The engine probes each file off the UI thread, the rows appear as they resolve, and
  one notice names every rejection with its reason (ADR 0007). The shell neither lists,
  probes nor filters.
- **Playback is left alone**, as for the context item: a drop starts and stops neither player.
- **Refused drags.** Anything without files shows the no-drop cursor, as standalone faderio's
  list does.
- **Out of scope:** dragging rows to reorder them inside the plugin's queue (standalone only,
  issue 10's "no drag").

## Testing decisions

Seam 2, the plugin's view: a drop raised on the view's queue list with a `DataObject` holding
`FileDrop`, as Windows delivers it.

- Two tones dropped on a queue of two: four rows in order once the add ends, the playlist file
  holds them, the output never played, no notice.
- A tone, a text file named `.wav` and a missing path: the tone is added; one notice reads
  "Added 1 track; rejected 2 — …", as `AddReport.Summarise` writes it.
- A drag of text only: the effect is None, and nothing is added.

The MusicBee **Looks** half gains an item: a promised track dragged from MusicBee's main panel
onto the view, docked and floating, appends to the queue and the playlist file with no notice.

## Acceptance criteria

- [x] A track dragged from MusicBee onto the plugin's view, docked or floating, is added to the
      open playlist in one engine call.
- [x] Rejected files are listed together, each with what disqualifies it.
- [x] Headless tests drive the drop through the plugin's view.

## Comments

- **Resolved 2026-10-02.** Built as specified above.
  - **The view:** `PluginView`'s queue list takes drops (`AllowDrop`); DragEnter and DragOver
    offer Copy when the drag holds `FileDrop` and None otherwise; DragDrop sends the paths to
    `Engine.Add`, the one call the context item makes through `Host.Add`.
  - **Tests:** `PluginTests.Drop`, the drag delivered as Windows delivers MusicBee's (Copy,
    Move and Link allowed): two tones appended in the drag's order, saved, never played, no
    `Player_Stop`, no notice; a tone, a text file named `.wav` and a missing path giving one
    notice "Added 1 track; rejected 2 — …" in the view's strip, no box; a text drag refused.
    The two drop tests failed before the change; the refusal test passed already, the list
    refusing everything.
  - **Smoke** on the portable MusicBee (WASAPI shared, the plugin installed from this build):
    - ✅ Loads with no box; the strip "Ready."; the host line "library · WASAPI shared at
      48000 Hz on Speakers (C-Media(R) Audio)".
    - ✅ (the user) Tracks dragged from MusicBee's main panel onto the view are added: `Evening.m3u8`
      was rewritten at 07:13 holding MusicBee's tracks, and the strip read "Ready.".
    - ⬜ Not seen by the session: the floating panel, the gone-file rejection by drop, and
      a refused text drag in the real view. The headless tests cover the rejection and the
      refusal.
  - **Found on the way:** in the solution-wide `dotnet test`, Core's net8.0 host crashed
    (AccessViolationException) after 618 tests, with every other run passing; Core net8.0 run
    alone passed 865 of 865. Core is untouched by this issue.
