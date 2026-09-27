# 02: A gone track opened from the manager

Status: resolved
Blocked by: —

Recorded as "Not handled" in
[playlists 05](../../202609-faderio-phase-2-playlists/issues/05-the-transition-manager.md).

## What happens today

The manager's queries (`AdjacenciesInStore`, `TracksInStore`, in `PlaybackEngineManager.cs`)
resolve each path the store names. A queued track is the queue's own; any other is probed. A file
that cannot be probed (gone, or no longer promised) becomes `new Track { FilePath = path }`: no
title, length 0, `Format` null. The manager lists it by its path, and nothing else marks it.

A double-click on such a row opens the editor on that adjacency. The fields load the stored
values, but every save is refused as a point past the end of a 0-length track, which is not the
reason. Capture, ±, Preview and Render all act on a track that is not there. The one useful thing
to do with such a transition is remove it, and that works only by accident.

## What to build

- **The engine says which file is gone.** Whatever the manager lists carries, for each side, whether
  its file could be probed. The shell never infers it from a blank title, a 0 length or a null
  `Format` (placement test 2).
- **The manager marks it.** A row whose other track is gone shows "⚠" before the path, greyed,
  with a tooltip saying the file was not found where the store says. The picker marks a gone track
  the same way.
- **The editor, opened on it, can remove but not change.** Under the heading, a line names the
  missing file: "*file* is missing: this transition can be removed, not changed." Save, Revert,
  capture, ± , Preview and Render are disabled. **Remove transition** and, in playlist mode,
  **Remove override** work, and the manager's row goes when the store no longer holds anything
  for the pair.
- **Commands stay total and honest.** `Author` and `AuthorOverride` on an adjacency with a gone
  side store nothing, and the reason names the missing file, not a field's value. `Unauthor` and
  `UnauthorOverride` work as for any adjacency. `Preview` and `PreviewStored` keep their current
  behaviour for a gone file (tested in playlists 05).
- A queued row marked unreadable (⚠ in the queue, build issue 10) is not this case. Its track was
  probed when added and has its length, and nothing about it changes here.

## Where the logic lives

- **Engine:** the gone flag on what the manager's queries return, and the refusal in `Author` /
  `AuthorOverride`.
- **Shell:** the ⚠ mark, the editor's line and its disabled buttons.

## Acceptance

- [x] A transition authored between two files, one of them then deleted: the manager lists the
      row with "⚠" and the path, greyed.
- [x] A double-click opens the editor with the "is missing" line; Save and Preview are disabled;
      **Remove transition** removes it and the row leaves the manager.
- [x] Calling `Author` on that adjacency stores nothing and the reason names the file.
- [x] Restoring the file and asking the manager again lists the row as normal and editable.

## Tests

- **unit (Core):** the manager's result flags a gone side (outgoing or incoming) and not a present
  one; `Author` and `AuthorOverride` with a gone side refuse, naming the file, and the store is
  unchanged; `Unauthor` removes the pair; a restored file is no longer flagged.
- **form:** the manager's gone row shows ⚠ and is greyed; the double-click opens the editor with the
  line and the disabled buttons; Remove transition empties the row out of the manager.

## Smoke

- **Looks:** extend the Transitions… item: author a transition, rename one of its files, open the
  manager: the row reads "⚠ *path*", greyed; double-click it: the editor says the file is missing,
  Save is greyed, Remove transition removes it.

## Comments

- Built 2026-09-27. **Engine:** `Track.Gone`, set only by the manager's lookup (`StoreTracks.Of`)
  for a path whose file cannot be probed. Such a track is its path alone. The flag goes wherever
  the manager's answers go: `StoredAdjacency.Other`, both sides of its `Adjacency`, and the
  picker's `TracksInStore`. A queued track is never gone. `Author` and `AuthorOverride` on an
  adjacency with a gone side store nothing, blank spec or not, and return no refusals. The
  notice says "Not saved: *a → b*. *path* is missing: this transition can be removed, not
  changed.", naming each gone file.
- **Decided here: not a `Refusal`.** A refusal is a field's value refused, shown beside that
  field. A gone file isn't about any field, so it takes the path the other whole-command failures
  already take (no library, a store that can't be written): an empty list and a "Not saved"
  notice. `Refusal` is unchanged.
- **Shell:**
  - `Presenter.FmtTrack` reads "⚠ *path*" for a gone track, in the grid and the picker. The
    grid greys the row, and the path cell's tooltip says "Not found where the store says: *path*".
    The picker is owner-drawn, so a gone track reads grey there too.
  - The editor shows the line under the heading (`Presenter.FmtMissing`, by file name, in red).
    The fields are read-only; Save, Revert, Preview, Render, From end, the curve, capture and ±
    are disabled. Remove transition and Remove override stay live.
  - `HasUnsavedEdits` is false on a gone adjacency: nothing there can be saved. So leaving after
    Remove transition doesn't ask Save / Discard / Cancel.
  - The picker keeps its track by path, not by record, so a track stays picked when its file goes
    or comes back.
- **Found in smoke:** the grid selects its first row for itself, so a lone gone row was always
  highlighted and its grey never showed. The manager is read-only, so the grid now clears that
  selection after listing and when the window is shown. A click still selects a row.
- **Not handled:** the two-line message gives the editor panel a vertical scroll bar at the
  default size, with Render (disabled here) at its edge. Separately, `--edit-first` twice failed
  to open the editor at launch during the smoke. The same `EditTransitionAt(0)` passes every
  headless test, so it looks like a startup race in `Program.cs`, not this change.
- **Tests:** Core, four in `TransitionManagerTests`: gone flagged on either side and never on a
  present or queued track; `Author` and `AuthorOverride` (with a spec and blank) store nothing and
  name the file; `UnauthorOverride` then `Unauthor` empty the manager; a restored file is not gone
  and saves again. Form, three in `HeadlessFormTests.TransitionManager`: the ⚠ row greyed with
  its tooltip, other rows not greyed, the picker marked; the double-click opens the editor with
  the line, read-only fields and disabled buttons, capture off even while playing, Remove override
  live, and Remove transition empties the row; a restored file lists normally and opens editable.
- **Looks smoke** (fresh temp library and machine folder):
  - A transition saved in the editor, its incoming file renamed, the app relaunched: the picker
    reads "⚠ *path*" greyed on the gone track. On Outgoing tone the row reads "⚠ *path*" (greyed
    once the selection fix was in).
  - Double-clicked, the editor shows "Incoming tone.wav is missing: this transition can be
    removed, not changed." Save, Revert, Preview, Render and Capture are disabled; Remove
    transition is enabled. Pressing it emptied `transitions.json` and the manager.
  - With the file put back (store pre-written for this pass), the row reads "Incoming tone" in
    normal text and opens editable, with Save and Preview enabled.
