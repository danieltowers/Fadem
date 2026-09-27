# 02: A gone track opened from the manager

Status: ready-for-agent
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

- [ ] A transition authored between two files, one of them then deleted: the manager lists the
      row with "⚠" and the path, greyed.
- [ ] A double-click opens the editor with the "is missing" line; Save and Preview are disabled;
      **Remove transition** removes it and the row leaves the manager.
- [ ] Calling `Author` on that adjacency stores nothing and the reason names the file.
- [ ] Restoring the file and asking the manager again lists the row as normal and editable.

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
